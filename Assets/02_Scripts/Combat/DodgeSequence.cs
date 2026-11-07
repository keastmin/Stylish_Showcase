using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct DodgeEffectValue
{
    public readonly float value;
    public readonly Color color;
    public readonly bool active;
    public DodgeEffectValue(float value, Color color, bool active) { this.value = value; this.color = color; this.active = active; }
}

public readonly struct DodgeSequenceEvent
{
    public readonly float time;
    public readonly string name;
    public DodgeSequenceEvent(float time, string name) { this.time = time; this.name = name; }
}

/// <summary>
/// Shared timing evaluator for runtime and scrubbing. Real-time envelopes are independent of motion time.
/// Preview integrates motion; runtime supplies the actual Animator clip time and phase changes.
/// </summary>
public sealed class DodgeSequence
{
    private sealed class CueState
    {
        public DodgeScreenCue cue;
        public bool counter, armed, started, suppressed;
        public float scheduled, start, from, forcedFade = -1, fadeFrom;
    }

    public readonly PlayerDodgeDefinition Definition;
    public readonly bool Backwards;
    public readonly List<DodgeSequenceEvent> Events = new List<DodgeSequenceEvent>();
    private readonly List<CueState> _cues = new List<CueState>();
    private readonly CombatSlowMotion _slow = new CombatSlowMotion();
    private readonly Func<bool> _judge;
    private readonly Func<DodgeEffectKind, DodgeEffectValue> _inherited;
    private bool _judgeAttempted, _slowStarted, _slowReleasing, _perfectClosed, _holdForCounter;
    private float _success = -1, _slowStart = -1, _counterStart = -1;
    private DodgePhase _completedPhase;
    private double _realTime;
    public float Time => (float)_realTime;
    public float SourceTime { get; private set; }
    public float SuccessTime => _success;
    public float CounterTime => _counterStart;
    public float SlowScale => _slowStarted ? _slow.Scale : 1;
    public bool SlowRequested => _slowStarted && !_slow.IsComplete;
    public bool PerfectActive => _success >= 0 && !_perfectClosed && !CounterStarted;
    public bool CounterStarted => _counterStart >= 0;
    public bool CounterActive => CounterStarted && Phase != DodgePhase.Complete;
    public bool WindowOpen => PerfectActive && Time >= _success + Mathf.Max(0, Definition.counterOpenDelay) &&
        Time < _success + Mathf.Max(0, Definition.counterOpenDelay) + Mathf.Max(.01f, Definition.counterWindowDuration);
    public bool Invulnerable => CounterActive ? Definition.counter.invulnerable : PerfectActive && Definition.invulnerableOnSuccess &&
        Time >= _success + Definition.invulnerabilityDelay && (Definition.invulnerabilityDuration <= 0 || Time < _success + Definition.invulnerabilityDuration);
    public DodgePhase Phase { get; private set; }
    public string EndReason { get; private set; } = "";
    public DodgeMotion Motion => Definition.Motion(Phase == DodgePhase.Complete ? _completedPhase : Phase, Backwards);

    public DodgeSequence(PlayerDodgeDefinition definition, bool backwards, Func<bool> judge, Func<DodgeEffectKind, DodgeEffectValue> inherited = null)
    {
        Definition = definition;
        Backwards = backwards;
        _judge = judge;
        _inherited = inherited;
        foreach (var cue in definition.dodgeEffects) if (cue != null) _cues.Add(new CueState { cue = cue });
        foreach (var cue in definition.counterEffects) if (cue != null) _cues.Add(new CueState { cue = cue, counter = true });
        Log("회피 시작");
        Anchor(DodgeEffectAnchor.DodgeStart);
        CheckJudgement(0, 0);
        UpdateScheduled();
    }

    public void Succeed()
    {
        if (_success >= 0 || Phase != DodgePhase.Dodge) return;
        _success = Time;
        Log("완벽 회피 성공");
        Anchor(DodgeEffectAnchor.PerfectSuccess);
        UpdateScheduled();
    }

