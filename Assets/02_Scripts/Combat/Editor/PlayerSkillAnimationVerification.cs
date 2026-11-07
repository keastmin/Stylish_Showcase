#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

/// <summary>Regression for the normal Animator replacing a manually sampled E pose later in the frame.</summary>
public static class PlayerSkillAnimationVerification
{
    private const string PlayerPath = "Assets/08_Prefabs/Combat/Player.prefab";
    private const string Report = "Temp/PlayerSkillAnimationVerification.result";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [MenuItem("Tools/Character/Player/Validate E Skill Animation (Play Mode)")]
    private static void RuntimeMenu() => Debug.Log(RunRuntime());

    public static string RunRuntime()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run E animation verification in Play Mode.");
        _checks = 0;
        GameObject fixture = null;
        PlayerCore player = null, reference = null;
        PlayerSkillRuntime runtime = null;
        PlayerSkillDefinition definition = null;
        PlayerUltimateDefinition ultimate = null;
        PlayableDirector referenceDirector = null;
        TimelineAsset referenceTimeline = null;
        var referenceTracks = new System.Collections.Generic.List<Object>();
        object referenceOwner = new object();
        RuntimeAnimatorController referenceController = null;
        bool referenceRootMotion = false;
        try
        {
            fixture = new GameObject("E isolated animation verification");
            fixture.SetActive(false); fixture.transform.position = new Vector3(31000, 2000, 31000);
            player = MakePlayer(fixture, "E animation subject", Vector3.zero);
            reference = MakePlayer(fixture, "E independent Timeline reference", Vector3.right * 30f);
            fixture.SetActive(true);
            Activate(player); Activate(reference);
            var director = player.DirectorContainer.Directors[DirectorID.Skill];
            referenceDirector = reference.DirectorContainer.Directors[DirectorID.Skill];
            Check(director.playableAsset != null && director.playableAsset == referenceDirector.playableAsset,
                "Subject and independent reference use the authored E Timeline");
            string sourceBefore = EditorJsonUtility.ToJson(director.playableAsset);
            var source = director.GetComponent<PlayerSkillSequenceSettings>().Definition;
            definition = source != null ? Object.Instantiate(source) : ScriptableObject.CreateInstance<PlayerSkillDefinition>();
            definition.duration = Mathf.Max(2f, (float)director.playableAsset.duration);
            definition.playbackSpeed = 1f; definition.playPresentation = true; definition.recoveryDuration = 0f;
            definition.useInputCancelTime = false;
            definition.motion.enabled = false; definition.hits.Clear(); definition.impactFrames.Clear(); definition.screenEffects.Clear();

            var animator = player.Animator;
            var baseline = new AnimatorBaseline(animator, director);
            var bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone).Select(i => (HumanBodyBones)i)
                .Where(bone => animator.GetBoneTransform(bone) != null && reference.Animator.GetBoneTransform(bone) != null).ToArray();
            Check(bones.Length >= 40, "The comparison covers the humanoid skeleton");
            animator.Update(0f);
            Quaternion[] idle = Pose(animator, bones);
            runtime = new PlayerSkillRuntime(player, director);

            referenceController = reference.Animator.runtimeAnimatorController;
            referenceRootMotion = reference.Animator.applyRootMotion;
            Vector3 referenceLocalPosition = reference.Animator.transform.localPosition;
            Quaternion referenceLocalRotation = reference.Animator.transform.localRotation;
            reference.Animator.runtimeAnimatorController = null;
            reference.Animator.applyRootMotion = false;
            reference.Animator.transform.SetLocalPositionAndRotation(referenceLocalPosition, referenceLocalRotation);
            referenceDirector.GetComponent<PlayerSkillAttackReceiver>().SetRuntimeOwner(referenceOwner);
            // A full-weight animation-only copy is independent of the live
            // Timeline's terminal ease. A fade to its cached/default pose used
            // to be invisible to checks that sampled only the attack's middle.
            referenceTimeline = ScriptableObject.CreateInstance<TimelineAsset>();
            PlayerSkillDataMigration.CopyAnimationTracks((TimelineAsset)referenceDirector.playableAsset,
                referenceDirector, reference.Animator, referenceTimeline, referenceDirector, reference.Animator, referenceTracks);
            foreach (var track in PlayerSkillStudio.AllTracks(referenceTimeline).OfType<AnimationTrack>())
            {
                var last = track.GetClips().OrderBy(clip => clip.end).LastOrDefault();
                if (last != null) last.easeOutDuration = 0d;
            }
            referenceDirector.playableAsset = referenceTimeline;
            referenceDirector.timeUpdateMode = DirectorUpdateMode.Manual;
            referenceDirector.time = 0d; referenceDirector.Play();

