using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Deterministic recharge checks and real FSM entry-gate checks in an isolated prefab scene.</summary>
public static class PlayerDodgeChargeVerification
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;

    [MenuItem("Tools/Combat/Validate Dodge Charges")]
    public static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run isolated dodge verification outside Play Mode.");
        _checks = 0;
        VerifySharedTimer();
        VerifyFrameSteps();
        VerifyStateGate();
        return "PlayerDodgeChargeVerification: PASS (" + _checks + " checks). Shared reset, atomic refill, failed input, pause and front/back/stop/counter FSM entry gates verified; no prefab saved.";
    }

    private static void VerifySharedTimer()
    {
        var charges = new PlayerDodgeChargePool();
        Check(charges.Charges == 2 && charges.CooldownRemaining == 0f, "New player begins at 2/2.");
        Check(charges.TryConsume(1f) && charges.Charges == 1, "First accepted dodge consumes one.");
        charges.Tick(.6f);
        Near(charges.CooldownRemaining, .4f, "First cooldown advances 0.6 seconds.");
        Check(charges.TryConsume(1f) && charges.Charges == 0, "Second accepted dodge consumes the last charge.");
        Near(charges.CooldownRemaining, 1f, "Second successful dodge resets the shared timer to a full second.");
        Check(!charges.TryConsume(1f), "Third dodge is rejected at zero.");
        Near(charges.CooldownRemaining, 1f, "Rejected use does not restart or shorten the timer.");
        charges.Tick(.4f);
        Check(charges.Charges == 0, "Original first-use expiry must not return an individual charge.");
        Near(charges.CooldownRemaining, .6f, "Only the latest successful use determines refill time.");
        Check(!charges.TryConsume(1f), "Repeated depleted press remains rejected.");
        Near(charges.CooldownRemaining, .6f, "Blocked press during countdown leaves the remaining time unchanged.");
        charges.Tick(.59f);
        Check(charges.Charges == 0, "There is no interim one-charge recovery.");
        charges.Tick(.01f);
        Check(charges.Charges == 2 && charges.CooldownRemaining == 0f, "Expiry restores both charges atomically.");
        Check(charges.TryConsume(1f), "One use after refill is accepted.");
        charges.Tick(1f);
        Check(charges.Charges == 2, "A single use also recovers the full pool after a second.");
        charges.TryConsume(2.5f);
        charges.Tick(0f);
        Near(charges.CooldownRemaining, 2.5f, "Zero delta time pauses recharge.");
        charges.Tick(-10f);
        Near(charges.CooldownRemaining, 2.5f, "Invalid negative time cannot recharge or extend the timer.");
        charges.Tick(1.5f);
        charges.TryConsume(2.5f);
        Near(charges.CooldownRemaining, 2.5f, "Inspector-compatible duration is reapplied on each accepted use.");
        charges.Tick(10f);
        Check(charges.Charges == 2 && charges.CooldownRemaining == 0f, "A long frame clamps the expired timer and restores both.");
    }

    private static void VerifyFrameSteps()
    {
        foreach (int rate in new[] { 30, 60, 120 })
        {
            var charges = new PlayerDodgeChargePool();
            charges.TryConsume(1f);
            charges.TryConsume(1f);
            for (int i = 0; i < rate - 1; i++)
            {
                charges.Tick(1f / rate);
                Check(charges.Charges == 0, "No partial refill before expiry at " + rate + " Hz.");
            }
            charges.Tick(1f / rate);
            Check(charges.Charges == 2, "Both charges refill at one second at " + rate + " Hz.");
        }
    }

    private static void VerifyStateGate()
    {
        GameObject root = PrefabUtility.LoadPrefabContents("Assets/08_Prefabs/Combat/Player.prefab");
        float oldTimeScale = Time.timeScale;
        try
        {
            Time.timeScale = 1f;
            PlayerCore player = root.GetComponent<PlayerCore>();
            TimelineDirectorContainer directors = root.GetComponent<TimelineDirectorContainer>();
            Check(player != null && directors != null, "Player prefab has required state-construction components.");
            typeof(PlayerCore).GetField("_lastDodgeCooldownTickTime", PrivateInstance).SetValue(player, 10d);
            MethodInfo calculateDelta = typeof(PlayerCore).GetMethod("CalculateDodgeCooldownDelta", PrivateInstance);
            float delta = (float)calculateDelta.Invoke(player, new object[] { 2f, 10.004d });
            Near(delta, .004f, "A long previous frame cannot recharge time before the accepted dodge.");
            delta = (float)calculateDelta.Invoke(player, new object[] { .2f, 10.204d });
            Near(delta, .2f, "Following ticks advance by their actual unscaled interval.");
            delta = (float)calculateDelta.Invoke(player, new object[] { .1f, 15d });
            Near(delta, .1f, "Elapsed wall time cannot exceed the supplied unscaled frame delta.");
            directors.InitTimelineDirectorContainer();
            typeof(PlayerCore).GetField("_directorContainer", PrivateInstance).SetValue(player, directors);
            var machine = new PlayerStateMachine(player);
            var initial = new ProbeState(player);
            var front = new ProbeFront(player);
            var back = new ProbeBack(player);
            var stop = new ProbeStop(player);
            var counter = new ProbeCounter(player);
            int notifications = 0;
            player.OnDodgeChargesChanged += (_, __) => notifications++;
            machine.InitStateMachine(initial);
            Check(machine.Transition(front) && player.DodgeCharges == 1 && initial.Exits == 1 && front.Enters == 1,
                "Front entry consumes exactly once and then exits the old state.");
            TickPlayer(player, .6f);
            Check(machine.Transition(stop) && player.DodgeCharges == 1 && stop.Enters == 1,
                "FrontDodgeStop continues the first dodge without consuming a charge.");
            Near(player.DodgeCooldownRemaining, .4f, "Stop continuation does not reset cooldown.");
            Check(machine.Transition(back) && player.DodgeCharges == 0, "Back entry consumes the second use.");
            Near(player.DodgeCooldownRemaining, 1f, "Back entry resets the shared timer.");
            int beforeRejected = notifications;
            int previousBackExits = back.Exits;
            int previousFrontEnters = front.Enters;
            Check(!machine.Transition(front), "Depleted front entry is rejected.");
            Check(back.Exits == previousBackExits && front.Enters == previousFrontEnters
                && ReferenceEquals(CurrentState(machine), back), "Rejected entry preserves the old state and executes no Exit/Enter effects.");
            Check(notifications == beforeRejected, "Rejected input emits no charge-change event.");
            Near(player.DodgeCooldownRemaining, 1f, "Rejected state transition does not change timer.");
            Check(machine.Transition(counter) && player.DodgeCharges == 0 && counter.Enters == 1,
                "Counterattack remains available independently of the dodge charge gate.");
            Check(machine.Transition(stop) && player.DodgeCharges == 0,
                "Stop continuation is allowed even with a depleted pool.");
            machine.InitStateMachine(front);
            Check(ReferenceEquals(CurrentState(machine), stop), "Direct initialization cannot bypass the dodge gate.");

            Time.timeScale = 0f;
            TickPlayer(player, 5f);
            Check(player.DodgeCharges == 0, "Global pause prevents recharge.");
            Near(player.DodgeCooldownRemaining, 1f, "Global pause preserves remaining time.");
            Time.timeScale = 1f;
            TickPlayer(player, 1f);
            Check(player.DodgeCharges == 2, "Resume permits the full shared refill.");
            Check(machine.Transition(back) && player.DodgeCharges == 1, "First post-recharge back entry succeeds.");
            int oldEnters = back.Enters;
            int oldExits = back.Exits;
            Check(machine.Transition(back) && player.DodgeCharges == 0 && back.Enters == oldEnters + 1 && back.Exits == oldExits + 1,
                "Back-to-same-Back transition consumes exactly one additional charge.");
            oldEnters = back.Enters;
            oldExits = back.Exits;
            Check(!machine.Transition(back) && back.Enters == oldEnters && back.Exits == oldExits,
                "Third same-state request leaves animation/state lifecycle untouched.");
        }
        finally
        {
            Time.timeScale = oldTimeScale;
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void TickPlayer(PlayerCore player, float elapsed) => typeof(PlayerCore)
        .GetMethod("TickDodgeCooldown", PrivateInstance).Invoke(player, new object[] { elapsed });
    private static PlayerStateBase CurrentState(PlayerStateMachine machine) => (PlayerStateBase)typeof(PlayerStateMachine)
        .GetField("_currentState", PrivateInstance).GetValue(machine);
    private static void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < .00001f, message);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Dodge charge verification failed: " + message);
        _checks++;
    }

    // These probes use the actual concrete state inheritance and FSM gate while
    // counting lifecycle effects instead of playing animations in the editor.
    private sealed class ProbeState : PlayerStateBase
    {
        public int Exits;
        public ProbeState(PlayerCore core) : base(core) { }
        public override void Enter() { }
        public override void Exit() => Exits++;
    }
    private sealed class ProbeFront : PlayerFrontDodgeState
    {
        public int Enters;
        public ProbeFront(PlayerCore core) : base(core) { }
        public override void Enter() => Enters++;
        public override void Exit() { }
    }
    private sealed class ProbeBack : PlayerBackDodgeState
    {
        public int Enters, Exits;
        public ProbeBack(PlayerCore core) : base(core) { }
        public override void Enter() => Enters++;
        public override void Exit() => Exits++;
    }
    private sealed class ProbeStop : PlayerFrontDodgeStopState
    {
        public int Enters;
        public ProbeStop(PlayerCore core) : base(core) { }
        public override void Enter() => Enters++;
        public override void Exit() { }
    }
    private sealed class ProbeCounter : PlayerDodgeAttackStartState
    {
        public int Enters;
        public ProbeCounter(PlayerCore core) : base(core) { }
        public override void Enter() => Enters++;
        public override void Exit() { }
    }
}
