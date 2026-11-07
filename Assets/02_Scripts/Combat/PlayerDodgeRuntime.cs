using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct DodgeTraceFrame
{
    public readonly float time, sourceTime, requestedScale, appliedScale;
    public readonly DodgePhase phase;
    public readonly bool window, invulnerable;
    public readonly Vector3 position;
    public DodgeTraceFrame(DodgeSequence sequence, PlayerCore core)
    {
        time = sequence.Time; sourceTime = sequence.SourceTime; requestedScale = sequence.SlowScale;
        appliedScale = core.IsHitStopped ? 0 : CombatTimeController.Scale; phase = sequence.Phase;
        window = sequence.WindowOpen; invulnerable = core.IsInvulnerable; position = core.transform.position;
    }
}

/// <summary>Adapts the shared timing evaluator to the existing FSM, Animator and combat-time owners.</summary>
public sealed class PlayerDodgeRuntime : IDisposable
{
    private readonly PlayerCore _core;
    private readonly PlayerDodgeAfterimage _afterimage;
    private PlayerDodgeDefinition _snapshot;
    private ScriptableObject _timeOwner;
    private bool _notifiedSuccess, _notifiedSlow, _notifiedEnd;
    private sealed class Tail { public DodgeSequence sequence; public PlayerDodgeDefinition snapshot; }
    private readonly List<Tail> _tails = new List<Tail>();
    public readonly List<DodgeTraceFrame> Recording = new List<DodgeTraceFrame>();
    public DodgeSequence Sequence { get; private set; }
    public EnemyCore Source { get; private set; }
    public bool Configured => _core.DodgeDefinition != null;
    public bool PerfectActive => Sequence != null && Sequence.PerfectActive;
    public bool WindowOpen => Sequence != null && Sequence.WindowOpen;
    public bool Invulnerable => Sequence != null && Sequence.Invulnerable;
    public float MotionMultiplier => Sequence != null && Sequence.Phase != DodgePhase.Complete ?
        Mathf.Max(.01f, Sequence.Motion.speed) / Mathf.Max(.01f, Sequence.Motion.originalStateSpeed) : 1;

    public PlayerDodgeRuntime(PlayerCore core) { _core = core; _afterimage = new PlayerDodgeAfterimage(core); }
    public bool UsesLegacyAfterimage => _afterimage.UsesLegacy;
    public ProjectR.VFX.ChromaticAfterimageTrail Afterimage => _afterimage.Effect;
    public void LateTick() => _afterimage.LateTick();

    public void BeginDodge(bool backwards)
    {
        if (Sequence != null)
        {
            Sequence.EndMotion(true);
            foreach (DodgeEffectKind kind in Enum.GetValues(typeof(DodgeEffectKind)))
                if (Sequence.OwnsChannel(kind)) foreach (var tail in _tails) tail.sequence.SuppressChannel(kind);
            _tails.Add(new Tail { sequence = Sequence, snapshot = _snapshot });
        }
        _snapshot = UnityEngine.Object.Instantiate(_core.DodgeDefinition);
        _snapshot.hideFlags = HideFlags.HideAndDontSave;
        _afterimage.Begin(_snapshot.afterimage);
        if (_timeOwner == null)
        { _timeOwner = ScriptableObject.CreateInstance<DodgeTimeRequest>(); _timeOwner.hideFlags = HideFlags.HideAndDontSave; }
        _notifiedSuccess = _notifiedSlow = _notifiedEnd = false;
        Source = null;
        Recording.Clear();
        Sequence = new DodgeSequence(_snapshot, backwards, () =>
        {
            EnemyCore found;
            if (!_core.IsPerfectDodge(out found)) return false;
            Source = found;
            return true;
        }, InheritedEffect);
        _afterimage.Trigger(DodgeAfterimageStart.DodgeStart);
        Synchronize();
    }

    public void ForcePerfect(EnemyCore source)
    {
        if (source == null || !source.isActiveAndEnabled || source.CurrentHP <= 0) return;
        if (Sequence == null || Sequence.Phase != DodgePhase.Dodge) BeginDodge(false);
        Source = source;
        Sequence.Succeed();
        Synchronize();
    }