            Check(runtime.Begin(definition), "E presentation cast begins");
            Check(animator.runtimeAnimatorController == null, "E detaches the ordinary Animator controller while it owns presentation");
            Check(!animator.applyRootMotion, "E disables Animator root motion while authored movement owns translation");
            Quaternion[] previous = null;
            foreach (float time in new[] { .2f, .65f, .9f, 1f, 1.3f, 2.58f, 2.62f, 2.66f, 2.68f })
            {
                runtime.Tick(time - runtime.Time);
                Quaternion[] sampled = Pose(animator, bones);
                if (time <= 1.3f)
                    Check(MaxAngle(idle, sampled) > .5f, "E differs from Idle at " + time + " seconds");
                animator.Update(0f);
                Check(MaxAngle(sampled, Pose(animator, bones)) < .1f,
                    "Animator.Update(0) preserves the E pose at " + time + " seconds");
                referenceDirector.time = time; referenceDirector.Evaluate();
                Check(MaxAngle(sampled, Pose(reference.Animator, bones)) < .2f,
                    "E pose matches the independent Timeline reference at " + time + " seconds");
                if (previous != null && (Mathf.Approximately(time, .65f) || Mathf.Approximately(time, .9f)))
                    Check(MaxAngle(previous, sampled) > .5f, "E progresses into a different attack pose at " + time + " seconds");
                previous = sampled;
                Near(animator.transform.localPosition, baseline.position, "Manual sampling preserves model local translation at " + time);
                Angle(animator.transform.localRotation, baseline.rotation, "Manual sampling preserves model local rotation at " + time);
            }
            runtime.Tick(1f);
            Check(runtime.IsActive && !runtime.IsComplete && runtime.OwnsModelAnimation,
                "The content endpoint keeps E's pose while the final motor step is unresolved");
            Quaternion[] heldEnd = Pose(animator, bones);
            animator.Update(0f);
            Check(MaxAngle(heldEnd, Pose(animator, bones)) < .1f,
                "Ordinary Animator evaluation cannot expose Idle before the final motor step resolves");
            Resolve(runtime, player);
            Check(runtime.IsComplete && runtime.OwnsModelAnimation, "Resolved completion retains the outgoing pose until FSM exit");
            runtime.Stop();
            Restored(animator, director, baseline, "Stop");
            Check(!runtime.IsActive && runtime.ActiveDefinition == null, "Stop releases E presentation and its data snapshot");

            Check(runtime.Begin(definition), "E can be cast again after Stop"); runtime.Tick(.2f);
            Check(runtime.Begin(definition), "A repeated Begin safely replaces the previous cast"); runtime.Tick(.65f);
            Check(animator.runtimeAnimatorController == null, "Recast retains a single presentation owner");
            Quaternion[] recastPose = Pose(animator, bones); animator.Update(0f);
            Check(MaxAngle(recastPose, Pose(animator, bones)) < .1f, "Recast pose survives ordinary Animator reevaluation");
            runtime.Stop(); Restored(animator, director, baseline, "Recast Stop");

