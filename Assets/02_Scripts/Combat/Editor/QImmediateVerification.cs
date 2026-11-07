#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Isolated Q regression checks. No authored assets or current scene actors are edited.
public static class QImmediateVerification
{
    private const string PlayerPath = "Assets/08_Prefabs/Combat/Player.prefab";
    private const string ClipPath = "Assets/Download Assets/Scythe Animation Pack/Animations/Humanoid/02_Attack/07_Combo_Attack_Air/Combo_Attack_Air_All.anim";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [MenuItem("Tools/Character/Player/Validate Q Immediate Cast Data")]
    private static void DataMenu() => Debug.Log(RunData());
    [MenuItem("Tools/Character/Player/Validate Q Immediate Cast (Play Mode)")]
    private static void RuntimeMenu() => Debug.Log(RunRuntime() + "\n" + RunPhysics());

    public static string RunData()
    {
        _checks = 0;
        var d = Definition();
        PlayerUltimateStudio studio = null;
        try
        {
            Near(d.InputCancelTime, d.Duration, "Default locks input until the complete sequence");
            d.useInputCancelTime = true; d.inputCancelTime = .4f; d.playbackSpeed = 2f;
            Near(d.InputCancelTime, .4f, "Input cancel threshold uses sequence seconds");
            Near(d.RuntimeDuration, .5f, "Natural duration includes all content and global speed");
            d.inputCancelTime = 999f; Near(d.InputCancelTime, d.Duration, "Cancel threshold cannot exceed source content");
            d.inputCancelTime = -3f; Near(d.InputCancelTime, .01f, "Negative cancel threshold is safe");
            d.inputCancelTime = float.NaN; Near(d.InputCancelTime, d.Duration, "NaN threshold falls back to content duration");
            d.exitBlendDuration = -1; Near(d.ExitBlendDuration, 0, "Negative blend is clamped");
            d.exitBlendDuration = float.PositiveInfinity; Near(d.ExitBlendDuration, .15f, "Nonfinite blend has a finite fallback");
            d.sequence[0].sourceEnd = .25f;
            var second = new UltimateSequenceClip { clip = d.sequence[0].clip, sourceStart = .25f,
                sourceEnd = .75f, speed = 2f, rootMotion = false };
            second.hits.AddRange(new[] { Hit("boundary", .5f, 1), Hit("late", .6f, 1), Hit("trimmed out", .1f, 1),
                new UltimateHitField { time = .7f, enabled = false } });
            second.cues.AddRange(new[] { new UltimateCue { time = .5f }, new UltimateCue { time = .6f }, new UltimateCue { time = .1f } });
            d.sequence.Add(second); d.inputCancelTime = .375f;
            studio = ScriptableObject.CreateInstance<PlayerUltimateStudio>();
            Set(studio, "_definition", d); Set(studio, "_player", null); Set(studio, "_selectedClip", 1);
            string original = EditorJsonUtility.ToJson(d);
            object[] counts = { 0, 0 };
            typeof(PlayerUltimateStudio).GetMethod("CountEventsAfterInputCancel", Hidden).Invoke(studio, counts);
            Check((int)counts[0] == 1 && (int)counts[1] == 1,
                "Studio conditional event count respects local trim, speed, boundary, and disabled hits");
            Near((float)Invoke(studio, "SourceToSequenceTime", .5f), .375f,
                "Studio converts selected source position into the full sequence's cancel threshold");
            Invoke(studio, "SeekInputCancel");
            Check((int)Get(studio, "_selectedClip") == 1, "Cancel seek selects the correct sequence segment");
            Near((float)Get(studio, "_time"), .5f, "Cancel seek restores source-clip time");
            Near((float)Get(studio, "_sequenceTime"), .375f, "Cancel seek preserves full sequence time");
            Check(EditorJsonUtility.ToJson(d) == original, "Studio cancel inspection leaves authored data unchanged");
            d.useInputCancelTime = false;
            typeof(PlayerUltimateStudio).GetMethod("CountEventsAfterInputCancel", Hidden).Invoke(studio, counts);
            Check((int)counts[0] == 0 && (int)counts[1] == 0, "Disabled input cancel has no conditional omissions");
            return Pass("data");
        }
        finally { if (studio != null) Object.DestroyImmediate(studio); Object.DestroyImmediate(d); }
    }