    public void Tick(float realDelta, bool autoAdvance = true, bool motionFrozen = false, float otherScale = 1, float observedSourceTime = -1)
    {
        if (realDelta <= 0) { UpdateScheduled(); return; }
        // Fixed upper step keeps source-marker crossings consistent across render frame rates.
        float remaining = realDelta;
        float startSource = SourceTime;
        float progressed = 0;
        while (remaining > .000001f)
        {
            float step = Mathf.Min(remaining, 1f / 240f);
            remaining -= step;
            progressed += step;
            float previous = SourceTime;
            float motionDelta = motionFrozen ? 0 : step * Mathf.Min(Mathf.Clamp01(otherScale), SlowScale) * Mathf.Max(.01f, Motion.speed);
            if (Phase != DodgePhase.Complete)
                SourceTime = observedSourceTime >= 0 ? Mathf.Lerp(startSource, Mathf.Max(startSource, observedSourceTime), progressed / realDelta) : SourceTime + motionDelta;
            _realTime += step;
            _slow.Tick(step);
            CheckJudgement(previous, SourceTime);
            CheckPoseCues(previous, SourceTime);
            UpdateScheduled();
            if (PerfectActive && Definition.closeAtAnimationMarker && Motion.perfectEnd >= 0 && SourceTime >= Motion.perfectEnd)
                ClosePerfect("애니메이션 종료 마커");
            if (PerfectActive && Time >= _success + Mathf.Max(0, Definition.counterOpenDelay) + Mathf.Max(.01f, Definition.counterWindowDuration))
                ClosePerfect("반격 입력 시간 만료");
            if (_slowStarted && !_slowReleasing && !_holdForCounter && !CounterStarted && Time >= _slowStart + Mathf.Max(.01f, Definition.slow.MaxDuration))
                ReleaseSlow(false);
            if (_holdForCounter && ((Phase == Definition.counter.releasePhase && SourceTime >= Definition.counter.releaseSourceTime) ||
                Phase > Definition.counter.releasePhase || Time >= _counterStart + Mathf.Max(.01f, Definition.counter.maxHold)))
                ReleaseCounterSlow();
            if (autoAdvance && Phase != DodgePhase.Complete && SourceTime >= Motion.End)
            {
                if (Phase == DodgePhase.Dodge) EndMotion(false);
                else if (Phase == DodgePhase.CounterEnd) EndMotion(false);
                else EnterPhase(Phase + 1);
            }
        }
    }

    private void CheckJudgement(float previous, float current)
    {
        if (Phase != DodgePhase.Dodge || _success >= 0 || _judgeAttempted && !Motion.judgeOverWindow) return;
        float begin = Mathf.Clamp(Motion.judgeStart, 0, Motion.Length);
        float end = Motion.judgeOverWindow ? Mathf.Max(begin, Motion.judgeEnd) : begin;
        if (current < begin || previous > end) return;
        _judgeAttempted = true;
        if (_judge != null && _judge()) Succeed();
    }

    private void UpdateScheduled()
    {
        if (PerfectActive && !_slowStarted && Time >= _success + Mathf.Max(0, Definition.slow.StartDelay))
        {
            _slowStarted = true;
            _slowStart = Time;
            _slow.Begin(Definition.slow);
            Log("슬로우 시작");
            Anchor(DodgeEffectAnchor.SlowStart);
        }
        foreach (var item in _cues)
            if (item.armed && !item.started && !item.suppressed && Time >= item.scheduled)
            {
                item.from = Effect(item.cue.kind).value;
                // A new cue owns the channel; an old tail never resurfaces afterwards.
                foreach (var old in _cues)
                    if (old != item && old.started && old.cue.kind == item.cue.kind) old.suppressed = true;
                item.started = true;
                item.start = item.scheduled;
                Log("화면 · " + item.cue.name);
            }
    }

    private void Anchor(DodgeEffectAnchor anchor)
    {
        foreach (var item in _cues)
            if (item.cue.enabled && !item.armed && !item.suppressed && item.cue.anchor == anchor && (!item.counter || CounterStarted))
            { item.armed = true; item.scheduled = Time + Mathf.Max(0, item.cue.delay); }
    }

    private void CheckPoseCues(float previous, float current)
    {
        foreach (var item in _cues)
            if (item.cue.enabled && !item.armed && !item.suppressed && item.cue.anchor == DodgeEffectAnchor.AnimationPose &&
                item.cue.posePhase == Phase && current >= item.cue.sourceTime && (!item.counter || CounterStarted))
            { item.armed = true; item.scheduled = Time + Mathf.Max(0, item.cue.delay); }
    }

    public bool TryCounter()
    {
        if (!WindowOpen) { Log("반격 입력 거부"); return false; }
        _counterStart = Time;
        _perfectClosed = true;
        Log("반격 시작");
        EndReason = "회피 반격";
        foreach (var item in _cues)
        {
            if (item.counter) continue;
            if (!item.started) item.suppressed = true;
            if (item.started && item.cue.onCounter == DodgeEffectHandoff.FadeOut)
            { item.fadeFrom = Value(item); item.forcedFade = Time; }
        }
        if (_slowStarted && !_slow.IsComplete)
        {
            if (_slowReleasing && Definition.counter.slowExit != DodgeSlowExit.Immediate)
            {
                _slow.Begin(new PerfectDodgeSettings { SlowScale = _slow.Scale, FadeInDuration = 0 });
                _slowReleasing = false;
            }
            switch (Definition.counter.slowExit)
            {
                case DodgeSlowExit.Immediate: ReleaseSlow(true); break;
                case DodgeSlowExit.Fade: ReleaseCounterSlow(); break;
                default: _holdForCounter = true; break;
            }
        }
        EnterPhase(DodgePhase.CounterStart);
        return true;
    }

