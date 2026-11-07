#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

/// <summary>Isolated E data/runtime regressions. Authored assets and scene actors are never edited.</summary>
public static class PlayerSkillDataVerification
{
    private const string PlayerPath = "Assets/08_Prefabs/Combat/Player.prefab";
    private const string ReportPath = "Temp/PlayerSkillDataVerification.result";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [MenuItem("Tools/Character/Player/Validate E Skill Data")]
    private static void DataMenu() => Debug.Log(RunDataChecks());

    [MenuItem("Tools/Character/Player/Validate E Skill Data (Play Mode)")]
    public static void RunPlayMode()
    {
        string result = RunRuntimeChecks() + "\n" + RunStateChecks() + "\n" + RunPhysicsChecks();
        File.WriteAllText(ReportPath, result + "\n");
        Debug.Log(result);
    }

    public static string RunDataChecks()
    {
        _checks = 0;
        var definition = Definition();
        GameObject fixture = null;
        try
        {
            definition.duration = 2f; definition.playbackSpeed = 2f; definition.recoveryDuration = .3f;
            Near(definition.RuntimeDuration, 1.3f, "Authored duration and combat recovery have independent speed semantics");
            definition.useInputCancelTime = false;
            Near(definition.InputCancelTime, 2f, "Without early input cancellation the threshold is the content endpoint");
            definition.useInputCancelTime = true; definition.inputCancelTime = .4f;
            Near(definition.InputCancelTime, .4f, "Input cancellation uses authored sequence seconds");
            definition.inputCancelTime = 99f; Near(definition.InputCancelTime, 2f, "Cancel threshold is bounded by authored content");
            definition.inputCancelTime = float.NaN; Near(definition.InputCancelTime, 2f, "Nonfinite threshold has a finite fallback");

            var motion = definition.motion;
            motion.enabled = true; motion.start = .2f; motion.end = 1.2f; motion.distance = 8f;
            motion.curve = new AnimationCurve(new Keyframe(0f, 2f), new Keyframe(.5f, 3f), new Keyframe(1f, 6f));
            Near(definition.EvaluateMotion(.1f), Vector3.zero, "Travel stays at the origin before its authored interval");
            Near(definition.EvaluateMotion(.2f), Vector3.zero, "Curve origin is normalized rather than adding a starting jump");
            Near(definition.EvaluateMotion(.7f), Vector3.forward * 2f, "Authored curve controls intermediate travel");
            Near(definition.EvaluateMotion(1.2f), Vector3.forward * 8f, "Distance means the complete authored travel");
            Near(definition.EvaluateMotion(50f), Vector3.forward * 8f, "Completed travel holds its endpoint");
            motion.enabled = false; Near(definition.EvaluateMotion(1f), Vector3.zero, "Disabling movement removes all authored displacement");

            definition.hits.Add(Hit("Beyond a shorter endpoint", 1.7f, 20f));
            definition.motion.start = 1.3f; definition.motion.end = 1.8f;
            definition.impactFrames.Add(new PlayerSkillImpactWindow { start = 1.5f, duration = .2f });
            definition.screenEffects.Add(new PlayerSkillScreenEffect { start = 1.6f, duration = .2f });
            definition.duration = 1f; definition.Normalize();
            Near(definition.hits[0].time, 1.7f, "Shortening content preserves a later marker's authored timestamp");
            Near(definition.motion.start, 1.3f, "Shortening content does not pull a later movement start forward");
            Near(definition.motion.end, 1.8f, "Shortening content preserves the movement endpoint");
            Near(definition.impactFrames[0].start, 1.5f, "Shortening content preserves later impact timing");
            Near(definition.screenEffects[0].start, 1.6f, "Shortening content preserves later postprocessing timing");
            definition.hits.Clear();

            fixture = new GameObject("E isolated query verification");
            fixture.transform.position = new Vector3(27000, 1500, 27000);
            var target = new GameObject("E geometry target"); target.transform.SetParent(fixture.transform, false);
            target.layer = 6; var collider = target.AddComponent<SphereCollider>(); collider.radius = .025f;
            Vector3 origin = fixture.transform.position;
            Quaternion facing = Quaternion.Euler(0f, 90f, 0f);
            var field = Hit("Geometry", 0f, 1f); field.position = new Vector3(0f, 1f, 2f);
            field.size = new Vector3(.5f, 1f, 2f); field.euler = new Vector3(0f, 30f, 0f);
            Vector3 center = origin + facing * field.position;
            Quaternion rotation = facing * Quaternion.Euler(field.euler);
            target.transform.position = center + rotation * Vector3.forward * .8f; Physics.SyncTransforms();
            Check(field.Overlap(origin, facing).Contains(collider), "Rotated box includes a target along its long axis");
            target.transform.position = center + rotation * Vector3.right * .5f; Physics.SyncTransforms();
            Check(!field.Overlap(origin, facing).Contains(collider), "Rotated box rejects a target outside its narrow axis");
            field.shape = PlayerSkillHitShape.Sphere; field.radius = .5f;
            target.transform.position = center + Vector3.up * .4f; Physics.SyncTransforms();
            Check(field.Overlap(origin, facing).Contains(collider), "Sphere includes a target inside its radius");
            target.transform.position = center + Vector3.up * .7f; Physics.SyncTransforms();
            Check(!field.Overlap(origin, facing).Contains(collider), "Sphere rejects a target outside its radius");
            field.shape = PlayerSkillHitShape.Capsule; field.radius = .2f; field.height = 2f; field.capsuleDirection = 2;
            target.transform.position = center + rotation * Vector3.forward * .8f; Physics.SyncTransforms();
            Check(field.Overlap(origin, facing).Contains(collider), "Rotated capsule includes its endpoint region");
            target.transform.position = center + rotation * Vector3.right * .4f; Physics.SyncTransforms();
            Check(!field.Overlap(origin, facing).Contains(collider), "Rotated capsule rejects a target beyond its radius");
            target.transform.position = center; field.targetLayers = 1 << 5; Physics.SyncTransforms();
            Check(!field.Overlap(origin, facing).Contains(collider), "Per-hit target layers filter the query");
            field.targetLayers = 1 << 6; collider.isTrigger = true; field.triggerInteraction = QueryTriggerInteraction.Ignore; Physics.SyncTransforms();
            Check(!field.Overlap(origin, facing).Contains(collider), "Per-hit trigger exclusion is honored");
            field.triggerInteraction = QueryTriggerInteraction.Collide;
            Check(field.Overlap(origin, facing).Contains(collider), "Per-hit trigger inclusion is honored");

            definition.duration = float.NaN; definition.playbackSpeed = float.PositiveInfinity;
            definition.recoveryDuration = -3f; definition.hits.Add(new PlayerSkillHit { radius = -2f, height = -1f });
            definition.Normalize();
            Check(!float.IsNaN(definition.Duration) && !float.IsInfinity(definition.RuntimeDuration), "Malformed timing normalizes to finite runtime values");
            Near(definition.RecoveryDuration, 0f, "Recovery cannot become negative");
            Check(definition.hits[0].radius > 0f && definition.hits[0].height >= definition.hits[0].radius * 2f,
                "Malformed capsule dimensions normalize to a usable query");
            string result = Pass("data and geometry");
            File.WriteAllText(ReportPath, result + "\n");
            return result;
        }
        finally { if (fixture != null) Object.DestroyImmediate(fixture); Object.DestroyImmediate(definition); }
    }

