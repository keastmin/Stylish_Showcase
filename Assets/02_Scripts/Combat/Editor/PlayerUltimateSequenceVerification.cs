using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Isolated sequence checks. Authoring assets are read only; all definitions and actors are temporary.</summary>
public static class PlayerUltimateSequenceVerification
{
    private const string PlayerPath = "Assets/08_Prefabs/Combat/Player.prefab";
    private const string AnimationBase = "Assets/Download Assets/Scythe Animation Pack/Animations/Humanoid/";
    private const string PreparationPath = AnimationBase + "05_Jump/02_Jump_Combat/01_Jump_Combat_0/Jump_Combat_Start_0.anim";
    private const string AttackPath = AnimationBase + "02_Attack/07_Combo_Attack_Air/Combo_Attack_Air_All.anim";
    private const string RecoveryPath = AnimationBase + "05_Jump/02_Jump_Combat/01_Jump_Combat_0/Jump_End_F_0.anim";
    private static int _checks;

    [MenuItem("Tools/Character/Player/Validate Ultimate Sequence Evaluation")]
    public static void EvaluationMenu() => Debug.Log(RunEvaluation());

    public static string RunEvaluation()
    {
        _checks = 0;
        var definition = ScriptableObject.CreateInstance<PlayerUltimateDefinition>();
        try
        {
            var prep = Entry("Preparation", Load(PreparationPath), 0f, .1f);
            var attackA = Entry("Attack A", Load(AttackPath), .2f, .8f, 2f);
            var attackB = Entry("Attack B", Load(AttackPath), .4f, 1f, .5f);
            attackA.hits.Add(Hit("A", .4f, 17f));
            attackB.hits.Add(Hit("B", .6f, 23f));
            definition.sequence.AddRange(new[] { prep, attackA, attackB });
            Check(definition.HasSequence && definition.Ready, "A sequence with valid clips and root motion disabled is ready.");
            Near(attackA.Duration, .3f, "A trimmed source range is divided by that clip's speed.");
            Near(attackB.Duration, 1.2f, "Slower clips expand their own timeline duration.");
            Near(definition.Duration, 1.6f, "Sequence duration sums all trimmed, speed-adjusted clips.");
            Near(attackA.SourceTime(.1f), .4f, "Timeline elapsed time maps back to original source-clip seconds.");
            Near(attackA.SourceTime(99f), .8f, "Sampling past an item clamps to its trim endpoint.");
            Near(HitTime(definition, 1, attackA.hits[0]), .2f, "A local source hit maps into the sequence after preparation.");
            Near(HitTime(definition, 2, attackB.hits[0]), .8f, "A second attack uses its own trim and speed for hit timing.");
            Check(UltimateSequenceUtility.GetSegment(definition, .1f, out int index, out float elapsed) && index == 1,
                "An exact clip boundary selects the next clip.");
            Near(elapsed, 0f, "The next clip begins at zero local elapsed time.");

            definition.sequence.Remove(attackB);
            definition.sequence.Insert(1, attackB);
            Near(attackA.hits[0].time, .4f, "Reordering clips leaves Attack A's authored local hit timestamp unchanged.");
            Near(attackB.hits[0].time, .6f, "Reordering clips leaves Attack B's authored local hit timestamp unchanged.");
            Near(HitTime(definition, 2, attackA.hits[0]), 1.4f, "Attack A's global timing follows its reordered clip.");
            var duplicate = Clone(attackA);
            definition.sequence.Insert(3, duplicate);
            Near(duplicate.hits[0].time, .4f, "Duplicating an attack preserves source-local timing.");
            duplicate.hits[0].damage = 91;
            Near(attackA.hits[0].damage, 17, "Duplicated hit settings do not alias the original attack's settings.");
            Check(duplicate.id != attackA.id, "A duplicated sequence item has its own identity.");

            var motionA = Entry("Turn", Load(AttackPath), 0, 1);
            motionA.rootMotion = true;
            motionA.applyRootHeight = true;
            motionA.rootSamples = Samples(1, new Vector3(0, 1, 2), Quaternion.Euler(0, 90, 0));
            var motionB = Entry("Continue", Load(AttackPath), 0, 1);
            motionB.rootMotion = true;
            motionB.rootSamples = Samples(1, Vector3.forward * 3, Quaternion.identity);
            definition.sequence.Clear();
            definition.sequence.AddRange(new[] { motionA, motionB });
            Check(definition.Ready, "Two cached root-motion items are ready.");
            Near(UltimateSequenceUtility.EvaluateMotion(definition, 0).position, Vector3.zero, "The sequence root starts at the cast origin.");
            var boundary = UltimateSequenceUtility.EvaluateMotion(definition, 1);
            Near(boundary.position, new Vector3(0, 1, 2), "The first clip keeps its root displacement at the boundary.");
            NearRotation(boundary.rotation, Quaternion.Euler(0, 90, 0), "The first clip's yaw carries into subsequent clips.");
            Near(UltimateSequenceUtility.EvaluateMotion(definition, 1.0001f).position, new Vector3(.0003f, 1, 2),
                "The next clip starts continuously at the previous endpoint, without returning to the origin.");
            var end = UltimateSequenceUtility.EvaluateMotion(definition, 2);
            Near(end.position, new Vector3(3, 1, 2), "The next clip's displacement is rotated into the accumulated root frame.");
            Check(end.usesHeight && UltimateSequenceUtility.UsesRootHeight(definition), "Authored vertical root motion is explicitly represented.");
            Near(UltimateSequenceUtility.EvaluateMotion(definition, 100).position, end.position, "Sampling beyond the sequence holds its final root position.");
            var early = UltimateSequenceUtility.EvaluateMotion(definition, .25f);
            var again = UltimateSequenceUtility.EvaluateMotion(definition, 2);
            Near(early.position, new Vector3(0, .25f, .5f), "Backward scrubbing returns the correct earlier root sample.");
            Near(again.position, end.position, "Reverse-then-forward scrubbing has no accumulated drift.");
            NearRotation(again.rotation, end.rotation, "Reverse scrubbing cannot accumulate additional yaw.");

            motionA.sourceStart = .25f;
            motionA.sourceEnd = .75f;
            motionA.speed = 2f;
            motionA.applyRootRotation = false;
            motionA.applyRootHeight = false;
            definition.sequence.Remove(motionB);
            Near(definition.Duration, .25f, "Root-motion sampling respects trim and local speed together.");
            Vector3 trimmedExpected = Quaternion.Inverse(Quaternion.Euler(0, 22.5f, 0)) * Vector3.forward;
            Near(UltimateSequenceUtility.EvaluateMotion(definition, definition.Duration).position, trimmedExpected,
                "Trimmed motion subtracts the source-start root and normalizes its facing.");
            NearRotation(UltimateSequenceUtility.EvaluateMotion(definition, definition.Duration).rotation, Quaternion.identity,
                "Disabling root rotation retains the cast-facing orientation.");
            return Pass("evaluation", "trim/speed, reordered and duplicated local hit times, accumulated root position/yaw, vertical mode and reverse scrubbing");
        }
        finally { Object.DestroyImmediate(definition); }
    }