    public static string RunRuntime()
    {
        RequirePlay(); _checks = 0;
        var d = Definition();
        GameObject fixture = null;
        PlayerCore player = null;
        PlayerSkillHitTestActor victim = null;
        VirtualCutsceneDefinition intro = null;
        float timeScale = Time.timeScale;
        bool audioPaused = AudioListener.pause;
        try
        {
            fixture = MakeFixture("Q immediate verification", new Vector3(24500, 1500, 24500));
            player = MakePlayer(fixture, d);
            var victimObject = new GameObject("Q isolated two-collider target");
            victimObject.transform.SetParent(fixture.transform, false);
            victimObject.transform.localPosition = new Vector3(0, 1, 2);
            victimObject.layer = 6;
            victimObject.AddComponent<SphereCollider>().radius = .2f;
            victimObject.AddComponent<BoxCollider>().size = Vector3.one * .2f;
            victim = victimObject.AddComponent<PlayerSkillHitTestActor>();
            Activate(fixture, player);
            var controller = player.Animator.runtimeAnimatorController;

            foreach (var field in typeof(PlayerStateMachine).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => typeof(PlayerStateBase).IsAssignableFrom(f.FieldType) && f.FieldType != typeof(PlayerUltimateState)))
            {
                Reset(player);
                player.AddSkillGauge(100);
                var state = (PlayerStateBase)field.GetValue(player.StateMachine);
                Check(player.StateMachine.Transition(state), "Fixture enters " + field.Name);
                Check(player.TryBeginUltimate(), "Q interrupts " + field.Name);
                Check(Current(player) == player.StateMachine.UltimateState && player.Ultimate.IsActive,
                    "Q owns the FSM after " + field.Name);
                Check(!player.TryBeginUltimate(), "Active Q cannot restart from " + field.Name);
                Check(player.DirectorContainer.Directors.Values.All(x => x == null || !x.playableGraph.IsValid()),
                    "Interrupted timelines release their graphs: " + field.Name);
            }

            Reset(player);
            var coordinator = Object.FindAnyObjectByType<HitstopCoordinator>();
            Check(coordinator != null, "Hitstop coordinator exists");
            HitstopCoordinator.Request(player, new IHitStopParticipant[] { victim }, 10);
            var pending = (IDictionary)Get(coordinator, "_pendingDurations");
            Check(pending.Contains(player) && pending.Contains(victim), "Old attack queues attacker and victim holds");
            Set(player.InputCollector, "_ultimateValue", true);
            Invoke(player, "Update");
            Set(player.InputCollector, "_ultimateValue", false);
            Check(player.Ultimate.IsActive && !pending.Contains(player), "Q input removes the pending attacker hold");
            Check(pending.Contains(victim), "Q keeps the old victim hold");
            HitstopCoordinator.Cancel(victim);

            Reset(player);
            Invoke(coordinator, "Hold", player, VirtualCutscenePlayback.CombatUnscaledTime + 10d);
            Invoke(coordinator, "Hold", victim, VirtualCutscenePlayback.CombatUnscaledTime + 10d);
            var active = (IDictionary)Get(coordinator, "_releaseTimes");
            Check(player.IsHitStopped && victim.IsHitStopped, "Fixture starts with active hitstop");
            Set(player.InputCollector, "_ultimateValue", true);
            Set(player.InputCollector, "_attackValue", true);
            Invoke(player, "Update");
            ClearInput(player);
            Check(player.Ultimate.IsActive && !player.IsHitStopped && !active.Contains(player),
                "Q input interrupts hitstop immediately even alongside attack input");
            Check(victim.IsHitStopped && active.Contains(victim), "Q leaves victim hitstop owned by coordinator");
            Invoke(coordinator, "Hold", player, VirtualCutscenePlayback.CombatUnscaledTime + 1d);
            Check(player.IsHitStopped, "A fresh Q hit can start a new attacker hold");
            HitstopCoordinator.Cancel(player); HitstopCoordinator.Cancel(victim);

            Reset(player); d.cooldown = 5;
            Check(player.TryBeginUltimate(), "Cooldown fixture cast begins");
            player.StateMachine.Transition(player.StateMachine.IdleState);
            Invoke(coordinator, "Hold", player, VirtualCutscenePlayback.CombatUnscaledTime + 10d);
            var stateBefore = Current(player);
            Check(!player.TryBeginUltimate() && Current(player) == stateBefore && player.IsHitStopped && active.Contains(player),
                "Rejected cooldown cast does not cancel state or hitstop");
            HitstopCoordinator.Cancel(player); d.cooldown = 0;

            Reset(player);
            d.useInputCancelTime = true; d.inputCancelTime = .5f; d.exitBlendDuration = .2f;
            var entry = d.sequence[0];
            entry.hits.AddRange(new[] { Hit("earlier", .2f, 7), Hit("boundary", .5f, 11), Hit("later", .6f, 13) });
            entry.cues.AddRange(new[] { new UltimateCue { name = "earlier", time = .2f },
                new UltimateCue { name = "boundary", time = .5f }, new UltimateCue { name = "later", time = .6f } });
            var cues = new List<string>();
            player.Ultimate.CueTriggered += cue => cues.Add(cue.name);
            int oldHits = victim.Hits.Count;
            Check(player.TryBeginUltimate(), "Input cancel fixture cast begins");
            d.inputCancelTime = .1f;
            player.Ultimate.Tick(.2f);
            Set(player.InputCollector, "_attackValue", true);
            player.StateMachine.UpdateTick(); ClearInput(player);
            Check(Current(player) == player.StateMachine.UltimateState && !player.Ultimate.CanCancelWithInput,
                "Attack before the cast's snapshotted threshold cannot interrupt Q");
            player.Ultimate.Tick(.5f - player.Ultimate.Time);
            Near(player.Ultimate.Time, .5f, "Cast preserves its snapshotted cancel threshold");
            ResolveStationary(player);
            Check(victim.Hits.Count == oldHits + 2 && victim.Hits[oldHits].DamageAmount == 7 && victim.Hits[oldHits + 1].DamageAmount == 11,
                "Crossed and boundary hits fire once across compound colliders");
            Check(cues.SequenceEqual(new[] { "earlier", "boundary" }), "Boundary cues still execute");
            Check(player.Ultimate.CanCancelWithInput && !player.Ultimate.IsComplete,
                "Reaching the threshold opens cancellation without completing Q");
            player.StateMachine.UpdateTick();
            Check(Current(player) == player.StateMachine.UltimateState && player.Ultimate.IsActive,
                "No input keeps Q running; the earlier attack press was not buffered");
            player.Ultimate.Tick(.7f - player.Ultimate.Time); ResolveStationary(player);
            Check(victim.Hits.Count == oldHits + 3 && victim.Hits[oldHits + 2].DamageAmount == 13 &&
                cues.SequenceEqual(new[] { "earlier", "boundary", "later" }),
                "No input preserves hits and cues after the cancel threshold");
            var bones = BoneTransforms(player.Animator);
            Vector3 rootPosition = player.transform.position;
            Set(player.InputCollector, "_moveValue", Vector2.up);
            player.StateMachine.UpdateTick();
            ClearInput(player);
            Check(Current(player) == player.StateMachine.RunStartState && !player.Ultimate.IsActive,
                "Movement cancels Q into RunStart without waiting for natural completion or blend");
            Check(player.Animator.runtimeAnimatorController == controller && player.Animator.fireEvents,
                "Completion restores controller and animation events");
            var blend = player.Animator.GetComponent<PlayerUltimateExitBlend>();
            Check(blend != null && blend.IsBlending, "Completion starts the configured outgoing pose blend");
            // Restoring the controller may reset its bones before LateUpdate.
            // Observe the first displayed exit pose, then ensure reevaluation
            // cannot replace it while blend progress is still zero this frame.
            Invoke(blend, "LateUpdate");
            Near(blend.Progress, 0f, "The first displayed exit pose starts at zero blend progress");
            var source = bones.Select(t => t.localRotation).ToArray();
            player.Animator.Update(.05f);
            var destination = bones.Select(t => t.localRotation).ToArray();
            Check(bones.Select((t, i) => Quaternion.Angle(source[i], destination[i])).Max() > .1f,
                "Fixture's next animation has a different pose");
            Invoke(blend, "LateUpdate");
            Check(bones.Select((t, i) => Quaternion.Angle(source[i], t.localRotation)).Max() < .1f,
                "Controller reevaluation preserves the first displayed Q exit pose");
            Near(player.transform.position, rootPosition, "Pose blending does not move the player root");
            player.BeginHitStop(); Invoke(blend, "Update"); Invoke(blend, "LateUpdate");
            Near(blend.Progress, 0f, "Hitstop freezes blend progress");
            player.EndHitStop();
            PlayerUltimateExitBlend.Cancel(player.Animator);
            Check(!blend.IsBlending, "Cancelling blend releases ownership");
            Check(bones.Select((t, i) => Quaternion.Angle(destination[i], t.localRotation)).Max() < .1f,
                "Cancelling blend restores the controller's unblended pose");

            foreach (string action in new[] { "_attackValue", "_dodgeValue", "_skillValue" })
            {
                Reset(player); d.inputCancelTime = .2f; player.AddSkillGauge(100); cues.Clear();
                Check(player.TryBeginUltimate(), "Next-action fixture begins: " + action);
                player.Ultimate.Tick(.2f); ResolveStationary(player);
                Set(player.InputCollector, action, true);
                player.StateMachine.UpdateTick(); ClearInput(player);
                PlayerStateBase expectedState = action == "_attackValue" ? (PlayerStateBase)player.StateMachine.BasicAttack1State :
                    action == "_dodgeValue" ? player.StateMachine.BackDodgeState : (PlayerStateBase)player.StateMachine.SkillState;
                Check(Current(player) == expectedState && !player.Ultimate.IsActive,
                    "The cancel window accepts the next action immediately: " + action);
                Check(blend.IsBlending, "Next action retains the outgoing Q blend: " + action);
                player.Ultimate.Tick(10); player.Ultimate.FixedTick();
                Check(!cues.Contains("later") && player.Mover.ConsumePendingDisplacement() == Vector3.zero,
                    "Cancelled Q cannot emit later cues or residual movement: " + action);
            }

            Reset(player); player.UseSkillGauge();
            var charges = (PlayerDodgeChargePool)Get(player, "_dodgeCharges");
            charges.TryConsume(10); charges.TryConsume(10);
            Check(player.TryBeginUltimate(), "Unavailable input fixture begins");
            player.Ultimate.Tick(.3f); ResolveStationary(player);
            foreach (string action in new[] { "_skillValue", "_dodgeValue", "_ultimateValue" })
            {
                Set(player.InputCollector, action, true); player.StateMachine.UpdateTick(); ClearInput(player);
                Check(Current(player) == player.StateMachine.UltimateState && player.Ultimate.IsActive,
                    "Unavailable input leaves Q active: " + action);
            }
            player.BeginHitStop();
            Check(!player.Ultimate.CanCancelWithInput, "Hitstop holds the open cancel window");
            player.EndHitStop();
            Set(player.InputCollector, "_dodgeValue", true); Set(player.InputCollector, "_attackValue", true);
            player.StateMachine.UpdateTick(); ClearInput(player);
            Check(Current(player) == player.StateMachine.BasicAttack1State,
                "Depleted dodge does not swallow a simultaneous valid attack");

            Reset(player); player.AddSkillGauge(100);
            Check(player.TryBeginUltimate(), "Simultaneous valid input fixture begins");
            player.Ultimate.Tick(.3f);
            Set(player.InputCollector, "_moveValue", Vector2.up);
            Set(player.InputCollector, "_dodgeValue", true); Set(player.InputCollector, "_attackValue", true);
            Set(player.InputCollector, "_skillValue", true); player.StateMachine.UpdateTick(); ClearInput(player);
            Check(Current(player) == player.StateMachine.FrontDodgeState && player.DodgeCharges == 1,
                "Valid dodge has priority and consumes exactly one charge");

            Reset(player); d.useInputCancelTime = false;
            Check(player.TryBeginUltimate(), "Disabled cancel fixture begins");
            player.Ultimate.Tick(.3f); Set(player.InputCollector, "_attackValue", true);
            player.StateMachine.UpdateTick(); ClearInput(player);
            Check(Current(player) == player.StateMachine.UltimateState && !player.Ultimate.CanCancelWithInput,
                "Disabling input cancel preserves the full sequence lock");
            d.useInputCancelTime = true;

            Reset(player); d.inputCancelTime = .2f;
            Check(player.TryBeginUltimate(), "Disable-cleanup fixture begins");
            player.Ultimate.Tick(1); ResolveStationary(player); player.StateMachine.UpdateTick();
            Check(blend.IsBlending && Current(player) == player.StateMachine.IdleState,
                "Without input, full sequence completion returns to Idle and starts the blend");
            player.enabled = false;
            Check(!blend.IsBlending && player.Animator.runtimeAnimatorController == controller,
                "Disabling PlayerCore clears blend and preserves controller");
            player.enabled = true;

            Reset(player); entry.hits.Clear(); entry.cues.Clear();
            entry.cues.Add(new UltimateCue { name = "combat-start", time = 0 });
            intro = Object.Instantiate(AssetDatabase.LoadAssetAtPath<VirtualCutsceneDefinition>("Assets/07_Datas/Player/UltimateCutscene.asset"));
            Check(intro != null && intro.Ready, "Authored intro asset is ready");
            intro.effects.Clear(); intro.sounds.Clear(); d.cutscene = intro;
            cues.Clear();
            Check(player.TryBeginUltimate() && player.Ultimate.IsIntroPlaying && VirtualCutscenePlayback.IsPlaying,
                "Q starts intro before combat");
            Check(cues.Count == 0 && player.Ultimate.Time == 0 && Time.timeScale == 0,
                "No combat cue advances while intro owns the world");
            Set(player.InputCollector, "_attackValue", true); player.StateMachine.UpdateTick(); ClearInput(player);
            Check(Current(player) == player.StateMachine.UltimateState && !player.Ultimate.CanCancelWithInput,
                "Intro cannot be cancelled by combat action input");
            VirtualCutscenePlayback.Active.Finish(true);
            Check(!VirtualCutscenePlayback.IsPlaying && !player.Ultimate.IsIntroPlaying && player.Ultimate.IsActive,
                "Finishing intro starts combat synchronously");
            Check(cues.SequenceEqual(new[] { "combat-start" }) && player.Animator.runtimeAnimatorController == null,
                "Same completion call installs combat pose and emits its zero-time cue");
            Near(Time.timeScale, timeScale, "Intro completion restores world time");
            Reset(player); cues.Clear();
            Check(player.TryBeginUltimate() && VirtualCutscenePlayback.IsPlaying, "Second intro starts cleanly");
            VirtualCutscenePlayback.Active.Finish(false);
            Check(!player.Ultimate.IsActive && cues.Count == 0 && player.Animator.runtimeAnimatorController == controller,
                "Cancelled intro does not start combat and restores animation ownership");
            return Pass("runtime");
        }
        finally
        {
            if (player != null) { VirtualCutscenePlayback.CancelFor(player); HitstopCoordinator.Cancel(player); }
            if (victim != null) HitstopCoordinator.Cancel(victim);
            if (fixture != null) Object.DestroyImmediate(fixture);
            if (intro != null) Object.DestroyImmediate(intro);
            Object.DestroyImmediate(d);
            Time.timeScale = timeScale; AudioListener.pause = audioPaused;
        }
    }

    public static string RunPhysics()
    {
        RequirePlay(); _checks = 0;
        var d = Definition(); GameObject fixture = null;
        var scene = SceneManager.CreateScene("Q endpoint local physics " + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        try
        {
            d.useInputCancelTime = true; d.inputCancelTime = .5f;
            var entry = d.sequence[0]; entry.rootMotion = true; entry.applyRootHeight = true;
            entry.rootSamples.AddRange(new[] { new UltimateRootSample { time = 0 },
                new UltimateRootSample { time = 1, position = new Vector3(0, 1, 4) } });
            fixture = MakeFixture("Q endpoint physical player", new Vector3(18000, 2500, 18000));
            SceneManager.MoveGameObjectToScene(fixture, scene);
            var player = MakePlayer(fixture, d); Activate(fixture, player);
            var body = player.GetComponent<Rigidbody>(); body.interpolation = RigidbodyInterpolation.None;
            Vector3 origin = body.position;
            Check(player.TryBeginUltimate(), "Moving Q begins");
            Quaternion facing = player.Ultimate.Facing;
            player.Ultimate.Tick(.5f);
            Check(player.Ultimate.CanCancelWithInput && !player.Ultimate.IsComplete,
                "Moving Q opens input cancel before natural completion");
            Set(player.InputCollector, "_attackValue", true); player.StateMachine.UpdateTick(); ClearInput(player);
            Invoke(player.Mover, "FixedUpdate"); scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
            Check(!player.Ultimate.IsActive && !player.Mover.UsesRootMotionHeight && Current(player) == player.StateMachine.BasicAttack1State,
                "Early input cancel cleans up root-height ownership immediately");
            Check(Vector3.Distance(body.position, origin) < .03f, "Cancelled unconsumed Q motion does not move the next action");
            Reset(player); body.position = origin; body.linearVelocity = Vector3.zero;
            Check(player.TryBeginUltimate(), "Natural moving Q begins");
            facing = player.Ultimate.Facing;
            player.Ultimate.Tick(10);
            player.Ultimate.FixedTick();
            Check(!player.Ultimate.IsComplete, "Final physical displacement must resolve before completion");
            Invoke(player.Mover, "FixedUpdate"); scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
            Vector3 expected = origin + facing * new Vector3(0, 1, 4);
            Check(Vector3.Distance(body.position, expected) < .03f, "Without input, physics receives the complete root displacement");
            player.Ultimate.FixedTick();
            Check(player.Ultimate.IsComplete, "Final hit-resolution step permits completion");
            player.StateMachine.UpdateTick();
            Check(!player.Ultimate.IsActive && !player.Mover.UsesRootMotionHeight,
                "Natural completion restores gameplay and releases authored root-height mode");
            Check(Vector3.Distance(body.position, expected) < .03f, "Exit does not erase final physics position");
            return Pass("local physics");
        }
        finally
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            Object.DestroyImmediate(d);
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }
    }

    private static PlayerUltimateDefinition Definition()
    {
        var d = ScriptableObject.CreateInstance<PlayerUltimateDefinition>();
        d.cooldown = 0; d.targetLayers = 1 << 6; d.useInputCancelTime = false;
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null) throw new InvalidOperationException("Missing verification clip: " + ClipPath);
        d.sequence.Add(new UltimateSequenceClip { clip = clip, sourceStart = 0, sourceEnd = 1, rootMotion = false });
        return d;
    }
    private static GameObject MakeFixture(string name, Vector3 position)
    {
        var go = new GameObject(name); go.SetActive(false); go.transform.position = position; return go;
    }
    private static PlayerCore MakePlayer(GameObject parent, PlayerUltimateDefinition definition)
    {
        var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath), parent.transform, false);
        go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var source in go.GetComponentsInChildren<AudioSource>(true)) { source.mute = true; source.playOnAwake = false; }
        var player = go.GetComponent<PlayerCore>();
        Set(player, "_ultimateDefinition", definition);
        var cameraObject = new GameObject("Q verification reference camera"); cameraObject.transform.SetParent(parent.transform, false);
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; Set(player, "_mainCamera", camera);
        return player;
    }
    private static void Activate(GameObject fixture, PlayerCore player)
    {
        fixture.SetActive(true); Physics.SyncTransforms(); player.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        player.StateMachine.InitStateMachine(player.StateMachine.IdleState);
    }
    private static void Reset(PlayerCore player)
    {
        HitstopCoordinator.Cancel(player); ClearInput(player);
        player.StateMachine.Transition(player.StateMachine.IdleState);
        player.Ultimate.TickCooldown(10000); PlayerUltimateExitBlend.Cancel(player.Animator);
        ((PlayerDodgeChargePool)Get(player, "_dodgeCharges")).Tick(10000);
        player.Rotator.RotateImmediately(Vector3.forward);
    }
    private static void ClearInput(PlayerCore player)
    {
        foreach (string field in new[] { "_ultimateValue", "_attackValue", "_skillValue", "_dodgeValue" }) Set(player.InputCollector, field, false);
        Set(player.InputCollector, "_moveValue", Vector2.zero);
    }
    private static void ResolveStationary(PlayerCore player)
    {
        player.Ultimate.FixedTick();
        Near(player.Mover.ConsumePendingDisplacement(), Vector3.zero, "Stationary sequence queues no movement");
        player.Ultimate.FixedTick();
    }
    private static Transform[] BoneTransforms(Animator animator)
        => Enumerable.Range(0, (int)HumanBodyBones.LastBone).Select(i => animator.GetBoneTransform((HumanBodyBones)i)).Where(t => t != null).ToArray();
    private static UltimateHitField Hit(string name, float time, float damage)
        => new UltimateHitField { name = name, time = time, damage = damage, hitStopFrames = 0, position = new Vector3(0, 1, 2), size = Vector3.one * 2 };
    private static PlayerStateBase Current(PlayerCore player) => (PlayerStateBase)Get(player.StateMachine, "_currentState");
    private static object Get(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] args)
        => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
    private static void RequirePlay() { if (!Application.isPlaying) throw new InvalidOperationException("Run in Play Mode."); }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Q verification: " + message); _checks++; }
    private static void Near(float a, float b, string message) => Check(Mathf.Abs(a - b) < .001f, message + " (" + a + " vs " + b + ")");
    private static void Near(Vector3 a, Vector3 b, string message) => Check(Vector3.Distance(a, b) < .003f, message + " (" + a + " vs " + b + ")");
    private static string Pass(string name) => "QImmediateVerification " + name + ": PASS (" + _checks + " checks).";
}
#endif
