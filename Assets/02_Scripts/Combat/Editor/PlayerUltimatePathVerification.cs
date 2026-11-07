using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Focused route checks. Runtime checks consume mover commands without simulating the active scene.</summary>
public static class PlayerUltimatePathVerification
{
    private static int _checks;

    [MenuItem("Tools/Character/Player/Validate Ultimate Path Evaluation")]
    public static void EvaluationMenu() => Debug.Log(RunEvaluation());

    public static string RunEvaluation()
    {
        _checks = 0;
        var path = new UltimateMotionWarp
        {
            enabled = true,
            mode = UltimateMotionWarpMode.Path,
            interpolation = UltimatePathInterpolation.Linear,
            // Deliberately unsorted, with vertical values that must not lift the player.
            points = new List<UltimateMotionWaypoint>
            {
                Point(2f, new Vector3(2f, 9f, 2f)),
                Point(1f, new Vector3(0f, 0f, 2f)),
                Point(3f, new Vector3(-1f, -5f, 4f))
            }
        };
        Near(path.EvaluateOffset(0f), Vector3.zero, "The route starts at the immutable local origin.");
        Near(path.EvaluateOffset(-1f), Vector3.zero, "Scrubbing before the clip starts stays at the origin.");
        Near(path.EvaluateOffset(.5f), new Vector3(0, 0, 1), "First-segment interpolation includes the implicit origin.");
        Near(path.EvaluateOffset(1f), new Vector3(0, 0, 2), "An exact waypoint samples its position.");
        Near(path.EvaluateOffset(1.5f), new Vector3(1, 0, 2), "The second segment changes movement direction.");
        Near(path.EvaluateOffset(2f), new Vector3(2, 0, 2), "Waypoint height is ignored for grounded movement.");
        Near(path.EvaluateOffset(2.5f), new Vector3(.5f, 0, 3), "The third segment follows both authored X and Z coordinates.");
        Near(path.EvaluateOffset(9f), new Vector3(-1, 0, 4), "The route holds its last point after the final key.");
        Near(path.EvaluateOffset(.5f), new Vector3(0, 0, 1), "Backward scrubbing has no history-dependent offset.");

        Quaternion facing = Quaternion.Euler(0, 90, 0);
        Vector3 playerOrigin = new Vector3(10, 0, 20);
        Vector3 routeOrigin = playerOrigin + facing * path.EvaluateOffset(2f);
        Near(routeOrigin, new Vector3(12, 0, 18), "Route X/Z axes rotate with cast facing.");
        var field = new UltimateHitField { position = new Vector3(0, 1, 1) };
        Near(UltimateFieldGeometry.Evaluate(field, routeOrigin, facing).center, new Vector3(13, 1, 18),
            "Damage field offsets remain relative to the player at the sampled route position.");

        path.interpolation = UltimatePathInterpolation.SmoothStep;
        Near(path.EvaluateOffset(.25f), new Vector3(0, 0, .3125f), "Smooth interpolation eases the first quarter of a segment.");
        Near(path.EvaluateOffset(.75f), new Vector3(0, 0, 1.6875f), "Smooth interpolation eases arrival without overshoot.");
        Near(path.EvaluateOffset(1f), new Vector3(0, 0, 2), "Smooth interpolation still reaches the exact key.");
        Near(path.EvaluateOffset(1.5f), new Vector3(1, 0, 2), "Smooth interpolation uses the next segment's own time interval.");

        path.interpolation = UltimatePathInterpolation.Linear;
        path.points.Add(Point(0f, Vector3.one * 999));
        path.points.Add(Point(-1f, Vector3.one * 999));
        path.points.Add(null);
        path.points.Add(Point(1f, new Vector3(0, 7, 3)));
        Near(path.EvaluateOffset(0f), Vector3.zero, "Malformed zero/negative keys cannot move the implicit origin.");
        Near(path.EvaluateOffset(1f), new Vector3(0, 0, 3), "The last duplicate-time entry deterministically wins.");
        Near(path.EvaluateOffset(1.5f), new Vector3(1, 0, 2.5f), "Interpolation uses that duplicate winner without division by zero.");
        path.points.Add(Point(float.NaN, Vector3.one * 999));
        path.points.Add(Point(float.PositiveInfinity, Vector3.one * 999));
        Near(path.EvaluateOffset(1.5f), new Vector3(1, 0, 2.5f), "Non-finite waypoint times cannot contaminate an otherwise valid route.");
        Near(path.EvaluateOffset(float.NaN), Vector3.zero, "An invalid scrub time cannot produce a NaN position.");
        path.enabled = false;
        Near(path.EvaluateOffset(2f), Vector3.zero, "Disabled route movement stays at the player origin.");
        path.enabled = true;
        path.points.Clear();
        Near(path.EvaluateOffset(2f), Vector3.zero, "An empty route is stationary.");
        path.points = null;
        Near(path.EvaluateOffset(2f), Vector3.zero, "A missing route list is stationary.");

        var forward = new UltimateMotionWarp { enabled = true, start = .1f, end = .5f, distance = 2f,
            curve = AnimationCurve.Linear(0, 0, 1, 1) };
        Check(forward.mode == UltimateMotionWarpMode.Forward, "Existing serialized warps keep Forward mode by default.");
        Near(forward.EvaluateOffset(.05f), Vector3.zero, "Legacy forward motion still waits for its start time.");
        Near(forward.EvaluateOffset(.3f), Vector3.forward, "Legacy forward distance/curve interpolation is preserved.");
        Near(forward.EvaluateOffset(.7f), Vector3.forward * 2, "Legacy forward motion holds its authored endpoint.");
        return "PlayerUltimatePathVerification evaluation: PASS (" + _checks + " checks). Multi-segment routes, facing/damage coordinates, keys, hold, backward scrub, easing, malformed duplicates and legacy Forward verified.";
    }

