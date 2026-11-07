using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

/// <summary>Checks completed game frames, rather than only the immediate manual Timeline sample.</summary>
public static class PlayerSkillAnimationFrameVerification
{
    private const string Pending = "ProjectR.EAnimationFrames";
    private const string Report = "Temp/PlayerSkillAnimationFrameVerification.result";
    private static PlayerCore _player;
    private static PlayerSkillSequenceSettings _settings;
    private static PlayerSkillDefinition _original, _test;
    private static PlayerSkillHitTestActor _victim;
    private static ESkillDataPreview _reference;
    private static Transform[] _bones, _referenceBones;
    private static RuntimeAnimatorController _controller;
    private static bool _rootMotion, _runInBackground;
    private static int _phase, _lastFrame, _frames;
    private static readonly int[] Stages = new int[4];
    private static float _largestError;
    private static int _exitFrames;
    private static Vector3 _origin;
    private static double _deadline;

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    [MenuItem("Tools/Character/Player/Validate E Animation in Game Frames")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run E animation frame verification from Edit Mode.");
        Directory.CreateDirectory("Temp");
        File.WriteAllText(Report, "Running E animation verification on completed game frames\n");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (_player == null)
            {
                _player = Object.FindAnyObjectByType<PlayerCore>();
                if (_player == null) throw new InvalidOperationException("Open a scene containing the player.");
                _runInBackground = Application.runInBackground;
                Application.runInBackground = true;
                _player.InputCollector.enabled = false;
                foreach (var enemy in Object.FindObjectsByType<EnemyCore>()) enemy.gameObject.SetActive(false);
                foreach (var audio in _player.GetComponentsInChildren<AudioSource>(true)) audio.mute = true;
                var director = _player.DirectorContainer.Directors[DirectorID.Skill];
                _settings = director.GetComponent<PlayerSkillSequenceSettings>();
                _original = _settings.Definition;
                if (_original == null) throw new InvalidOperationException("Connect the E data before verification.");
                _controller = _player.Animator.runtimeAnimatorController;
                _rootMotion = _player.Animator.applyRootMotion;
                _player.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var target = new GameObject("E frame verification target") { layer = 6 };
                target.transform.position = new Vector3(1000, 1000, 1000);
                var box = target.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = Vector3.one * 100f;
                _victim = target.AddComponent<PlayerSkillHitTestActor>();
                _phase = 0;
                Begin();
                return;
            }
            if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("E animation frame verification timed out.");
            if (_lastFrame == Time.frameCount) return;
            _lastFrame = Time.frameCount;
            if (!_player.StateMachine.IsSkillActive)
            {
                if (_player.Animator.runtimeAnimatorController != _controller || _player.Animator.applyRootMotion != _rootMotion)
                    throw new Exception("E did not restore the Animator after natural completion.");
                var blend = _player.Animator.GetComponent<PlayerUltimateExitBlend>();
                if (_exitFrames++ == 0)
                {
                    if (blend == null || !blend.IsBlending) throw new Exception("Natural E exit did not retain its outgoing pose.");
                    ComparePose(Mathf.Max(0f, _test.Duration - .000001f), "First completed exit frame");
                    float distance = Vector3.ProjectOnPlane(_player.Mover.PhysicsPosition - _origin, Vector3.up).magnitude;
                    float expected = _test.EvaluateMotion(_test.Duration).magnitude;
                    if (Mathf.Abs(distance - expected) > .03f) throw new Exception("Animation ownership changed E's data-driven travel.");
                }
                if (blend != null && blend.IsBlending) return;
                if (Stages.Any(count => count == 0)) throw new Exception("A preparation/windup/dash/exit animation segment was never observed.");
                if (_victim.Hits.Count != 9) throw new Exception("The authored E no longer produces its nine hits.");
                File.AppendAllText(Report, $"phase={_phase} hitstop={(_phase == 0 ? 0 : 12)} frames={_frames} exitFrames={_exitFrames} stages={string.Join(",", Stages)} maxBoneError={_largestError:0.000000}deg hits={_victim.Hits.Count}\n");
                if (++_phase < 2) { Begin(); return; }
                Finish(null);
                return;
            }
            var runtime = _player.StateMachine.SkillState.Runtime;
            float time = runtime.Time;
            if (time < .15f) return;
            ComparePose(Mathf.Min(time, _test.Duration - .000001f), "Completed E frame");
            _frames++;
            Stages[time < .533333f ? 0 : time < .866667f ? 1 : time < 1.1f ? 2 : 3]++;
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void ComparePose(float time, string stage)
    {
        _reference.Sample(time, _test);
        float error = 0f;
        for (int i = 0; i < _bones.Length; i++)
            if (_bones[i] != null && _referenceBones[i] != null)
                error = Mathf.Max(error, Quaternion.Angle(_bones[i].localRotation, _referenceBones[i].localRotation));
        _largestError = Mathf.Max(_largestError, error);
        if (error > .15f) throw new Exception($"{stage} {Time.frameCount} at E {time:0.000000}s lost the expected pose ({error:0.000}deg).");
    }

    private static void Begin()
    {
        _reference?.Dispose(); _reference = null;
        if (_test != null) Object.DestroyImmediate(_test);
        _test = Object.Instantiate(_original);
        _test.hideFlags = HideFlags.HideAndDontSave;
        _test.useInputCancelTime = false;
        foreach (var hit in _test.hits) { hit.hitStopFrames = _phase == 0 ? 0 : 12; hit.shakeForce = 0f; }
        _settings.Definition = _test;
        HitstopCoordinator.Cancel(_player);
        _player.StateMachine.Transition(_player.StateMachine.IdleState);
        _player.Mover.ConsumePendingDisplacement();
        _origin = new Vector3(1000, 1000, 1000);
        _player.transform.position = _origin;
        _player.GetComponent<Rigidbody>().position = _origin;
        _player.Rotator.RotateImmediately(Vector3.forward);
        var director = _player.DirectorContainer.Directors[DirectorID.Skill];
        _reference = new ESkillDataPreview();
        _reference.Build(_player, director, (UnityEngine.Timeline.TimelineAsset)director.playableAsset);
        var model = (GameObject)typeof(ESkillDataPreview).GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_reference);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        var referenceAnimator = model.GetComponent<Animator>();
        _bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone).Select(i => _player.Animator.GetBoneTransform((HumanBodyBones)i)).ToArray();
        _referenceBones = Enumerable.Range(0, (int)HumanBodyBones.LastBone).Select(i => referenceAnimator.GetBoneTransform((HumanBodyBones)i)).ToArray();
        _victim.Hits.Clear(); _victim.Health = 1000f;
        _frames = 0; _exitFrames = 0; _largestError = 0f; Array.Clear(Stages, 0, Stages.Length);
        _lastFrame = Time.frameCount;
        _deadline = EditorApplication.timeSinceStartup + 40d;
        _player.AddSkillGauge(100f);
        _player.StateMachine.Transition(_player.StateMachine.SkillState);
    }

    private static void Finish(Exception error)
    {
        SessionState.SetBool(Pending, false);
        _reference?.Dispose(); _reference = null;
        if (_player != null) _player.StateMachine.SkillState.StopRuntime();
        if (_settings != null) _settings.Definition = _original;
        if (_test != null) Object.DestroyImmediate(_test);
        if (_victim != null) { HitstopCoordinator.Cancel(_victim); Object.DestroyImmediate(_victim.gameObject); }
        Application.runInBackground = _runInBackground;
        string result = error == null ? "PASS: E poses, terminal hold and transition to the next animation survive completed game frames and hitstop." : "FAIL: " + error;
        File.AppendAllText(Report, result + "\n");
        if (error != null) Debug.LogException(error); else Debug.Log(result);
        _player = null; _settings = null; _test = _original = null; _victim = null;
        EditorApplication.ExitPlaymode();
    }
}