            definition.duration = .5f; definition.recoveryDuration = 2f;
            runtime.Begin(definition); runtime.Tick(.6f); Resolve(runtime, player);
            Check(runtime.IsActive && !runtime.IsComplete, "E retains its authored recovery after presentation ends");
            Check(runtime.OwnsModelAnimation && animator.runtimeAnimatorController == null,
                "Authored recovery holds the final E pose instead of restoring the controller early");
            Quaternion[] recoveryPose = Pose(animator, bones);
            runtime.Tick(.1f); animator.Update(0f);
            Check(MaxAngle(recoveryPose, Pose(animator, bones)) < .1f, "Recovery retains its pose across subsequent updates");
            runtime.Stop();
            Restored(animator, director, baseline, "Stop after recovery");

            definition.duration = Mathf.Max(2f, (float)director.playableAsset.duration);
            definition.recoveryDuration = 0f; definition.useInputCancelTime = true; definition.inputCancelTime = 0f;
            var settings = director.GetComponent<PlayerSkillSequenceSettings>(); settings.Definition = definition;
            BeginState(player); var stateRuntime = player.StateMachine.SkillState.Runtime;
            stateRuntime.Tick(.2f);
            Check(animator.runtimeAnimatorController == null, "FSM E owns presentation before early cancellation");
            Set(player.InputCollector, "_attackValue", true); player.StateMachine.UpdateTick(); ClearInput(player);
            Check(Current(player) == player.StateMachine.BasicAttack1State && !stateRuntime.IsActive,
                "Early accepted input releases E before entering the next action");
            Restored(animator, director, baseline, "Early input cancellation");
            referenceDirector.time = stateRuntime.Time; referenceDirector.Evaluate();
            VerifyHandoff(player, bones, Pose(reference.Animator, bones), definition.ExitBlendDuration, "Early E-to-attack cancellation");
            Reset(player);

            definition.useInputCancelTime = false;
            foreach (string destination in new[] { "Idle", "Run", "Attack", "Dodge" })
            {
                BeginState(player); stateRuntime = player.StateMachine.SkillState.Runtime;
                stateRuntime.Tick(definition.Duration); Resolve(stateRuntime, player);
                if (destination == "Run") Set(player.InputCollector, "_moveValue", Vector2.up);
                if (destination == "Attack") Set(player.InputCollector, "_attackValue", true);
                if (destination == "Dodge") Set(player.InputCollector, "_dodgeValue", true);
                player.StateMachine.UpdateTick(); ClearInput(player);
                PlayerStateBase expected = destination == "Run" ? player.StateMachine.RunStartState :
                    destination == "Attack" ? player.StateMachine.BasicAttack1State :
                    destination == "Dodge" ? player.StateMachine.BackDodgeState : player.StateMachine.IdleState;
                Check(Current(player) == expected && !stateRuntime.IsActive,
                    "Natural E completion immediately enters " + destination + " without an intervening Idle tick");
                Restored(animator, director, baseline, "Natural exit to " + destination);
                referenceDirector.time = Math.Min(definition.Duration, referenceTimeline.duration - .000001d);
                referenceDirector.Evaluate();
                VerifyHandoff(player, bones, Pose(reference.Animator, bones), definition.ExitBlendDuration, "Natural E-to-" + destination);
                Reset(player);
            }