    [MenuItem("Tools/Character/Player/Validate Ultimate Path Runtime (Play Mode)")]
    public static void RuntimeMenu() => Debug.Log(RunRuntimePath());

    public static string RunRuntimePath()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before checking Ultimate route runtime.");
        _checks = 0;
        var fixture = new GameObject("Ultimate path verification (temporary)");
        fixture.SetActive(false);
        fixture.transform.position = new Vector3(17000, 1000, 17000);
        var definition = ScriptableObject.CreateInstance<PlayerUltimateDefinition>();
        try
        {
            definition.clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/03_Animations/Player/Clips/Attack/Ultimate.anim");
            definition.targetLayers = 0;
            definition.hits.Clear();
            definition.warp.enabled = true;
            definition.warp.mode = UltimateMotionWarpMode.Path;
            definition.warp.interpolation = UltimatePathInterpolation.Linear;
            definition.warp.points = new List<UltimateMotionWaypoint>
            {
                Point(.4f, new Vector3(0, 0, 2)),
                Point(.8f, new Vector3(2, 0, 2)),
                Point(1.2f, new Vector3(2, 0, 0))
            };
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/08_Prefabs/Combat/Player.prefab");
            GameObject clone = Object.Instantiate(prefab, fixture.transform, false);
            clone.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var source in clone.GetComponentsInChildren<AudioSource>(true))
            {
                source.mute = true;
                source.playOnAwake = false;
            }
            var player = clone.GetComponent<PlayerCore>();
            var serialized = new SerializedObject(player);
            serialized.FindProperty("_ultimateDefinition").objectReferenceValue = definition;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            fixture.SetActive(true);
            Physics.SyncTransforms();
            player.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            player.StateMachine.InitStateMachine(player.StateMachine.IdleState);
            player.Rotator.RotateImmediately(Vector3.right);
            Check(player.TryBeginUltimate(), "A path-enabled E cast starts normally.");
            Near(player.Ultimate.Facing * Vector3.forward, Vector3.right, "The cast captures its rotated facing direction.");

            definition.warp.points[1].position = new Vector3(99, 0, 99);
            definition.warp.points.Add(Point(1.4f, new Vector3(50, 0, 50)));
            Near(player.Ultimate.ActiveDefinition.warp.EvaluateOffset(.8f), new Vector3(2, 0, 2),
                "Editing a waypoint leaves the active cast's route snapshot unchanged.");
            Check(player.Ultimate.ActiveDefinition.warp.points.Count == 3,
                "Adding a waypoint also leaves the active route list unchanged.");

