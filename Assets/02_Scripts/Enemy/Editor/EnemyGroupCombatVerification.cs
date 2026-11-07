using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Deterministic integration checks against the real group coordinators. The fixture
/// uses isolated temporary scenes; it never plays animations or saves assets.
/// </summary>
public static class EnemyGroupCombatVerification
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const string ReportPath = "Temp/EnemyGroupCombatVerification.result";
    private static readonly List<string> Evidence = new();

    [MenuItem("Tools/Character/Enemy/Validate Group Combat")]
    public static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Group combat verification runs in Edit Mode.");
        Evidence.Clear();
        var random = UnityEngine.Random.state;
        Scene activeScene = SceneManager.GetActiveScene();
        bool dirtyBefore = activeScene.isDirty;
        Directory.CreateDirectory("Temp");
        try
        {
            UnityEngine.Random.InitState(314159);
            VerifyTimingOrderAndFairness();
            VerifyLeaseAndLifecycle();
            VerifySuspension();
            VerifyPositioning();
            VerifyPositioningOwnership();
            VerifyWorldObstacles();
            VerifyStateTransitions();
            Check(SceneManager.GetActiveScene() == activeScene && activeScene.isDirty == dirtyBefore,
                "The active authored scene and its dirty state are preserved.");
            string result = $"EnemyGroupCombatVerification: PASS ({Evidence.Count} checks). " +
                "Request-order independence, fair turns, exclusive attack ownership, recovery, " +
                "lease expiry, lifecycle, pause, reach, lanes, stable slots, ownership transfer, " +
                "world-obstacle reassignment and overlap checked. " +
                "This is deterministic logic coverage, not a gameplay or animation-quality verdict.";
            File.WriteAllText(ReportPath, result + "\n" + string.Join("\n", Evidence));
            return result;
        }
        catch (Exception exception)
        {
            File.WriteAllText(ReportPath, "FAIL: " + exception + "\n" + string.Join("\n", Evidence));
            throw;
        }
        finally { UnityEngine.Random.state = random; }
    }

    private static void VerifyTimingOrderAndFairness()
    {
        using var fixture = new Fixture(4, false);
        var timing = fixture.Timing;
        foreach (EnemyCore enemy in fixture.Enemies)
            Check(!timing.IsReadyToAttack(enemy, 4f), "Initial requests collect before a turn is granted.");
        fixture.Tick(.1f);
        EnemyCore forwardWinner = timing.SelectedAttacker;
        Check(forwardWinner != null, "The collected group receives a preparation turn.");

        fixture.ReplaceTiming();
        timing = fixture.Timing;
        for (int i = fixture.Enemies.Length - 1; i >= 0; i--)
            timing.IsReadyToAttack(fixture.Enemies[i], 4f);
        fixture.Tick(.1f);
        Check(timing.SelectedAttacker == forwardWinner,
            "Reversing request order leaves an equally ready group's selected attacker unchanged.");

        var counts = new Dictionary<EnemyCore, int>();
        foreach (EnemyCore enemy in fixture.Enemies) counts[enemy] = 0;
        for (int turn = 0; turn < 12; turn++)
        {
            fixture.SubmitAll(turn % 2 == 0);
            if (timing.SelectedAttacker == null) fixture.Tick(.25f);
            EnemyCore selected = timing.SelectedAttacker;
            Check(selected != null && timing.TryBeginAttack(selected), "A prepared turn commits exactly one attacker.");
            counts[selected]++;
            fixture.SetState(selected, selected.StateMachine.CloseAttackNoticeState);
            foreach (EnemyCore other in fixture.Enemies)
                if (other != selected)
                    Check(!timing.TryBeginAttack(other), "Other enemies cannot commit during an attack sequence.");
            fixture.Tick(.2f);
            fixture.SetState(selected, selected.StateMachine.EngageState);
            timing.ReleaseAttack(selected);
            timing.PrioritizeAttack(selected);
            Check(!timing.IsReadyToAttack(selected, 4f), "Urgent counter requests cannot bypass the repeat cooldown.");
            fixture.SubmitAll(turn % 2 != 0);
            fixture.Tick(.1f);
            Check(timing.SelectedAttacker == null, "The shared recovery gap remains empty after attack completion.");
            fixture.SubmitAll(turn % 2 == 0);
            fixture.Tick(.15f);
        }
        foreach (var count in counts)
            Check(count.Value >= 2, "Every continuously eligible enemy receives repeated turns within twelve attacks.");
    }

    private static void VerifyLeaseAndLifecycle()
    {
        using var fixture = new Fixture(3, false);
        var timing = fixture.Timing;
        fixture.SubmitAll();
        fixture.Tick(.1f);
        EnemyCore first = timing.SelectedAttacker;
        fixture.SubmitAll();
        fixture.Tick(1.21f);
        Check(timing.SelectedAttacker != null && timing.SelectedAttacker != first,
            "An unconsumed preparation lease backs off and gives another enemy a turn.");
        EnemyCore attacker = timing.SelectedAttacker;
        Check(timing.TryBeginAttack(attacker), "A replacement preparation turn can commit.");
        fixture.SetState(attacker, attacker.StateMachine.CloseAttackState);
        Set(attacker, "_isHitStopped", true);
        fixture.Tick(10f);
        Check(timing.ActiveAttacker == attacker,
            "A live attack retains exclusion through long animation or hitstop delays.");
        Set(attacker, "_isHitStopped", false);

        timing.enabled = false;
        Invoke(timing, "OnDisable");
        Check(attacker.AttackTimingController == timing && timing.SelectedAttacker == null &&
              !timing.IsReadyToAttack(attacker, 4f) && !timing.TryBeginAttack(attacker),
            "Disabling a coordinator retains binding and fails closed instead of allowing independent attacks.");
        timing.enabled = true;
        Invoke(timing, "OnEnable");
        fixture.Tick(.01f);
        Check(timing.ActiveAttacker == attacker && attacker.AttackTimingController == timing,
            "Re-enabled coordination adopts an already running attack before granting another.");

        fixture.SetState(attacker, attacker.StateMachine.EngageState);
        fixture.Tick(.2f);
        Check(timing.ActiveAttacker == null, "A cancelled attack state cannot strand the exclusive token.");
        timing.Unregister(attacker);
        Check(attacker.AttackTimingController == null && !timing.HasAttackIntent(attacker),
            "Unregister clears both the enemy binding and any queued intent.");
        fixture.Tick(.3f);
        fixture.SubmitAll();
        fixture.Tick(.1f);
        EnemyCore selected = timing.SelectedAttacker;
        Check(selected != null, "The remaining group recovers after token cleanup.");
        Set(selected, "_currentHP", 0f);
        fixture.Tick(.01f);
        Check(timing.SelectedAttacker != selected && selected.AttackTimingController == null,
            "Death during preparation removes the participant and releases its turn.");
        Set(selected, "_currentHP", 100f);
        timing.Register(selected);
        fixture.SubmitAll();
        fixture.Tick(.1f);
        selected = timing.SelectedAttacker;
        Check(selected != null, "A recovered participant can join scheduling again.");
        Set(selected.TargetDetector, "_targetCollider", null);
        fixture.Tick(.01f);
        Check(!timing.HasAttackIntent(selected) && timing.SelectedAttacker != selected,
            "Target loss immediately invalidates a queued preparation turn.");
    }

    private static void VerifySuspension()
    {
        using var fixture = new Fixture(2, false);
        fixture.SubmitAll();
        fixture.Tick(.1f);
        var timing = fixture.Timing;
        EnemyCore selected = timing.SelectedAttacker;
        Check(selected != null, "Pause fixture has a real selected preparation turn.");
        FieldInfo resumeFrame = typeof(ProjectR.UI.PauseSettingsController)
            .GetField("_resumeAfterFrame", PrivateStatic);
        object original = resumeFrame.GetValue(null);
        float before = Get<float>(timing, "_combatTime");
        try
        {
            resumeFrame.SetValue(null, int.MaxValue);
            fixture.Tick(30f);
            Near(Get<float>(timing, "_combatTime"), before, "Input suspension freezes the group combat clock.");
            Check(!timing.IsReadyToAttack(selected, 4f) && !timing.TryBeginAttack(selected),
                "A selected attack cannot commit while combat input is suspended.");
        }
        finally { resumeFrame.SetValue(null, original); }
        fixture.Tick(.01f);
        Check(timing.SelectedAttacker == selected,
            "Resuming retains the unused preparation lease instead of consuming paused time.");
    }

    private static void VerifyPositioning()
    {
        using var fixture = new Fixture(12, true);
        var positioner = fixture.Positioning;
        // These checks isolate group geometry. VerifyWorldObstacles separately
        // exercises the real Physics queries against a temporary box collider.
        Set(positioner, "_obstacleLayers", (LayerMask)0);
        var assigned = new Vector3[fixture.Enemies.Length];
        for (int i = 0; i < fixture.Enemies.Length; i++)
        {
            Check(positioner.TryGetAssignedPosition(fixture.Enemies[i], out assigned[i]),
                "Every registered enemy receives a holding position.");
            Check(Finite(assigned[i]), "Holding positions are finite even above one ring's capacity.");
            for (int j = 0; j < i; j++)
                Check((assigned[i] - assigned[j]).sqrMagnitude > .01f,
                    "Different enemies reserve different holding positions.");
        }
        for (int i = fixture.Enemies.Length - 1; i >= 0; i--)
        {
            positioner.TryGetAssignedPosition(fixture.Enemies[i], out Vector3 repeated);
            Near(repeated, assigned[i], "Repeated reverse-order queries preserve existing holding slots.");
        }
        Vector3 movement = new(2f, 0f, -1f);
        fixture.Target.transform.position += movement;
        positioner.TryGetAssignedPosition(fixture.Enemies[0], out Vector3 translated);
        Near(translated, assigned[0] + movement, "The formation follows target translation without slot reshuffling.");
        fixture.Target.transform.position -= movement;

        for (int i = 2; i < fixture.Enemies.Length; i++)
            positioner.Unregister(fixture.Enemies[i]);
        EnemyCore attacker = fixture.Enemies[0], blocker = fixture.Enemies[1];
        attacker.transform.position = new Vector3(0f, 0f, -3f);
        blocker.transform.position = new Vector3(4f, 0f, 0f);
        Check(positioner.IsAttackPositionReady(attacker, 4f), "An unobstructed in-range attacker is ready.");
        Check(!positioner.IsAttackPositionReady(attacker, 2f), "A short weapon cannot inherit a longer encounter reach.");
        attacker.transform.position = new Vector3(0f, 0f, -5f);
        Check(!positioner.IsAttackPositionReady(attacker, 20f), "A long weapon remains bounded by the encounter envelope.");
        attacker.transform.position = new Vector3(0f, 0f, -3f);
        blocker.transform.position = new Vector3(0f, 0f, -1.5f);
        Check(!positioner.IsAttackPositionReady(attacker, 4f), "A teammate in the attack corridor blocks commitment.");
        Check(Get<EnemyCore>(positioner, "_laneRequester") == null,
            "Read-only candidate evaluation never claims a lane or changes group priority.");
        Check(!positioner.CanBeginAttack(attacker, 4f) && Get<EnemyCore>(positioner, "_laneRequester") == attacker,
            "The chosen attacker's blocked commitment requests a yielding lane.");
        blocker.transform.position = new Vector3(4f, 0f, 0f);
        Check(positioner.CanBeginAttack(attacker, 4f), "Clearing the corridor allows the prepared attacker to commit.");
        positioner.NotifyAttackStarted(attacker);
        Check(!positioner.CanBeginAttack(blocker, 4f), "Positioning also excludes a second committed attacker.");
        positioner.NotifyAttackEnded(attacker);
        Check(positioner.CommittedAttacker == null, "Attack completion clears positioning ownership.");

        attacker.transform.position = blocker.transform.position = new Vector3(0f, 0f, -3f);
        IDictionary states = Get<IDictionary>(positioner, "_agentStates");
        Vector3 a = (Vector3)Invoke(positioner, "CalculateSteering", attacker, states[attacker]);
        Vector3 b = (Vector3)Invoke(positioner, "CalculateSteering", blocker, states[blocker]);
        Check(Finite(a) && Finite(b) && (a - b).sqrMagnitude > .01f,
            "Exact overlapping spawns receive finite, different escape steering.");
        Vector3 velocity = positioner.AdjustMovement(attacker, new Vector3(0f, .3f, 2f));
        Check(Finite(velocity) && new Vector2(velocity.x, velocity.z).magnitude <= 2.001f &&
              Mathf.Abs(velocity.y - .3f) < .001f,
            "Crowd steering respects the authored horizontal speed and vertical motion.");
        positioner.Unregister(attacker);
        Check(!positioner.TryGetAssignedPosition(attacker, out _) && attacker.PositioningController == null,
            "Unregister releases the holding slot and enemy binding.");
    }

    private static void VerifyPositioningOwnership()
    {
        using var fixture = new Fixture(1, true);
        EnemyCore enemy = fixture.Enemies[0];
        var original = fixture.Positioning;
        var replacement = fixture.Make("Replacement positioning").AddComponent<EnemyPositioningController>();
        Set(replacement, "_autoDiscoverEnemies", false);
        Set(replacement, "_obstacleLayers", (LayerMask)0);
        Invoke(replacement, "OnEnable");
        try
        {
            replacement.Register(enemy);
            Check(enemy.PositioningController == replacement &&
                  !Get<IList>(original, "_enemies").Contains(enemy) &&
                  !Get<IDictionary>(original, "_agentStates").Contains(enemy),
                "Transferring an enemy removes both runtime membership and slot ownership from its previous coordinator.");
            Check(replacement.TryGetAssignedPosition(enemy, out Vector3 assigned),
                "A transferred enemy acquires a slot from its new coordinator.");
            original.enabled = false;
            Invoke(original, "OnDisable");
            original.enabled = true;
            Invoke(original, "OnEnable");
            Check(enemy.PositioningController == replacement &&
                  !original.TryGetAssignedPosition(enemy, out _),
                "Disabling and re-enabling the old coordinator cannot steal a transferred participant back.");
            replacement.TryGetAssignedPosition(enemy, out Vector3 retained);
            Near(retained, assigned, "The new coordinator's reservation survives the old coordinator's lifecycle.");
        }
        finally { Invoke(replacement, "OnDisable"); }
    }

    private static void VerifyWorldObstacles()
    {
        // Preview scenes have isolated physics worlds. A hidden additive scene
        // using default physics lets the production static Physics casts see the
        // wall. An otherwise unused layer and distant position isolate the query.
        using var fixture = new Fixture(1, true, true);
        var positioner = fixture.Positioning;
        EnemyCore enemy = fixture.Enemies[0];
        const int obstacleLayer = 29;
        const int obstacleMask = 1 << obstacleLayer;
        Vector3 origin = new(2000f, 0f, 2000f);
        fixture.Target.transform.position = origin;
        enemy.transform.position = origin + Vector3.back * 6f;
        Set(positioner, "_obstacleLayers", (LayerMask)obstacleMask);
        Check(positioner.TryGetAssignedPosition(enemy, out Vector3 originalSlot),
            "An unobstructed actor initially receives a reachable holding slot.");

        GameObject wall = fixture.Make("Temporary slot obstacle");
        wall.layer = obstacleLayer;
        wall.transform.position = originalSlot + Vector3.forward * .6f + Vector3.up * 1.5f;
        var collider = wall.AddComponent<BoxCollider>();
        collider.size = new Vector3(.8f, 3f, .2f);
        Physics.SyncTransforms();
        Check(SlotPathClear(enemy, originalSlot, obstacleMask),
            "The initial reserved slot stops before the wall and is still reachable.");

        Vector3 smallTargetMove = Vector3.forward * 1.2f;
        fixture.Target.transform.position += smallTargetMove;
        Physics.SyncTransforms();
        Vector3 blockedSlot = originalSlot + smallTargetMove;
        Check(!SlotPathClear(enemy, blockedSlot, obstacleMask),
            "A small target movement carries the existing reservation behind a real world obstacle.");
        Set(positioner, "_combatClock", 5f);
        positioner.TryGetAssignedPosition(enemy, out Vector3 firstProbe);
        Near(firstProbe, blockedSlot, "A single blocked probe retains its slot to avoid transient reservation churn.");
        Set(positioner, "_combatClock", 6f);
        Check(positioner.TryGetAssignedPosition(enemy, out Vector3 reassigned) &&
              (reassigned - blockedSlot).sqrMagnitude > .1f && SlotPathClear(enemy, reassigned, obstacleMask),
            "Repeated blocked probes reassign a reachable sector without requiring a large target teleport.");

        // Exercise the same cast through both actual attack and movement APIs.
        enemy.transform.position = fixture.Target.transform.position + Vector3.back * 3f;
        wall.transform.position = enemy.transform.position + Vector3.forward * 1.5f + Vector3.up * 1.5f;
        Physics.SyncTransforms();
        Check(!positioner.IsAttackPositionReady(enemy, 4f),
            "A world wall inside an otherwise valid attack range blocks attack commitment.");
        Vector3 steered = positioner.AdjustMovement(enemy, Vector3.forward * 2f);
        Check(Finite(steered) && steered.magnitude <= 2.001f && Mathf.Abs(steered.x) > .1f && steered.z < .5f,
            "A nearby wall redirects locomotion along its surface within the authored speed limit.");
        wall.transform.position += Vector3.right * 20f;
        Physics.SyncTransforms();
        Check(positioner.IsAttackPositionReady(enemy, 4f),
            "Removing the world obstacle restores readiness for the same attacker and target.");

        // The production player has a hurtbox and a separate solid body collider
        // under sibling branches. An ancestor-only target filter misses that body.
        GameObject playerRoot = fixture.Make("Player body ownership");
        playerRoot.transform.position = fixture.Target.transform.position;
        Animator playerAnimator = playerRoot.AddComponent<Animator>();
        var directors = playerRoot.AddComponent<TimelineDirectorContainer>();
        var player = playerRoot.AddComponent<PlayerCore>();
        Set(player, "_animator", playerAnimator);
        Set(player, "_directorContainer", directors);
        fixture.Target.transform.SetParent(playerRoot.transform, true);
        GameObject body = fixture.Make("Sibling solid player body");
        body.transform.SetParent(playerRoot.transform, false);
        body.layer = obstacleLayer;
        var bodyCollider = body.AddComponent<CapsuleCollider>();
        bodyCollider.center = Vector3.up * .9f;
        bodyCollider.height = 1.8f;
        bodyCollider.radius = .45f;
        Physics.SyncTransforms();
        Check(!body.transform.IsChildOf(fixture.Target.transform) &&
              !fixture.Target.transform.IsChildOf(body.transform) &&
              !SlotPathClear(enemy, fixture.Target.transform.position, obstacleMask),
            "The sibling player body is a real solid cast hit outside the target transform's ancestry.");
        Check(positioner.IsAttackPositionReady(enemy, 4f),
            "A sibling collider belonging to the target's PlayerCore cannot be mistaken for a world wall.");
        wall.transform.position = enemy.transform.position + Vector3.forward * 1.5f + Vector3.up * 1.5f;
        Physics.SyncTransforms();
        Check(!positioner.IsAttackPositionReady(enemy, 4f),
            "Ignoring the player's solid body still preserves real wall blocking in the same cast.");
    }

    private static bool SlotPathClear(EnemyCore enemy, Vector3 slot, int obstacleMask)
    {
        Vector3 direction = slot - enemy.transform.position;
        direction.y = 0f;
        return !Physics.SphereCast(enemy.transform.position + Vector3.up * .75f, .3f,
            direction.normalized, out _, direction.magnitude, obstacleMask, QueryTriggerInteraction.Ignore);
    }

    private static void VerifyStateTransitions()
    {
        using var fixture = new Fixture(1, false);
        EnemyCore enemy = fixture.Enemies[0];
        var composite = new TrackingComposite(enemy);
        var first = new TrackingState(enemy);
        var second = new TrackingState(enemy);
        enemy.StateMachine.Transition(composite);
        composite.Switch(first);
        first.Pending = Vector3.forward;
        composite.Switch(first);
        Check(first.Entries == 1 && first.Pending == Vector3.forward,
            "Repeating the same locomotion decision preserves its state and pending root motion.");
        composite.Switch(second);
        Check(first.ClearedBeforeExit && first.Pending == Vector3.zero,
            "Changing locomotion clears the outgoing state's root motion before discarding it.");
        second.Pending = Vector3.right;
        enemy.StateMachine.Transition(first);
        Check(second.ClearedBeforeExit && second.Pending == Vector3.zero,
            "Leaving the composite state clears its current substate before Exit removes the reference.");
        Check(first.Pending == Vector3.zero,
            "Re-entering a reused state cannot apply movement retained from its previous activation.");
        var dead = new TrackingDeadState(enemy);
        enemy.StateMachine.DeadState = dead;
        Set(enemy, "_currentHP", 0f);
        enemy.StateMachine.UpdateTick();
        Check(enemy.StateMachine.CurrentState == dead && dead.Entries == 1 && first.Updates == 0,
            "Death transitions before dispatch so the previous state cannot issue another group attack.");
    }

    private sealed class TrackingComposite : EnemyCompositeState
    {
        public TrackingComposite(EnemyCore core) : base(core) { }
        public void Switch(EnemyStateBase state) => TransitionSubState(state);
    }

    private sealed class TrackingState : EnemyStateBase
    {
        public int Entries, Updates;
        public bool ClearedBeforeExit;
        public Vector3 Pending { get => AnimDeltaPos; set => AnimDeltaPos = value; }
        public TrackingState(EnemyCore core) : base(core) { }
        public override void Enter() => Entries++;
        public override void UpdateTick() => Updates++;
        public override void Exit() => ClearedBeforeExit = AnimDeltaPos == Vector3.zero;
    }

    private sealed class TrackingDeadState : EnemyDeadState
    {
        public int Entries;
        public TrackingDeadState(EnemyCore core) : base(core) { }
        public override void Enter() => Entries++;
    }

    private sealed class Fixture : IDisposable
    {
        public readonly Scene Scene;
        public readonly EnemyCore[] Enemies;
        public readonly GameObject Target;
        public readonly Camera Camera;
        public readonly EnemyPositioningController Positioning;
        private readonly bool _defaultPhysicsScene;
        private readonly Scene _previousActiveScene;
        private readonly EnemyAttackSO _attack;
        public EnemyAttackTimingController Timing { get; private set; }

        public Fixture(int count, bool positioning, bool defaultPhysicsScene = false)
        {
            _defaultPhysicsScene = defaultPhysicsScene;
            _previousActiveScene = SceneManager.GetActiveScene();
            Scene = defaultPhysicsScene
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive)
                : EditorSceneManager.NewPreviewScene();
            _attack = ScriptableObject.CreateInstance<EnemyAttackSO>();
            _attack.AttackID = EnemyAttackID.CloseAttack1Hit;
            _attack.AttackWarpDistance = 4f;
            Target = Make("Target");
            var targetCollider = Target.AddComponent<CapsuleCollider>();
            targetCollider.isTrigger = true;
            Camera = Make("Camera").AddComponent<Camera>();
            Camera.enabled = false;
            Camera.orthographic = true;
            Camera.orthographicSize = 20f;
            Camera.transform.SetPositionAndRotation(new Vector3(0f, 20f, 0f),
                Quaternion.LookRotation(Vector3.down, Vector3.forward));
            Enemies = new EnemyCore[count];
            for (int i = 0; i < count; i++)
            {
                GameObject go = Make("Enemy " + i);
                float angle = i * Mathf.PI * 2f / count;
                go.transform.position = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 3f;
                var callback = go.AddComponent<EnemyAnimatorCallback>();
                var detector = go.AddComponent<EnemyTargetDetector>();
                Set(detector, "_targetCollider", targetCollider);
                var rotator = go.AddComponent<EnemyRotator>();
                var enemy = go.AddComponent<EnemyCore>();
                Set(enemy, "_currentHP", 100f);
                Set(enemy, "_animatorCallback", callback);
                Set(enemy, "_targetDetector", detector);
                Set(enemy, "_rotator", rotator);
                enemy.AttackDataDictionary = new Dictionary<EnemyAttackID, EnemyAttackSO>
                { { EnemyAttackID.CloseAttack1Hit, _attack } };
                var machine = new EnemyStateMachine(enemy);
                Set(enemy, "_stateMachine", machine);
                SetState(enemy, machine.EngageState);
                Enemies[i] = enemy;
            }
            if (positioning)
            {
                Positioning = Make("Positioning").AddComponent<EnemyPositioningController>();
                Set(Positioning, "_autoDiscoverEnemies", false);
                Set(Positioning, "_obstacleLayers", (LayerMask)0);
                Invoke(Positioning, "OnEnable");
                foreach (EnemyCore enemy in Enemies) Positioning.Register(enemy);
            }
            ReplaceTiming();
        }

        public void ReplaceTiming()
        {
            if (Timing != null)
            {
                Invoke(Timing, "OnDisable");
                Invoke(Timing, "OnDestroy");
                Object.DestroyImmediate(Timing.gameObject);
            }
            Timing = Make("Timing").AddComponent<EnemyAttackTimingController>();
            Set(Timing, "_autoDiscoverEnemies", false);
            Set(Timing, "_initialAttackDelayRange", Vector2.zero);
            Set(Timing, "_repeatAttackDelayRange", new Vector2(.3f, .3f));
            Set(Timing, "_betweenAttackGapRange", new Vector2(.2f, .2f));
            Set(Timing, "_combatCamera", Camera);
            Invoke(Timing, "OnEnable");
            foreach (EnemyCore enemy in Enemies) Timing.Register(enemy);
        }

        public GameObject Make(string name)
        {
            var value = new GameObject("Group combat verification / " + name)
                { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(value, Scene);
            return value;
        }

        public void SetState(EnemyCore enemy, EnemyStateBase state) => Set(enemy.StateMachine, "_currentState", state);
        public void Tick(float delta) => Invoke(Timing, "Tick", delta);
        public void SubmitAll(bool reverse = false)
        {
            for (int i = 0; i < Enemies.Length; i++)
                Timing.IsReadyToAttack(Enemies[reverse ? Enemies.Length - 1 - i : i], 4f);
        }
        public void Dispose()
        {
            if (Timing != null)
            {
                Invoke(Timing, "OnDisable");
                Invoke(Timing, "OnDestroy");
            }
            if (Positioning != null) Invoke(Positioning, "OnDisable");
            if (_defaultPhysicsScene)
            {
                if (_previousActiveScene.IsValid() && _previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(_previousActiveScene);
                if (Scene.IsValid()) EditorSceneManager.CloseScene(Scene, true);
            }
            else if (Scene.IsValid()) EditorSceneManager.ClosePreviewScene(Scene);
            if (_attack != null) Object.DestroyImmediate(_attack);
        }
    }

    private static FieldInfo Field(object target, string name) =>
        target.GetType().GetField(name, PrivateInstance) ?? throw new MissingFieldException(target.GetType().Name, name);
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target);
    private static object Invoke(object target, string name, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(name, PrivateInstance);
        if (method == null) throw new MissingMethodException(target.GetType().Name, name);
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
    }
    private static bool Finite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    private static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .0001f, message);
    private static void Near(Vector3 actual, Vector3 expected, string message) => Check((actual - expected).sqrMagnitude < .0001f, message);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Enemy group combat: " + message);
        Evidence.Add("PASS " + message);
    }
}
