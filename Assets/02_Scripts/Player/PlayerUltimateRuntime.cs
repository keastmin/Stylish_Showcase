using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns a cast snapshot. Authored data changes apply to the next cast.</summary>
public sealed class PlayerUltimateRuntime
{
    private readonly PlayerCore _core;
    private readonly List<HashSet<IDamageable>> _hitTargets = new();
    private readonly List<IHitStopParticipant> _victims = new();
    private readonly List<GameObject> _spawnedEffects = new();
    private UltimatePosePlayer _pose;
    private readonly List<ScheduledHit> _scheduledHits = new();
    private readonly Dictionary<UltimatePull, Vector3> _pullCenters = new();
    private readonly HashSet<EnemyCore> _controlled = new();
    private object _castToken;
    private readonly List<ScheduledCue> _scheduledCues = new();
    private bool _previousFireEvents;
    private Quaternion _facing;
    private Quaternion _castFacing;
    private float _consumedMotionTime;
    private Vector3 _retainedMotion;
    private bool[] _firedCues;
    private bool[] _firedHits;
    private bool _pendingStep;
    private bool _pendingReplay;
    private float _pendingTime;
    private float _resolvedTime;
    private Quaternion _pendingFacing;
    private bool _heightMode;
    private bool _combatStarted;

    private struct ScheduledHit { public UltimateHitField field; public float time; }
    private struct ScheduledCue { public UltimateCue cue; public float time; }