            player.Ultimate.Tick(.2f);
            player.Ultimate.FixedTick();
            Vector3 total = player.Mover.ConsumePendingDisplacement();
            Near(total, Vector3.right, "First route segment submits one meter along rotated forward.");
            player.Ultimate.Tick(.2f);
            player.Ultimate.FixedTick();
            player.BeginHitStop();
            player.Ultimate.Tick(5f);
            player.Ultimate.FixedTick();
            Near(player.Ultimate.Time, .4f, "Hitstop holds route time at the turning key.");
            Near(player.Mover.ConsumePendingDisplacement(), Vector3.zero, "No route movement escapes while hitstopped.");
            player.EndHitStop();
            player.Ultimate.Tick(.2f);
            player.Ultimate.FixedTick();
            Vector3 resumed = player.Mover.ConsumePendingDisplacement();
            Near(resumed, new Vector3(1, 0, -1), "Resume preserves pending forward motion plus the new sideways route segment.");
            total += resumed;
            player.Ultimate.FixedTick();
            Near(player.Mover.ConsumePendingDisplacement(), Vector3.zero, "The resumed movement is not submitted twice.");
            player.Ultimate.Tick(.2f);
            player.Ultimate.FixedTick();
            Vector3 sideways = player.Mover.ConsumePendingDisplacement();
            Near(sideways, Vector3.back, "The next key continues the sideways turn.");
            total += sideways;
            player.Ultimate.Tick(.4f);
            player.Ultimate.FixedTick();
            Vector3 returning = player.Mover.ConsumePendingDisplacement();
            Near(returning, Vector3.left * 2, "The final segment can move backward from the previous waypoint.");
            total += returning;
            Near(total, Vector3.back * 2, "All submitted route segments reach the rotated authored endpoint exactly.");