    public static string RunRuntimeChecks()
    {
        RequirePlay(); _checks = 0;
        var definition = Definition(); GameObject fixture = null;
        PlayerCore player = null; PlayerSkillRuntime runtime = null;
        PlayerSkillHitTestActor victim = null; VolumeProfile profile = null;
        try
        {
            fixture = MakeFixture("E runtime verification", new Vector3(27500, 1600, 27500));
            player = MakePlayer(fixture); victim = MakeVictim(fixture);
            Activate(fixture, player);
            var director = player.DirectorContainer.Directors[DirectorID.Skill];
            runtime = new PlayerSkillRuntime(player, director);
            definition.hits.AddRange(new[] { Hit("late", .7f, 7), Hit("tie A", .2f, 2), Hit("tie B", .2f, 3),
                Hit("zero", 0f, 1), Hit("end", 1f, 9), Hit("disabled", .3f, 50) });
            definition.hits[5].enabled = false;
            Check(runtime.Begin(definition), "Standalone E begins from its definition");
            Check(victim.Hits.Count == 1 && victim.Hits[0].DamageAmount == 1f, "Zero-time marker fires once at cast start");
            definition.hits[1].damage = 200f;
            definition.hits[1].position = Vector3.one * 200f;
            definition.duration = 5f;
            runtime.Tick(.9f); ResolveStationary(runtime, player);
            Check(victim.Hits.Select(h => h.DamageAmount).SequenceEqual(new[] { 1f, 2f, 3f, 7f }),
                "Crossed markers execute chronologically with stable ties and a cast snapshot");
            Check(victim.DamageAttempts == 4, "Compound colliders generate one damage attempt per marker");
            Near(runtime.ActiveDefinition.Duration, 1f, "Live duration edits do not alter the active cast");
            runtime.Tick(10f); ResolveStationary(runtime, player);
            Check(victim.Hits.Count == 5 && victim.Hits[4].DamageAmount == 9f, "Endpoint marker fires exactly once");
            runtime.Tick(10f); ResolveStationary(runtime, player);
            Check(victim.Hits.Count == 5, "Completed and disabled markers never replay");
            Check(runtime.IsComplete, "Natural duration is independent from Timeline content");
            runtime.Stop(); Check(!runtime.IsActive && runtime.ActiveDefinition == null, "Stop releases the cast snapshot");

            definition.duration = .4f; definition.hits.Clear(); definition.hits.Add(Hit("trimmed later hit", .8f, 30f));
            victim.Hits.Clear(); victim.DamageAttempts = 0;
            runtime.Begin(definition); runtime.Tick(2f); ResolveStationary(runtime, player);
            Check(runtime.IsComplete && victim.Hits.Count == 0, "Markers beyond a shorter data endpoint are skipped rather than moved to its final frame");
            Near(definition.hits[0].time, .8f, "Skipped later markers retain their authored time for a future duration extension");
            runtime.Stop();

            definition.duration = 1f; definition.hits.Clear(); definition.hits.Add(Hit("miss", .2f, 11));
            victim.Hits.Clear(); victim.DamageAttempts = 0;
            Vector3 victimPosition = victim.transform.position;
            victim.transform.position += Vector3.right * 20f; Physics.SyncTransforms();
            runtime.Begin(definition); runtime.Tick(.3f); ResolveStationary(runtime, player);
            victim.transform.position = victimPosition; Physics.SyncTransforms();
            runtime.Tick(.3f); ResolveStationary(runtime, player);
            Check(victim.Hits.Count == 0, "A missed marker is consumed before a target enters later");
            runtime.Stop(); victim.RejectDamage = true; victim.DamageAttempts = 0;
            runtime.Begin(definition); runtime.Tick(.3f); ResolveStationary(runtime, player);
            Check(victim.DamageAttempts == 1 && victim.Hits.Count == 0, "Rejected compound targets receive only one attempt");
            runtime.Stop(); victim.RejectDamage = false;

            definition.hits.Clear(); definition.useInputCancelTime = true; definition.inputCancelTime = .2f;
            runtime.Begin(definition); runtime.Tick(.3f); ResolveStationary(runtime, player);
            Check(runtime.CanCancelWithInput && !runtime.IsComplete && runtime.IsActive,
                "Opening input cancellation alone keeps E active");
            float beforeStop = runtime.Time;
            player.BeginHitStop(); runtime.Tick(.5f); runtime.FixedTick();
            Near(runtime.Time, beforeStop, "Hitstop freezes the gameplay clock independently of presentation");
            Check(!runtime.CanCancelWithInput, "Hitstop suppresses the cancellation window");
            player.EndHitStop(); runtime.Stop();

            var previousAsset = director.playableAsset; director.playableAsset = null;
            try
            {
                runtime.Begin(definition); runtime.Tick(.8f); ResolveStationary(runtime, player);
                Check(runtime.IsActive && !runtime.IsComplete, "E runs when no Timeline is assigned");
                director.Stop(); runtime.Tick(.3f); ResolveStationary(runtime, player);
                Check(runtime.IsComplete, "External Timeline Stop cannot truncate data gameplay");
            }
            finally { runtime.Stop(); director.playableAsset = previousAsset; }

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            definition.screenEffects.Add(new PlayerSkillScreenEffect { start = 0f, duration = .8f, profile = profile,
                weight = .7f, envelope = AnimationCurve.Constant(0f, 1f, 1f) });
            runtime.Begin(definition); runtime.Tick(.3f);
            var volumes = ((List<Volume>)Get(runtime, "_volumes")).Where(v => v != null).ToArray();
            Check(volumes.Length > 0 && volumes.Any(v => v.weight > .69f), "Authored screen profile and envelope drive a runtime Volume");
            Check(volumes.All(v => v.sharedProfile != profile), "A cast owns screen profile copies rather than live authored overrides");
            runtime.Tick(.6f);
            Check(volumes.All(v => v == null || !v.isActiveAndEnabled || v.weight < .001f), "Screen effect ends at its authored duration");
            runtime.Stop();
            Check(!runtime.IsActive, "Screen effect cleanup shares the cast Stop path");
            RunTimelineGate(player, runtime, definition, victim);
            return Pass("runtime");
        }
        finally
        {
            runtime?.Stop();
            if (player != null) { HitstopCoordinator.Cancel(player); VirtualCutscenePlayback.CancelFor(player); }
            if (victim != null) HitstopCoordinator.Cancel(victim);
            if (fixture != null) Object.DestroyImmediate(fixture);
            if (profile != null) Object.DestroyImmediate(profile);
            Object.DestroyImmediate(definition);
        }
    }

