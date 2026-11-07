using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PlayerDodgeVerification
{
    private const string Key = "ProjectR.DodgeStudioVerification";
    private static int _checks;
    private static PlayerCore _player;
    private static EnemyCore _enemy;
    private static PlayerDodgeDefinition _original, _test;
    private static Func<EnemyCore> _originalCheck;
    private static double _liveStart;
    private static int _liveFrames;
    private static bool _live;
    private static int _playUpdates;
    private static bool _savedBackground;

    static PlayerDodgeVerification()
    {
        if (SessionState.GetBool(Key, false)) EditorApplication.update += WaitForPlay;
    }

    private static PlayerDodgeDefinition Definition()
    {
        var d = ScriptableObject.CreateInstance<PlayerDodgeDefinition>();
        d.front.speed = 1;
        d.closeAtAnimationMarker = false;
        d.slow = new PerfectDodgeSettings { SlowScale = .2f, StartDelay = .1f, FadeInDuration = 0, FadeOutDuration = .1f, MaxDuration = 1 };
        d.counterOpenDelay = .15f;
        d.counterWindowDuration = .5f;
        d.dodgeEffects.Clear();
        return d;
    }

    [MenuItem("Tools/Combat/Validate Dodge Studio Timing")]
    public static void ValidateTiming()
    {
        _checks = 0;
        var d = Definition();
        try
        {
            ValidateDelayedFadeIn();
            var normal = new DodgeSequence(d, false, () => false);
            normal.Tick(.2f, false);
            Check(!normal.PerfectActive && !normal.Invulnerable && !normal.WindowOpen, "Normal dodge has no perfect protection");
            Near(normal.SlowScale, 1, "Normal dodge leaves combat speed alone");

            var s = new DodgeSequence(d, false, () => true);
            Check(s.Invulnerable && !s.WindowOpen, "Success protects before presentation delay");
            s.Tick(.11f, false);
            Near(s.SlowScale, .2f, "Slow start uses its own delay");
            Check(!s.WindowOpen, "Counter window can open later than slow motion");
            s.Tick(.05f, false);
            Check(s.WindowOpen, "Independent counter input delay opens");
            s.ClosePerfect("test");
            Check(!s.Invulnerable && !s.WindowOpen, "Natural end closes protection and input immediately");
            s.Tick(.11f, false);
            Near(s.SlowScale, 1, "Natural fade finishes in real time");

            d.slow.StartDelay = d.counterOpenDelay = 0;
            d.slow.FadeInDuration = .2f;
            s = new DodgeSequence(d, false, () => true);
            s.Tick(.05f, false);
            float before = s.SlowScale;
            Check(before > .2f && before < 1, "Counter can occur during slow fade-in");
            Check(s.TryCounter(), "Open counter is consumed");
            Near(s.SlowScale, 1, "Immediate handoff removes own slow without waiting for fade");
            Check(s.Invulnerable && !s.WindowOpen && !s.TryCounter(), "Handoff has protection and cannot consume twice");

            d.counter.slowExit = DodgeSlowExit.Fade;
            d.counter.fadeDuration = .1f;
            d.counter.fadeCurve = AnimationCurve.Linear(0, 0, 1, 1);
            s = new DodgeSequence(d, false, () => true);
            s.Tick(.05f, false); before = s.SlowScale;
            s.TryCounter();
            Near(s.SlowScale, before, "Counter fade begins at current scale");
            s.Tick(.05f, false);
            Near(s.SlowScale, (before + 1) * .5f, "Counter fade uses separate duration and curve", .003f);
            s.Tick(.055f, false);
            Near(s.SlowScale, 1, "Counter fade reaches normal speed");

            d.slow.FadeInDuration = 0;
            d.counter.slowExit = DodgeSlowExit.UntilAnimation;
            d.counter.releasePhase = DodgePhase.CounterLoop;
            d.counter.releaseSourceTime = .1f;
            d.counter.maxHold = 2;
            d.counter.fadeDuration = 0;
            s = new DodgeSequence(d, false, () => true);
            s.TryCounter(); s.Tick(.2f, false);
            Near(s.SlowScale, .2f, "Hold follows counter animation instead of real-time delay");
            s.EnterPhase(DodgePhase.CounterLoop);
            s.Tick(.05f, false, false, 1, .12f);
            Near(s.SlowScale, 1, "Observed loop source marker releases slow");
            s = new DodgeSequence(d, false, () => true);
            s.TryCounter(); s.Tick(2.01f, false, true);
            Near(s.SlowScale, 1, "Hold safety limit finishes even if motion never reaches the marker");

            d.counter.slowExit = DodgeSlowExit.Immediate;
            d.releaseSlowWithWindow = false;
            d.slow.MaxDuration = .1f;
            d.slow.FadeOutDuration = 0;
            s = new DodgeSequence(d, false, () => true);
            s.Tick(.11f, false);
            Check(s.WindowOpen, "Input can outlive independent slow duration");
            Near(s.SlowScale, 1, "Independent slow duration includes fade-in");

            d.slow.MaxDuration = 1;
            d.releaseSlowWithWindow = true;
            d.front.judgeStart = .1f; d.front.judgeEnd = .2f; d.front.judgeOverWindow = true;
            int attempts = 0;
            s = new DodgeSequence(d, false, () => ++attempts >= 3);
            s.Tick(.12f, false);
            Check(s.PerfectActive && attempts == 3, "Window judges until first success only");
            s.Tick(.1f, false);
            Check(attempts == 3, "Window never grants success a second time");
            d.front.judgeStart = 0; d.front.judgeOverWindow = false;

            var cue = new DodgeScreenCue { anchor = DodgeEffectAnchor.PerfectSuccess, fadeIn = 0, hold = .4f, fadeOut = .2f, strength = .45f };
            d.dodgeEffects.Add(cue);
            s = new DodgeSequence(d, false, () => true);
            s.Tick(.2f, false); s.TryCounter(); s.Tick(.1f, false);
            Near(s.Effect(DodgeEffectKind.Vignette).value, .45f, "Continue preserves screen effect across counter");
            s.Tick(.31f, false);
            Near(s.Effect(DodgeEffectKind.Vignette).value, 0, "Continue keeps original end time instead of restarting");
            cue.onCounter = DodgeEffectHandoff.FadeOut; cue.counterFade = .1f;
            s = new DodgeSequence(d, false, () => true);
            s.Tick(.1f, false); s.TryCounter(); s.Tick(.11f, false);
            Near(s.Effect(DodgeEffectKind.Vignette).value, 0, "Counter screen fade has independent length");
            cue.onCounter = DodgeEffectHandoff.ReplaceWhenCounterEffectStarts;
            d.counterEffects.Add(new DodgeScreenCue { anchor = DodgeEffectAnchor.CounterStart, fadeIn = .1f, hold = .1f, fadeOut = 0, strength = .8f, inCurve = AnimationCurve.Linear(0, 0, 1, 1) });
            s = new DodgeSequence(d, false, () => true);
            s.Tick(.1f, false); s.TryCounter();
            Near(s.Effect(DodgeEffectKind.Vignette).value, .45f, "Counter screen replacement starts from current intensity");
            s.Tick(.05f, false);
            Near(s.Effect(DodgeEffectKind.Vignette).value, .625f, "Counter replacement blends without resetting to zero", .003f);
            s.Tick(.2f, false);
            Near(s.Effect(DodgeEffectKind.Vignette).value, 0, "Old screen channel does not resurface after replacement ends");

            d.counterEffects.Clear();
            cue.hold = 2;
            var prior = new DodgeSequence(d, false, () => true);
            prior.Tick(.1f, false);
            var nextDefinition = UnityEngine.Object.Instantiate(d);
            try
            {
                nextDefinition.slow.StartDelay = .2f;
                nextDefinition.dodgeEffects[0].anchor = DodgeEffectAnchor.SlowStart;
                nextDefinition.dodgeEffects[0].fadeIn = .1f;
                nextDefinition.dodgeEffects[0].strength = .8f;
                nextDefinition.dodgeEffects[0].inCurve = AnimationCurve.Linear(0, 0, 1, 1);
                var next = new DodgeSequence(nextDefinition, false, () => true, kind => prior.Effect(kind));
                Near(next.Effect(DodgeEffectKind.Vignette).value, .45f, "Consecutive dodge inherits old screen before new cue starts");
                next.Tick(.2f, false);
                Near(next.Effect(DodgeEffectKind.Vignette).value, .45f, "Consecutive cue starts from the current inherited value");
                next.Tick(.05f, false);
                Near(next.Effect(DodgeEffectKind.Vignette).value, .625f, "Consecutive cue blends without a zero-intensity flash", .005f);
            }
            finally { UnityEngine.Object.DestroyImmediate(nextDefinition); }

            d.counterEffects.Clear();
            d.closeAtAnimationMarker = true;
            d.front.perfectEnd = .2f;
            s = new DodgeSequence(d, false, () => true);
            s.Tick(.05f, false, false, 1, .3f);
            Check(!s.PerfectActive && s.EndReason.Contains("마커"), "Skipped source markers still close the window");
            s.Cancel();
            Check(!s.SlowRequested && !s.Effect(DodgeEffectKind.Vignette).active, "Cancel removes slow and all owned screen cues");

            d.closeAtAnimationMarker = false;
            d.slow.StartDelay = .1f; d.slow.FadeInDuration = .1f;
            float reference = -1;
            foreach (int fps in new[] { 30, 60, 120 })
            {
                s = new DodgeSequence(d, false, () => true);
                for (int i = 0; i < fps / 5; i++) s.Tick(1f / fps, false);
                if (reference < 0) reference = s.SlowScale;
                Near(s.SlowScale, reference, "30/60/120Hz envelope consistency", .005f);
            }
            Debug.Log("Dodge Studio timing: " + _checks + " checks passed.");
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/DodgeStudioTiming.result", "PASS " + _checks);
        }
        finally { UnityEngine.Object.DestroyImmediate(d); }
    }

    private static void ValidateDelayedFadeIn()
    {
        var idle = new CombatSlowMotion();
        idle.Tick(.5f);
        Near(idle.Scale, 1, "Ticking before Begin preserves normal speed");
        var d = Definition();
        d.counterWindowDuration = 3;
        d.slow.MaxDuration = 2;
        d.slow.FadeInDuration = .4f;
        d.slow.FadeInCurve = AnimationCurve.Linear(0, 0, 1, 1);
        try
        {
            var s = new DodgeSequence(d, false, () => true);
            s.Tick(.09f, false);
            Near(s.SlowScale, 1, "Presentation delay preserves normal speed before a nonzero fade");
            s.Tick(.06f, false);
            Near(s.SlowScale, .9f, "Delayed fade descends from 100%, not from zero", .01f);
            s.Tick(.15f, false);
            Near(s.SlowScale, .6f, "Fade duration controls the speed at its midpoint", .01f);
            s.Tick(.21f, false);
            Near(s.SlowScale, .2f, "Delayed fade reaches the authored target");

            // Compare the same point in real time with two different entry durations.
            d.slow.FadeInDuration = .1f;
            var fast = new DodgeSequence(d, false, () => true);
            fast.Tick(.3f, false);
            d.slow.FadeInDuration = .6f;
            var gradual = new DodgeSequence(d, false, () => true);
            gradual.Tick(.3f, false);
            Check(gradual.SlowScale > fast.SlowScale + .4f, "A longer entry visibly changes speed at the same preview time");
            Check(gradual.SourceTime > fast.SourceTime + .05f, "A longer entry changes the sampled animation pose as well as the numeric scale");

            foreach(int fps in new[] {30,60,120})
            {
                s = new DodgeSequence(d, false, () => true);
                for(int i=0;i<fps*3/10;i++) s.Tick(1f/fps, false);
                Near(s.SlowScale, gradual.SlowScale, "Delayed fade remains consistent at "+fps+" Hz", .01f);
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(d); }
    }

    [MenuItem("Tools/Combat/Validate Dodge Studio Runtime")]
    public static void ValidateRuntime()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode에서 시작하세요.");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("씬 변경을 먼저 저장하세요.");
        ValidateTiming();
        SessionState.SetInt(Key + ".checks", _checks);
        SessionState.SetBool(Key, true);
        EditorApplication.update -= WaitForPlay;
        EditorApplication.update += WaitForPlay;
        EditorApplication.isPlaying = true;
    }

    private static void WaitForPlay()
    {
        if (!EditorApplication.isPlaying || ++_playUpdates < 5) return;
        var ready = UnityEngine.Object.FindAnyObjectByType<PlayerCore>();
        if (ready == null || ready.StateMachine == null || Field<PlayerStateBase>(ready.StateMachine, "_currentState") == null) return;
        EditorApplication.update -= WaitForPlay;
        _checks = SessionState.GetInt(Key + ".checks", 0);
        try
        {
            _player = UnityEngine.Object.FindAnyObjectByType<PlayerCore>();
            _enemy = UnityEngine.Object.FindAnyObjectByType<EnemyCore>();
            Check(_player != null && _player.Dodge != null && _enemy != null, "Scene runtime initializes");
            _original = _player.DodgeDefinition;
            _savedBackground = Application.runInBackground;
            Application.runInBackground = true;
            Check(_original != null && _player.GetComponent<PlayerDodgeScreenEffects>() != null, "Studio asset and screen output are connected");
            _test = UnityEngine.Object.Instantiate(_original);
            _test.closeAtAnimationMarker = false;
            _test.slow.StartDelay = .07f; _test.slow.SlowScale = .2f; _test.slow.FadeInDuration = _test.slow.FadeOutDuration = 0;
            _test.slow.MaxDuration = 1;
            _test.counterOpenDelay = .1f; _test.counterWindowDuration = .5f;
            _test.dodgeEffects[0].anchor = DodgeEffectAnchor.PerfectSuccess;
            _test.dodgeEffects[0].fadeIn = 0; _test.dodgeEffects[0].hold = 2;
            _originalCheck = _player.OnPerfectDodgeCheck;
            _player.GetComponent<UnityEngine.InputSystem.PlayerInput>().DeactivateInput();
            SetDefinition(_test);
            _player.OnPerfectDodgeCheck = () => null;
            EnterDodge();
            Check(!_player.IsInvulnerable && !_player.IsPerfectDodgeActive, "Normal studio dodge keeps damage reception");
            _player.OnPerfectDodgeCheck = () => _enemy;
            EnterDodge();
            Check(_player.IsInvulnerable && !_player.IsPerfectDodgeWindowOpen, "Perfect studio dodge protects during start delay");
            Near(CombatTimeController.Scale, 1, "Runtime delay does not slow combat early");
            _player.Dodge.Tick(.08f);
            Near(CombatTimeController.Scale, .2f, "Shared evaluator drives real combat speed");
            Check(!_player.IsPerfectDodgeWindowOpen, "Runtime input delay is independent");
            _player.Dodge.Tick(.03f);
            Check(_player.IsPerfectDodgeWindowOpen, "Runtime counter window opens");
            var other = new GameObject("Dodge verification other slow owner");
            CombatTimeController.Begin(other, new PerfectDodgeSettings { SlowScale = .4f, FadeInDuration = 0 });
            _player.BeginHitStop();
            Check(_player.TryBeginDodgeAttack(), "Real FSM accepts counterattack");
            Check(_player.IsInvulnerable && _player.DodgeAttackTarget == _enemy, "Counter handoff preserves protection and target");
            Near(CombatTimeController.Scale, .4f, "Counter removes only its own request");
            Near(_player.Animator.speed, 0, "Counter handoff never cancels hitstop");
            _player.EndHitStop();
            Near(_player.Animator.speed, .4f, "Hitstop resumes the current remaining request");
            CombatTimeController.End(other, _test.slow, true);
            UnityEngine.Object.Destroy(other);
            Near(_player.Dodge.Sequence.Effect(DodgeEffectKind.Vignette).value, _test.dodgeEffects[0].strength, "Runtime screen effect continues after counter");
            foreach (var phase in new[] { DodgePhase.CounterLoop, DodgePhase.CounterEnd })
            {
                _player.AnimationEvent.OnAnimationEndActionInvoke(); _player.StateMachine.UpdateTick();
                Check(_player.Dodge.Sequence.Phase == phase && _player.IsInvulnerable, "Runtime advances protected " + phase);
            }
            _player.AnimationEvent.OnAnimationEndActionInvoke(); _player.StateMachine.UpdateTick();
            Check(!_player.IsInvulnerable && _player.DodgeAttackTarget == null, "Counter completion clears protection and target");

            _test.slow.StartDelay = _test.counterOpenDelay = 0;
            EnterDodge();
            _test.slow.SlowScale = .8f;
            _player.Dodge.Tick(.05f);
            Near(CombatTimeController.Scale, .2f, "Active dodge uses a snapshot while authoring values change");
            _player.EndPerfectDodge(true);
            Near(CombatTimeController.Scale, 1, "Explicit cancellation releases manual owner");

            _test.closeAtAnimationMarker = true; _test.front.perfectEnd = .7f; _test.slow.SlowScale = .2f;
            EnterDodge();
            _player.Dodge.AnimationPerfectEnd();
            Check(_player.IsPerfectDodgeActive, "Original clip event does not override an edited source marker");
            _player.Dodge.Sequence.Tick(.1f, false, false, 1, .71f);
            _player.Dodge.Tick(0);
            Check(!_player.IsPerfectDodgeActive, "Edited source marker closes the real runtime window");

            _player.StateMachine.Transition(_player.StateMachine.IdleState);
            _player.Dodge.Dispose();
            SetDefinition(null);
            var legacy = Field<PerfectDodgeSettings>(_player, "_perfectDodge");
            var savedLegacy = JsonUtility.ToJson(legacy);
            legacy.StartDelay = 0; legacy.SlowScale = .35f; legacy.FadeInDuration = legacy.FadeOutDuration = 0;
            _player.BeginPerfectDodge(_enemy);
            Check(_player.IsPerfectDodgeWindowOpen && _player.IsInvulnerable, "Disconnect restores legacy window and protection");
            Near(CombatTimeController.Scale, .35f, "Disconnect restores legacy slow motion");
            _player.EndPerfectDodge(true);
            Near(CombatTimeController.Scale, 1, "Legacy cleanup still works");
            JsonUtility.FromJsonOverwrite(savedLegacy, legacy);

            SetDefinition(_test);
            _test.closeAtAnimationMarker = false;
            _test.counterWindowDuration = 3;
            _test.slow.StartDelay = .1f; _test.slow.FadeInDuration = .4f;
            _test.slow.FadeInCurve = AnimationCurve.Linear(0, 0, 1, 1);
            _test.slow.SlowScale = .2f; _test.slow.MaxDuration = 2; _test.slow.FadeOutDuration = 0;
            EnterDodge();
            _player.Dodge.Tick(.15f);
            Near(CombatTimeController.Scale, .9f, "Delayed entry drives the real combat speed from normal", .01f);
            Near(_player.Animator.speed, .9f, "Delayed entry reaches the real Animator", .01f);
            _player.Dodge.Tick(.15f);
            Near(CombatTimeController.Scale, .6f, "Real combat follows the shared fade midpoint", .01f);
            _player.Dodge.Tick(.21f);
            Near(CombatTimeController.Scale, .2f, "Real delayed entry reaches its target");
            _player.EndPerfectDodge(true);
            Near(CombatTimeController.Scale, 1, "Delayed entry cleanup restores normal speed");

            _test.closeAtAnimationMarker = true;
            _test.slow.StartDelay = _test.slow.FadeInDuration = 0;
            _test.front.perfectEnd = _original.front.perfectEnd;
            _test.back.perfectEnd = _original.back.perfectEnd;
            _test.slow.SlowScale = .35f;
            _test.counterWindowDuration = 3;
            _liveStart = EditorApplication.timeSinceStartup; _liveFrames = 0; _live = true;
            EnterDodge(true);
            EditorApplication.update += ObserveLive;
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void ObserveLive()
    {
        if (!_live || !EditorApplication.isPlaying) { Finish(new Exception("Live verification interrupted")); return; }
        try
        {
            _liveFrames++;
            if (_player.Dodge.Sequence == null) throw new InvalidOperationException("Live frame lost session");
            if (!_player.IsPerfectDodgeActive)
            {
                Check(_player.Dodge.Sequence.EndReason.Contains("마커"), "Actual Animator closes at authored source marker; actual reason: " + _player.Dodge.Sequence.EndReason);
                Check(_player.Dodge.Recording.Count > 2, "Live runtime records completed game frames");
                Near(CombatTimeController.Scale, 1, "Actual Animator-driven natural end restores speed");
                Finish(null);
            }
            else if (EditorApplication.timeSinceStartup - _liveStart > 6)
                Finish(new Exception("Actual Animator did not reach perfect-dodge end marker"));
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void EnterDodge(bool backwards = false)
    {
        _player.StateMachine.Transition(_player.StateMachine.IdleState);
        Field<PlayerDodgeChargePool>(_player, "_dodgeCharges").Tick(100);
        Check(_player.StateMachine.Transition(backwards ? _player.StateMachine.BackDodgeState : _player.StateMachine.FrontDodgeState), "Recharged test dodge is accepted");
    }

    private static void SetDefinition(PlayerDodgeDefinition definition)
        => typeof(PlayerCore).GetField("_dodgeDefinition", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_player, definition);
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); _checks++; }
    private static void Near(float actual, float expected, string message, float tolerance = .001f)
        => Check(Mathf.Abs(actual - expected) <= tolerance, message + " (actual " + actual + ", expected " + expected + ")");

    private static void Finish(Exception exception)
    {
        EditorApplication.update -= WaitForPlay;
        EditorApplication.update -= ObserveLive;
        _live = false;
        string diagnostic = _player != null && _player.Dodge?.Sequence != null ?
            "\nSession time=" + _player.Dodge.Sequence.Time + ", source=" + _player.Dodge.Sequence.SourceTime + ", reason=" + _player.Dodge.Sequence.EndReason : "";
        if (_player != null)
        {
            _player.Dodge?.Dispose();
            SetDefinition(_original);
            _player.OnPerfectDodgeCheck = _originalCheck;
        }
        if (_test != null) UnityEngine.Object.Destroy(_test);
        Application.runInBackground = _savedBackground;
        Directory.CreateDirectory("Temp");
        string result = exception == null ? "PASS " + _checks + " checks; " + _liveFrames + " live editor frames" : "FAIL " + exception + diagnostic;
        File.WriteAllText("Temp/DodgeStudioRuntime.result", result);
        if (exception == null) Debug.Log("Dodge Studio: " + result);
        else Debug.LogException(exception);
        SessionState.SetBool(Key, false);
        EditorApplication.isPlaying = false;
    }
}
