using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

/// <summary>Small, isolated integration checks for the E prototype. No assets or scenes are saved.</summary>
public static class PlayerUltimateVerification
{
    private const string PlayerPath = "Assets/08_Prefabs/Combat/Player.prefab";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [MenuItem("Tools/Character/Player/Validate Ultimate Assets and Geometry")]
    public static void ValidateAssetsMenu() => Debug.Log(RunAssetAndGeometry());

    public static string RunAssetAndGeometry()
    {
        _checks = 0;
        var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Input Actions/PlayerInputAction.inputactions");
        Check(input != null, "Player input asset is present.");
        InputAction skill = input.FindAction("Player/Skill", true);
        InputAction ultimate = input.FindAction("Player/Ultimate", true);
        Check(skill.bindings.Any(b => b.path == "<Keyboard>/q"), "The existing skill uses Q.");
        Check(!skill.bindings.Any(b => b.path == "<Keyboard>/e"), "E no longer also invokes the old skill.");
        Check(ultimate.bindings.Any(b => b.path == "<Keyboard>/e"), "Ultimate uses E.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        Check(prefab != null, "Player prefab is present.");
        var core = prefab.GetComponent<PlayerCore>();
        Check(core != null && core.UltimateDefinition != null, "Player prefab references the editable Ultimate asset.");
        var definition = core.UltimateDefinition;
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/03_Animations/Player/Clips/Attack/Ultimate.anim");
        Check(definition.clip == clip && clip != null, "Runtime uses the supplied Ultimate animation clip.");
        Check(definition.hits.Any(h => h != null && h.enabled), "At least one authored damage field is active.");
        foreach (UltimateHitField hit in definition.hits.Where(h => h != null))
            Check(hit.time >= 0 && hit.time <= definition.clip.length + .0001f,
                "Authored legacy hit markers stay inside the clip.");
        VerifyMarkers();
        VerifyGeometry();
        return "PlayerUltimateVerification assets/geometry: PASS (" + _checks + " checks). Q/E bindings, clip/data wiring, single-frame markers, box/sphere rotation, offset, edge and height checked; no asset saved.";
    }

    [MenuItem("Tools/Character/Player/Validate Ultimate Marker Timing")]
    public static void ValidateMarkerTimingMenu() => Debug.Log(RunMarkerTiming());

    public static string RunMarkerTiming()
    {
        _checks = 0;
        VerifyMarkers();
        return "PlayerUltimateVerification marker timing: PASS (" + _checks + " checks). Point crossing, zero/end boundaries, disabled markers and clip-frame preview equality.";
    }

    private static void VerifyMarkers()
    {
        var hit = new UltimateHitField { time = .2f };
        Check(UltimateFieldGeometry.Crossed(hit, 0f, .4f), "A long frame still crosses the single hit marker.");
        Check(!UltimateFieldGeometry.Crossed(hit, 0f, .19f), "Frames before a hit cannot activate it.");
        Check(!UltimateFieldGeometry.Crossed(hit, .22f, .3f), "Frames after a hit cannot activate it.");
        Check(!UltimateFieldGeometry.Crossed(hit, .3f, .1f), "Reverse preview scrubbing is not a runtime hit.");
        Check(UltimateFieldGeometry.Crossed(hit, .19f, .2f), "The destination endpoint includes its marker exactly once.");
        Check(!UltimateFieldGeometry.Crossed(hit, .2f, .21f), "The next interval excludes an already crossed marker at its start.");
        Check(!UltimateFieldGeometry.Crossed(hit, .2f, .2f), "A stationary clock never retriggers a marker.");
        Check(UltimateFieldGeometry.IsActive(hit, .2f), "The exact marker frame is visible as active.");
        Check(UltimateFieldGeometry.IsActive(hit, .201f, 60f), "A sample in the same 60fps frame keeps the field visible.");
        Check(!UltimateFieldGeometry.IsActive(hit, .2f - 1f / 60f, 60f), "The preceding frame hides the field.");
        Check(!UltimateFieldGeometry.IsActive(hit, .2f + 1f / 60f, 60f), "The following frame hides the field.");
        Check(UltimateFieldGeometry.IsActive(hit, .201f, 30f) &&
              !UltimateFieldGeometry.IsActive(hit, .2f + 1f / 30f, 30f),
            "Preview visibility uses the source clip frame rate.");
        hit.enabled = false;
        Check(!UltimateFieldGeometry.Crossed(hit, 0f, 1f) && !UltimateFieldGeometry.IsActive(hit, .205f),
            "Disabled fields are disabled for timing and preview alike.");
        hit.enabled = true;
        hit.time = 0f;
        Check(UltimateFieldGeometry.Crossed(hit, -.0001f, 0f) && !UltimateFieldGeometry.Crossed(hit, 0f, .1f),
            "A zero-time marker belongs to cast entry, not the first later update.");
        hit.time = 1f;
        Check(UltimateFieldGeometry.Crossed(hit, .99f, 1f) && !UltimateFieldGeometry.Crossed(hit, 1f, 1.1f),
            "A clip-end marker belongs to the completing interval exactly once.");
    }

    private static void VerifyGeometry()
    {
        GameObject probe = new GameObject("Ultimate geometry verification (temporary)");
        probe.hideFlags = HideFlags.HideAndDontSave;
        probe.layer = 6;
        var collider = probe.AddComponent<SphereCollider>();
        collider.radius = .025f;
        collider.isTrigger = true;
        try
        {
            Vector3 origin = new Vector3(16000, 1000, 16000);
            Quaternion facing = Quaternion.Euler(0, 90, 0);
            var field = new UltimateHitField
            {
                position = new Vector3(1, 2, 3), euler = new Vector3(20, 35, 10),
                size = new Vector3(2, 4, 6), radius = 2, shape = UltimateFieldShape.Box
            };
            UltimateFieldPose pose = UltimateFieldGeometry.Evaluate(field, origin, facing);
            Near(pose.center, origin + facing * field.position, "Field offsets use the cast-facing frame.");
            Check(Quaternion.Angle(pose.rotation, facing * Quaternion.Euler(field.euler)) < .001f,
                "Field local Euler angles combine with player facing.");
            Near(pose.halfExtents, new Vector3(1, 2, 3), "Box sizes are full widths, not half widths.");
            Probe(pose.center, true, "Box center overlaps.");
            Probe(pose.center + pose.rotation * new Vector3(.9f, 0, 0), true, "Inside the rotated box edge overlaps.");
            Probe(pose.center + pose.rotation * new Vector3(1.1f, 0, 0), false, "Outside the rotated box edge misses.");
            Probe(pose.center + pose.rotation * new Vector3(0, 1.9f, 0), true, "Inside the rotated height overlaps.");
            Probe(pose.center + pose.rotation * new Vector3(0, 2.1f, 0), false, "Above the rotated height misses.");
            field.shape = UltimateFieldShape.Sphere;
            Probe(pose.center + Vector3.right * 1.9f, true, "Inside sphere radius overlaps.");
            Probe(pose.center + Vector3.right * 2.1f, false, "Outside sphere radius misses.");
            Probe(pose.center + Vector3.up * 2.1f, false, "Sphere uses the same radius vertically.");
            Check(!UltimateFieldGeometry.Overlap(field, origin, facing, 1 << 5).Contains(collider),
                "Runtime queries respect the target layer mask.");

            void Probe(Vector3 position, bool expected, string message)
            {
                probe.transform.position = position;
                Physics.SyncTransforms();
                bool overlaps = UltimateFieldGeometry.Overlap(field, origin, facing, 1 << 6).Contains(collider);
                Check(overlaps == expected, message);
            }
        }
        finally { Object.DestroyImmediate(probe); }
    }

    [MenuItem("Tools/Character/Player/Validate Ultimate Runtime (Play Mode)")]
    public static void ValidateRuntimeMenu() => Debug.Log(RunRuntime());

    public static string RunRuntime()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before running Ultimate runtime verification.");
        _checks = 0;
        GameObject fixture = new GameObject("Ultimate runtime verification (temporary)");
        fixture.SetActive(false);
        fixture.transform.position = new Vector3(15000, 1000, 15000);
        PlayerUltimateDefinition definition = ScriptableObject.CreateInstance<PlayerUltimateDefinition>();
        try
        {
            definition.clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/03_Animations/Player/Clips/Attack/Ultimate.anim");
            definition.cooldown = 8;
            definition.playbackSpeed = 1;
            definition.warp.enabled = false;
            definition.targetLayers = 1 << 6;
            definition.hits.Add(new UltimateHitField { name = "First marker", time = .2f,
                position = new Vector3(0, 1, 2), size = new Vector3(2, 2, 2), damage = 17, hitStopFrames = 0 });
            definition.hits.Add(new UltimateHitField { name = "Second marker", time = .5f,
                position = new Vector3(0, 1, 2), size = new Vector3(2, 2, 2), damage = 23, hitStopFrames = 0 });

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            GameObject clone = Object.Instantiate(prefab, fixture.transform, false);
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            foreach (var source in clone.GetComponentsInChildren<AudioSource>(true))
            {
                source.mute = true;
                source.playOnAwake = false;
            }
            var player = clone.GetComponent<PlayerCore>();
            var serialized = new SerializedObject(player);
            serialized.FindProperty("_ultimateDefinition").objectReferenceValue = definition;
            if (serialized.FindProperty("_mainCamera").objectReferenceValue == null)
                serialized.FindProperty("_mainCamera").objectReferenceValue = Camera.main;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject victim = new GameObject("Two-collider victim");
            victim.transform.SetParent(fixture.transform, false);
            victim.transform.localPosition = new Vector3(0, 1, 2);
            victim.layer = 6;
            victim.AddComponent<SphereCollider>().radius = .15f;
            victim.AddComponent<BoxCollider>().size = Vector3.one * .3f;
            var actor = victim.AddComponent<PlayerSkillHitTestActor>();

            fixture.SetActive(true);
            Physics.SyncTransforms();
            player.StateMachine.InitStateMachine(player.StateMachine.IdleState);
            player.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            bool oldRootMotion = player.Animator.applyRootMotion;
            bool oldFireEvents = player.Animator.fireEvents;
            RuntimeAnimatorController oldController = player.Animator.runtimeAnimatorController;
            Check(player.TryBeginUltimate(), "E entry is accepted from idle.");
            Check(player.Ultimate.IsActive && player.Ultimate.ActiveDefinition.clip == definition.clip,
                "Accepted E creates an active cast using the Ultimate clip.");
            Check(oldController != null && player.Animator.runtimeAnimatorController == null,
                "The active pose graph temporarily detaches the normal Animator Controller.");
            Check(!player.TryBeginUltimate(), "An active cast rejects a repeated E entry.");
            Near(player.Ultimate.CooldownRemaining, 8, "Accepted E starts the cooldown exactly once.");
            Transform[] animatedTransforms = player.Animator.GetComponentsInChildren<Transform>();
            Quaternion[] initialPose = animatedTransforms.Select(t => t.localRotation).ToArray();

            // Editing the asset during a cast must not mutate its in-flight damage.
            definition.hits[1].damage = 999;
            player.Ultimate.Tick(.4f);
            Check(animatedTransforms.Where((t, i) => Quaternion.Angle(t.localRotation, initialPose[i]) > .25f).Any(),
                "Advancing the real Ultimate graph changes the player's sampled bone pose.");
            Quaternion[] sampledPose = animatedTransforms.Select(t => t.localRotation).ToArray();
            // Regression: the ordinary Animator update used to replace this pose with
            // the still-connected controller's Idle pose later in the rendered frame.
            player.Animator.Update(0f);
            float overwrittenAngle = animatedTransforms.Select((t, i) => Quaternion.Angle(t.localRotation, sampledPose[i])).Max();
            Check(overwrittenAngle < .1f,
                "A normal Animator update preserves the sampled Ultimate bone pose (max change " + overwrittenAngle + " degrees).");
            Check(actor.Hits.Count == 1 && actor.Hits[0].DamageAmount == 17,
                "A 400ms step executes a crossed marker exactly once across two victim colliders.");
            player.Ultimate.Tick(.1f);
            Check(actor.Hits.Count == 2 && actor.Hits[1].DamageAmount == 23,
                "The next field can hit the same victim and keeps its cast-start damage snapshot.");
            player.Ultimate.Tick(.05f);
            player.Ultimate.Tick(.05f);
            Check(actor.Hits.Count == 2, "Remaining in the field after its marker does not repeat damage.");
            player.Ultimate.Tick(definition.Duration);
            player.StateMachine.UpdateTick();
            Check(!player.Ultimate.IsActive, "The cast ends after the clip duration.");
            Check(player.Animator.applyRootMotion == oldRootMotion && player.Animator.fireEvents == oldFireEvents,
                "Clip completion restores Animator root-motion and event flags.");
            Check(ReferenceEquals(CurrentState(player.StateMachine), player.StateMachine.IdleState),
                "Completion returns the player FSM to idle.");
            Check(player.Animator.runtimeAnimatorController == oldController && oldController != null,
                "Completing the custom graph preserves the normal Animator Controller.");
            player.Animator.Update(.25f);
            player.Animator.Update(.25f);
            Check(player.Animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"),
                "The normal Animator Controller resumes its Idle state after Ultimate completion.");
            float idleTime = player.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            player.Animator.Update(.1f);
            Check(player.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime > idleTime,
                "The resumed controller continues to advance instead of remaining frozen.");
            Check(!player.TryBeginUltimate(), "Cooldown blocks immediate recast without interrupting idle.");
            player.Ultimate.TickCooldown(8f);
            Check(player.TryBeginUltimate(), "Cooldown expiry permits another cast.");
            player.Ultimate.Tick(.4f);
            Check(actor.Hits.Count == 3, "A fresh cast clears field victim history.");
            VerifyWarpCommands(player, definition);
            player.Ultimate.TickCooldown(8f);
            Check(player.TryBeginUltimate(), "Another cast can begin after the warp verification completes.");
            player.Ultimate.Tick(.25f);
            player.gameObject.SetActive(false);
            Check(!player.Ultimate.IsActive, "Disabling the player cleans up its active cast.");
            Check(player.Animator.runtimeAnimatorController == oldController &&
                  player.Animator.applyRootMotion == oldRootMotion && player.Animator.fireEvents == oldFireEvents,
                "Disabling midway through a cast restores the original controller and Animator flags.");
            return "PlayerUltimateVerification runtime: PASS (" + _checks + " checks). Real E entry, sampled pose persistence through Animator.Update, controller isolation/resume, crossed markers, collider/marker deduplication, per-cast snapshot, cooldown, warp movement commands through hitstop/FixedTick, completion and mid-cast disable restoration checked on an isolated clone. Warp checks measure submitted movement, not terrain collision.";
        }
        finally
        {
            Object.DestroyImmediate(fixture);
            Object.DestroyImmediate(definition);
        }
    }