    public static string RunStateChecks()
    {
        RequirePlay(); _checks = 0;
        var definition = Definition(); GameObject fixture = null;
        PlayerCore player = null; PlayerUltimateDefinition ultimate = null;
        try
        {
            fixture = MakeFixture("E state verification", new Vector3(28000, 1700, 28000));
            player = MakePlayer(fixture); Activate(fixture, player);
            var director = player.DirectorContainer.Directors[DirectorID.Skill];
            var settings = director.GetComponent<PlayerSkillSequenceSettings>();
            Check(settings != null, "Skill settings exist on the fixture");
            settings.Definition = definition;
            // Leave generous margins around thresholds: Editor tool execution can have a long current frame.
            definition.duration = 10f; definition.useInputCancelTime = true; definition.inputCancelTime = 5f;
            var states = player.StateMachine;
            BeginState(player); var runtime = states.SkillState.Runtime;
            runtime.Tick(1f); Set(player.InputCollector, "_attackValue", true); states.UpdateTick(); ClearInput(player);
            Check(states.IsSkillActive, "Attack input before the authored threshold cannot cancel E");
            runtime.Tick(5f); ResolveStationary(runtime, player); states.UpdateTick();
            Check(states.IsSkillActive && runtime.IsActive, "Reaching the threshold without input does not exit E");
            Set(player.InputCollector, "_attackValue", true); states.UpdateTick(); ClearInput(player);
            Check(Current(player) == states.BasicAttack1State && !runtime.IsActive, "Attack after the threshold enters the next state immediately");
            Near(player.Mover.ConsumePendingDisplacement(), Vector3.zero, "Cancelled E leaves no pending displacement");

            Reset(player); BeginState(player); runtime = states.SkillState.Runtime;
            runtime.Tick(6f); ResolveStationary(runtime, player);
            var charges = (PlayerDodgeChargePool)Get(player, "_dodgeCharges");
            while (charges.IsAvailable) charges.TryConsume(10f);
            Set(player.InputCollector, "_dodgeValue", true); states.UpdateTick(); ClearInput(player);
            Check(states.IsSkillActive && runtime.IsActive, "Unavailable dodge input leaves E running");
            charges.Tick(10000f);
            Set(player.InputCollector, "_dodgeValue", true); states.UpdateTick(); ClearInput(player);
            Check(Current(player) == states.BackDodgeState && !runtime.IsActive, "Available dodge connects after the cancellation threshold");

            Reset(player); BeginState(player); runtime = states.SkillState.Runtime;
            runtime.Tick(6f); ResolveStationary(runtime, player);
            Set(player.InputCollector, "_moveValue", Vector2.up); states.UpdateTick(); ClearInput(player);
            Check(Current(player) == states.RunStartState && !runtime.IsActive, "Move input connects after the cancellation threshold");

            Reset(player); definition.useInputCancelTime = false; definition.recoveryDuration = 3f;
            BeginState(player); runtime = states.SkillState.Runtime;
            runtime.Tick(10f); ResolveStationary(runtime, player);
            states.UpdateTick();
            Check(states.IsSkillActive && !runtime.IsComplete, "Natural content endpoint keeps the authored recovery active");
            Set(player.InputCollector, "_attackValue", true); states.UpdateTick(); ClearInput(player);
            Check(states.IsSkillActive, "Input stays blocked during natural recovery");
            player.BeginHitStop(); float recoveryTime = runtime.Time;
            runtime.Tick(1f); states.UpdateTick();
            Check(states.IsSkillActive && !runtime.IsComplete, "Hitstop does not consume recovery");
            Near(runtime.Time, recoveryTime, "Recovery hitstop preserves the authored content clock");
            player.EndHitStop(); runtime.Tick(4f); ResolveStationary(runtime, player); states.UpdateTick();
            Check(Current(player) == states.IdleState && !runtime.IsActive, "Recovery ends naturally without a new input");

            Reset(player); definition.recoveryDuration = 0f; definition.playPresentation = true;
            if (player.UltimateDefinition != null)
            {
                ultimate = Object.Instantiate(player.UltimateDefinition); ultimate.cutscene = null; ultimate.cooldown = 0f;
                Set(player, "_ultimateDefinition", ultimate); player.Ultimate.TickCooldown(10000f);
                BeginState(player); runtime = states.SkillState.Runtime;
                Check(player.TryBeginUltimate(), "Q retains its immediate interrupt of E");
                Check(!runtime.IsActive && runtime.ActiveDefinition == null, "Q interruption releases E data and presentation ownership");
                Check(director.state != PlayState.Playing, "Q interruption stops E presentation");
                Reset(player);
            }
            BeginState(player); runtime = states.SkillState.Runtime;
            player.gameObject.SetActive(false);
            Check(!runtime.IsActive && runtime.ActiveDefinition == null, "Disabling the player releases the E runtime snapshot");
            Check(director.state != PlayState.Playing, "Disabling the player stops E presentation");
            return Pass("FSM input, recovery and interruption");
        }
        finally
        {
            if (player != null) { HitstopCoordinator.Cancel(player); VirtualCutscenePlayback.CancelFor(player); }
            if (fixture != null) Object.DestroyImmediate(fixture);
            if (ultimate != null) Object.DestroyImmediate(ultimate);
            Object.DestroyImmediate(definition);
        }
    }

