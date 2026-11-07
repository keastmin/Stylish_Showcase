using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
public sealed class HitstopCoordinator : MonoBehaviour
{
    public const int CombatFrameRate = 60;

    private static HitstopCoordinator _instance;

    private readonly Dictionary<IHitStopParticipant, double> _releaseTimes = new();
    private readonly List<IHitStopParticipant> _releaseBuffer = new();
    private readonly Dictionary<IHitStopParticipant, double> _pendingDurations = new();
    private readonly List<KeyValuePair<IHitStopParticipant, double>> _startBuffer = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    public static void Request(
        IHitStopParticipant attacker,
        IReadOnlyList<IHitStopParticipant> victims,
        int attackerFrameCount)
    {
        if (attackerFrameCount <= 0 || attacker == null || victims == null || victims.Count == 0)
            return;

        HitstopCoordinator coordinator = EnsureInstance();
        double frameDuration = 1d / CombatFrameRate;

        coordinator.QueueHold(attacker, attackerFrameCount * frameDuration);

        double victimDuration = ((double)attackerFrameCount + 1d) * frameDuration;
        for (int i = 0; i < victims.Count; i++)
        {
            IHitStopParticipant victim = victims[i];
            if (victim != null)
                coordinator.QueueHold(victim, victimDuration);
        }
    }

    // 공격자와 VFX는 건드리지 않고, 실제 피해를 받은 대상만 기준값 + 1프레임 정지합니다.
    public static void RequestVictimsOnly(
        IReadOnlyList<IHitStopParticipant> victims,
        int baseFrameCount)
    {
        if (baseFrameCount <= 0 || victims == null || victims.Count == 0)
            return;

        HitstopCoordinator coordinator = EnsureInstance();
        double duration = ((double)baseFrameCount + 1d) / CombatFrameRate;

        for (int i = 0; i < victims.Count; i++)
            coordinator.QueueHold(victims[i], duration);
    }

    private static HitstopCoordinator EnsureInstance()
    {
        if (_instance != null)
            return _instance;

        _instance = FindAnyObjectByType<HitstopCoordinator>();
        if (_instance != null)
            return _instance;

        GameObject coordinatorObject = new GameObject(nameof(HitstopCoordinator));
        DontDestroyOnLoad(coordinatorObject);
        _instance = coordinatorObject.AddComponent<HitstopCoordinator>();
        return _instance;
    }

    // A priority action interrupts only this participant's current/queued hold.
    // Victims retain their remaining hitstop and future hits can start a new hold.
    public static void Cancel(IHitStopParticipant participant)
    {
        if (ReferenceEquals(participant, null)) return;
        if (_instance != null)
        {
            _instance._releaseTimes.Remove(participant);
            _instance._pendingDurations.Remove(participant);
        }
        if (!IsDestroyed(participant)) participant.EndHitStop();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (_releaseTimes.Count == 0)
            return;

        double now = VirtualCutscenePlayback.CombatUnscaledTime;
        _releaseBuffer.Clear();

        foreach (KeyValuePair<IHitStopParticipant, double> pair in _releaseTimes)
        {
            if (pair.Value <= now || IsDestroyed(pair.Key))
                _releaseBuffer.Add(pair.Key);
        }

        for (int i = 0; i < _releaseBuffer.Count; i++)
        {
            IHitStopParticipant participant = _releaseBuffer[i];
            _releaseTimes.Remove(participant);

            if (!IsDestroyed(participant))
                participant.EndHitStop();
        }
    }

    private void OnDestroy()
    {
        if (_instance != this)
            return;

        foreach (IHitStopParticipant participant in _releaseTimes.Keys)
        {
            if (!IsDestroyed(participant))
                participant.EndHitStop();
        }

        _releaseTimes.Clear();
        _releaseBuffer.Clear();
        _pendingDurations.Clear();
        _startBuffer.Clear();
        _instance = null;
    }

    private void QueueHold(IHitStopParticipant participant, double duration)
    {
        if (IsDestroyed(participant))
            return;
        if (!_pendingDurations.TryGetValue(participant, out double pending) || duration > pending)
            _pendingDurations[participant] = duration;
    }

    private void LateUpdate()
    {
        // Timeline notifications run inside animation evaluation. Freezing an
        // Animator there discards motion from the very sample that caused the hit.
        // Finish that sample (including OnAnimatorMove) before freezing everyone.
        double now = VirtualCutscenePlayback.CombatUnscaledTime;
        _startBuffer.Clear();
        _startBuffer.AddRange(_pendingDurations);
        _pendingDurations.Clear();
        foreach (var pair in _startBuffer)
            Hold(pair.Key, now + pair.Value);
        _startBuffer.Clear();
    }

    private void Hold(IHitStopParticipant participant, double releaseTime)
    {
        if (IsDestroyed(participant))
            return;

        if (_releaseTimes.TryGetValue(participant, out double currentReleaseTime))
        {
            if (releaseTime > currentReleaseTime)
                _releaseTimes[participant] = releaseTime;

            return;
        }

        _releaseTimes.Add(participant, releaseTime);
        participant.BeginHitStop();
    }

    private static bool IsDestroyed(IHitStopParticipant participant)
    {
        return participant == null || participant is Object unityObject && unityObject == null;
    }
}