    public PlayerUltimateDefinition ActiveDefinition { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsIntroPlaying { get; private set; }
    public float Time { get; private set; }
    public float CooldownRemaining { get; private set; }
    public float CooldownDuration { get; private set; }
    public Quaternion Facing => _facing;
    public int LastHitCount { get; private set; }
    public bool IsReady => !IsActive && CooldownRemaining <= 0f && _core.UltimateDefinition != null && _core.UltimateDefinition.Ready;

    // Camera rigs, sound, or other presentation can subscribe without modifying damage logic.
    public event Action<UltimateCue> CueTriggered;
    public event Action<UltimateHitField, int> FieldHit;

    public PlayerUltimateRuntime(PlayerCore core) => _core = core;

    public bool Begin()
    {
        if (!IsReady) return false;
        ActiveDefinition = UnityEngine.Object.Instantiate(_core.UltimateDefinition);
        ActiveDefinition.hideFlags = HideFlags.HideAndDontSave;
        CooldownDuration = Mathf.Max(0f, ActiveDefinition.cooldown);
        CooldownRemaining = CooldownDuration;
        Time = 0f;
        LastHitCount = 0;
        _consumedMotionTime = 0f;
        _retainedMotion = Vector3.zero;
        _facing = _core.Rotator.FacingRotation;
        _castFacing = _facing;
        _pendingStep = _pendingReplay = false;
        _resolvedTime = 0f;
        _castToken = new object();
        BuildSchedule();
        SortScheduledHits();
        _hitTargets.Clear();
        foreach (ScheduledHit field in _scheduledHits)
            _hitTargets.Add(new HashSet<IDamageable>());
        _firedCues = new bool[_scheduledCues.Count];
        _firedHits = new bool[_scheduledHits.Count];
        _previousFireEvents = _core.Animator.fireEvents;
        IsActive = true;
        _combatStarted = false;
        if (ActiveDefinition.cutscene != null && ActiveDefinition.cutscene.Ready)
        {
            IsIntroPlaying = true;
            if (VirtualCutscenePlayback.TryPlay(ActiveDefinition.cutscene, _core, completed =>
            {
                IsIntroPlaying = false;
                if (!IsActive) return;
                if (!completed) { Stop(); return; }
                try { BeginCombat(); }
                catch (Exception exception) { Debug.LogException(exception, _core); Stop(); }
            })) return true;
            IsIntroPlaying = false;
        }
        BeginCombat();
        return true;
    }

    private void BeginCombat()
    {
        _combatStarted = true;
        _core.Animator.fireEvents = false;
        _pose = UltimatePosePlayer.Create(_core.Animator, ActiveDefinition);
        _heightMode = WantsRootHeightAt(0f);
        if (_heightMode) _core.Mover.BeginRootMotionHeight();
        SampleAnimation();
        EvaluateHits(-0.0001f, 0f);
        EvaluateCues(-0.0001f, 0f);
    }

    public void TickCooldown(float gameDeltaSeconds)
        => CooldownRemaining = Mathf.Max(0f, CooldownRemaining - Mathf.Max(0f, gameDeltaSeconds));

    public void Tick(float combatDeltaSeconds)
    {
        if (!IsActive || !_combatStarted || IsIntroPlaying || _core.IsHitStopped || combatDeltaSeconds <= 0f) return;
        float previous = Time;
        Time = Mathf.Min(ActiveDefinition.Duration, Time + combatDeltaSeconds * Mathf.Max(0.05f, ActiveDefinition.playbackSpeed));
        SampleAnimation();
        if (!ActiveDefinition.HasSequence)
        {
            EvaluateHits(previous, Time);
            EvaluateCues(previous, Time);
        }
        // The state performs Exit after the final sample so lifetime cleanup has one path.
    }

    public bool CanCancelWithInput => IsActive && _combatStarted && !IsIntroPlaying && !_core.IsHitStopped &&
        ActiveDefinition.useInputCancelTime && Time >= ActiveDefinition.InputCancelTime;

    public bool IsComplete => IsActive && _combatStarted && !IsIntroPlaying && Time >= ActiveDefinition.Duration &&
        (ActiveDefinition.HasSequence ? _consumedMotionTime >= Time && !_pendingStep :
         ActiveDefinition.warp == null || !ActiveDefinition.warp.enabled || _consumedMotionTime >= Time);

    public void FixedTick()
    {
        if (!IsActive || !_combatStarted || IsIntroPlaying || _core.IsHitStopped) return;
        if (ActiveDefinition.HasSequence)
        {
            // A hitstop can begin after a move command but before physics consumes it.
            // Replay that command first; only then resolve its hit markers at the actual body pose.
            if (_pendingReplay)
            {
                _core.Mover.Move(_retainedMotion / Mathf.Max(0.0001f, UnityEngine.Time.fixedDeltaTime));
                _retainedMotion = Vector3.zero;
                _pendingReplay = false;
                return;
            }
            if (_pendingStep)
            {
                _facing = _pendingFacing;
                EvaluateHits(_resolvedTime, _pendingTime);
                EvaluateCues(_resolvedTime, _pendingTime);
                _resolvedTime = _pendingTime;
                _pendingStep = false;
            }
            // Hit/cue callbacks can stop the cast or begin hitstop during resolution.
            // Never consume timeline motion that the motor will refuse to accept.
            if (!IsActive || _core.IsHitStopped) return;
            if (Time <= _consumedMotionTime) return;
            UltimateSequenceMotion before = UltimateSequenceUtility.EvaluateMotion(ActiveDefinition, _consumedMotionTime);
            UltimateSequenceMotion after = UltimateSequenceUtility.EvaluateMotion(ActiveDefinition, Time);
            bool wantsHeight = WantsRootHeightBetween(_consumedMotionTime, Time);
            if (wantsHeight && !_heightMode) _core.Mover.BeginRootMotionHeight();
            else if (!wantsHeight && _heightMode) _core.Mover.EndRootMotionHeight();
            _heightMode = wantsHeight;
            _facing = _castFacing * after.rotation;
            _core.Rotator.RotateImmediately(_facing * Vector3.forward);
            _core.Mover.Move((_castFacing * (after.position - before.position) + _retainedMotion) /
                Mathf.Max(0.0001f, UnityEngine.Time.fixedDeltaTime));
            _retainedMotion = Vector3.zero;
            _consumedMotionTime = Time;
            _pendingFacing = _facing;
            _pendingTime = Time;
            _pendingStep = true;
            return;
        }
        UltimateMotionWarp warp = ActiveDefinition.warp;
        Vector3 delta = warp == null ? Vector3.zero : warp.EvaluateOffset(Time) - warp.EvaluateOffset(_consumedMotionTime);
        _consumedMotionTime = Time;
        _core.Mover.Move((_facing * delta + _retainedMotion) / Mathf.Max(0.0001f, UnityEngine.Time.fixedDeltaTime));
        _retainedMotion = Vector3.zero;
    }

    public void PreservePendingMotion()
    {
        _retainedMotion += _core.Mover.ConsumePendingDisplacement();
        if (ActiveDefinition != null && ActiveDefinition.HasSequence && _retainedMotion.sqrMagnitude > 0.0000001f)
            _pendingReplay = true;
    }

    private void SampleAnimation()
    {
        _pose?.Sample(Time);
    }

    private void EvaluateHits(float previous, float current)
    {
        Physics.SyncTransforms();
        for (int i = 0; i < _scheduledHits.Count; i++)
        {
            ScheduledHit scheduled = _scheduledHits[i];
            UltimateHitField field = scheduled.field;
            if (_firedHits[i] || field == null || !field.enabled || current < previous ||
                !(previous < scheduled.time && scheduled.time <= current)) continue;
            // A missed marker is still consumed. A target entering on a later frame cannot
            // receive damage from an attack instant that has already passed.
            _firedHits[i] = true;
            UltimatePull release = ActiveDefinition.FindPull(field.releasePullId);
            if (release != null)
                foreach (EnemyCore enemy in _controlled)
                    if (enemy != null) enemy.Control.Release(_castToken, release);

            UltimatePull pull = ActiveDefinition.FindPull(field.pullId);
            Vector3 center = Vector3.zero;
            Func<Vector3> follow = null;
            if (pull != null)
            {
                if (pull.anchor != UltimatePullAnchor.FirstMarker || !_pullCenters.TryGetValue(pull, out center))
                    center = pull.Center(CurrentPosition, _facing);
                if (pull.anchor == UltimatePullAnchor.FirstMarker) _pullCenters.TryAdd(pull, center);
                if (pull.anchor == UltimatePullAnchor.FollowPlayer)
                {
                    Vector3 lastCenter = center;
                    follow = () => _core != null && _core.isActiveAndEnabled
                        ? lastCenter = pull.Center(_core.Mover.PhysicsPosition, _core.Rotator.FacingRotation)
                        : lastCenter;
                }
            }

            Collider[] colliders = UltimateFieldGeometry.Overlap(field, CurrentPosition, _facing, ActiveDefinition.targetLayers);
            _victims.Clear();
            int count = 0;
            foreach (Collider collider in colliders)
            {
                if (collider == null || collider.transform.IsChildOf(_core.transform)) continue;
                IDamageable target = collider.GetComponentInParent<IDamageable>();
                // Compound colliders represent one target even when it rejects the damage.
                if (target == null || !_hitTargets[i].Add(target)) continue;
                DamageData damage = new(_core.gameObject, Mathf.Max(0f, field.damage), Mathf.Max(0, field.hitStopFrames), field.stagger);
                if (target is EnemyCore enemy && pull != null)
                {
                    if (!enemy.TakeHit(damage, _castToken, pull, center, follow)) continue;
                    if (enemy.Control.Active) _controlled.Add(enemy);
                }
                else if (!target.TryTakeDamage(damage)) continue;
                count++;
                if (target is IHitStopParticipant participant) _victims.Add(participant);
            }
            if (count == 0) continue;
            LastHitCount += count;
            _core.GetComponentInChildren<PlayerSFXController>()?.PlayOneShotHitFeedbackSFX();
            if (field.hitStopMode == AttackHitStopMode.VictimsOnly)
                HitstopCoordinator.RequestVictimsOnly(_victims, field.hitStopFrames);
            else
                HitstopCoordinator.Request(_core, _victims, field.hitStopFrames);
            FieldHit?.Invoke(field, count);
        }
    }

    private void EvaluateCues(float previous, float current)
    {
        for (int i = 0; i < _scheduledCues.Count; i++)
        {
            ScheduledCue scheduled = _scheduledCues[i];
            UltimateCue cue = scheduled.cue;
            if (cue == null || _firedCues[i] || scheduled.time < previous || scheduled.time > current) continue;
            _firedCues[i] = true;
            if (cue.prefab != null)
            {
                GameObject effect = UnityEngine.Object.Instantiate(cue.prefab,
                    CurrentPosition + _facing * cue.position, _facing * Quaternion.Euler(cue.euler));
                _spawnedEffects.Add(effect);
                CombatVfxTime.RegisterHierarchy(effect, owner: _core);
                UnityEngine.Object.Destroy(effect, Mathf.Max(0.05f, cue.lifetime));
            }
            CueTriggered?.Invoke(cue);
        }
    }

    public void Stop()
    {
        if (IsActive && !IsComplete)
            foreach (EnemyCore enemy in _controlled)
                if (enemy != null) enemy.Control.Release(_castToken, null);
        bool restoreAnimation = IsActive && _combatStarted;
        IsActive = false;
        IsIntroPlaying = false;
        VirtualCutscenePlayback.CancelFor(_core);
        _controlled.Clear();
        _pullCenters.Clear();
        _castToken = null;
        _pose?.Dispose();
        _pose = null;
        if (_heightMode) _core.Mover.EndRootMotionHeight();
        _heightMode = false;
        if (restoreAnimation && _core.Animator != null)
            _core.Animator.fireEvents = _previousFireEvents;
        _combatStarted = false;
        foreach (GameObject effect in _spawnedEffects)
            if (effect != null) UnityEngine.Object.Destroy(effect);
        _spawnedEffects.Clear();
        if (ActiveDefinition != null) UnityEngine.Object.Destroy(ActiveDefinition);
        ActiveDefinition = null;
        _hitTargets.Clear();
        _retainedMotion = Vector3.zero;
        _pendingStep = _pendingReplay = false;
        _scheduledHits.Clear();
        _scheduledCues.Clear();
        _firedHits = Array.Empty<bool>();
    }

    private Vector3 CurrentPosition => ActiveDefinition != null && ActiveDefinition.HasSequence
        ? _core.Mover.PhysicsPosition : _core.transform.position;

    private bool WantsRootHeightAt(float time)
    {
        if (!UltimateSequenceUtility.GetSegment(ActiveDefinition, time, out int index, out _)) return false;
        UltimateSequenceClip entry = ActiveDefinition.sequence[index];
        return entry.rootMotion && entry.applyRootHeight;
    }

    private bool WantsRootHeightBetween(float from, float to)
    {
        float start = 0f;
        foreach (UltimateSequenceClip entry in ActiveDefinition.sequence)
        {
            float end = start + entry.Duration;
            if (from < end && to > start && entry.rootMotion && entry.applyRootHeight) return true;
            start = end;
        }
        return false;
    }

    private void BuildSchedule()
    {
        _scheduledHits.Clear();
        _scheduledCues.Clear();
        if (!ActiveDefinition.HasSequence)
        {
            foreach (UltimateHitField hit in ActiveDefinition.hits)
                if (hit != null) _scheduledHits.Add(new ScheduledHit { field = hit, time = hit.time });
            foreach (UltimateCue cue in ActiveDefinition.cues)
                if (cue != null) _scheduledCues.Add(new ScheduledCue { cue = cue, time = cue.time });
            return;
        }
        float sequenceStart = 0f;
        foreach (UltimateSequenceClip entry in ActiveDefinition.sequence)
        {
            float speed = Mathf.Max(0.05f, entry.speed);
            if (entry.hits != null)
                foreach (UltimateHitField hit in entry.hits)
                {
                    if (hit == null || hit.time < entry.SourceStart || hit.time > entry.SourceEnd) continue;
                    _scheduledHits.Add(new ScheduledHit
                    {
                        field = hit,
                        time = sequenceStart + (hit.time - entry.SourceStart) / speed
                    });
                }
            if (entry.cues != null)
                foreach (UltimateCue cue in entry.cues)
                    if (cue != null && cue.time >= entry.SourceStart && cue.time <= entry.SourceEnd)
                        _scheduledCues.Add(new ScheduledCue { cue = cue, time = sequenceStart + (cue.time - entry.SourceStart) / speed });
            sequenceStart += entry.Duration;
        }
    }

    private void SortScheduledHits()
    {
        // Timeline dragging can reorder marker times without reordering the authoring list.
        // Resolve crossed instants chronologically, preserving authored order for ties.
        for (int i = 1; i < _scheduledHits.Count; i++)
        {
            ScheduledHit hit = _scheduledHits[i];
            int previous = i - 1;
            while (previous >= 0 && _scheduledHits[previous].time > hit.time)
            {
                _scheduledHits[previous + 1] = _scheduledHits[previous];
                previous--;
            }
            _scheduledHits[previous + 1] = hit;
        }
    }
}
