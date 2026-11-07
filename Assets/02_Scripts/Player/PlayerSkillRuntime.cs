using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Timeline;

/// <summary>Owns E gameplay independently of its optional presentation Timeline.</summary>
public sealed class PlayerSkillRuntime
{
    private readonly PlayerCore _core;
    private readonly PlayableDirector _director;
    private readonly List<PlayerSkillHit> _hits = new();
    private readonly HashSet<IDamageable> _targets = new();
    private readonly List<IHitStopParticipant> _victims = new();
    private readonly List<Volume> _volumes = new();
    private readonly List<VolumeProfile> _profiles = new();
    private readonly List<PlayerSkillImpactFrame> _impacts = new();
    private readonly List<(TrackAsset track, UnityEngine.Object binding)> _impactBindings = new();
    private PlayerSkillAttackReceiver _receiver;
    private Quaternion _facing;
    private Vector3 _consumedOffset, _retainedMotion;
    private float _consumedMotionTime, _resolvedTime, _pendingTime, _recoveryRemaining;
    private int _nextHit;
    private bool _pendingStep, _pendingReplay, _presentationStarted;
    private DirectorUpdateMode _previousUpdateMode;
    private bool _previousFireEvents;
    private RuntimeAnimatorController _previousAnimatorController;
    private bool _previousApplyRootMotion, _ownsModelAnimation;

    public PlayerSkillDefinition ActiveDefinition { get; private set; }
    public float Time { get; private set; }
    public bool IsActive { get; private set; }
    public bool OwnsModelAnimation => _ownsModelAnimation;
    public int LastHitCount { get; private set; }
    public bool IsComplete => IsActive && Time >= ActiveDefinition.Duration && _recoveryRemaining <= 0f &&
        _consumedMotionTime >= Time && !_pendingStep && !_pendingReplay && _retainedMotion.sqrMagnitude < .0000001f;
    public bool CanCancelWithInput => IsActive && !_core.IsHitStopped && ActiveDefinition.useInputCancelTime &&
        Time >= ActiveDefinition.InputCancelTime && Time < ActiveDefinition.Duration;

    public PlayerSkillRuntime(PlayerCore core, PlayableDirector director) { _core = core; _director = director; }

    public bool Begin(PlayerSkillDefinition definition)
    {
        if (definition == null || _core == null) return false;
        Stop();
        ActiveDefinition = UnityEngine.Object.Instantiate(definition);
        ActiveDefinition.hideFlags = HideFlags.HideAndDontSave;
        ActiveDefinition.Normalize();
        Time = 0f; LastHitCount = 0; _nextHit = 0;
        _consumedMotionTime = _resolvedTime = _pendingTime = 0f;
        _consumedOffset = _retainedMotion = Vector3.zero;
        _pendingStep = _pendingReplay = false;
        _recoveryRemaining = ActiveDefinition.RecoveryDuration;
        _facing = _core.Rotator.FacingRotation;
        IsActive = true;
        try
        {
            _previousFireEvents = _core.Animator.fireEvents;
            if (_director != null)
            {
                _previousUpdateMode = _director.timeUpdateMode;
                // Release the previous graph's requests before changing any binding.
                _director.Stop();
            }
            foreach (PlayerSkillHit hit in ActiveDefinition.hits) if (hit != null) _hits.Add(hit);
            // Stable insertion sort keeps authored order at equal timestamps.
            for (int i = 1; i < _hits.Count; i++)
            {
                PlayerSkillHit hit = _hits[i]; int j = i - 1;
                while (j >= 0 && (_hits[j].time > hit.time ||
                    _hits[j].time == hit.time && _hits[j].executionOrder > hit.executionOrder))
                { _hits[j + 1] = _hits[j]; j--; }
                _hits[j + 1] = hit;
            }
            _receiver = _director != null ? _director.GetComponent<PlayerSkillAttackReceiver>() : null;
            _receiver?.SetRuntimeOwner(this);
            _core.Animator.fireEvents = false;
            DetachImpactTracks();
            CreateVolumes();
            if (_director != null)
            {
                _director.timeUpdateMode = DirectorUpdateMode.Manual;
                if (ActiveDefinition.playPresentation && _director.playableAsset != null)
                {
                    AcquireModelAnimation();
                    _director.GetComponent<PlayerSkillCameraDirector>()?.CaptureSkillStart(_core.transform.position, _facing);
                    _core.DirectorContainer.Play(DirectorID.Skill);
                    _presentationStarted = true;
                    SamplePresentation();
                }
            }
            SampleScreenEffects();
            EvaluateHits(-.0001f, 0f);
            return true;
        }
        catch { Stop(); throw; }
    }