    public void Tick(float delta)
    {
        if (!Configured) { if (Sequence != null) DisposeSession(); return; }
        if (Sequence == null || Time.timeScale <= 0) return;
        for (int i = _tails.Count - 1; i >= 0; i--)
        {
            _tails[i].sequence.Tick(delta, false, true);
            if (!_tails[i].sequence.HasPendingWork)
            { UnityEngine.Object.Destroy(_tails[i].snapshot); _tails.RemoveAt(i); }
        }
        if (PerfectActive && (Source == null || !Source.isActiveAndEnabled || Source.CurrentHP <= 0))
        { Sequence.Cancel(); Synchronize(); return; }
        float observed = Sequence.SourceTime;
        if (Sequence.Phase != DodgePhase.Complete)
        {
            var animator = _core.Animator;
            int hash = Animator.StringToHash(Sequence.Motion.stateName ?? "");
            var state = animator.GetCurrentAnimatorStateInfo(0);
            if (animator.IsInTransition(0))
            {
                var next = animator.GetNextAnimatorStateInfo(0);
                if (next.shortNameHash == hash) state = next;
            }
            if (state.shortNameHash == hash) observed = Mathf.Max(0, state.normalizedTime) * Sequence.Motion.Length;
        }
        Sequence.Tick(delta, false, _core.IsHitStopped, 1, observed);
        Synchronize();
        if (Recording.Count < 3600) Recording.Add(new DodgeTraceFrame(Sequence, _core));
    }

    public bool PrepareCounter()
    {
        if (!WindowOpen) return false;
        if (Source == null || !Source.isActiveAndEnabled || Source.CurrentHP <= 0)
        { Sequence.Cancel(); Synchronize(); return false; }
        return Sequence.TryCounter();
    }

    public void CounterPrepared() => Synchronize();

    public void EnterCounterPhase(DodgePhase phase)
    {
        if (Sequence == null || !Sequence.CounterStarted) return;
        if (Sequence.Phase != phase) Sequence.EnterPhase(phase);
        Synchronize();
    }

    public void BeforeTransition(PlayerStateBase previous, PlayerStateBase next)
    {
        if (!Configured || Sequence == null) return;
        if (previous is PlayerDodgeState) _afterimage.EndMotion();
        if (previous is PlayerDodgeState && !(next is PlayerDodgeAttackState))
            Sequence.EndMotion(Sequence.PerfectActive);
        if (previous is PlayerDodgeAttackState && !(next is PlayerDodgeAttackState))
            Sequence.EndMotion(Sequence.Phase != DodgePhase.CounterEnd || Sequence.SourceTime + .05f < Sequence.Motion.End);
        Synchronize();
    }

    public void EndPerfect(bool immediate)
    {
        if (Sequence == null) return;
        if (immediate) Sequence.Cancel();
        else Sequence.ClosePerfect("애니메이션 이벤트");
        Synchronize();
    }

    public void AnimationPerfectEnd()
    {
        // The imported source marker is evaluated by DodgeSequence. The original
        // clip event must not close a window a second time or override an edited marker.
    }

    private void Synchronize()
    {
        if (Sequence == null) return;
        if (_timeOwner != null)
        {
            float scale = Sequence.SlowScale;
            bool requested = Sequence.SlowRequested;
            foreach (var tail in _tails) { if (tail.sequence.SlowRequested) { scale = Mathf.Min(scale, tail.sequence.SlowScale); requested = true; } }
            if (requested) CombatTimeController.SetEvaluatedScale(_timeOwner, scale);
            else CombatTimeController.RemoveEvaluatedScale(_timeOwner);
        }
        if (!_notifiedSuccess && Sequence.SuccessTime >= 0)
        { _notifiedSuccess = true; _core.NotifyDodgeSuccess(); _afterimage.Trigger(DodgeAfterimageStart.PerfectSuccess); }
        bool started = Sequence.Events.Exists(e => e.name == "슬로우 시작");
        if (!_notifiedSlow && started)
        { _notifiedSlow = true; _core.NotifyDodgeSlowStarted(Source); _afterimage.Trigger(DodgeAfterimageStart.SlowStart); }
        if (_notifiedSlow && !_notifiedEnd && !Sequence.PerfectActive)
        { _notifiedEnd = true; _core.NotifyDodgeWindowEnded(); }
        _core.RefreshDodgeAnimationSpeed();
    }

    private void DisposeSession()
    {
        _afterimage.Dispose();
        if (_timeOwner != null) { CombatTimeController.RemoveEvaluatedScale(_timeOwner); UnityEngine.Object.Destroy(_timeOwner); }
        if (_snapshot != null) UnityEngine.Object.Destroy(_snapshot);
        foreach (var tail in _tails) if (tail.snapshot != null) UnityEngine.Object.Destroy(tail.snapshot);
        _tails.Clear();
        Sequence = null; Source = null; _snapshot = null; _timeOwner = null;
    }

    private DodgeEffectValue InheritedEffect(DodgeEffectKind kind)
    {
        for (int i = _tails.Count - 1; i >= 0; i--)
            if (_tails[i].sequence.OwnsChannel(kind)) return _tails[i].sequence.Effect(kind);
        return default;
    }

    public void Dispose() => DisposeSession();
}

public sealed class DodgeTimeRequest : ScriptableObject { }
