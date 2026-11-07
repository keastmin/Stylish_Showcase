using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Integration checks use disposable definitions and never save test edits.</summary>
public static class VirtualCutsceneVerification
{
    public static string LastReport { get; private set; } = "Not run";
    private static int _checks, _phase, _frames, _cueCount;
    private static double _started;
    private static PlayerCore _player;
    private static PlayerUltimateDefinition _original, _test;
    private static VirtualCutsceneDefinition _cutscene;
    private static Vector3 _position;
    private static (EnemyCore enemy, Vector3 position, float health)[] _enemies;
    private static float _timeScale, _cooldown;
    private static bool _audioPause;
    private static RuntimeAnimatorController _controller;
    private static Camera _camera;
    private static bool _cameraEnabled;
    private static string _originalJson;

    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException("Cutscene check failed: " + message); }

    [MenuItem("Tools/Character/Player/Validate Cutscene Studio (Edit Mode)")]
    public static void EditModeMenu() => Debug.Log(RunEditMode());

    public static string RunEditMode()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode.");
        _checks = 0;
        var original = AssetDatabase.LoadAssetAtPath<VirtualCutsceneDefinition>(VirtualCutsceneSetup.DefaultPath);
        var player = Object.FindAnyObjectByType<PlayerCore>();
        Check(original != null && original.Ready, "default definition is usable");
        Check(player != null && player.UltimateDefinition.cutscene == original, "scene Q ultimate references the cutscene");
        var definition = Object.Instantiate(original);
        string playerJson = EditorJsonUtility.ToJson(player), assetJson = EditorJsonUtility.ToJson(original);
        var sourceController = player.Animator.runtimeAnimatorController;
        var sourcePosition = player.transform.position;
        bool dirty = player.gameObject.scene.isDirty;
        int previewCount = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
        var render = new PreviewRenderUtility();
        VirtualCutsceneStage stage = null;
        AudioClip mix = null;
        var effectFixture = new GameObject("Cutscene particle verification") { hideFlags = HideFlags.HideAndDontSave };
        effectFixture.SetActive(false);
        var emitter = effectFixture.AddComponent<ParticleSystem>();
        var emission = emitter.emission; emission.rateOverTime = 30;
        var main = emitter.main; main.startLifetime = 2; main.startSpeed = 1;
        definition.effects.Add(new VirtualCutsceneEffect { prefab = effectFixture, start = .3f, duration = .8f });
        try
        {
            stage = new VirtualCutsceneStage(definition, player.Animator, render.camera);
            render.AddSingleGO(stage.Root);
            Check(stage.Animator != null && stage.Animator != player.Animator, "independent visual actor");
            Check(stage.Root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, "no gameplay scripts in visual clone");
            Check(stage.Root.GetComponentsInChildren<Collider>(true).Length == 0, "no gameplay collision in visual clone");
            Check(stage.Root.GetComponentsInChildren<AudioSource>(true).Length == 0, "no copied combat sound");
            Check(stage.Root.GetComponentsInChildren<Transform>(true).All(t => t.gameObject.layer == definition.renderLayer), "render-layer isolation");
            Check(!stage.Animator.fireEvents && !stage.Animator.applyRootMotion, "animation cannot trigger damage or move combat actor");
            var bones = stage.Animator.GetComponentsInChildren<Transform>();
            stage.Sample(.15f);
            var rotations = bones.Select(t => t.localRotation).ToArray();
            stage.Sample(.8f);
            Check(bones.Where((t, i) => Quaternion.Angle(t.localRotation, rotations[i]) > .01f).Any(), "manual animation changes bone poses");
            stage.Sample(.15f);
            Check(bones.Where((t, i) => Quaternion.Angle(t.localRotation, rotations[i]) > .02f).Count() == 0, "scrubbing backward is deterministic");
            var effect = stage.Root.GetComponentsInChildren<ParticleSystem>(true).Last();
            Check(!effect.gameObject.activeInHierarchy, "effect hidden before its cue");
            stage.Sample(.6f);
            Check(effect.gameObject.activeInHierarchy && effect.particleCount > 0, "effect is simulated during its cue");
            var particles = new ParticleSystem.Particle[effect.main.maxParticles];
            int particleCount = effect.GetParticles(particles);
            stage.Sample(.9f); stage.Sample(.6f);
            var resampled = new ParticleSystem.Particle[particles.Length];
            Check(effect.GetParticles(resampled) == particleCount && Vector3.Distance(particles[0].position, resampled[0].position) < .001f,
                "particle scrubbing uses deterministic simulation");
            stage.Sample(1.2f);
            Check(!effect.gameObject.activeInHierarchy, "effect hidden after its cue");
            var first = definition.shots[0];
            var last = definition.shots[definition.shots.Count - 1];
            definition.shots.Reverse();
            Check(definition.ShotAt(0) == first && definition.ShotAt(definition.Duration) == last, "camera cuts ignore list order and hold final shot");
            stage.Sample(definition.Duration);
            Check(Vector3.Distance(stage.Camera.transform.position, last.endPosition) < .001f, "camera reaches authored endpoint");
            mix = VirtualCutsceneAudioPreview.BuildMix(definition);
            Check(definition.sounds.Count == 0 || mix != null, "authored sound can be mixed for preview");
            var sound = new VirtualCutsceneSound { start = 1, duration = 2, volume = .8f, fadeIn = .5f, fadeOut = .5f };
            Check(sound.VolumeAt(.9f) == 0 && sound.VolumeAt(3) == 0 && Mathf.Abs(sound.VolumeAt(1.25f) - .4f) < .001f,
                "sound envelope respects scheduling and gain");
            Check(Vector3.Distance(player.transform.position, sourcePosition) < .00001f && player.Animator.runtimeAnimatorController == sourceController,
                "source actor transform and controller unchanged");
            Check(EditorJsonUtility.ToJson(player) == playerJson && EditorJsonUtility.ToJson(original) == assetJson, "source component and authored asset unchanged");
            Check(player.gameObject.scene.isDirty == dirty, "preview does not dirty the gameplay scene");
        }
        finally
        {
            stage?.Dispose(); render.Cleanup();
            if (mix != null) Object.DestroyImmediate(mix);
            Object.DestroyImmediate(definition);
            Object.DestroyImmediate(effectFixture);
        }
        Check(UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount == previewCount, "preview scene released");
        return LastReport = "Cutscene Studio Edit Mode: PASS (" + _checks + " checks).";
    }

    [MenuItem("Tools/Character/Player/Validate Cutscene Studio (Play Mode)")]
    public static void RuntimeMenu() => StartRuntime();

    public static string StartRuntime()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in Play Mode.");
        if (_test != null) throw new InvalidOperationException("Already running.");
        _checks = _phase = _frames = _cueCount = 0;
        _player = Object.FindAnyObjectByType<PlayerCore>();
        if (_player == null) throw new InvalidOperationException("No player.");
        _original = _player.UltimateDefinition;
        _originalJson = EditorJsonUtility.ToJson(_original);
        _timeScale = Time.timeScale; _audioPause = AudioListener.pause;
        _controller = _player.Animator.runtimeAnimatorController;
        _camera = _player.MainCamera; _cameraEnabled = _camera != null && _camera.enabled;
        _test = Object.Instantiate(_original);
        _test.cooldown = 0;
        _cutscene = Object.Instantiate(_original.cutscene);
        _cutscene.duration = .65f;
        _test.cutscene = _cutscene;
        _test.hits.Clear(); _test.cues.Clear();
        foreach (var segment in _test.sequence) { segment.hits.Clear(); segment.cues.Clear(); }
        if (_test.HasSequence) _test.sequence[0].cues.Add(new UltimateCue { time = _test.sequence[0].SourceStart, name = "verification-start" });
        else _test.cues.Add(new UltimateCue { time = 0, name = "verification-start" });
        SetUltimate(_test);
        _player.Ultimate.CueTriggered += OnCue;
        _player.StateMachine.Transition(_player.StateMachine.IdleState);
        _player.Ultimate.TickCooldown(10000);
        _position = _player.transform.position;
        _enemies = Object.FindObjectsByType<EnemyCore>().Select(e => (e, e.transform.position, e.CurrentHP)).ToArray();
        _started = EditorApplication.timeSinceStartup;
        LastReport = "RUNNING";
        EditorApplication.update += RuntimeTick;
        try
        {
            Check(_player.TryBeginUltimate(), "existing Q entry point accepts cast");
            Check(VirtualCutscenePlayback.IsPlaying && _player.Ultimate.IsIntroPlaying && _player.Ultimate.IsActive, "intro belongs to active cast");
            _cooldown = _player.Ultimate.CooldownRemaining;
            Check(Time.timeScale == 0 && AudioListener.pause && _camera != null && !_camera.enabled, "combat time, audio and camera suspended");
            CheckDisplayOutput(VirtualCutscenePlayback.Active);
            Check(_cueCount == 0 && _player.Ultimate.Time == 0, "no combat animation or zero-time cues before intro ends");
        }
        catch (Exception exception) { CompleteRuntime(exception); }
        return LastReport;
    }

    private static void OnCue(UltimateCue cue) { if (cue.name == "verification-start") _cueCount++; }
    private static void SetUltimate(PlayerUltimateDefinition definition)
        => typeof(PlayerCore).GetField("_ultimateDefinition", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_player, definition);

    private static void CheckDisplayOutput(VirtualCutscenePlayback active)
    {
        var displays = Camera.allCameras.Where(c => c.name == "Cutscene Display Camera" && c.isActiveAndEnabled).ToArray();
        Check(displays.Length == 1, "exactly one active cutscene display camera");
        var display = displays[0];
        Check(display.targetTexture == null && display.targetDisplay == 0,
            "cutscene retains a camera rendering to the Game View display");
        Check(display.cullingMask == 0 && display.gameObject.scene == active.Stage.Root.scene,
            "display camera draws no combat geometry and belongs to the temporary stage");
        var data = display.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        Check(data != null && !data.renderPostProcessing, "display camera does not post-process the cutscene overlay");
        Check(active.Output != null && active.Output.IsCreated() && active.Stage.Camera.isActiveAndEnabled &&
            active.Stage.Camera.targetTexture == active.Output, "cutscene visuals still render to the overlay texture");
        Check(_camera != null && !_camera.enabled, "gameplay camera stays suspended during cutscene output");
    }

    private static void CheckDisplayStopped(string context)
        => Check(!Camera.allCameras.Any(c => c.name == "Cutscene Display Camera" && c.isActiveAndEnabled),
            context + " immediately stops temporary display rendering");

    private static void RuntimeTick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup - _started > 20) throw new InvalidOperationException("Runtime verification timed out or Play Mode ended.");
            _frames++;
            if (_phase == 0)
            {
                if (VirtualCutscenePlayback.IsPlaying)
                {
                    var active = VirtualCutscenePlayback.Active;
                    CheckDisplayOutput(active);
                    Check(Vector3.Distance(_player.transform.position, _position) < .001f && _enemies.All(e => e.enemy == null || Vector3.Distance(e.enemy.transform.position, e.position) < .001f), "combat bodies remain still");
                    Check(_enemies.All(e => e.enemy == null || e.enemy.CurrentHP == e.health) && _cueCount == 0, "cutscene cannot damage enemies");
                    Check(_player.Ultimate.Time == 0 && _player.Ultimate.CooldownRemaining == _cooldown, "combat sequence and cooldown stay frozen");
                    Check(_player.Animator.runtimeAnimatorController == _controller, "combat controller preserved during intro");
                    if (active.Stage.Root.activeInHierarchy)
                        Check(active.Stage.Root.GetComponentsInChildren<MonoBehaviour>(true).All(b => b is UnityEngine.Rendering.Universal.UniversalAdditionalLightData), "no copied gameplay scripts become active");
                    return;
                }
                Check(_cueCount == 1 && _player.Ultimate.IsActive && !_player.Ultimate.IsIntroPlaying, "zero-time combat cue fires once after returning");
                Check(Mathf.Approximately(Time.timeScale, _timeScale) && AudioListener.pause == _audioPause, "time and audio restored");
                Check(_camera.enabled == _cameraEnabled, "gameplay camera restored");
                CheckDisplayStopped("natural completion");
                _player.StateMachine.Transition(_player.StateMachine.IdleState);
                _player.Ultimate.TickCooldown(10000);
                Check(_player.TryBeginUltimate(), "repeat cast starts cleanly");
                Check(VirtualCutscenePlayback.IsPlaying, "repeat intro starts");
                CheckDisplayOutput(VirtualCutscenePlayback.Active);
                VirtualCutscenePlayback.Active.enabled = false;
                Check(!VirtualCutscenePlayback.IsPlaying && !_player.Ultimate.IsActive, "disabling playback cancels the cast");
                Check(Time.timeScale == _timeScale && AudioListener.pause == _audioPause && _camera.enabled == _cameraEnabled, "interruption restores global state");
                CheckDisplayStopped("cancellation");
                _phase = 1; _frames = 0;
            }
            else if (_frames > 3)
            {
                Check(_player.Animator.runtimeAnimatorController == _controller, "combat controller restored after cancellation");
                Check(!Resources.FindObjectsOfTypeAll<Camera>().Any(c => c.name == "Cutscene Render Camera"), "temporary render camera removed");
                Check(!Resources.FindObjectsOfTypeAll<Camera>().Any(c => c.name == "Cutscene Display Camera"), "temporary display camera removed");
                Check(!Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).name.StartsWith("Virtual Cutscene Stage ")), "temporary runtime scene unloaded");
                Check(EditorJsonUtility.ToJson(_original) == _originalJson, "authored ultimate data unchanged");
                CompleteRuntime(null);
            }
        }
        catch (Exception exception) { CompleteRuntime(exception); }
    }

    private static void CompleteRuntime(Exception exception)
    {
        EditorApplication.update -= RuntimeTick;
        if (_player != null)
        {
            VirtualCutscenePlayback.CancelFor(_player);
            _player.Ultimate.CueTriggered -= OnCue;
            _player.StateMachine.Transition(_player.StateMachine.IdleState);
            SetUltimate(_original);
        }
        Time.timeScale = _timeScale; AudioListener.pause = _audioPause;
        if (_test != null) Object.Destroy(_test);
        if (_cutscene != null) Object.Destroy(_cutscene);
        _test = null; _cutscene = null;
        LastReport = exception == null ? "Cutscene Studio Play Mode: PASS (" + _checks + " checks). Natural completion, combat freeze, deferred damage, repeat cast, cancellation and cleanup." : "FAIL: " + exception.Message;
        if (exception != null) Debug.LogException(exception); else Debug.Log(LastReport);
    }
}