    public void Tick(float combatDeltaSeconds)
    {
        if (!IsActive || _core.IsHitStopped || combatDeltaSeconds <= 0f) return;
        float delta = Mathf.Max(0f, PlayerSkillDefinition.Finite(combatDeltaSeconds, 0f));
        float remainingContent = Mathf.Max(0f, ActiveDefinition.Duration - Time) / ActiveDefinition.PlaybackSpeed;
        Time = Mathf.Min(ActiveDefinition.Duration, Time + delta * ActiveDefinition.PlaybackSpeed);
        if (delta > remainingContent) _recoveryRemaining -= delta - remainingContent;
        SamplePresentation();
        SampleScreenEffects();
    }

    public void FixedTick()
    {
        if (!IsActive || _core.IsHitStopped) return;
        if (_pendingReplay)
        {
            _core.Mover.Move(_retainedMotion / Mathf.Max(.0001f, UnityEngine.Time.fixedDeltaTime));
            _retainedMotion = Vector3.zero; _pendingReplay = false;
            return;
        }
        if (_pendingStep)
        {
            EvaluateHits(_resolvedTime, _pendingTime);
            _resolvedTime = _pendingTime; _pendingStep = false;
        }
        if (!IsActive || _core.IsHitStopped) return;
        // Even a motionless cast resolves markers only after the motor's previous physics step.
        if (Time <= _consumedMotionTime && _retainedMotion.sqrMagnitude <= .0000001f &&
            ActiveDefinition.EvaluateMotion(Time) == _consumedOffset) return;
        Vector3 offset = ActiveDefinition.EvaluateMotion(Time);
        Vector3 delta = _facing * (offset - _consumedOffset) + _retainedMotion;
        _core.Mover.Move(delta / Mathf.Max(.0001f, UnityEngine.Time.fixedDeltaTime));
        _retainedMotion = Vector3.zero; _consumedOffset = offset; _consumedMotionTime = Time;
        _pendingTime = Time; _pendingStep = true;
    }

    public void PreservePendingMotion()
    {
        if (!IsActive) return;
        _retainedMotion += _core.Mover.ConsumePendingDisplacement();
        if (_retainedMotion.sqrMagnitude > .0000001f) _pendingReplay = true;
    }

    private void EvaluateHits(float previous, float current)
    {
        Physics.SyncTransforms();
        while (_nextHit < _hits.Count && _hits[_nextHit].time <= current)
        {
            PlayerSkillHit hit = _hits[_nextHit++];
            if (!hit.enabled || hit.time <= previous) continue;
            _receiver?.PlayHitShake(hit.shakeForce);
            _targets.Clear(); _victims.Clear(); int accepted = 0;
            foreach (Collider collider in hit.Overlap(_core.Mover.PhysicsPosition, _facing))
            {
                if (collider == null || collider.transform.IsChildOf(_core.transform)) continue;
                IDamageable target = collider.GetComponentInParent<IDamageable>();
                if (target == null || !_targets.Add(target)) continue;
                var data = new DamageData(_core.gameObject, hit.damage, hit.hitStopFrames, hit.stagger);
                if (!target.TryTakeDamage(data)) continue;
                accepted++; _core.AddSkillGauge(hit.skillGaugeAdditive);
                if (target is IHitStopParticipant participant) _victims.Add(participant);
            }
            LastHitCount += accepted;
            if (accepted > 0) _core.GetComponentInChildren<PlayerSFXController>(true)?.PlayOneShotHitFeedbackSFX();
            if (hit.hitStopMode == AttackHitStopMode.VictimsOnly)
                HitstopCoordinator.RequestVictimsOnly(_victims, hit.hitStopFrames);
            else HitstopCoordinator.Request(_core, _victims, hit.hitStopFrames);
            if (!IsActive) return;
        }
    }