    public static string RunPhysicsChecks()
    {
        RequirePlay(); _checks = 0;
        var definition = Definition(); GameObject fixture = null; PlayerSkillRuntime runtime = null;
        PlayerCore player = null;
        var scene = SceneManager.CreateScene("E isolated endpoint physics " + Guid.NewGuid().ToString("N"),
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        try
        {
            fixture = MakeFixture("E endpoint physics", new Vector3(29000, 1800, 29000));
            SceneManager.MoveGameObjectToScene(fixture, scene);
            player = MakePlayer(fixture); Activate(fixture, player);
            var body = player.GetComponent<Rigidbody>(); body.interpolation = RigidbodyInterpolation.None;
            Vector3 origin = body.position;
            player.Rotator.RotateImmediately(Vector3.right);
            definition.motion.enabled = true; definition.motion.start = 0f; definition.motion.end = 1f;
            definition.motion.distance = 4f; definition.motion.curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            runtime = new PlayerSkillRuntime(player, player.DirectorContainer.Directors[DirectorID.Skill]);
            Check(runtime.Begin(definition), "Moving E begins in an isolated physics scene");
            runtime.Tick(.5f); runtime.FixedTick();
            runtime.PreservePendingMotion(); player.BeginHitStop(); runtime.Tick(.5f); runtime.FixedTick();
            Near(player.Mover.ConsumePendingDisplacement(), Vector3.zero, "Hitstop queues no new physical movement");
            Near(runtime.Time, .5f, "Hitstop preserves movement evaluation time");
            player.EndHitStop(); runtime.FixedTick();
            Invoke(player.Mover, "FixedUpdate"); scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
            Near(body.position, origin + Vector3.right * 2f, "Pending displacement survives a hitstop before motor consumption");
            runtime.FixedTick(); runtime.Tick(.5f); runtime.FixedTick();
            Check(!runtime.IsComplete, "Final movement cannot complete before the physics motor consumes it");
            Invoke(player.Mover, "FixedUpdate"); scene.GetPhysicsScene().Simulate(Time.fixedDeltaTime);
            Near(body.position, origin + Vector3.right * 4f, "Cast facing rotates the complete authored distance into world movement");
            runtime.FixedTick();
            Check(runtime.IsComplete, "Final marker resolution permits natural completion");
            runtime.Stop();
            Near(player.Mover.ConsumePendingDisplacement(), Vector3.zero, "Natural Stop leaves no motion in the next action");
            Near(body.position, origin + Vector3.right * 4f, "Cleanup preserves the final physical position");
            return Pass("local physics and hitstop");
        }
        finally
        {
            runtime?.Stop(); if (player != null) HitstopCoordinator.Cancel(player);
            if (fixture != null) Object.DestroyImmediate(fixture);
            Object.DestroyImmediate(definition);
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }
    }

    private static PlayerSkillDefinition Definition()
    {
        var definition = ScriptableObject.CreateInstance<PlayerSkillDefinition>();
        definition.duration = 1f; definition.playbackSpeed = 1f; definition.playPresentation = false;
        definition.motion.enabled = false; definition.hits.Clear(); definition.screenEffects.Clear(); definition.impactFrames.Clear();
        return definition;
    }
    private static void RunTimelineGate(PlayerCore player, PlayerSkillRuntime runtime, PlayerSkillDefinition definition,
        PlayerSkillHitTestActor victim)
    {
        var director = player.DirectorContainer.Directors[DirectorID.Skill];
        var receiver = director.GetComponent<PlayerSkillAttackReceiver>();
        var originalAsset = director.playableAsset;
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        // Marker-only assets have a zero automatic duration. Keep the control graph
        // alive so OnNotify is tested through the same playing-state gate as gameplay.
        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.fixedDuration = 2d;
        var track = timeline.CreateTrack<PlayerSkillHitTrack>(null, "E duplicate notification verification");
        var marker = track.CreateMarker<PlayerSkillHitMarker>(.75d); marker.ExplicitDamageFieldNumber = 1;
        try
        {
            director.playableAsset = timeline; director.SetGenericBinding(track, receiver);
            var box = receiver.GetDamageField(1)?.Hitbox as BoxCollider;
            Check(box != null, "Legacy notification control has a query box");
            var receiverData = new SerializedObject(receiver);
            var legacyField = receiverData.FindProperty("_damageFields").GetArrayElementAtIndex(0);
            legacyField.FindPropertyRelative("_targetLayers").intValue = 1 << 6;
            legacyField.FindPropertyRelative("_hitStopFrame").intValue = 0;
            legacyField.FindPropertyRelative("_skillGaugeAdditive").floatValue = 0f;
            receiverData.ApplyModifiedPropertiesWithoutUndo();
            box.transform.SetPositionAndRotation(victim.transform.position, Quaternion.identity);
            box.center = Vector3.zero; box.size = Vector3.one * 10f;
            victim.Hits.Clear(); victim.DamageAttempts = 0; Physics.SyncTransforms();
            director.time = 0d; director.Play();
            Check(director.playableGraph.IsValid() && director.state == PlayState.Playing, "Legacy notification control has a live Timeline graph");
            receiver.OnNotify(director.playableGraph.GetRootPlayable(0), marker, null);
            Check(victim.DamageAttempts == 1, "The unowned legacy notification actually reaches the target");
            director.Stop();
            victim.Hits.Clear(); victim.DamageAttempts = 0;
            definition.hits.Clear(); definition.screenEffects.Clear(); definition.playPresentation = true;
            definition.hits.Add(Hit("authored once", .2f, 5f));
            runtime.Begin(definition);
            Check(director.playableGraph.IsValid() && director.state == PlayState.Playing, "Data cast keeps an actual presentation graph active");
            receiver.OnNotify(director.playableGraph.GetRootPlayable(0), marker, null);
            Check(victim.DamageAttempts == 0, "An owned live Timeline notification cannot execute a second damage path");
            runtime.Tick(.3f); ResolveStationary(runtime, player);
            Check(victim.Hits.Count == 1 && victim.Hits[0].DamageAmount == 5f && victim.DamageAttempts == 1,
                "Data scheduling produces exactly one authored hit alongside the presentation Timeline");
        }
        finally
        {
            runtime.Stop(); director.Stop(); director.ClearGenericBinding(track); director.playableAsset = originalAsset;
            Object.DestroyImmediate(marker); Object.DestroyImmediate(track); Object.DestroyImmediate(timeline);
        }
    }
    private static PlayerSkillHit Hit(string name, float time, float damage)
        => new PlayerSkillHit { name = name, time = time, damage = damage, hitStopFrames = 0, shakeForce = 0f,
            position = new Vector3(0f, 1f, 2f), size = Vector3.one * 2f, targetLayers = 1 << 6 };
    private static GameObject MakeFixture(string name, Vector3 position)
    {
        var fixture = new GameObject(name); fixture.SetActive(false); fixture.transform.position = position; return fixture;
    }
    private static PlayerCore MakePlayer(GameObject parent)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        if (prefab == null) throw new InvalidOperationException("Missing E verification player prefab");
        var go = Object.Instantiate(prefab, parent.transform, false); go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var source in go.GetComponentsInChildren<AudioSource>(true)) { source.mute = true; source.playOnAwake = false; }
        var player = go.GetComponent<PlayerCore>();
        var cameraObject = new GameObject("E verification reference camera"); cameraObject.transform.SetParent(parent.transform, false);
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; Set(player, "_mainCamera", camera);
        return player;
    }
    private static PlayerSkillHitTestActor MakeVictim(GameObject parent)
    {
        var go = new GameObject("E isolated compound target"); go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = new Vector3(0f, 1f, 2f); go.layer = 6;
        go.AddComponent<SphereCollider>().radius = .1f; go.AddComponent<BoxCollider>().size = Vector3.one * .1f;
        return go.AddComponent<PlayerSkillHitTestActor>();
    }
    private static void Activate(GameObject fixture, PlayerCore player)
    {
        fixture.SetActive(true); Physics.SyncTransforms(); player.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        player.InputCollector.enabled = false; ClearInput(player);
        player.StateMachine.InitStateMachine(player.StateMachine.IdleState);
    }
    private static void BeginState(PlayerCore player)
    {
        player.AddSkillGauge(100f);
        Check(player.StateMachine.Transition(player.StateMachine.SkillState), "E state accepts a prepared cast");
        Check(player.StateMachine.IsSkillActive && player.StateMachine.SkillState.Runtime.IsActive, "E state owns the authored runtime");
    }
    private static void ResolveStationary(PlayerSkillRuntime runtime, PlayerCore player)
    {
        runtime.FixedTick(); player.Mover.ConsumePendingDisplacement();
        runtime.FixedTick(); player.Mover.ConsumePendingDisplacement();
        runtime.FixedTick(); player.Mover.ConsumePendingDisplacement();
    }
    private static void Reset(PlayerCore player)
    {
        HitstopCoordinator.Cancel(player); ClearInput(player);
        player.StateMachine.Transition(player.StateMachine.IdleState); player.Ultimate.TickCooldown(10000f);
        PlayerUltimateExitBlend.Cancel(player.Animator); ((PlayerDodgeChargePool)Get(player, "_dodgeCharges")).Tick(10000f);
        player.Mover.ConsumePendingDisplacement(); player.Rotator.RotateImmediately(Vector3.forward);
    }
    private static void ClearInput(PlayerCore player)
    {
        foreach (string field in new[] { "_ultimateValue", "_attackValue", "_skillValue", "_dodgeValue" }) Set(player.InputCollector, field, false);
        Set(player.InputCollector, "_moveValue", Vector2.zero);
    }
    private static PlayerStateBase Current(PlayerCore player) => (PlayerStateBase)Get(player.StateMachine, "_currentState");
    private static object Get(object target, string name) => target.GetType().GetField(name, Hidden).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
    private static void RequirePlay() { if (!Application.isPlaying) throw new InvalidOperationException("Run E runtime verification in Play Mode."); }
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("E data verification: " + message);
        _checks++;
    }
    private static void Near(float a, float b, string message) => Check(Mathf.Abs(a - b) < .003f, message + " (" + a + " vs " + b + ")");
    private static void Near(Vector3 a, Vector3 b, string message) => Check(Vector3.Distance(a, b) < .03f, message + " (" + a + " vs " + b + ")");
    private static string Pass(string area) => "PlayerSkillDataVerification " + area + ": PASS (" + _checks + " checks).";
}
#endif