            player.Ultimate.Tick(definition.Duration);
            Check(!player.Ultimate.IsComplete, "Clip completion waits for the final fixed-step route sample.");
            player.Ultimate.FixedTick();
            Near(player.Mover.ConsumePendingDisplacement(), Vector3.zero, "Holding beyond the route endpoint introduces no extra travel.");
            Check(player.Ultimate.IsComplete, "The final fixed-step sample completes the path cast.");
            player.StateMachine.UpdateTick();
            Check(!player.Ultimate.IsActive && player.StateMachine.CanStartUltimate,
                "Path completion restores a locomotion state and releases the cast graph.");
            return "PlayerUltimatePathVerification runtime: PASS (" + _checks + " checks). Cast snapshot, rotated turns/backtracking, FixedTick endpoint, hitstop preservation and cleanup verified. Checks measure submitted movement; terrain collisions are not simulated.";
        }
        finally
        {
            Object.DestroyImmediate(fixture);
            Object.DestroyImmediate(definition);
        }
    }

    [MenuItem("Tools/Character/Player/Validate Ultimate Path Local Physics (Play Mode)")]
    public static void PhysicsMenu() => Debug.Log(RunPhysicsPath());

    public static string RunPhysicsPath()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before checking Ultimate route physics.");
        _checks = 0;
        Scene scene = default;
        GameObject fixture = null;
        PlayerUltimateDefinition definition = null;
        try
        {
            scene = SceneManager.CreateScene("UltimatePathVerification_" + Guid.NewGuid().ToString("N"),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            PhysicsScene physics = scene.GetPhysicsScene();
            Check(physics.IsValid() && !physics.Equals(Physics.defaultPhysicsScene),
                "The test owns a local physics scene, separate from the user's default simulation.");
            fixture = new GameObject("Ultimate local physics fixture");
            fixture.SetActive(false);
            fixture.transform.position = new Vector3(0, 1000, 0);
            SceneManager.MoveGameObjectToScene(fixture, scene);
            float step = Time.fixedDeltaTime;
            Check(step > 0f && step < .1f, "The fixed timestep permits a bounded route simulation.");
            definition = ScriptableObject.CreateInstance<PlayerUltimateDefinition>();
            definition.clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/03_Animations/Player/Clips/Attack/Ultimate.anim");
            definition.targetLayers = 0;
            definition.hits.Clear();
            definition.warp.enabled = true;
            definition.warp.mode = UltimateMotionWarpMode.Path;
            definition.warp.interpolation = UltimatePathInterpolation.Linear;
            definition.warp.points = new List<UltimateMotionWaypoint>
            {
                Point(step * 20, new Vector3(0, 0, 2)),
                Point(step * 40, new Vector3(2, 0, 2)),
                Point(step * 60, new Vector3(2, 0, 0))
            };
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/08_Prefabs/Combat/Player.prefab");
            GameObject clone = Object.Instantiate(prefab, fixture.transform, false);
            clone.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var source in clone.GetComponentsInChildren<AudioSource>(true))
            {
                source.mute = true;
                source.playOnAwake = false;
            }
            var player = clone.GetComponent<PlayerCore>();
            var serialized = new SerializedObject(player);
            serialized.FindProperty("_ultimateDefinition").objectReferenceValue = definition;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            // The mover's usual ground probe queries the default world. Disable only that
            // probe on this temporary actor so local planar movement is fully isolated.
            var moverSettings = new SerializedObject(clone.GetComponent<PlayerMover>());
            moverSettings.FindProperty("_groundLayerMask").intValue = 0;
            moverSettings.ApplyModifiedPropertiesWithoutUndo();
            fixture.SetActive(true);
            player.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            player.StateMachine.InitStateMachine(player.StateMachine.IdleState);
            player.Rotator.RotateImmediately(Vector3.right);
            var body = clone.GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.None;
            body.useGravity = false;
            Vector3 origin = body.position;
            Check(player.TryBeginUltimate(), "The physical route cast starts at a rotated facing.");
            Quaternion facing = player.Ultimate.Facing;
            MethodInfo applyMover = typeof(PlayerMover).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(applyMover != null, "The verification uses the real PlayerMover fixed-step velocity application.");

            for (int i = 1; i <= 60; i++)
            {
                player.Ultimate.Tick(step);
                player.Ultimate.FixedTick();
                if (i == 10)
                {
                    Vector3 beforePause = body.position;
                    player.BeginHitStop();
                    player.Ultimate.Tick(step * 3);
                    player.Ultimate.FixedTick();
                    applyMover.Invoke(player.Mover, null);
                    physics.Simulate(step);
                    NearPhysics(body.position, beforePause, "The actual Rigidbody stays still while hitstopped.");
                    Near(player.Ultimate.Time, step * 10, "Local physics simulation does not advance the paused route clock.");
                    player.EndHitStop();
                    player.Ultimate.FixedTick();
                }
                applyMover.Invoke(player.Mover, null);
                physics.Simulate(step);
                if (i == 20) NearPhysics(body.position, origin + facing * new Vector3(0, 0, 2),
                    "The physical body reaches the first forward waypoint despite the interrupted movement command.");
                if (i == 40) NearPhysics(body.position, origin + facing * new Vector3(2, 0, 2),
                    "The physical body turns and reaches the second waypoint.");
                if (i == 60) NearPhysics(body.position, origin + facing * new Vector3(2, 0, 0),
                    "The physical body backtracks to the final authored waypoint.");
            }
            for (int i = 0; i < 10; i++)
            {
                player.Ultimate.Tick(step);
                player.Ultimate.FixedTick();
                applyMover.Invoke(player.Mover, null);
                physics.Simulate(step);
            }
            NearPhysics(body.position, origin + facing * new Vector3(2, 0, 0),
                "The physical body remains at the final point after route movement ends.");
            player.Ultimate.Tick(definition.Duration);
            player.Ultimate.FixedTick();
            applyMover.Invoke(player.Mover, null);
            physics.Simulate(step);
            player.StateMachine.UpdateTick();
            Check(!player.Ultimate.IsActive && player.StateMachine.CanStartUltimate,
                "The physically simulated cast exits normally after the clip finishes.");
            return "PlayerUltimatePathVerification local physics: PASS (" + _checks + " checks). Actual Rigidbody movement reached all three rotated route keys and held its endpoint within 2cm, including a hitstop between queueing and applying movement. Only a temporary local physics scene was simulated; its unload is queued.";
        }
        finally
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            if (definition != null) Object.DestroyImmediate(definition);
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }
    }

    private static UltimateMotionWaypoint Point(float time, Vector3 position) => new UltimateMotionWaypoint { time = time, position = position };
    private static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .001f, message);
    private static void Near(Vector3 actual, Vector3 expected, string message) => Check(Vector3.Distance(actual, expected) < .002f, message);
    private static void NearPhysics(Vector3 actual, Vector3 expected, string message) => Check(Vector3.Distance(actual, expected) < .02f,
        message + " Actual: " + actual.ToString("F3") + "; expected: " + expected.ToString("F3"));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Ultimate path verification: " + message);
        _checks++;
    }
}