    private void SamplePresentation()
    {
        if (!_presentationStarted || _director == null) return;
        double end = _director.playableAsset != null ? _director.playableAsset.duration : 0d;
        // The motor may still be resolving E's final step, or authored recovery
        // may still own input. Keep the last pose until the FSM actually exits;
        // restoring the controller here exposes its default state in between.
        // Evaluate just inside the Timeline: its exact end has zero clip weight.
        _director.time = end > 0d ? Math.Min(Time, Math.Max(0d, end - .000001d)) : Time;
        Transform model = _ownsModelAnimation ? _core.Animator.transform : null;
        Vector3 position = model != null ? model.localPosition : Vector3.zero;
        Quaternion rotation = model != null ? model.localRotation : Quaternion.identity;
        try { _director.Evaluate(); }
        finally
        {
            // Gameplay data owns travel and facing, including when animation clips
            // contain root transforms. Sampling supplies only the skeleton pose.
            if (model != null) model.SetLocalPositionAndRotation(position, rotation);
        }
    }

    private void AcquireModelAnimation()
    {
        Animator animator = _core.Animator;
        if (animator == null || _ownsModelAnimation) return;
        bool animatesModel = false;
        foreach (PlayableBinding output in _director.playableAsset.outputs)
        {
            if (output.outputTargetType != typeof(Animator) ||
                output.sourceObject is TrackAsset track && track.mutedInHierarchy) continue;
            UnityEngine.Object binding = _director.GetGenericBinding(output.sourceObject);
            if (binding == animator || binding == animator.gameObject) { animatesModel = true; break; }
        }
        if (!animatesModel) return;

        // A manually sampled Timeline is evaluated in Update. The normal Animator
        // would otherwise run afterward and replace E's windup/dash with Idle.
        _previousAnimatorController = animator.runtimeAnimatorController;
        _previousApplyRootMotion = animator.applyRootMotion;
        _ownsModelAnimation = true;
        Vector3 position = animator.transform.localPosition;
        Quaternion rotation = animator.transform.localRotation;
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.transform.SetLocalPositionAndRotation(position, rotation);
    }

    private void StopPresentation()
    {
        try { if (_director != null) _director.Stop(); }
        finally
        {
            _presentationStarted = false;
            ReleaseModelAnimation();
        }
    }

    private void ReleaseModelAnimation()
    {
        if (!_ownsModelAnimation) return;
        _ownsModelAnimation = false;
        Animator animator = _core != null ? _core.Animator : null;
        if (animator != null)
        {
            Vector3 position = animator.transform.localPosition;
            Quaternion rotation = animator.transform.localRotation;
            animator.runtimeAnimatorController = _previousAnimatorController;
            animator.applyRootMotion = _previousApplyRootMotion;
            animator.transform.SetLocalPositionAndRotation(position, rotation);
        }
        _previousAnimatorController = null;
    }

    private void CreateVolumes()
    {
        foreach (PlayerSkillScreenEffect effect in ActiveDefinition.screenEffects)
        {
            Volume volume = null;
            if (effect != null && effect.profile != null)
            {
                var go = new GameObject("E Skill Screen Effect") { hideFlags = HideFlags.HideAndDontSave };
                volume = go.AddComponent<Volume>(); volume.isGlobal = true; volume.weight = 0f;
                volume.priority = PlayerSkillDefinition.Finite(effect.priority, 1000f);
                // Clone each override too: live profile edits apply to the next cast.
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.hideFlags = HideFlags.HideAndDontSave;
                foreach (VolumeComponent component in effect.profile.components)
                    if (component != null)
                    {
                        VolumeComponent copy = UnityEngine.Object.Instantiate(component);
                        copy.hideFlags = HideFlags.HideAndDontSave; profile.components.Add(copy);
                    }
                _profiles.Add(profile); volume.sharedProfile = profile;
            }
            _volumes.Add(volume);
        }
    }