            if (player.UltimateDefinition != null)
            {
                ultimate = Object.Instantiate(player.UltimateDefinition); ultimate.cutscene = null; ultimate.cooldown = 0f;
                Set(player, "_ultimateDefinition", ultimate); player.Ultimate.TickCooldown(10000f);
                BeginState(player); stateRuntime = player.StateMachine.SkillState.Runtime; stateRuntime.Tick(.2f);
                Check(player.TryBeginUltimate(), "Q interrupts an E presentation cast");
                Check(!stateRuntime.IsActive && director.state != PlayState.Playing, "Q releases the E presentation graph");
                player.Ultimate.Stop();
                Check(animator.runtimeAnimatorController == baseline.controller && animator.applyRootMotion == baseline.rootMotion,
                    "Q receives and restores the original controller and root-motion baseline after interrupting E");
                Reset(player);

                // Exercise the real Q -> E transition. The outgoing Q blend must
                // capture each new raw E pose, not restore its previous raw target.
                ultimate.useInputCancelTime = true; ultimate.inputCancelTime = 0f; ultimate.exitBlendDuration = 1f;
                player.AddSkillGauge(100f); player.Ultimate.TickCooldown(10000f);
                Check(player.TryBeginUltimate(), "Q-to-E blend fixture begins Q");
                player.Ultimate.Tick(.2f);
                Set(player.InputCollector, "_skillValue", true); player.StateMachine.UpdateTick(); ClearInput(player);
                stateRuntime = player.StateMachine.SkillState.Runtime;
                var blend = animator.GetComponent<PlayerUltimateExitBlend>();
                Check(player.StateMachine.IsSkillActive && stateRuntime.IsActive && stateRuntime.OwnsModelAnimation,
                    "Q connects into E with E owning its manually evaluated animation");
                Check(blend != null && blend.IsBlending, "Q-to-E retains the outgoing Q pose blend");
                stateRuntime.Tick(.2f - stateRuntime.Time);
                Quaternion[] firstRaw = Pose(animator, bones);
                Invoke(blend, "Update");
                Check(MaxAngle(firstRaw, Pose(animator, bones)) < .1f, "Q blend Update preserves E's first raw pose");
                Set(blend, "_elapsed", 0f); Set(blend, "_startFrame", Time.frameCount); Invoke(blend, "LateUpdate");
                Quaternion[] blendSource = Pose(animator, bones);
                Check(MaxAngle(blendSource, firstRaw) > .5f, "The outgoing Q source pose differs from the new E target");
                float[] blendTimes = { .65f, .9f, 1.3f };
                float[] progress = { .25f, .5f, .75f };
                Quaternion[] finalRaw = null;
                for (int i = 0; i < blendTimes.Length; i++)
                {
                    stateRuntime.Tick(blendTimes[i] - stateRuntime.Time);
                    Quaternion[] raw = Pose(animator, bones); finalRaw = raw;
                    Invoke(blend, "Update");
                    Check(MaxAngle(raw, Pose(animator, bones)) < .1f,
                        "Q blend Update keeps the current raw E pose at " + blendTimes[i] + " seconds");
                    Set(blend, "_elapsed", progress[i]); Set(blend, "_startFrame", Time.frameCount);
                    Invoke(blend, "LateUpdate");
                    float weight = Mathf.SmoothStep(0f, 1f, progress[i]);
                    Quaternion[] expected = blendSource.Select((rotation, index) => Quaternion.Slerp(rotation, raw[index], weight)).ToArray();
                    Check(MaxAngle(expected, Pose(animator, bones)) < .1f,
                        "Q-to-E LateUpdate blends toward the current E pose at progress " + progress[i]);
                    Check(blend.IsBlending, "Outgoing Q blending remains active at progress " + progress[i]);
                }
                PlayerUltimateExitBlend.Cancel(animator);
                Check(MaxAngle(finalRaw, Pose(animator, bones)) < .1f, "Ending Q-to-E blending restores the latest raw E target");
                Reset(player); Restored(animator, director, baseline, "Q-to-E blend cleanup");
            }

            BeginState(player); stateRuntime = player.StateMachine.SkillState.Runtime; stateRuntime.Tick(.2f);
            player.enabled = false;
            Check(!stateRuntime.IsActive, "Disabling PlayerCore releases active E presentation ownership");
            Restored(animator, director, baseline, "PlayerCore disable");
            player.enabled = true; Reset(player);