    [MenuItem("Tools/Character/Player/Validate Ultimate Sequence Root Bake")]
    public static void BakeMenu() => Debug.Log(RunBake());

    public static string RunBake()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run source root extraction verification in Edit Mode.");
        _checks = 0;
        PlayerCore player = Object.FindAnyObjectByType<PlayerCore>();
        Check(player != null && player.Animator != null, "A scene player supplies the actual Avatar for root extraction.");
        var definition = ScriptableObject.CreateInstance<PlayerUltimateDefinition>();
        try
        {
            foreach (string path in new[] { PreparationPath, AttackPath, RecoveryPath })
            {
                var entry = Entry(path, Load(path), 0, -1);
                entry.rootMotion = true;
                definition.sequence.Add(entry);
            }
            Vector3 originalPosition = player.Animator.transform.position;
            Quaternion originalRotation = player.Animator.transform.rotation;
            bool sceneDirty = player.gameObject.scene.isDirty;
            var sourceHashes = definition.sequence.Select(e => AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(e.clip))).ToArray();
            var sourceDirty = definition.sequence.Select(e => EditorUtility.IsDirty(e.clip)).ToArray();
            UltimateRootMotionBaker.Bake(definition, player);
            Check(UltimateRootMotionBaker.IsCurrent(definition, player), "The baked transient sequence records the correct clip/Avatar cache identity.");
            bool anyMotion = false;
            for (int i = 0; i < definition.sequence.Count; i++)
            {
                var entry = definition.sequence[i];
                Check(entry.rootSamples.Count > 2, "Every source clip produces a sampled root track.");
                Near(entry.rootSamples[0].position, Vector3.zero, "Each source clip's root track has its own zero origin.");
                Near(entry.rootSamples[entry.rootSamples.Count - 1].time, entry.clip.length, "Root extraction includes the complete source endpoint.");
                Check(entry.rootSamples.Zip(entry.rootSamples.Skip(1), (a, b) => b.time > a.time).All(v => v),
                    "Root sample times strictly increase.");
                anyMotion |= entry.rootSamples.Any(s => s.position.sqrMagnitude > .0001f);
                Check(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(entry.clip)) == sourceHashes[i] &&
                      EditorUtility.IsDirty(entry.clip) == sourceDirty[i], "Extraction leaves the source animation asset unchanged.");
            }
            Check(anyMotion, "The three real source clips contain nonzero extracted root displacement.");
            Near(player.Animator.transform.position, originalPosition, "Baking leaves the scene player's position unchanged.");
            NearRotation(player.Animator.transform.rotation, originalRotation, "Baking leaves the scene player's rotation unchanged.");
            Check(player.gameObject.scene.isDirty == sceneDirty, "Baking does not change the scene's save state.");
            var repeat = UltimateRootMotionBaker.Extract(definition.sequence[0].clip, player.Animator);
            var firstEnd = definition.sequence[0].rootSamples.Last();
            Near(repeat.Last().position, firstEnd.position, "Repeated extraction produces the same endpoint independently of prior samples.");
            NearRotation(repeat.Last().rotation, firstEnd.rotation, "Repeated extraction produces the same final root rotation.");
            return Pass("root bake", "three actual source clips, cache identity, complete deterministic samples, source assets and scene preservation");
        }
        finally { Object.DestroyImmediate(definition); }
    }

    [MenuItem("Tools/Character/Player/Validate Ultimate Sequence Runtime (Play Mode)")]
    public static void RuntimeMenu() => Debug.Log(RunRuntime());

    public static string RunRuntime()
    {
        RequirePlay();
        _checks = 0;
        var definition = ScriptableObject.CreateInstance<PlayerUltimateDefinition>();
        GameObject fixture = null;
        try
        {
            var prep = Entry("Preparation", Load(PreparationPath), 0, .1f);
            var attackA = Entry("Attack A", Load(AttackPath), .2f, .5f);
            var attackB = Entry("Attack B", Load(AttackPath), 1f, 1.4f, 2f);
            var recovery = Entry("Recovery", Load(RecoveryPath), 0, .1f);
            attackA.hits.Add(Hit("A", .25f, 17f));
            attackB.hits.Add(Hit("B", 1.1f, 23f));
            // These lie completely outside B's trim and must never fire on entry/exit.
            attackB.hits.Add(Hit("Trimmed out", .3f, 777f));
            attackA.cues.Add(new UltimateCue { name = "A cue", time = .25f });
            attackB.cues.Add(new UltimateCue { name = "B cue", time = 1.1f });
            attackB.cues.Add(new UltimateCue { name = "Trimmed cue", time = .3f });
            var duplicate = Clone(attackA);
            duplicate.label = "Attack A duplicate";
            duplicate.cues[0].name = "A duplicate cue";
            definition.sequence.AddRange(new[] { prep, attackA, attackB, duplicate, recovery });
            definition.targetLayers = 1 << 6;
            fixture = Fixture("Ultimate sequence runtime verification", new Vector3(19000, 1000, 19000));
            PlayerCore player = MakePlayer(fixture, definition);
            GameObject victim = new GameObject("Sequence two-collider victim");
            victim.transform.SetParent(fixture.transform, false);
            victim.transform.localPosition = new Vector3(0, 1, 2);
            victim.layer = 6;
            victim.AddComponent<SphereCollider>().radius = .15f;
            victim.AddComponent<BoxCollider>().size = Vector3.one * .3f;
            var actor = victim.AddComponent<PlayerSkillHitTestActor>();
            Activate(fixture, player);
            var cues = new List<string>();
            player.Ultimate.CueTriggered += cue => cues.Add(cue.name);
            Check(player.TryBeginUltimate(), "The five-item preparation/three-attacks/recovery sequence starts.");
            Check(player.Ultimate.ActiveDefinition.sequence.Count == 5, "The active cast snapshots every sequence item.");
            attackB.hits[0].damage = 999f;
            attackB.speed = .25f;
            definition.sequence.Reverse();
            Check(player.Ultimate.ActiveDefinition.sequence[1].label == "Attack A", "Reordering authoring data does not reorder an active cast.");
            Near(player.Ultimate.ActiveDefinition.sequence[2].speed, 2f, "Changing a clip speed does not retime an active cast.");

            player.Ultimate.Tick(.35f);
            ResolveStationaryStep(player);
            Check(actor.Hits.Count == 1 && actor.Hits[0].DamageAmount == 17f,
                "Crossing the first local marker deals one hit despite two victim colliders.");
            Check(cues.SequenceEqual(new[] { "A cue" }), "The first source-local cue fires once on its attack item.");
            player.Ultimate.Tick(.2f);
            player.BeginHitStop();
            player.Ultimate.FixedTick();
            Check(actor.Hits.Count == 1, "Hitstop preserves a queued second attack without applying it early.");
            player.Ultimate.Tick(2f);
            Near(player.Ultimate.Time, .55f, "Hitstop freezes the sequence clock across a clip boundary.");
            player.EndHitStop();
            ResolveStationaryStep(player);
            Check(actor.Hits.Count == 2 && actor.Hits[1].DamageAmount == 23f,
                "The second attack resumes at its own trimmed, accelerated source time and snapshot damage.");
            Check(cues.SequenceEqual(new[] { "A cue", "B cue" }), "Only the in-trim second cue fires after resume.");
            player.Ultimate.FixedTick();
            Check(actor.Hits.Count == 2 && cues.Count == 2, "Repeated fixed sampling does not repeat hits or cues.");
            player.Ultimate.Tick(.2f);
            ResolveStationaryStep(player);
            Check(actor.Hits.Count == 3 && actor.Hits[2].DamageAmount == 17f,
                "A duplicated attack has independent victim history and can hit the same target again.");
            Check(cues.SequenceEqual(new[] { "A cue", "B cue", "A duplicate cue" }), "Duplicate attack cues have independent once-only state.");
            player.Ultimate.Tick(10f);
            ResolveStationaryStep(player);
            player.StateMachine.UpdateTick();
            Check(actor.Hits.Count == 3 && Mathf.Abs(actor.Health - 943f) < .001f,
                "The complete multi-attack cast totals 57 damage and excludes trimmed-away hits.");
            Check(!player.Ultimate.IsActive && player.StateMachine.CanStartUltimate,
                "Recovery completes the sequence and restores locomotion.");
            return Pass("runtime", "three independent attack instances, trim/speed, deep cast snapshot, source-local cues, collider/marker deduplication, queued hitstop and recovery");
        }
        finally
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            Object.DestroyImmediate(definition);
        }
    }

    [MenuItem("Tools/Character/Player/Validate Ultimate Marker Runtime (Play Mode)")]
    public static void MarkerRuntimeMenu() => Debug.Log(RunMarkerRuntime());

    public static string RunMarkerRuntime()
    {
        RequirePlay();
        _checks = 0;
        // The same observable checks cover both schedule builders. Sequence markers use
        // source-local seconds inside a trimmed clip running at twice its source speed.
        foreach (bool sequence in new[] { false, true })
        {
            var definition = ScriptableObject.CreateInstance<PlayerUltimateDefinition>();
            GameObject fixture = null;
            try
            {
                AnimationClip clip = Load(AttackPath);
                definition.clip = clip;
                definition.cooldown = 0f;
                definition.targetLayers = 1 << 6;
                definition.warp.enabled = false;
                UltimateSequenceClip entry = sequence ? Entry("Trimmed marker attack", clip, .2f, 1.2f, 2f) : null;
                if (sequence) definition.sequence.Add(entry);
                List<UltimateHitField> hits = sequence ? entry.hits : definition.hits;
                float Source(float elapsed) => sequence ? entry.SourceStart + elapsed * entry.speed : elapsed;
                // Deliberately unsorted authoring order. The first large step must
                // execute .1 before .2, while preserving A-before-B at equal times.
                hits.Add(Hit("At clip end", sequence ? entry.SourceEnd : clip.length, 13f));
                hits.Add(Hit("Same frame A", Source(.2f), 3f));
                hits.Add(Hit("Same frame B", Source(.2f), 5f));
                hits.Add(Hit("Earlier time listed later", Source(.1f), 2f));
                hits.Add(Hit("Empty when crossed", Source(.3f), 7f));
                hits.Add(Hit("Rejected when crossed", Source(.4f), 11f));
                hits.Add(Hit("At cast entry", Source(0f), 1f));
                var disabled = Hit("Disabled", Source(.25f), 999f);
                disabled.enabled = false;
                hits.Add(disabled);
                if (sequence)
                {
                    hits.Add(Hit("Before trim", .1f, 777f));
                    hits.Add(Hit("After trim", 1.3f, 777f));
                }

                string mode = sequence ? "Sequence" : "Legacy";
                fixture = Fixture("Ultimate " + mode + " marker verification", new Vector3(19800, 1000, 19800));
                PlayerCore player = MakePlayer(fixture, definition);
                var victim = new GameObject("Marker two-collider victim");
                victim.transform.SetParent(fixture.transform, false);
                victim.transform.localPosition = new Vector3(0, 1, 2);
                victim.layer = 6;
                victim.AddComponent<SphereCollider>().radius = .15f;
                victim.AddComponent<BoxCollider>().size = Vector3.one * .3f;
                var actor = victim.AddComponent<PlayerSkillHitTestActor>();
                Activate(fixture, player);
                Check(player.TryBeginUltimate(), mode + " marker cast starts.");
                Check(actor.DamageAttempts == 1 && actor.Hits.Select(h => h.DamageAmount).SequenceEqual(new[] { 1f }),
                    mode + " zero-time marker fires at entry once across compound colliders.");

                Advance(.25f);
                Check(actor.DamageAttempts == 4 && actor.Hits.Select(h => h.DamageAmount).SequenceEqual(new[] { 1f, 2f, 3f, 5f }),
                    mode + " a skipped frame executes unsorted markers chronologically and preserves independent same-frame authoring order.");
                Advance(0f);
                player.Ultimate.FixedTick();
                Check(actor.DamageAttempts == 4, mode + " a repeated clock sample cannot replay crossed markers.");

                victim.transform.localPosition = new Vector3(10, 1, 2);
                Physics.SyncTransforms();
                Advance(.06f);
                Check(actor.DamageAttempts == 4, mode + " an empty marker resolves without hitting an absent target.");
                victim.transform.localPosition = new Vector3(0, 1, 2);
                Physics.SyncTransforms();
                Advance(.02f);
                Check(actor.DamageAttempts == 4, mode + " a target entering after the empty marker receives no late hit.");

                actor.RejectDamage = true;
                Advance(.08f);
                Check(actor.DamageAttempts == 5 && actor.Hits.Count == 4,
                    mode + " a rejecting compound-collider target gets only one damage attempt for its marker.");
                actor.RejectDamage = false;
                Advance(.02f);
                Check(actor.DamageAttempts == 5 && actor.Hits.Count == 4,
                    mode + " becoming damageable after the rejected marker does not retry it.");

                float beforeStop = player.Ultimate.Time;
                player.BeginHitStop();
                Advance(10f);
                Check(Mathf.Abs(player.Ultimate.Time - beforeStop) < .0001f && actor.DamageAttempts == 5,
                    mode + " hitstop preserves the clock and does not consume the future endpoint marker.");
                player.EndHitStop();
                Advance(10f);
                Check(actor.DamageAttempts == 6 && actor.Hits.Select(h => h.DamageAmount).SequenceEqual(new[] { 1f, 2f, 3f, 5f, 13f }),
                    mode + " the exact clip-end marker fires once after resume; disabled and trimmed-away markers never fire.");
                Advance(0f);
                Check(actor.DamageAttempts == 6, mode + " resampling the clip end cannot repeat its marker.");
                player.StateMachine.UpdateTick();
                Check(!player.Ultimate.IsActive && player.StateMachine.CanStartUltimate, mode + " marker completion releases the cast.");

                Check(player.TryBeginUltimate(), mode + " a fresh marker cast is accepted.");
                Check(actor.DamageAttempts == 7 && actor.Hits.Count == 6 && actor.Hits[5].DamageAmount == 1f,
                    mode + " a fresh cast resets the zero-time marker's fired state.");
                Advance(.25f);
                Check(actor.DamageAttempts == 10 && actor.Hits.Select(h => h.DamageAmount).SequenceEqual(new[] { 1f, 2f, 3f, 5f, 13f, 1f, 2f, 3f, 5f }),
                    mode + " a fresh cast resets each independent same-frame marker.");

                void Advance(float delta)
                {
                    player.Ultimate.Tick(delta);
                    if (sequence) ResolveStationaryStep(player);
                }
            }
            finally
            {
                if (fixture != null) Object.DestroyImmediate(fixture);
                Object.DestroyImmediate(definition);
            }
        }
        return Pass("marker runtime", "legacy and trimmed/accelerated sequence markers, chronological dispatch from unsorted rows, same-frame stable order, compound-collider attempts including rejection, empty-marker consumption, zero/end boundaries, hitstop and recast reset");
    }

    public static string RunResolvedHitStop()
    {
        RequirePlay();
        _checks = 0;
        var definition = ScriptableObject.CreateInstance<PlayerUltimateDefinition>();
        GameObject fixture = null;
        try
        {
            var attack = Entry("Root attack", Load(AttackPath), 0, .4f);
            attack.rootMotion = true;
            attack.rootSamples = Samples(.4f, Vector3.forward * 2, Quaternion.identity);
            var hit = Hit("Stop on resolved hit", .05f, 11);
            hit.position = new Vector3(0, 1, 1);
            hit.size = Vector3.one * 4;
            attack.hits.Add(hit);
            definition.sequence.Add(attack);
            definition.targetLayers = 1 << 6;
            fixture = Fixture("Ultimate resolved-hitstop verification", new Vector3(19500, 1000, 19500));
            PlayerCore player = MakePlayer(fixture, definition);
            var victim = new GameObject("Resolved hit target");
            victim.transform.SetParent(fixture.transform, false);
            victim.transform.localPosition = new Vector3(0, 1, 1.5f);
            victim.layer = 6;
            victim.AddComponent<SphereCollider>().radius = .15f;
            var actor = victim.AddComponent<PlayerSkillHitTestActor>();
            Activate(fixture, player);
            Rigidbody body = player.GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.None;
            player.Ultimate.FieldHit += (_, __) => player.BeginHitStop();
            Check(player.TryBeginUltimate(), "The root attack with a resolved-hit callback starts.");
            Quaternion facing = player.Ultimate.Facing;
            player.Ultimate.Tick(.1f);
            player.Ultimate.FixedTick();
            Vector3 firstMove = player.Mover.ConsumePendingDisplacement();
            Near(firstMove, facing * Vector3.forward * .5f, "The first queued root step carries half a meter.");
            // This command regression updates only the isolated actor's body position;
            // RunPhysics separately exercises actual integration in a local PhysicsScene.
            body.position += firstMove;
            player.transform.position = body.position;
            Physics.SyncTransforms();
            player.Ultimate.Tick(.1f);
            player.Ultimate.FixedTick();
            Check(actor.Hits.Count == 1 && player.IsHitStopped,
                "Resolving the prior movement lands on a real target and synchronously starts attacker hitstop.");
            Near(player.Mover.ConsumePendingDisplacement(), Vector3.zero,
                "The same FixedTick cannot submit newer movement after its hit callback stops the mover.");
            player.EndHitStop();
            player.Ultimate.FixedTick();
            Vector3 resumedMove = player.Mover.ConsumePendingDisplacement();
            Near(resumedMove, facing * Vector3.forward * .5f,
                "After hitstop, the newer unconsumed root interval is still submitted instead of being lost.");
            body.position += resumedMove;
            player.transform.position = body.position;
            player.Ultimate.FixedTick();
            Check(actor.Hits.Count == 1, "Resolving the resumed movement does not repeat the earlier target hit.");
            return Pass("resolved hitstop", "real target hit callback, stopped motor guard, preserved newer root interval and hit deduplication");
        }
        finally
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            Object.DestroyImmediate(definition);
        }
    }

    [MenuItem("Tools/Character/Player/Validate Ultimate Sequence Local Physics (Play Mode)")]
    public static void PhysicsMenu() => Debug.Log(RunPhysics());

    public static string RunPhysics()
    {
        RequirePlay();
        _checks = 0;
        Scene scene = default;
        GameObject fixture = null;
        var definition = ScriptableObject.CreateInstance<PlayerUltimateDefinition>();
        try
        {
            scene = SceneManager.CreateScene("UltimateSequenceVerification_" + Guid.NewGuid().ToString("N"),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            PhysicsScene physics = scene.GetPhysicsScene();
            Check(physics.IsValid() && !physics.Equals(Physics.defaultPhysicsScene), "Sequence physics uses an isolated simulation.");
            float step = Time.fixedDeltaTime;
            float partDuration = step * 20;
            var first = Entry("Rise and turn", Load(AttackPath), 0, partDuration);
            first.rootMotion = true;
            first.applyRootHeight = true;
            first.rootSamples = Samples(partDuration, new Vector3(0, 1, 2), Quaternion.Euler(0, 90, 0));
            var second = Entry("Continue after turn", Load(AttackPath), 0, partDuration);
            second.rootMotion = true;
            second.applyRootHeight = true;
            second.rootSamples = Samples(partDuration, Vector3.forward * 3, Quaternion.identity);
            definition.sequence.AddRange(new[] { first, second });
            definition.targetLayers = 0;
            fixture = Fixture("Ultimate sequence physics verification", new Vector3(0, 1000, 0));
            SceneManager.MoveGameObjectToScene(fixture, scene);
            PlayerCore player = MakePlayer(fixture, definition);
            var moverSettings = new SerializedObject(player.GetComponent<PlayerMover>());
            moverSettings.FindProperty("_groundLayerMask").intValue = 0;
            moverSettings.ApplyModifiedPropertiesWithoutUndo();
            Activate(fixture, player);
            player.Rotator.RotateImmediately(Vector3.right);
            Rigidbody body = player.GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.None;
            body.useGravity = false;
            Vector3 origin = body.position;
            Check(player.TryBeginUltimate(), "The root-motion sequence begins with a rotated cast facing.");
            Quaternion castFacing = player.Ultimate.Facing;
            MethodInfo applyMover = typeof(PlayerMover).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(applyMover != null, "The simulation applies the actual PlayerMover velocity path.");
            Vector3 firstEndpoint = origin + castFacing * new Vector3(0, 1, 2);
            Vector3 finalEndpoint = origin + castFacing * new Vector3(3, 1, 2);
            for (int i = 1; i <= 40; i++)
            {
                player.Ultimate.Tick(step);
                player.Ultimate.FixedTick();
                if (i == 10)
                {
                    Vector3 before = body.position;
                    player.BeginHitStop();
                    player.Ultimate.Tick(step * 4);
                    player.Ultimate.FixedTick();
                    applyMover.Invoke(player.Mover, null);
                    physics.Simulate(step);
                    NearPhysics(body.position, before, "Hitstop keeps the actual body stationary before a queued XYZ root step is applied.");
                    Near(player.Ultimate.Time, step * 10, "Hitstop also preserves the sequence source-time mapping.");
                    player.EndHitStop();
                    player.Ultimate.FixedTick();
                }
                applyMover.Invoke(player.Mover, null);
                physics.Simulate(step);
                if (i == 20) NearPhysics(body.position, firstEndpoint, "The physical body reaches the first elevated root endpoint.");
                if (i == 21) Check(Vector3.Distance(body.position, firstEndpoint) < .3f,
                    "The second clip begins near the first endpoint instead of returning to the cast origin.");
            }
            NearPhysics(body.position, finalEndpoint, "The second clip accumulates translation in the first clip's rotated root frame.");
            player.Ultimate.Tick(1f);
            player.Ultimate.FixedTick();
            applyMover.Invoke(player.Mover, null);
            physics.Simulate(step);
            player.Ultimate.FixedTick();
            player.StateMachine.UpdateTick();
            NearPhysics(body.position, finalEndpoint, "Completing the sequence does not snap the body back to its original position.");
            Check(!player.Ultimate.IsActive && player.StateMachine.CanStartUltimate, "The physical sequence releases its graph and returns to locomotion.");
            player.Mover.Move(Vector3.up);
            applyMover.Invoke(player.Mover, null);
            Check(!player.Mover.UsesRootMotionHeight && body.linearVelocity.y <= 0f,
                "After exit, authored upward velocity is filtered and normal landing behavior resumes.");
            return Pass("local physics", "real XYZ root displacement, accumulated yaw, boundary continuity, hitstop retention, no origin reset and restored movement mode") + " Temporary scene unload queued; default scene was not simulated.";
        }
        finally
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            Object.DestroyImmediate(definition);
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }
    }

    [MenuItem("Tools/Character/Player/Validate Ultimate Pull (Play Mode)")]
    public static async void PullMenu()
    {
        try { Debug.Log(await RunPull()); }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    public static async Awaitable<string> RunPull()
    {
        RequirePlay();
        int checks = 0;
        void CheckPull(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Ultimate pull verification: " + message);
            checks++;
        }
        void NearPull(Vector3 actual, Vector3 expected, string message)
            => CheckPull(Vector3.Distance(actual, expected) < .025f, $"{message} ({actual:F4} vs {expected:F4})");
        var template = Object.FindAnyObjectByType<EnemyCore>();
        CheckPull(template != null, "The open combat scene has an EnemyCore to clone.");
        GameObject fixture = null;
        var definition = ScriptableObject.CreateInstance<PlayerUltimateDefinition>();
        try
        {
            definition.clip = Load(AttackPath);
            definition.cooldown = 0f;
            definition.targetLayers = 1 << 6;
            var pull = new UltimatePull { id = "verification", speed = 20f, duration = 1f, acceleration = 0f, deceleration = 0f };
            definition.pulls.Add(pull);
            var miss = Hit("First anchor, misses", 0f, 1f);
            miss.position = Vector3.right * 20f;
            miss.pullId = pull.id;
            var hit = Hit("Second marker, control only", .1f, 0f);
            hit.size = new Vector3(3f, 5f, 5f);
            hit.pullId = pull.id;
            var refresh = Hit("Refresh", .2f, 1f);
            refresh.size = hit.size;
            refresh.pullId = pull.id;
            var release = Hit("Release even when it misses", .4f, 0f);
            release.position = miss.position;
            release.releasePullId = pull.id;
            definition.hits.AddRange(new[] { miss, hit, refresh, release });

            Vector3 origin = new(4000f, 500f, 4000f);
            fixture = Fixture("Ultimate pull verification", origin);
            var player = MakePlayer(fixture, definition);
            var enemy = Object.Instantiate(template, fixture.transform, false);
            enemy.transform.SetLocalPositionAndRotation(Vector3.forward * 4f, Quaternion.identity);
            foreach (var collider in enemy.GetComponentsInChildren<Collider>(true)) collider.gameObject.layer = 6;
            foreach (var source in enemy.GetComponentsInChildren<AudioSource>(true)) { source.mute = true; source.playOnAwake = false; }
            Activate(fixture, player);
            enemy.StateMachine.InitEnemyStateMachine(enemy.StateMachine.IdleState);
            enemy.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            player.Rotator.RotateImmediately(Vector3.forward);
            CheckPull(player.Ultimate.Begin() && !enemy.Control.Active, "A missed zero-time marker fixes the anchor without controlling anyone.");
            Quaternion facing = player.Ultimate.Facing;
            Vector3 center = pull.Center(origin, facing);
            float originalRadius = pull.radius;
            pull.offset = new Vector2(0f, -10f);
            pull.radius = 1.5f;
            var playerBody = player.GetComponent<Rigidbody>();
            playerBody.position = origin + Vector3.forward;
            player.transform.position = playerBody.position;
            player.Ultimate.Tick(.11f);
            CheckPull(enemy.Control.Active && player.Ultimate.LastHitCount == 1, "An actual overlapping zero-damage marker applies control.");
            player.Ultimate.Tick(.1f);
            CheckPull(enemy.Control.Active && player.Ultimate.LastHitCount == 2, "The linked second hit applies damage while keeping control.");

            var body = enemy.GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.None;
            playerBody.useGravity = false;
            async Awaitable Simulate(float seconds)
            {
                for (int i = 0; i < Mathf.CeilToInt(seconds / Time.fixedDeltaTime); i++)
                    await Awaitable.FixedUpdateAsync();
            }
            Vector3 initial = body.position;
            enemy.BeginHitStop();
            await Simulate(1.1f);
            NearPull(body.position, initial, "Hitstop freezes the real rigidbody.");
            enemy.EndHitStop();
            CheckPull(enemy.Control.Active, "Hitstop also freezes the control lifetime.");
            await Simulate(.3f);
            NearPull(body.position, center + Vector3.forward * originalRadius,
                "The body reaches the authored radius at the original missed marker's center, using the cast snapshot.");

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(fixture.transform, true);
            wall.transform.position = origin + Vector3.forward * 3f + Vector3.up;
            wall.transform.localScale = new Vector3(8f, 5f, .15f);
            enemy.Mover.WarpTo(initial);
            Physics.SyncTransforms();
            await Simulate(.3f);
            CheckPull(body.position.z > wall.transform.position.z, "The real body remains on its side of a blocking wall.");
            player.Ultimate.Tick(.2f);
            CheckPull(!enemy.Control.Active, "The release marker clears control before its damage query, even when it misses.");
            player.Ultimate.Stop();
            return $"PlayerUltimateSequenceVerification pull: PASS ({checks} checks). Real enemy hit, first-miss anchor, cast snapshot, linked hits, hitstop, radius, wall and release.";
        }
        finally
        {
            if (fixture != null) Object.DestroyImmediate(fixture);
            Object.DestroyImmediate(definition);
        }
    }

    private static AnimationClip Load(string path)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) throw new InvalidOperationException("Missing sequence test source: " + path);
        return clip;
    }

    private static UltimateSequenceClip Entry(string label, AnimationClip clip, float start, float end, float speed = 1f)
        => new UltimateSequenceClip { label = label, clip = clip, sourceStart = start, sourceEnd = end, speed = speed, rootMotion = false };

    private static UltimateSequenceClip Clone(UltimateSequenceClip source)
    {
        var copy = JsonUtility.FromJson<UltimateSequenceClip>(JsonUtility.ToJson(source));
        copy.clip = source.clip;
        copy.id = Guid.NewGuid().ToString("N");
        return copy;
    }

    private static List<UltimateRootSample> Samples(float end, Vector3 displacement, Quaternion turn)
        => new List<UltimateRootSample>
        {
            new UltimateRootSample { time = 0, position = Vector3.zero, rotation = Quaternion.identity },
            new UltimateRootSample { time = end, position = displacement, rotation = turn }
        };

    private static UltimateHitField Hit(string name, float time, float damage)
        => new UltimateHitField { name = name, time = time, damage = damage, hitStopFrames = 0,
            position = new Vector3(0, 1, 2), size = Vector3.one * 2 };

    private static float HitTime(PlayerUltimateDefinition definition, int index, UltimateHitField field)
        => UltimateSequenceUtility.GetSegmentStart(definition, index) +
           (field.time - definition.sequence[index].SourceStart) / Mathf.Max(.05f, definition.sequence[index].speed);

    private static GameObject Fixture(string name, Vector3 position)
    {
        var fixture = new GameObject(name);
        fixture.SetActive(false);
        fixture.transform.position = position;
        return fixture;
    }

    private static PlayerCore MakePlayer(GameObject fixture, PlayerUltimateDefinition definition)
    {
        var clone = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath), fixture.transform, false);
        clone.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var source in clone.GetComponentsInChildren<AudioSource>(true)) { source.mute = true; source.playOnAwake = false; }
        var player = clone.GetComponent<PlayerCore>();
        var serialized = new SerializedObject(player);
        serialized.FindProperty("_ultimateDefinition").objectReferenceValue = definition;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return player;
    }

    private static void Activate(GameObject fixture, PlayerCore player)
    {
        fixture.SetActive(true);
        Physics.SyncTransforms();
        player.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        player.StateMachine.InitStateMachine(player.StateMachine.IdleState);
    }

    private static void ResolveStationaryStep(PlayerCore player)
    {
        player.Ultimate.FixedTick();
        Vector3 displacement = player.Mover.ConsumePendingDisplacement();
        if (displacement.sqrMagnitude > .000001f)
            throw new InvalidOperationException("This fixture must remain stationary; moving sequences use the local physics verification.");
        player.Ultimate.FixedTick();
    }

    private static void RequirePlay()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run sequence runtime verification in Play Mode.");
    }
    private static string Pass(string category, string details) => "PlayerUltimateSequenceVerification " + category + ": PASS (" + _checks + " checks). " + details + ".";
    private static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .001f, message + " (" + actual + " vs " + expected + ")");
    private static void Near(Vector3 actual, Vector3 expected, string message) => Check(Vector3.Distance(actual, expected) < .003f, message + " (" + actual.ToString("F4") + " vs " + expected.ToString("F4") + ")");
    private static void NearPhysics(Vector3 actual, Vector3 expected, string message) => Check(Vector3.Distance(actual, expected) < .025f, message + " (" + actual.ToString("F4") + " vs " + expected.ToString("F4") + ")");
    private static void NearRotation(Quaternion actual, Quaternion expected, string message) => Check(Quaternion.Angle(actual, expected) < .02f, message);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Ultimate sequence verification: " + message);
        _checks++;
    }
}
