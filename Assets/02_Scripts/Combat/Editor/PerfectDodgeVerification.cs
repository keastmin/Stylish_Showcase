using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Playables;

// Batch entry point: -executeMethod PerfectDodgeVerification.RunBatch (without -quit).
[InitializeOnLoad]
public static class PerfectDodgeVerification
{
    private const string BatchKey = "ProjectR.PerfectDodgeVerification";
    private static int _playUpdates;

    static PerfectDodgeVerification()
    {
        if (SessionState.GetBool(BatchKey, false))
            EditorApplication.update += RunWhenPlaying;
    }

    [MenuItem("Tools/Combat/Validate Slow Motion Curves")]
    public static void ValidateCurves()
    {
        var settings = new PerfectDodgeSettings
        {
            SlowScale = 0.4f,
            FadeInDuration = 0.5f,
            FadeOutDuration = 0.7f,
            FadeInCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f),
            FadeOutCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f)
        };
        var motion = new CombatSlowMotion();
        motion.Begin(settings);
        motion.Tick(0.25f);
        Near(motion.Scale, 0.7f, "Inspector duration/scale drives fade-in");
        motion.Release(settings);
        Near(motion.Scale, 0.7f, "Release during fade-in must not jump");
        motion.Tick(0.35f);
        Near(motion.Scale, 0.85f, "Fade-out starts at the current scale");
        motion.Release(settings);
        motion.Tick(0.35f);
        Check(motion.IsComplete, "Repeated release must not restart fade-out");
        Near(motion.Scale, 1f, "Fade-out reaches exactly normal speed");

        settings.FadeInDuration = 0f;
        settings.FadeOutDuration = 0f;
        settings.SlowScale = 0f;
        motion.Begin(settings);
        Check(motion.Scale > 0f, "Animation-event based window must never freeze at zero");
        motion.Release(settings);
        Near(motion.Scale, 1f, "Zero duration is immediate");
        Check(motion.IsComplete, "Zero-duration release completes");
        Debug.Log("PerfectDodgeVerification: curve tests passed.");
    }

    public static void RunBatch()
    {
        try
        {
            ValidateCurves();
            EditorSceneManager.OpenScene("Assets/01_Scenes/CombatPrototypeScene.unity");
            SessionState.SetBool(BatchKey, true);
            EditorApplication.update -= RunWhenPlaying;
            EditorApplication.update += RunWhenPlaying;
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            Finish(exception);
        }
    }

    private static void RunWhenPlaying()
    {
        if (!EditorApplication.isPlaying || ++_playUpdates < 5 || Time.frameCount < 3)
            return;
        EditorApplication.update -= RunWhenPlaying;
        try
        {
            ValidateRuntime();
            Finish(null);
        }
        catch (Exception exception)
        {
            Finish(exception);
        }
    }

    private static void ValidateRuntime()
    {
        // This suite verifies the retained legacy implementation. The Studio has
        // its own runtime suite and is explicitly disconnected for this fixture.
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerCore>();
        var binding = typeof(PlayerCore).GetField("_dodgeDefinition", BindingFlags.Instance | BindingFlags.NonPublic);
        var original = binding.GetValue(player);
        player.Dodge?.Dispose();
        binding.SetValue(player, null);
        try { ValidateLegacyRuntime(); }
        finally { binding.SetValue(player, original); }
    }

    private static void ValidateLegacyRuntime()
    {
        var player = UnityEngine.Object.FindAnyObjectByType<PlayerCore>();
        var enemy = UnityEngine.Object.FindAnyObjectByType<EnemyCore>();
        Check(player != null && player.StateMachine != null && enemy != null, "Scene actors initialize");
        var settings = Field<PerfectDodgeSettings>(player, "_perfectDodge");
        settings.SlowScale = 0.35f;
        settings.StartDelay = 0f;
        settings.MaxDuration = 10f;
        settings.FadeInDuration = 0f;
        settings.FadeOutDuration = 0f;
        float originalTimeScale = Time.timeScale;
        float playerBaseSpeed = player.Animator.speed;
        float enemyBaseSpeed = enemy.Animator.speed;
        var oldCheck = player.OnPerfectDodgeCheck;
        player.OnPerfectDodgeCheck = () => enemy;

        ValidateDelayedStart(player, enemy, settings);

        EnterRechargedDodge(player, player.StateMachine.FrontDodgeState);
        Check(player.IsPerfectDodgeWindowOpen && player.PerfectDodgeSource == enemy, "Dodge opens target window");
        Near(player.Animator.speed, playerBaseSpeed * settings.SlowScale, "Player slows");
        Near(enemy.Animator.speed, enemyBaseSpeed * settings.SlowScale, "All enemies slow");
        Near(Time.timeScale, originalTimeScale, "Global time is untouched");

        ValidateVfx(settings);
        ValidateTimeline(player, settings);

        var sender = player.GetComponentInChildren<PlayerAnimationEventSender>();
        Check(sender != null, "Animation event sender exists");
        sender.OnPerfectDodgeEndInvoke();
        Check(!player.IsPerfectDodgeWindowOpen, "Scene's persistent end-event binding closes window");
        Near(CombatTimeController.Scale, 1f, "Natural end releases slow motion");
        Check(!player.TryBeginDodgeAttack(), "Closed window rejects counterattack");

        EnterRechargedDodge(player, player.StateMachine.BackDodgeState);
        // A headless Editor has no focused Game view or necessarily any paired hardware.
        InputSystem.settings = UnityEngine.Object.Instantiate(InputSystem.settings);
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        var mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
        var keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
        var playerInput = player.GetComponent<PlayerInput>();
        playerInput.enabled = false;
        playerInput.enabled = true;
        playerInput.SwitchCurrentControlScheme("PC", keyboard, mouse);
        playerInput.ActivateInput();
        InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
        UpdateGameInput();
        Check(mouse.leftButton.isPressed, "Headless mouse receives queued click");
        player.InputCollector.SendMessage("Update");
        Check(player.InputCollector.IsInputAttack, "Left click is collected");
        player.SendMessage("Update");
        Check(CurrentState(player) == player.StateMachine.DodgeAttackStartState, "Left click prioritizes DodgeAttack over RunAttack");
        Check(player.DodgeAttackTarget == enemy && !player.IsPerfectDodgeWindowOpen, "Target survives dodge Exit; window consumed");
        Near(CombatTimeController.Scale, 1f, "Counterattack removes slow motion immediately");
        Check(!player.TryBeginDodgeAttack(), "Counter cannot consume the same window twice");
        InputSystem.QueueStateEvent(mouse, new MouseState());
        UpdateGameInput();
        player.InputCollector.SendMessage("Update");

        player.AnimationEvent.OnAnimationEndActionInvoke();
        player.StateMachine.UpdateTick();
        Check(CurrentState(player) == player.StateMachine.DodgeAttackLoopState, "Counter start advances to loop");
        player.AnimationEvent.OnAnimationEndActionInvoke();
        player.StateMachine.UpdateTick();
        Check(CurrentState(player) == player.StateMachine.DodgeAttackEndState, "Counter loop advances to end");
        player.AnimationEvent.OnAnimationEndActionInvoke();
        player.StateMachine.UpdateTick();
        Check(CurrentState(player) == player.StateMachine.IdleState && player.DodgeAttackTarget == null,
            "Counter completes without NotImplementedException and clears target");

        EnterRechargedDodge(player, player.StateMachine.FrontDodgeState);
        player.BeginHitStop();
        enemy.BeginHitStop();
        player.EndPerfectDodge(true);
        Near(player.Animator.speed, 0f, "Slow cancellation does not cancel player hitstop");
        Near(enemy.Animator.speed, 0f, "Slow cancellation does not cancel enemy hitstop");
        player.EndHitStop();
        enemy.EndHitStop();
        Near(player.Animator.speed, playerBaseSpeed, "Hitstop restores current scale, not stale slow scale");
        Near(enemy.Animator.speed, enemyBaseSpeed, "Enemy hitstop restores current scale");

        EnterRechargedDodge(player, player.StateMachine.BackDodgeState);
        player.StateMachine.Transition(player.StateMachine.IdleState);
        Near(CombatTimeController.Scale, 1f, "Interrupted dodge cleans up its request");

        EnterRechargedDodge(player, player.StateMachine.FrontDodgeState);
        enemy.gameObject.SetActive(false);
        Check(!player.TryBeginDodgeAttack(), "Missing target rejects counter safely");
        Near(CombatTimeController.Scale, 1f, "Missing target releases slow motion");
        enemy.gameObject.SetActive(true);

        var otherOwner = new GameObject("Other slow owner");
        CombatTimeController.Begin(otherOwner, new PerfectDodgeSettings { SlowScale = 0.6f, FadeInDuration = 0f });
        player.BeginPerfectDodge(enemy);
        player.EndPerfectDodge(true);
        Near(CombatTimeController.Scale, 0.6f, "Cancel only the owning request");
        CombatTimeController.End(otherOwner, settings, true);
        UnityEngine.Object.Destroy(otherOwner);
        ValidateInvulnerability(player, enemy, settings);
        player.BeginPerfectDodge(enemy);
        player.enabled = false;
        Near(CombatTimeController.Scale, 1f, "Player disable cleans up slow motion");
        player.OnPerfectDodgeCheck = oldCheck;
    }

    private static void ValidateDelayedStart(PlayerCore player, EnemyCore enemy, PerfectDodgeSettings settings)
    {
        settings.StartDelay = 0.1f;
        settings.MaxDuration = 0.2f;
        player.BeginPerfectDodge(enemy);
        Check(player.IsPerfectDodgeActive && player.IsInvulnerable,
            "Perfect dodge protects immediately before slow motion");
        Check(!player.TryTakeDamage(new DamageData(enemy.gameObject, 1f, 0)),
            "Delayed slow motion does not delay damage immunity");
        Check(!player.IsPerfectDodgeWindowOpen && !player.TryBeginDodgeAttack(),
            "Counterattack stays closed during the presentation delay");
        Near(CombatTimeController.Scale, 1f, "Presentation delay leaves combat at normal speed");

        TickPerfectDodge(player, 0.099f);
        Check(!player.IsPerfectDodgeWindowOpen, "Window stays closed until the full delay elapses");
        TickPerfectDodge(player, 0.002f);
        Check(player.IsPerfectDodgeWindowOpen, "Delay completion opens the counterattack window");
        Near(CombatTimeController.Scale, settings.SlowScale, "Delay completion starts slow motion");

        TickPerfectDodge(player, 0.19f);
        Check(player.IsPerfectDodgeWindowOpen, "Window stays open during its configured duration");
        TickPerfectDodge(player, 0.02f);
        Check(!player.IsPerfectDodgeActive && !player.IsPerfectDodgeWindowOpen,
            "Maximum duration closes protection and counterattack window");
        Near(CombatTimeController.Scale, 1f, "Maximum duration releases slow motion");

        settings.StartDelay = 0f;
        settings.MaxDuration = 10f;
    }

    private static void ValidateInvulnerability(PlayerCore player, EnemyCore enemy, PerfectDodgeSettings settings)
    {
        var damage = new DamageData(enemy.gameObject, 17f, 6);
        int damageEvents = 0;
        Action<DamageData> onDamaged = _ => damageEvents++;
        player.OnDamaged += onDamaged;

        player.OnPerfectDodgeCheck = () => null;
        EnterRechargedDodge(player, player.StateMachine.FrontDodgeState);
        Check(!player.IsInvulnerable && player.TryTakeDamage(damage), "Normal dodge still takes damage");
        Check(damageEvents == 1, "Accepted damage emits its damage event");
        player.OnPerfectDodgeCheck = () => enemy;
        EnterRechargedDodge(player, player.StateMachine.FrontDodgeState);
        AssertProtected("Perfect dodge", player, damage);
        Check(player.IsPerfectDodgeWindowOpen && player.PerfectDodgeSource == enemy,
            "Rejected damage preserves the perfect dodge window and target");
        Near(CombatTimeController.Scale, settings.SlowScale, "Rejected damage does not cancel slow motion");

        var pool = enemy.HitboxPool;
        var victims = Field<System.Collections.Generic.List<IHitStopParticipant>>(pool, "_hitStopVictims");
        victims.Clear();
        typeof(EnemyHitboxPool).GetMethod("GiveDamage", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(pool, new object[] { new[] { player.GetComponent<Collider>() },
                new EnemyAttackHitboxInfo { DamageAmount = damage.DamageAmount }, damage.HitStopFrame });
        HitstopCoordinator.Request(enemy, victims, damage.HitStopFrame);
        Check(victims.Count == 0 && !player.IsHitStopped && !enemy.IsHitStopped,
            "Invulnerable hit is excluded from both attacker and victim hitstop");

        bool protectedDuringHandoff = false;
        Action duringEnd = () => protectedDuringHandoff = player.IsInvulnerable && !player.TryTakeDamage(damage);
        player.OnPerfectDodgeEnded += duringEnd;
        Check(player.TryBeginDodgeAttack(), "Protected dodge can still counterattack");
        player.OnPerfectDodgeEnded -= duringEnd;
        Check(protectedDuringHandoff, "Perfect dodge to counter handoff has no invulnerability gap");
        foreach (string stage in new[] { "Start", "Loop", "End" })
        {
            AssertProtected("DodgeAttack " + stage, player, damage);
            player.AnimationEvent.OnAnimationEndActionInvoke();
            player.StateMachine.UpdateTick();
        }
        Check(damageEvents == 1, "Protected hits never emit damage events");
        Check(!player.IsInvulnerable && player.TryTakeDamage(damage), "Counter completion restores damage reception");

        EnterRechargedDodge(player, player.StateMachine.BackDodgeState);
        settings.FadeOutDuration = 0.5f;
        player.AnimationEvent.OnPerfectDodgeEndInvoke();
        Check(CombatTimeController.Scale < 1f && !player.IsInvulnerable && player.TryTakeDamage(damage),
            "Natural end restores damage reception even while fading out");
        settings.FadeOutDuration = 0f;

        EnterRechargedDodge(player, player.StateMachine.FrontDodgeState);
        player.TryBeginDodgeAttack();
        player.StateMachine.Transition(player.StateMachine.IdleState);
        Check(!player.IsInvulnerable && player.TryTakeDamage(damage), "Interrupted counter does not leave invulnerability behind");
        player.OnDamaged -= onDamaged;
        player.StateMachine.Transition(player.StateMachine.IdleState);
    }

    private static void AssertProtected(string stage, PlayerCore player, DamageData damage)
    {
        var state = CurrentState(player);
        var previousDamage = player.LastDamageData;
        damage.DamageAmount += 1f;
        Check(player.IsInvulnerable && !player.TryTakeDamage(damage), stage + " rejects damage");
        Check(CurrentState(player) == state && player.LastDamageData.Equals(previousDamage),
            stage + " preserves state and last accepted damage");
    }

    private static void ValidateVfx(PerfectDodgeSettings settings)
    {
        var effect = new GameObject("Slow VFX test");
        var particles = effect.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.simulationSpeed = 2f;
        CombatVfxTime.RegisterHierarchy(effect);
        Near(particles.main.simulationSpeed, 2f * settings.SlowScale, "New effect inherits current slow scale");
        effect.SetActive(false);
        Near(particles.main.simulationSpeed, 2f, "Pool return restores authored speed");
        effect.SetActive(true);
        Near(particles.main.simulationSpeed, 2f * settings.SlowScale, "Pool reuse reapplies current scale once");
        CombatVfxTime.RegisterHierarchy(effect, true);
        Near(particles.main.simulationSpeed, 2f, "Timeline-controlled particle is not slowed twice");
        var ui = new GameObject("UI test", typeof(Canvas));
        var uiEffect = new GameObject("UI particle", typeof(ParticleSystem));
        uiEffect.transform.SetParent(ui.transform);
        CombatVfxTime.RegisterHierarchy(ui);
        Check(uiEffect.GetComponent<CombatVfxTime>() == null, "UI stays outside combat registration");
        UnityEngine.Object.Destroy(effect);
        UnityEngine.Object.Destroy(ui);
    }

    private static void ValidateTimeline(PlayerCore player, PerfectDodgeSettings settings)
    {
        player.DirectorContainer.Play(DirectorID.BasicAttack1);
        var director = player.DirectorContainer.Directors[DirectorID.BasicAttack1];
        Check(director.playableGraph.IsValid(), "Combat Timeline creates a graph");
        var root = director.playableGraph.GetRootPlayable(0);
        Near((float)root.GetSpeed(), settings.SlowScale, "New Timeline inherits slow scale");
        player.BeginHitStop();
        Near((float)root.GetSpeed(), 0f, "Timeline freezes during hitstop");
        player.EndHitStop();
        Near((float)root.GetSpeed(), settings.SlowScale, "Timeline resumes current slow scale");
        director.Stop();
    }

    // These cases isolate perfect-dodge behavior; charge timing has its own verification.
    private static void EnterRechargedDodge(PlayerCore player, PlayerStateBase state)
    {
        typeof(PlayerCore).GetMethod("TickDodgeCooldown", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(player, new object[] { player.DodgeCooldownDuration });
        Check(player.StateMachine.Transition(state), "Recharged dodge enters for perfect-dodge scenario");
    }
    private static PlayerStateBase CurrentState(PlayerCore player) => Field<PlayerStateBase>(player.StateMachine, "_currentState");
    private static void TickPerfectDodge(PlayerCore player, float realDeltaTime) =>
        typeof(PlayerCore).GetMethod("TickPerfectDodge", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(player, new object[] { realDeltaTime });
    private static void UpdateGameInput()
    {
        // The public no-argument update selects Editor input when a headless Game view has no focus.
        typeof(InputSystem).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Static,
            null, new[] { typeof(InputUpdateType) }, null).Invoke(null, new object[] { InputUpdateType.Dynamic });
    }
    private static T Field<T>(object instance, string name) => (T)instance.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
        Debug.Log("PASS: " + message);
    }
    private static void Near(float actual, float expected, string message) =>
        Check(Mathf.Abs(actual - expected) < 0.0001f, $"{message} (actual={actual}, expected={expected})");

    private static void Finish(Exception exception)
    {
        SessionState.SetBool(BatchKey, false);
        if (exception != null)
            Debug.LogException(exception);
        else
            Debug.Log("PerfectDodgeVerification: ALL TESTS PASSED");
        EditorApplication.Exit(exception == null ? 0 : 1);
    }
}