            definition.playPresentation = false;
            runtime.Begin(definition); runtime.Tick(.2f);
            Check(animator.runtimeAnimatorController == baseline.controller && animator.applyRootMotion == baseline.rootMotion,
                "Gameplay without presentation preserves the ordinary controller and root-motion setting");
            runtime.Stop(); Restored(animator, director, baseline, "Presentation-disabled Stop");
            Check(EditorJsonUtility.ToJson(director.playableAsset) == sourceBefore, "Pose and lifecycle verification leaves the authored Timeline unchanged");
            string result = "PlayerSkillAnimationVerification runtime: PASS (" + _checks + " checks, " + bones.Length + " humanoid bones).";
            File.WriteAllText(Report, result + "\n");
            return result;
        }
        finally
        {
            runtime?.Stop();
            if (player != null) { player.StateMachine.SkillState.StopRuntime(); VirtualCutscenePlayback.CancelFor(player); HitstopCoordinator.Cancel(player); }
            if (referenceDirector != null)
            {
                referenceDirector.Stop(); referenceDirector.GetComponent<PlayerSkillAttackReceiver>().ReleaseRuntimeOwner(referenceOwner);
            }
            if (reference != null && reference.Animator != null)
            {
                reference.Animator.runtimeAnimatorController = referenceController;
                reference.Animator.applyRootMotion = referenceRootMotion;
                HitstopCoordinator.Cancel(reference);
            }
            if (fixture != null) Object.DestroyImmediate(fixture);
            foreach (Object track in referenceTracks) if (track != null) Object.DestroyImmediate(track);
            if (referenceTimeline != null) Object.DestroyImmediate(referenceTimeline);
            if (definition != null) Object.DestroyImmediate(definition);
            if (ultimate != null) Object.DestroyImmediate(ultimate);
        }
    }

    private sealed class AnimatorBaseline
    {
        public readonly RuntimeAnimatorController controller;
        public readonly bool rootMotion;
        public readonly Vector3 position;
        public readonly Quaternion rotation;
        public readonly DirectorUpdateMode updateMode;
        public AnimatorBaseline(Animator animator, PlayableDirector director)
        {
            controller = animator.runtimeAnimatorController; rootMotion = animator.applyRootMotion;
            position = animator.transform.localPosition; rotation = animator.transform.localRotation;
            updateMode = director.timeUpdateMode;
        }
    }
    private static void VerifyHandoff(PlayerCore player, HumanBodyBones[] bones, Quaternion[] source, float duration, string reason)
    {
        Animator animator = player.Animator;
        var blend = animator.GetComponent<PlayerUltimateExitBlend>();
        Check(blend != null && blend.IsBlending, reason + " captures the outgoing E pose");
        Vector3 position = animator.transform.localPosition;
        animator.Update(0f);
        Set(blend, "_elapsed", 0f); Set(blend, "_startFrame", Time.frameCount);
        Invoke(blend, "LateUpdate");
        Check(MaxAngle(source, Pose(animator, bones)) < .2f,
            reason + " starts from E's actual last pose after controller restoration");
        foreach (float progress in new[] { .25f, .5f, .75f, 1f })
        {
            Invoke(blend, "Update"); animator.Update(.03f);
            Quaternion[] target = Pose(animator, bones);
            Set(blend, "_elapsed", duration * progress); Set(blend, "_startFrame", Time.frameCount);
            Invoke(blend, "LateUpdate");
            float weight = Mathf.SmoothStep(0f, 1f, progress);
            Quaternion[] expected = source.Select((rotation, i) => Quaternion.Slerp(rotation, target[i], weight)).ToArray();
            Check(MaxAngle(expected, Pose(animator, bones)) < .2f,
                reason + " follows the current destination pose at " + progress);
            if (progress == .5f)
            {
                Quaternion[] held = Pose(animator, bones);
                player.BeginHitStop();
                try { animator.Update(0f); Invoke(blend, "LateUpdate"); }
                finally { player.EndHitStop(); }
                Check(MaxAngle(held, Pose(animator, bones)) < .1f, reason + " freezes the displayed blend during hitstop");
            }
        }
        Check(!blend.IsBlending, reason + " releases all pose overrides at completion");
        Near(animator.transform.localPosition, position, reason + " adds no model translation");
    }
    private static void Restored(Animator animator, PlayableDirector director, AnimatorBaseline baseline, string reason)
    {
        Check(animator.runtimeAnimatorController == baseline.controller, reason + " restores the original Animator controller");
        Check(animator.applyRootMotion == baseline.rootMotion, reason + " restores the original root-motion flag");
        Near(animator.transform.localPosition, baseline.position, reason + " restores the model local position");
        Angle(animator.transform.localRotation, baseline.rotation, reason + " restores the model local rotation");
        // The runtime keeps Manual timing until its full gameplay lifetime ends, including recovery.
        Check(director.state != PlayState.Playing, reason + " releases the presentation graph");
    }
    private static Quaternion[] Pose(Animator animator, HumanBodyBones[] bones)
        => bones.Select(bone => animator.GetBoneTransform(bone).localRotation).ToArray();
    private static float MaxAngle(Quaternion[] from, Quaternion[] to)
        => from.Select((rotation, index) => Quaternion.Angle(rotation, to[index])).Max();
    private static PlayerCore MakePlayer(GameObject parent, string name, Vector3 localPosition)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        if (prefab == null) throw new InvalidOperationException("Missing animation verification player prefab");
        var go = Object.Instantiate(prefab, parent.transform, false); go.name = name;
        go.transform.SetLocalPositionAndRotation(localPosition, Quaternion.identity);
        foreach (var source in go.GetComponentsInChildren<AudioSource>(true)) { source.mute = true; source.playOnAwake = false; }
        var player = go.GetComponent<PlayerCore>();
        var cameraObject = new GameObject(name + " reference camera"); cameraObject.transform.SetParent(parent.transform, false);
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; Set(player, "_mainCamera", camera);
        return player;
    }
    private static void Activate(PlayerCore player)
    {
        player.InputCollector.enabled = false; ClearInput(player); player.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        player.StateMachine.InitStateMachine(player.StateMachine.IdleState); player.Animator.Update(0f);
    }
    private static void BeginState(PlayerCore player)
    {
        player.AddSkillGauge(100f);
        Check(player.StateMachine.Transition(player.StateMachine.SkillState) && player.StateMachine.SkillState.Runtime.IsActive,
            "Fixture enters E through the actual FSM");
    }
    private static void Resolve(PlayerSkillRuntime runtime, PlayerCore player)
    {
        runtime.FixedTick(); player.Mover.ConsumePendingDisplacement();
        runtime.FixedTick(); player.Mover.ConsumePendingDisplacement();
    }
    private static void Reset(PlayerCore player)
    {
        HitstopCoordinator.Cancel(player); ClearInput(player); player.StateMachine.Transition(player.StateMachine.IdleState);
        player.Ultimate.TickCooldown(10000f); PlayerUltimateExitBlend.Cancel(player.Animator); player.Mover.ConsumePendingDisplacement();
    }
    private static void ClearInput(PlayerCore player)
    {
        foreach (string field in new[] { "_ultimateValue", "_attackValue", "_skillValue", "_dodgeValue" }) Set(player.InputCollector, field, false);
        Set(player.InputCollector, "_moveValue", Vector2.zero);
    }
    private static PlayerStateBase Current(PlayerCore player)
        => (PlayerStateBase)player.StateMachine.GetType().GetField("_currentState", Hidden).GetValue(player.StateMachine);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    private static void Invoke(object target, string name) => target.GetType().GetMethod(name, Hidden).Invoke(target, null);
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("E animation verification: " + message);
        _checks++;
    }
    private static void Near(Vector3 a, Vector3 b, string message) => Check(Vector3.Distance(a, b) < .001f, message + " (" + a + " vs " + b + ")");
    private static void Angle(Quaternion a, Quaternion b, string message) => Check(Quaternion.Angle(a, b) < .1f, message);
}
#endif