    public void EnterPhase(DodgePhase phase)
    {
        Phase = phase;
        SourceTime = 0;
        if (phase == DodgePhase.CounterStart) Anchor(DodgeEffectAnchor.CounterStart);
        if (phase == DodgePhase.CounterLoop) { Log("반격 Loop"); Anchor(DodgeEffectAnchor.CounterLoop); }
        if (phase == DodgePhase.CounterEnd) { Log("반격 End"); Anchor(DodgeEffectAnchor.CounterEnd); }
        CheckPoseCues(0, 0);
        UpdateScheduled();
    }

    public void ClosePerfect(string reason, bool immediate = false)
    {
        if (_success < 0 || _perfectClosed) return;
        _perfectClosed = true;
        EndReason = reason;
        Log("완벽 회피 종료 · " + reason);
        if (Definition.releaseSlowWithWindow || immediate) ReleaseSlow(immediate);
    }

    public void EndMotion(bool interrupted)
    {
        if (Phase == DodgePhase.Complete) return;
        ClosePerfect(interrupted ? "행동 중단" : "회피 동작 종료", interrupted);
        if (CounterActive) { EndReason = interrupted ? "반격 중단" : "반격 완료"; Log(EndReason); }
        _completedPhase = Phase;
        Phase = DodgePhase.Complete;
        if (_holdForCounter || interrupted) ReleaseCounterSlow();
        // An anchor in a discarded animation must not fire in a later action.
        foreach (var cue in _cues) if (!cue.armed) cue.suppressed = true;
    }

    public void Cancel()
    {
        ClosePerfect("요청 정리", true);
        ReleaseSlow(true);
        _completedPhase = Phase;
        Phase = DodgePhase.Complete;
        foreach (var item in _cues) item.suppressed = true;
        Log("연출 정리");
    }

    private void ReleaseCounterSlow()
    {
        _holdForCounter = false;
        ReleaseSlow(false, Definition.counter.fadeDuration, Definition.counter.fadeCurve);
    }

    private void ReleaseSlow(bool immediate, float duration = -1, AnimationCurve curve = null)
    {
        if (!_slowStarted || _slowReleasing && !immediate) return;
        _slowReleasing = true;
        _holdForCounter = false;
        var settings = new PerfectDodgeSettings { FadeOutDuration = immediate ? 0 : duration >= 0 ? duration : Definition.slow.FadeOutDuration,
            FadeOutCurve = curve ?? Definition.slow.FadeOutCurve };
        if (immediate)
        {
            // CombatSlowMotion deliberately ignores repeated Release; start at the current value first.
            _slow.Begin(new PerfectDodgeSettings { SlowScale = _slow.Scale, FadeInDuration = 0 });
        }
        _slow.Release(settings);
        Log(immediate ? "슬로우 즉시 해제" : "슬로우 복귀 시작");
    }

    public DodgeEffectValue Effect(DodgeEffectKind kind)
    {
        CueState selected = null;
        foreach (var item in _cues)
            if (item.started && !item.suppressed && item.cue.kind == kind && (selected == null || item.start >= selected.start)) selected = item;
        if (selected == null) return _inherited != null ? _inherited(kind) : new DodgeEffectValue(0, Color.black, false);
        float value = Value(selected);
        return new DodgeEffectValue(value, selected.cue.color, Mathf.Abs(value) > .00001f);
    }

    public bool OwnsChannel(DodgeEffectKind kind) => _cues.Exists(c => c.started && !c.suppressed && c.cue.kind == kind);
    public void SuppressChannel(DodgeEffectKind kind)
    {
        foreach (var cue in _cues) if (cue.cue.kind == kind) cue.suppressed = true;
    }
    public bool HasPendingWork => PerfectActive || CounterActive || SlowRequested || _cues.Exists(c => c.armed && !c.suppressed &&
        (!c.started || (c.forcedFade >= 0 ? Time < c.forcedFade + c.cue.counterFade : Time < c.start + c.cue.Duration)));

    private float Value(CueState item)
    {
        var cue = item.cue;
        if (item.forcedFade >= 0)
        {
            float t = cue.counterFade > 0 ? (Time - item.forcedFade) / cue.counterFade : 1;
            return Mathf.Lerp(item.fadeFrom, 0, Curve(cue.outCurve, t));
        }
        float elapsed = Mathf.Max(0, Time - item.start);
        if (elapsed < cue.fadeIn) return Mathf.Lerp(item.from, cue.strength, Curve(cue.inCurve, elapsed / cue.fadeIn));
        elapsed -= Mathf.Max(0, cue.fadeIn);
        if (elapsed < cue.hold) return cue.strength;
        elapsed -= Mathf.Max(0, cue.hold);
        return Mathf.Lerp(cue.strength, 0, Curve(cue.outCurve, cue.fadeOut > 0 ? elapsed / cue.fadeOut : 1));
    }

    public static float Curve(AnimationCurve curve, float progress)
    {
        progress = Mathf.Clamp01(progress);
        if (progress >= 1) return 1;
        if (progress <= 0) return 0;
        return Mathf.Clamp01(curve != null && curve.length > 0 ? curve.Evaluate(progress) : progress);
    }

    private void Log(string name) => Events.Add(new DodgeSequenceEvent(Time, name));
}