    private void SampleScreenEffects()
    {
        for (int i = 0; i < _volumes.Count; i++)
        {
            if (_volumes[i] == null) continue;
            PlayerSkillScreenEffect effect = ActiveDefinition.screenEffects[i];
            bool active = effect.enabled && Time >= effect.start && Time < effect.start + effect.duration && Time < ActiveDefinition.Duration;
            float t = Mathf.Clamp01((Time - effect.start) / Mathf.Max(.001f, effect.duration));
            float envelope = effect.envelope != null && effect.envelope.length > 0 ? effect.envelope.Evaluate(t) : 1f;
            _volumes[i].weight = active ? Mathf.Clamp01(effect.weight * PlayerSkillDefinition.Finite(envelope, 0f)) : 0f;
        }
        bool impactActive = false; float normalizedTime = 0f;
        foreach (PlayerSkillImpactWindow window in ActiveDefinition.impactFrames)
            if (window != null && window.enabled && Time >= window.start && Time < window.start + window.duration && Time < ActiveDefinition.Duration)
            { impactActive = true; normalizedTime = (Time - window.start) / Mathf.Max(.001f, window.duration); }
        foreach (PlayerSkillImpactFrame impact in _impacts)
            if (impact != null) impact.SampleExternal(this, impactActive, normalizedTime);
    }

    private void DetachImpactTracks()
    {
        if (_director == null) return;
        if (_director.playableAsset is TimelineAsset timeline)
            foreach (TrackAsset track in Tracks(timeline.GetRootTracks()))
                if (_director.GetGenericBinding(track) is PlayerSkillImpactFrame impact)
                {
                    _impactBindings.Add((track, impact)); _director.SetGenericBinding(track, null);
                    if (!_impacts.Contains(impact)) _impacts.Add(impact);
                }
        foreach (PlayerSkillImpactFrame impact in _director.GetComponents<PlayerSkillImpactFrame>())
            if (!_impacts.Contains(impact)) _impacts.Add(impact);
        foreach (PlayerSkillImpactFrame impact in _impacts) impact.BeginExternalSession(this);
    }

    private static IEnumerable<TrackAsset> Tracks(IEnumerable<TrackAsset> roots)
    {
        foreach (TrackAsset root in roots)
        {
            yield return root;
            foreach (TrackAsset child in Tracks(root.GetChildTracks())) yield return child;
        }
    }

    public void Stop()
    {
        bool wasActive = IsActive; IsActive = false;
        if (wasActive && _core != null) _core.Mover.ConsumePendingDisplacement();
        if (wasActive && _director != null)
        {
            StopPresentation();
            _director.timeUpdateMode = _previousUpdateMode;
        }
        else ReleaseModelAnimation();
        foreach (var binding in _impactBindings)
            if (_director != null && binding.track != null) _director.SetGenericBinding(binding.track, binding.binding);
        _impactBindings.Clear();
        foreach (PlayerSkillImpactFrame impact in _impacts) if (impact != null) impact.EndExternalSession(this);
        _impacts.Clear();
        _receiver?.ReleaseRuntimeOwner(this); _receiver = null;
        if (wasActive && _core != null && _core.Animator != null) _core.Animator.fireEvents = _previousFireEvents;
        foreach (Volume volume in _volumes)
            if (volume != null) { volume.weight = 0f; volume.enabled = false; Destroy(volume.gameObject); }
        _volumes.Clear();
        foreach (VolumeProfile profile in _profiles)
            if (profile != null)
            {
                foreach (VolumeComponent component in profile.components) if (component != null) Destroy(component);
                Destroy(profile);
            }
        _profiles.Clear(); _hits.Clear(); _targets.Clear(); _victims.Clear();
        if (ActiveDefinition != null) Destroy(ActiveDefinition);
        ActiveDefinition = null; _retainedMotion = Vector3.zero; _pendingStep = _pendingReplay = false;
    }

    private static void Destroy(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
    }
}