    private static void VerifyWarpCommands(PlayerCore player, PlayerUltimateDefinition definition)
    {
        player.StateMachine.Transition(player.StateMachine.IdleState);
        definition.hits.Clear();
        definition.warp.enabled = true;
        definition.warp.start = 0f;
        definition.warp.end = 1f;
        definition.warp.distance = 3f;
        definition.warp.curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        player.Ultimate.TickCooldown(8f);
        Check(player.TryBeginUltimate(), "The authored motion-warp cast starts.");
        Quaternion facing = player.Ultimate.Facing;
        player.Ultimate.Tick(.25f);
        player.Ultimate.FixedTick();
        // Pause after the FSM queues movement but before PlayerMover consumes it.
        player.BeginHitStop();
        player.Ultimate.Tick(5f);
        player.Ultimate.FixedTick();
        Near(player.Ultimate.Time, .25f, "Hitstop freezes the animation/warp time.");
        Near(player.Mover.ConsumePendingDisplacement(), Vector3.zero, "Hitstop submits no movement to the mover.");
        player.EndHitStop();
        player.Ultimate.Tick(.25f);
        player.Ultimate.FixedTick();
        Vector3 firstDisplacement = player.Mover.ConsumePendingDisplacement();
        Near(firstDisplacement, facing * Vector3.forward * 1.5f,
            "Resuming submits both the retained pre-hitstop movement and the newly elapsed movement.");
        player.Ultimate.FixedTick();
        Near(player.Mover.ConsumePendingDisplacement(), Vector3.zero,
            "A second FixedTick at the same clip time cannot submit that movement twice.");

        player.Ultimate.Tick(definition.Duration);
        Check(!player.Ultimate.IsComplete, "Animation end waits for the final warp movement to reach FixedTick.");
        player.StateMachine.UpdateTick();
        Check(player.Ultimate.IsActive, "An Update before that FixedTick does not exit and discard the final warp step.");
        player.Ultimate.FixedTick();
        Vector3 finalDisplacement = player.Mover.ConsumePendingDisplacement();
        Near(firstDisplacement + finalDisplacement, facing * Vector3.forward * 3f,
            "Warp movement submitted across the interrupted cast totals the authored three meters exactly.");
        Check(player.Ultimate.IsComplete, "The cast completes after its final warp step is submitted.");
        player.StateMachine.UpdateTick();
        Check(!player.Ultimate.IsActive && ReferenceEquals(CurrentState(player.StateMachine), player.StateMachine.IdleState),
            "Completed warp returns to idle through normal cleanup.");
    }

    private static PlayerStateBase CurrentState(PlayerStateMachine machine) => (PlayerStateBase)typeof(PlayerStateMachine)
        .GetField("_currentState", PrivateInstance).GetValue(machine);
    private static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .001f, message);
    private static void Near(Vector3 actual, Vector3 expected, string message) => Check(Vector3.Distance(actual, expected) < .002f, message);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Ultimate verification: " + message);
        _checks++;
    }
}
