using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks the real Player/Dodge adapter with temporary settings; never saves combat assets.</summary>
[InitializeOnLoad]
public static class DodgeAfterimageVerification
{
    const string Key = "ProjectR.DodgeAfterimageVerification";
    const string ResultPath = "Temp/DodgeAfterimageVerification.result";
    static int frames, checks;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static DodgeAfterimageVerification() { if (SessionState.GetInt(Key, 0) != 0) EditorApplication.update += Tick; }

    [MenuItem("Tools/Combat/Validate Dodge Afterimage Integration")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start in Edit Mode.");
        if (SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Use one saved scene for this runtime verification.");
        SessionState.SetString(Key + ".scene", SceneManager.GetActiveScene().path);
        SessionState.SetString(Key + ".asset", File.ReadAllText("Assets/07_Datas/Player/PlayerDodge.asset"));
        SessionState.SetInt(Key, 1);
        frames = 0;
        EditorSceneManager.OpenScene("Assets/01_Scenes/CombatPrototypeScene.unity", OpenSceneMode.Single);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.isPaused = false;
        EditorApplication.isPlaying = true;
    }

    static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); checks++; }
    static void Tick()
    {
        int stage = SessionState.GetInt(Key, 0);
        if (stage == 2 && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.update -= Tick;
            SessionState.SetInt(Key, 0);
            string scene = SessionState.GetString(Key + ".scene", "");
            if (!string.IsNullOrEmpty(scene)) EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            return;
        }
        if (stage != 1 || !EditorApplication.isPlaying || ++frames < 12) return;
        var player = Object.FindAnyObjectByType<PlayerCore>();
        var enemy = Object.FindObjectsByType<EnemyCore>(FindObjectsSortMode.None).FirstOrDefault(e => e.CurrentHP > 0);
        if (player == null || player.Dodge == null || enemy == null) return;
        var original = player.DodgeDefinition;
        var test = Object.Instantiate(original);
        var input = player.GetComponent<UnityEngine.InputSystem.PlayerInput>();
        var originalCheck = player.OnPerfectDodgeCheck;
        float scale = Time.timeScale;
        checks = 0;
        try
        {
            input?.DeactivateInput();
            player.OnPerfectDodgeCheck = () => null;
            typeof(PlayerCore).GetField("_dodgeDefinition", Private).SetValue(player, test);
            test.closeAtAnimationMarker = false;
            test.dodgeEffects.Clear(); test.counterEffects.Clear();
            test.slow.StartDelay = 0; test.slow.SlowScale = .25f; test.slow.FadeInDuration = 0;
            test.afterimage = new DodgeAfterimageSettings();
            var s = test.afterimage;
            Check(s.mode == DodgeAfterimageMode.Legacy && s.UsesLegacy, "Existing data must default to legacy.");
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectR.VFX.Editor.ChromaticAfterimageBuilder.PrefabPath);
            s.prefab = asset; s.mode = DodgeAfterimageMode.Chromatic; s.opacity = .42f; s.snapshotLifetime = .87f;
            s.snapshotInterval = .08f; s.fragmentsPerSecond = 345; s.glintsPerSecond = 23;
            Check(s.HasValidPrefab && !s.UsesLegacy, "New-only mode requires a valid independent VFX prefab.");
            player.Dodge.BeginDodge(false);
            Check(player.Dodge.Afterimage == null, "Perfect-success trigger must not play for an ordinary dodge.");
            player.Dodge.ForcePerfect(enemy);
            var effect = player.Dodge.Afterimage;
            Check(effect != null && effect.IsEmitting, "Real perfect-success notification starts the new VFX.");
            Check(effect.sourceRoot == player.Animator.transform && effect.externalClock, "VFX binds to the real animated model and combat clock.");
            Check(Mathf.Abs(effect.opacity - .42f) < .0001f && Mathf.Abs(effect.snapshotLifetime - .87f) < .0001f, "Authored appearance is copied into runtime.");
            Check(effect.fragmentsPerSecond == 345 && effect.glintsPerSecond == 23, "Both GPU emission rates reach runtime.");
            Check(effect.gameObject.layer == player.Animator.gameObject.layer, "Runtime does not retain the preview-only layer.");
            effect.Tick(.01f);
            Check(effect.ActiveSnapshots > 0 && effect.particles.GetMesh("SourcePose") != null, "Runtime captures a pose and binds the GPU emission surface.");
            s.opacity = .99f;
            Check(Mathf.Abs(effect.opacity - .42f) < .0001f, "Editing settings does not rewrite the active action snapshot.");
            player.Dodge.ForcePerfect(enemy);
            Check(effect == player.Dodge.Afterimage, "Repeated success notifications reuse the effect instance.");
            player.BeginHitStop(); player.Dodge.LateTick();
            Check(effect.particles.pause, "Hitstop pauses the GPU effect.");
            player.EndHitStop(); player.Dodge.LateTick();
            Check(!effect.particles.pause && Mathf.Abs(effect.particles.playRate - CombatTimeController.Scale) < .0001f, "GPU resumes at the combat slow scale.");
            Time.timeScale = 0; player.Dodge.LateTick();
            Check(effect.particles.pause, "Global pause freezes the optional VFX.");
            Time.timeScale = scale;
            player.Dodge.BeforeTransition(player.StateMachine.FrontDodgeState, player.StateMachine.IdleState);
            Check(!effect.IsEmitting && effect.ActiveSnapshots > 0, "Leaving dodge stops emission while existing poses finish fading.");
            s.mode = DodgeAfterimageMode.Both; player.Dodge.BeginDodge(false); player.Dodge.ForcePerfect(enemy);
            Check(player.Dodge.UsesLegacyAfterimage && player.Dodge.Afterimage != null, "Both mode enables legacy plus the new effect.");
            s.mode = DodgeAfterimageMode.Off; player.Dodge.BeginDodge(false); player.Dodge.ForcePerfect(enemy);
            Check(!player.Dodge.UsesLegacyAfterimage && player.Dodge.Afterimage == null, "Off suppresses both dodge afterimage paths.");
            s.mode = DodgeAfterimageMode.Chromatic; s.prefab = null; player.Dodge.BeginDodge(false);
            Check(player.Dodge.UsesLegacyAfterimage && player.Dodge.Afterimage == null, "Missing optional prefab falls back to legacy.");
            s.prefab = asset; s.start = DodgeAfterimageStart.DodgeStart; player.Dodge.BeginDodge(true);
            Check(player.Dodge.Afterimage != null && player.Dodge.Afterimage.IsEmitting, "Dodge-start trigger also supports an ordinary backward dodge.");
            s.start = DodgeAfterimageStart.SlowStart; test.slow.StartDelay = .08f;
            player.Dodge.Dispose(); player.Dodge.BeginDodge(false); player.Dodge.ForcePerfect(enemy);
            Check(player.Dodge.Afterimage == null, "Slow-start trigger waits for the authored delay.");
            player.Dodge.Tick(.09f);
            Check(player.Dodge.Afterimage != null && player.Dodge.Afterimage.IsEmitting, "Delayed slow-start notification starts the effect.");
            Check(File.ReadAllText("Assets/07_Datas/Player/PlayerDodge.asset") == SessionState.GetString(Key + ".asset", ""), "Verification leaves authored dodge settings unchanged on disk.");
            File.WriteAllText(ResultPath, "PASS " + checks + " runtime checks\n");
            Debug.Log("Dodge Afterimage Integration: " + checks + " checks passed.");
        }
        catch (Exception e) { File.WriteAllText(ResultPath, "FAIL after " + checks + " checks\n" + e); Debug.LogException(e); }
        finally
        {
            Time.timeScale = scale;
            player.EndHitStop(); player.Dodge.Dispose();
            player.OnPerfectDodgeCheck = originalCheck;
            typeof(PlayerCore).GetField("_dodgeDefinition", Private).SetValue(player, original);
            Object.Destroy(test);
            SessionState.SetInt(Key, 2);
            EditorApplication.isPlaying = false;
        }
    }
}
