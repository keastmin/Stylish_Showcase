using System.Collections.Generic;
using UnityEngine;

public enum EnemyPositioningAction
{
    Hold,
    Advance,
    Retreat,
    StrafeLeft,
    StrafeRight
}

/// <summary>
/// Reserves stable approach sectors around each target. Waiting enemies keep space,
/// while a short preparation lease lets the next attacker approach and clear its lane.
/// </summary>
[DefaultExecutionOrder(-90)]
public class EnemyPositioningController : MonoBehaviour
{
    [Header("Participants")]
    [SerializeField] private List<EnemyCore> _enemies = new();
    [SerializeField] private bool _autoDiscoverEnemies = true;

    [Header("Combat Envelope")]
    [SerializeField, Min(0f)] private float _minimumCombatRadius = 2.1f;
    [SerializeField, Min(0f)] private float _maximumCombatRadius = 4.4f;
    [SerializeField, Min(0f)] private float _radialDeadZone = 0.65f;
    [SerializeField, Range(0f, 2f)] private float _orbitInfluence = 0.72f;
    [SerializeField] private Vector2 _preferenceChangeInterval = new(2.4f, 5.5f);
    [SerializeField] private Vector2 _actionHoldInterval = new(0.35f, 0.9f);

    [Header("Stable Surround Positions")]
    [SerializeField, Range(4, 16)] private int _sectorsPerRing = 10;
    [SerializeField, Min(0.5f)] private float _holdingRingSpacing = 1.5f;
    [SerializeField, Min(0.1f)] private float _slotArrivalDistance = 0.45f;
    [SerializeField, Min(1f)] private float _targetRelocationDistance = 6f;

    [Header("Neighbour Avoidance")]
    [SerializeField, Min(0.1f)] private float _personalSpaceRadius = 1.35f;
    [SerializeField, Range(0f, 4f)] private float _separationInfluence = 2.2f;
    [SerializeField, Min(0f)] private float _motionLookAhead = 1.25f;
    [SerializeField, Range(0f, 1f)] private float _motionSteeringStrength = 0.72f;

    [Header("World Obstacle Avoidance")]
    [SerializeField, Tooltip("Default and Ground contain the arena walls and floor. Character colliders are filtered separately.")]
    private LayerMask _obstacleLayers = (1 << 0) | (1 << 7);
    [SerializeField, Min(0.05f)] private float _obstacleProbeRadius = 0.3f;
    [SerializeField, Min(0.1f)] private float _obstacleProbeHeight = 0.75f;

    [Header("Attack Lane")]
    [SerializeField, Min(0f)] private float _minimumAttackDistance = 1.15f;
    [SerializeField, Min(0f)] private float _maximumAttackDistance = 4.25f;
    [SerializeField, Min(0.1f)] private float _attackLaneHalfWidth = 0.9f;
    [SerializeField, Min(0f)] private float _attackLaneEndPadding = 0.55f;
    [SerializeField, Min(0f)] private float _yieldDuration = 0.9f;
    [SerializeField, Min(0f)] private float _laneRequestMemory = 1.15f;

    [Header("Debug")]
    [SerializeField] private bool _drawDebugGizmos;

    private sealed class AgentState
    {
        public float ComfortRadius;
        public float TargetComfortRadius;
        public float OrbitSign;
        public float NextPreferenceTime;
        public float LastPreferenceSampleTime;
        public float NextActionTime;
        public float YieldUntil;
        public Vector3 YieldDirection;
        public EnemyPositioningAction Action;
        public Vector3 SmoothedAvoidanceVelocity;
        public int SlotIndex = -1;
        public Transform SlotTarget;
        public Vector3 SlotAnchorPosition;
        public float NextSlotProbeTime;
        public int BlockedSlotProbeCount;
    }

    private static readonly List<EnemyPositioningController> ActiveControllers = new();

    private readonly Dictionary<EnemyCore, AgentState> _agentStates = new();
    private readonly List<EnemyCore> _cleanupBuffer = new();
    private readonly RaycastHit[] _obstacleHits = new RaycastHit[32];

    private EnemyCore _committedAttacker;
    private EnemyCore _laneRequester;
    private float _laneRequestUntil;
    private float _laneRequestReach;
    private float _combatClock;

    public EnemyCore CommittedAttacker => _committedAttacker;

    private void OnEnable()
    {
        if (!ActiveControllers.Contains(this))
            ActiveControllers.Add(this);

        RegisterConfiguredEnemies();

        if (_autoDiscoverEnemies)
            RegisterDiscoveredEnemies();
    }

    private void Update()
    {
        if (VirtualCutscenePlayback.IsPlaying || ProjectR.UI.PauseSettingsController.InputBlocked)
            return;

        _combatClock += CombatTimeController.DeltaTime;
        CleanupInvalidEnemies();

        if (_committedAttacker != null && !IsAvailable(_committedAttacker))
            NotifyAttackEnded(_committedAttacker);

        if (_laneRequester != null &&
            (_combatClock >= _laneRequestUntil || !_laneRequester.IsEngagedForCoordination))
        {
            _laneRequester = null;
        }
    }

    private void OnDisable()
    {
        ActiveControllers.Remove(this);

        foreach (EnemyCore enemy in _agentStates.Keys)
        {
            if (enemy != null)
                enemy.ClearPositioningController(this);
        }

        _agentStates.Clear();
        _committedAttacker = null;
        _laneRequester = null;
    }

    public static EnemyPositioningController FindFor(EnemyCore enemy)
    {
        if (enemy == null)
            return null;

        if (enemy.PositioningController != null && enemy.PositioningController.isActiveAndEnabled &&
            enemy.PositioningController.gameObject.scene == enemy.gameObject.scene)
            return enemy.PositioningController;

        EnemyPositioningController closest = null;
        float closestSqrDistance = float.PositiveInfinity;

        for (int i = ActiveControllers.Count - 1; i >= 0; i--)
        {
            EnemyPositioningController controller = ActiveControllers[i];
            if (controller == null || !controller.isActiveAndEnabled)
            {
                ActiveControllers.RemoveAt(i);
                continue;
            }

            if (controller.gameObject.scene != enemy.gameObject.scene)
                continue;

            if (controller._enemies.Contains(enemy))
                return controller;

            float sqrDistance = (controller.transform.position - enemy.transform.position).sqrMagnitude;
            if (sqrDistance >= closestSqrDistance)
                continue;

            closest = controller;
            closestSqrDistance = sqrDistance;
        }

        return closest;
    }

    public void Register(EnemyCore enemy)
    {
        if (enemy == null)
            return;

        if (!_enemies.Contains(enemy))
            _enemies.Add(enemy);

        if (!_agentStates.ContainsKey(enemy))
            _agentStates.Add(enemy, CreateAgentState(enemy));

        enemy.SetPositioningController(this);
    }

    public void Unregister(EnemyCore enemy)
    {
        // Unity's destroyed-object equality reports null while the managed key
        // still exists in our collections. Always remove that exact registration.
        if (ReferenceEquals(enemy, null))
            return;

        if (ReferenceEquals(_committedAttacker, enemy))
            _committedAttacker = null;

        if (ReferenceEquals(_laneRequester, enemy))
            _laneRequester = null;

        _agentStates.Remove(enemy);
        _enemies.RemoveAll(candidate => ReferenceEquals(candidate, enemy));
        if (enemy != null)
            enemy.ClearPositioningController(this);
    }

    public EnemyPositioningAction GetRecommendedAction(EnemyCore enemy)
    {
        if (!TryGetActiveState(enemy, out AgentState state) || enemy.TargetTransform == null ||
            !enemy.IsEngagedForCoordination)
            return EnemyPositioningAction.Hold;

        EnsureSlot(enemy, state);

        if (_combatClock < state.NextActionTime)
            return state.Action;

        RefreshPreferences(enemy, state);

        Vector3 steering = CalculateSteering(enemy, state);
        state.Action = ResolveAction(enemy, steering, state.Action);
        state.NextActionTime = _combatClock + Mathf.Min(0.35f, RandomInRange(_actionHoldInterval));
        return state.Action;
    }

    public bool CanBeginAttack(EnemyCore enemy, float attackMaximumReach = 0f)
    {
        if (!TryGetActiveState(enemy, out _) || enemy.TargetTransform == null ||
            !enemy.IsEngagedForCoordination)
            return false;

        if (_committedAttacker != null)
            return _committedAttacker == enemy;

        Vector3 toTarget = Flatten(enemy.TargetTransform.position - enemy.transform.position);
        float distance = toTarget.magnitude;
        float maximumAttackDistance = GetAttackReach(attackMaximumReach);

        if (distance < _minimumAttackDistance || distance > maximumAttackDistance)
        {
            TryClaimLaneRequest(enemy, maximumAttackDistance);
            return false;
        }

        EnemyCore blocker = FindFirstLaneBlocker(enemy, enemy.TargetTransform.position);
        if (blocker == null && !TryFindWorldObstacle(enemy, toTarget, distance, out _))
            return true;

        if (TryClaimLaneRequest(enemy, maximumAttackDistance) && blocker != null)
            AskEnemyToYield(blocker, enemy, enemy.TargetTransform.position);

        return false;
    }

    /// <summary>Read-only readiness used to rank candidates; never reserves a lane or changes movement.</summary>
    public bool IsAttackPositionReady(EnemyCore enemy, float attackMaximumReach = 0f)
    {
        if (!IsAvailable(enemy) || enemy.TargetTransform == null || !enemy.IsEngagedForCoordination)
            return false;
        if (_committedAttacker != null && _committedAttacker != enemy)
            return false;

        Vector3 toTarget = Flatten(enemy.TargetTransform.position - enemy.transform.position);
        float distance = toTarget.magnitude;
        return distance >= _minimumAttackDistance && distance <= GetAttackReach(attackMaximumReach) &&
               FindFirstLaneBlocker(enemy, enemy.TargetTransform.position) == null &&
               !TryFindWorldObstacle(enemy, toTarget, distance, out _);
    }

    public bool TryGetAssignedPosition(EnemyCore enemy, out Vector3 position)
    {
        position = default;
        if (!IsAvailable(enemy) || enemy.TargetTransform == null ||
            !_agentStates.TryGetValue(enemy, out AgentState state))
            return false;
        EnsureSlot(enemy, state);
        position = GetSlotPosition(enemy, state);
        return true;
    }

    private float GetAttackReach(float requestedReach)
    {
        return requestedReach > 0f ? Mathf.Min(_maximumAttackDistance, requestedReach) : _maximumAttackDistance;
    }

    public void NotifyAttackStarted(EnemyCore enemy)
    {
        if (!TryGetActiveState(enemy, out _) ||
            (_committedAttacker != null && _committedAttacker != enemy))
            return;

        _committedAttacker = enemy;
        _laneRequester = null;
        AskLaneBlockersToYield(enemy, enemy.TargetTransform != null
            ? enemy.TargetTransform.position
            : enemy.transform.position + enemy.Rotator.FacingDirection * _maximumAttackDistance);
    }

    public void NotifyAttackEnded(EnemyCore enemy)
    {
        if (_committedAttacker == enemy)
            _committedAttacker = null;
        if (_laneRequester == enemy)
            _laneRequester = null;
    }

    public Vector3 AdjustMovement(EnemyCore enemy, Vector3 desiredVelocity)
    {
        if (!TryGetActiveState(enemy, out AgentState state))
            return desiredVelocity;

        Vector3 flatVelocity = Flatten(desiredVelocity);
        float speed = flatVelocity.magnitude;
        if (speed <= 0.01f)
        {
            state.SmoothedAvoidanceVelocity = Vector3.zero;
            return desiredVelocity;
        }

        Vector3 moveDirection = flatVelocity / speed;
        Vector3 avoidance = Vector3.zero;
        float brake = 0f;

        foreach (KeyValuePair<EnemyCore, AgentState> pair in _agentStates)
        {
            EnemyCore other = pair.Key;
            if (other == enemy || !IsAvailable(other))
                continue;

            Vector3 toOther = Flatten(other.transform.position - enemy.transform.position);
            float forwardDistance = Vector3.Dot(toOther, moveDirection);
            if (forwardDistance <= 0f || forwardDistance > _motionLookAhead + _personalSpaceRadius)
                continue;

            Vector3 lateralOffset = toOther - moveDirection * forwardDistance;
            float lateralDistance = lateralOffset.magnitude;
            if (lateralDistance >= _personalSpaceRadius)
                continue;

            float urgency = (1f - lateralDistance / _personalSpaceRadius) *
                            (1f - Mathf.Clamp01(forwardDistance / (_motionLookAhead + _personalSpaceRadius)));

            bool enemyHasPriority = enemy == _committedAttacker;
            bool otherHasPriority = other == _committedAttacker;
            float priorityMultiplier = enemyHasPriority ? 0.35f : otherHasPriority ? 1.35f : 1f;

            Vector3 side = lateralDistance > 0.05f
                ? -lateralOffset.normalized
                : GetPassingSide(moveDirection);

            avoidance += side * urgency * speed * priorityMultiplier;
            brake = Mathf.Max(brake, urgency * (enemyHasPriority ? 0.2f : 0.65f));
        }

        Vector3 targetAvoidance = Vector3.ClampMagnitude(avoidance, speed);
        state.SmoothedAvoidanceVelocity = Vector3.MoveTowards(
            state.SmoothedAvoidanceVelocity,
            targetAvoidance,
            speed * 8f * Time.fixedDeltaTime);

        Vector3 steeredVelocity = flatVelocity * (1f - brake) + state.SmoothedAvoidanceVelocity;
        steeredVelocity = Vector3.ClampMagnitude(steeredVelocity, speed);

        Vector3 result = Vector3.Lerp(flatVelocity, steeredVelocity, _motionSteeringStrength);
        result = AvoidWorldObstacles(enemy, result, speed);
        result.y = desiredVelocity.y;
        return result;
    }

    private Vector3 CalculateSteering(EnemyCore enemy, AgentState state)
    {
        Vector3 enemyPosition = enemy.transform.position;
        Vector3 targetPosition = enemy.TargetTransform.position;
        Vector3 toTarget = Flatten(targetPosition - enemyPosition);
        float distance = toTarget.magnitude;
        Vector3 forward = distance > 0.001f ? toTarget / distance : -GetSlotDirection(state.SlotIndex);
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float desiredRadius = GetSlotRadius(state);
        if (_laneRequester == enemy)
            desiredRadius = Mathf.Clamp(_laneRequestReach * 0.82f,
                _minimumAttackDistance + 0.15f, Mathf.Max(_minimumAttackDistance + 0.15f, _laneRequestReach - 0.1f));
        float radialError = distance - desiredRadius;
        Vector3 steering = Vector3.zero;

        float radialTolerance = _laneRequester == enemy ? 0.15f : Mathf.Min(_radialDeadZone, _slotArrivalDistance);
        if (Mathf.Abs(radialError) > radialTolerance)
            steering += forward * Mathf.Sign(radialError) * Mathf.Min(Mathf.Abs(radialError), 2f);

        // A stable sector replaces perpetual orbiting. Approach tangentially instead
        // of taking a straight chord through the player or another sector.
        if (_laneRequester != enemy)
        {
            Vector3 slotDirection = GetSlotDirection(state.SlotIndex);
            float angle = Vector3.SignedAngle(-forward, slotDirection, Vector3.up) * Mathf.Deg2Rad;
            float arcDistance = angle * Mathf.Max(distance, desiredRadius);
            if (Mathf.Abs(arcDistance) > _slotArrivalDistance)
                steering -= right * Mathf.Clamp(arcDistance, -2f, 2f) * Mathf.Max(0.35f, _orbitInfluence);
        }

        foreach (EnemyCore other in _agentStates.Keys)
        {
            if (other == enemy || !IsAvailable(other))
                continue;

            Vector3 away = Flatten(enemyPosition - other.transform.position);
            float neighbourDistance = away.magnitude;
            if (neighbourDistance >= _personalSpaceRadius)
                continue;

            float closeness = 1f - neighbourDistance / _personalSpaceRadius;
            Vector3 separation = neighbourDistance > 0.001f
                ? away / neighbourDistance
                : CoincidentSeparation(enemy, other);
            steering += separation * closeness * _separationInfluence;
        }

        EnemyCore priorityEnemy = _committedAttacker != null ? _committedAttacker : _laneRequester;
        if (priorityEnemy != null && priorityEnemy != enemy && priorityEnemy.TargetTransform == enemy.TargetTransform &&
            IsBlockingLane(enemy, priorityEnemy, targetPosition))
        {
            AskEnemyToYield(enemy, priorityEnemy, targetPosition);
        }

        if (_laneRequester == enemy)
        {
            if (distance > _laneRequestReach - 0.15f)
                steering += forward * 2.35f;
            else if (distance < _minimumAttackDistance + 0.1f)
                steering -= forward * 1.8f;

            EnemyCore blocker = FindFirstLaneBlocker(enemy, targetPosition);
            if (blocker != null)
            {
                Vector3 toBlocker = Flatten(blocker.transform.position - enemyPosition);
                float sideSign = Mathf.Sign(Vector3.Dot(toBlocker, right));
                if (Mathf.Approximately(sideSign, 0f))
                    sideSign = state.OrbitSign;

                steering += right * -sideSign * 1.45f;
            }
        }

        if (_combatClock < state.YieldUntil)
        {
            Vector3 awayFromTarget = -forward;
            steering += state.YieldDirection * 2.6f + awayFromTarget * 0.55f;
        }

        return steering;
    }

    private bool TryClaimLaneRequest(EnemyCore enemy, float reach)
    {
        if (_laneRequester != null && _laneRequester != enemy && _combatClock < _laneRequestUntil)
            return false;

        // Repeated failed checks must not perpetually extend a preparation lease.
        if (_laneRequester == enemy && _combatClock < _laneRequestUntil)
            return true;

        _laneRequester = enemy;
        _laneRequestUntil = _combatClock + _laneRequestMemory;
        _laneRequestReach = reach;
        if (_agentStates.TryGetValue(enemy, out AgentState state))
            state.NextActionTime = 0f;
        return true;
    }

    private EnemyPositioningAction ResolveAction(
        EnemyCore enemy,
        Vector3 steering,
        EnemyPositioningAction previousAction)
    {
        if (steering.sqrMagnitude < 0.16f)
            return EnemyPositioningAction.Hold;

        Vector3 toTarget = Flatten(enemy.TargetTransform.position - enemy.transform.position).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, toTarget);
        float forwardAmount = Vector3.Dot(steering, toTarget);
        float sideAmount = Vector3.Dot(steering, right);

        const float actionHysteresis = 1.18f;
        bool wasRadial = previousAction == EnemyPositioningAction.Advance ||
                         previousAction == EnemyPositioningAction.Retreat;

        if (Mathf.Abs(forwardAmount) > Mathf.Abs(sideAmount) * (wasRadial ? 1f / actionHysteresis : actionHysteresis))
            return forwardAmount >= 0f ? EnemyPositioningAction.Advance : EnemyPositioningAction.Retreat;

        return sideAmount >= 0f ? EnemyPositioningAction.StrafeRight : EnemyPositioningAction.StrafeLeft;
    }

    private EnemyCore FindFirstLaneBlocker(EnemyCore attacker, Vector3 targetPosition)
    {
        EnemyCore nearestBlocker = null;
        float nearestProgress = float.PositiveInfinity;

        foreach (EnemyCore other in _agentStates.Keys)
        {
            if (other == attacker || !IsAvailable(other))
                continue;

            if (!TryGetLaneProgress(other.transform.position, attacker.transform.position, targetPosition,
                    out float progress, out float lateralDistance))
            {
                continue;
            }

            if (lateralDistance > _attackLaneHalfWidth || progress >= nearestProgress)
                continue;

            nearestBlocker = other;
            nearestProgress = progress;
        }

        return nearestBlocker;
    }

    private bool IsBlockingLane(EnemyCore possibleBlocker, EnemyCore attacker, Vector3 targetPosition)
    {
        return TryGetLaneProgress(
                   possibleBlocker.transform.position,
                   attacker.transform.position,
                   targetPosition,
                   out _,
                   out float lateralDistance) &&
               lateralDistance <= _attackLaneHalfWidth;
    }

    private bool TryGetLaneProgress(
        Vector3 point,
        Vector3 laneStart,
        Vector3 laneEnd,
        out float progress,
        out float lateralDistance)
    {
        Vector3 lane = Flatten(laneEnd - laneStart);
        float laneLength = lane.magnitude;
        if (laneLength <= 0.001f)
        {
            progress = 0f;
            lateralDistance = float.PositiveInfinity;
            return false;
        }

        Vector3 laneDirection = lane / laneLength;
        Vector3 offset = Flatten(point - laneStart);
        progress = Vector3.Dot(offset, laneDirection);
        lateralDistance = (offset - laneDirection * progress).magnitude;
        return progress > 0.1f && progress < laneLength - _attackLaneEndPadding;
    }

    private void AskLaneBlockersToYield(EnemyCore attacker, Vector3 targetPosition)
    {
        foreach (EnemyCore other in _agentStates.Keys)
        {
            if (other != attacker && IsAvailable(other) && IsBlockingLane(other, attacker, targetPosition))
                AskEnemyToYield(other, attacker, targetPosition);
        }
    }

    private void AskEnemyToYield(EnemyCore blocker, EnemyCore attacker, Vector3 targetPosition)
    {
        if (!_agentStates.TryGetValue(blocker, out AgentState state) || !blocker.IsEngagedForCoordination)
            return;

        Vector3 laneDirection = Flatten(targetPosition - attacker.transform.position).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, laneDirection);
        float side = Vector3.Dot(Flatten(blocker.transform.position - attacker.transform.position), right);

        if (Mathf.Abs(side) <= 0.08f)
            side = state.OrbitSign;

        // Keep the chosen side for the lifetime of one yield to avoid left/right jitter.
        if (_combatClock >= state.YieldUntil)
            state.YieldDirection = right * Mathf.Sign(side);
        state.YieldUntil = Mathf.Max(state.YieldUntil, _combatClock + _yieldDuration);
        state.NextActionTime = 0f;
    }

    private void RefreshPreferences(EnemyCore enemy, AgentState state)
    {
        float sampleDelta = Mathf.Max(0f, _combatClock - state.LastPreferenceSampleTime);
        state.ComfortRadius = Mathf.MoveTowards(
            state.ComfortRadius,
            state.TargetComfortRadius,
            sampleDelta * 0.45f);
        state.LastPreferenceSampleTime = _combatClock;

        if (_combatClock < state.NextPreferenceTime)
            return;

        float normalizedIdNoise = Mathf.Repeat(IdFraction(enemy) + _combatClock * 0.071f, 1f);
        float baseRadius = Mathf.Lerp(_minimumCombatRadius, _maximumCombatRadius, 0.7f);
        state.TargetComfortRadius = Mathf.Clamp(baseRadius + (normalizedIdNoise - 0.5f) * 0.3f,
            _minimumCombatRadius, _maximumCombatRadius);

        state.NextPreferenceTime = _combatClock + RandomInRange(_preferenceChangeInterval);
    }

    private AgentState CreateAgentState(EnemyCore enemy)
    {
        int stableId = StableId(enemy);
        float comfortRadius = Mathf.Clamp(Mathf.Lerp(_minimumCombatRadius, _maximumCombatRadius, 0.7f) +
            (IdFraction(enemy) - 0.5f) * 0.3f, _minimumCombatRadius, _maximumCombatRadius);
        return new AgentState
        {
            ComfortRadius = comfortRadius,
            TargetComfortRadius = comfortRadius,
            OrbitSign = (stableId & 1) == 0 ? 1f : -1f,
            NextPreferenceTime = _combatClock + RandomInRange(_preferenceChangeInterval),
            LastPreferenceSampleTime = _combatClock,
            NextActionTime = 0f,
            Action = EnemyPositioningAction.Hold
        };
    }

    private void EnsureSlot(EnemyCore enemy, AgentState state)
    {
        Transform target = enemy.TargetTransform;
        bool hasStableSlot = state.SlotIndex >= 0 && state.SlotTarget == target &&
            Flatten(target.position - state.SlotAnchorPosition).sqrMagnitude <
            _targetRelocationDistance * _targetRelocationDistance;
        if (hasStableSlot)
        {
            if (_combatClock < state.NextSlotProbeTime)
                return;
            state.NextSlotProbeTime = _combatClock + 0.75f;
            Vector3 toSlot = Flatten(GetSlotPosition(enemy, state) - enemy.transform.position);
            if (!TryFindWorldObstacle(enemy, toSlot, toSlot.magnitude, out _))
            {
                state.BlockedSlotProbeCount = 0;
                return;
            }
            // Transient obstruction or a single target step must not reshuffle a
            // formation. Two spaced failures allow recovery after smaller moves.
            if (++state.BlockedSlotProbeCount < 2)
                return;
        }

        int previousSlot = state.SlotIndex;
        state.SlotTarget = target;
        state.SlotAnchorPosition = target.position;
        Vector3 radial = Flatten(enemy.transform.position - target.position).normalized;
        if (radial.sqrMagnitude < 0.001f)
            radial = Quaternion.Euler(0f, IdFraction(enemy) * 360f, 0f) * Vector3.forward;

        float bestScore = float.PositiveInfinity;
        int bestSlot = previousSlot;
        bool foundClearSlot = false;
        // Enough sectors for every participant, with a free overflow ring.
        int candidateCount = ((_agentStates.Count / _sectorsPerRing) + 1) * _sectorsPerRing;
        for (int slot = 0; slot < candidateCount; slot++)
        {
            bool occupied = false;
            foreach (KeyValuePair<EnemyCore, AgentState> other in _agentStates)
            {
                if (other.Key != enemy && IsAvailable(other.Key) && other.Value.SlotTarget == target &&
                    other.Value.SlotIndex == slot)
                {
                    occupied = true;
                    break;
                }
            }
            if (occupied)
                continue;

            // Prefer an inner ring, then the nearest free angle; no periodic reshuffle.
            float score = (slot / _sectorsPerRing) * 4f + 1f - Vector3.Dot(radial, GetSlotDirection(slot));
            Vector3 candidate = target.position + GetSlotDirection(slot) *
                GetSlotRadius(state, slot);
            Vector3 toCandidate = Flatten(candidate - enemy.transform.position);
            bool blocked = TryFindWorldObstacle(enemy, toCandidate, toCandidate.magnitude, out _);
            if (blocked && (hasStableSlot || foundClearSlot))
                continue;
            if (!blocked && !foundClearSlot)
                bestScore = float.PositiveInfinity;
            if (score >= bestScore)
                continue;
            foundClearSlot |= !blocked;
            bestScore = score;
            bestSlot = slot;
        }
        // If every alternative is obstructed, retain the previous reservation and
        // let local sliding continue instead of bouncing between unreachable slots.
        state.SlotIndex = bestSlot;
        state.BlockedSlotProbeCount = 0;
        state.NextSlotProbeTime = _combatClock + 1.5f + IdFraction(enemy) * 0.25f;
        state.NextActionTime = 0f;
    }

    private Vector3 GetSlotDirection(int slot)
    {
        slot = Mathf.Max(0, slot);
        float offset = ((slot / _sectorsPerRing) & 1) * 0.5f;
        float degrees = (slot % _sectorsPerRing + offset) * (360f / _sectorsPerRing);
        return Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
    }

    private float GetSlotRadius(AgentState state)
    {
        return GetSlotRadius(state, state.SlotIndex);
    }

    private float GetSlotRadius(AgentState state, int slot)
    {
        // Adjacent reservations must retain enough chord distance for personal space.
        float minimumSpacingRadius = _personalSpaceRadius /
            (2f * Mathf.Sin(Mathf.PI / _sectorsPerRing));
        return Mathf.Max(state.ComfortRadius, minimumSpacingRadius) +
               (Mathf.Max(0, slot) / _sectorsPerRing) * Mathf.Max(_holdingRingSpacing, _personalSpaceRadius);
    }

    private Vector3 GetSlotPosition(EnemyCore enemy, AgentState state)
    {
        return enemy.TargetTransform.position + GetSlotDirection(state.SlotIndex) * GetSlotRadius(state);
    }

    private Vector3 AvoidWorldObstacles(EnemyCore enemy, Vector3 velocity, float speed)
    {
        float probeDistance = Mathf.Max(_obstacleProbeRadius, _motionLookAhead);
        if (!TryFindWorldObstacle(enemy, velocity, probeDistance, out RaycastHit hit))
            return velocity;

        Vector3 normal = Flatten(hit.normal).normalized;
        if (normal.sqrMagnitude < 0.01f)
            return Vector3.zero;
        Vector3 slide = Vector3.ProjectOnPlane(velocity, normal);
        if (slide.sqrMagnitude < speed * speed * 0.1f)
        {
            Vector3 tangent = Vector3.Cross(Vector3.up, normal);
            AgentState state = _agentStates[enemy];
            float sign = state.OrbitSign;
            if (enemy.TargetTransform != null)
            {
                EnsureSlot(enemy, state);
                float toSlot = Vector3.Dot(GetSlotPosition(enemy, state) - enemy.transform.position, tangent);
                if (Mathf.Abs(toSlot) > 0.1f)
                    sign = Mathf.Sign(toSlot);
            }
            slide = tangent * sign * speed * 0.65f;
        }
        if (TryFindWorldObstacle(enemy, slide, probeDistance * 0.65f, out _))
            return Vector3.zero;
        return Vector3.ClampMagnitude(slide, speed);
    }

    private bool TryFindWorldObstacle(EnemyCore enemy, Vector3 direction, float distance, out RaycastHit nearest)
    {
        nearest = default;
        if (distance <= 0.001f || direction.sqrMagnitude < 0.001f || _obstacleLayers.value == 0)
            return false;

        int count = Physics.SphereCastNonAlloc(enemy.transform.position + Vector3.up * _obstacleProbeHeight,
            _obstacleProbeRadius, direction.normalized, _obstacleHits, distance,
            _obstacleLayers, QueryTriggerInteraction.Ignore);
        float nearestDistance = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _obstacleHits[i];
            Collider collider = hit.collider;
            if (collider == null || hit.normal.y > 0.65f ||
                collider.GetComponentInParent<EnemyCore>() != null ||
                collider.GetComponentInParent<PlayerCore>() != null)
                continue;
            Transform target = enemy.TargetTransform;
            if (target != null && (collider.transform.IsChildOf(target) ||
                target.IsChildOf(collider.transform)))
                continue;
            if (hit.distance >= nearestDistance)
                continue;
            nearestDistance = hit.distance;
            nearest = hit;
            found = true;
        }
        // Saturated buffers can hide a wall behind ignored character colliders.
        return found || count == _obstacleHits.Length;
    }

    private static Vector3 CoincidentSeparation(EnemyCore enemy, EnemyCore other)
    {
        int a = StableId(enemy);
        int b = StableId(other);
        int seed = (Mathf.Min(a, b) ^ Mathf.Max(a, b)) & 1023;
        Vector3 axis = Quaternion.Euler(0f, seed * (360f / 1024f), 0f) * Vector3.forward;
        return a < b ? axis : -axis;
    }

    private static float IdFraction(EnemyCore enemy)
    {
        return Mathf.Abs(StableId(enemy) % 997) / 997f;
    }

    private bool TryGetActiveState(EnemyCore enemy, out AgentState state)
    {
        if (enemy == null)
        {
            state = null;
            return false;
        }

        if (!_agentStates.TryGetValue(enemy, out state))
        {
            Register(enemy);
            _agentStates.TryGetValue(enemy, out state);
        }

        return state != null && IsAvailable(enemy);
    }

    private void RegisterConfiguredEnemies()
    {
        if (_enemies == null)
            _enemies = new List<EnemyCore>();

        for (int i = _enemies.Count - 1; i >= 0; i--)
        {
            EnemyCore enemy = _enemies[i];
            if (enemy == null)
            {
                _enemies.RemoveAt(i);
                continue;
            }

            // Re-enabling an old controller must not reclaim an enemy that was
            // deliberately moved to another live controller while this was off.
            EnemyPositioningController owner = enemy.PositioningController;
            if (owner != null && owner != this && owner.isActiveAndEnabled)
            {
                _enemies.RemoveAt(i);
                continue;
            }

            Register(enemy);
        }
    }

    private void RegisterDiscoveredEnemies()
    {
        EnemyCore[] sceneEnemies = FindObjectsByType<EnemyCore>();
        foreach (EnemyCore enemy in sceneEnemies)
        {
            if (FindFor(enemy) == this)
                Register(enemy);
        }
    }

    private void CleanupInvalidEnemies()
    {
        _cleanupBuffer.Clear();
        foreach (EnemyCore enemy in _agentStates.Keys)
        {
            if (enemy == null || !enemy.isActiveAndEnabled || enemy.CurrentHP <= 0f)
                _cleanupBuffer.Add(enemy);
        }

        foreach (EnemyCore enemy in _cleanupBuffer)
            Unregister(enemy);
        _enemies.RemoveAll(enemy => enemy == null);
    }

    private static bool IsAvailable(EnemyCore enemy)
    {
        return enemy != null && enemy.isActiveAndEnabled && enemy.CurrentHP > 0f;
    }

    private static Vector3 GetPassingSide(Vector3 forward)
    {
        return Vector3.Cross(Vector3.up, forward);
    }

    private static int StableId(EnemyCore enemy)
    {
        return enemy.GetEntityId().GetHashCode();
    }

    private static Vector3 Flatten(Vector3 value)
    {
        value.y = 0f;
        return value;
    }

    private static float RandomInRange(Vector2 range)
    {
        return Random.Range(range.x, range.y);
    }

    private void OnValidate()
    {
        _minimumCombatRadius = Mathf.Max(0f, _minimumCombatRadius);
        _maximumCombatRadius = Mathf.Max(_minimumCombatRadius, _maximumCombatRadius);
        _radialDeadZone = Mathf.Max(0f, _radialDeadZone);
        _personalSpaceRadius = Mathf.Max(0.1f, _personalSpaceRadius);
        _motionLookAhead = Mathf.Max(0f, _motionLookAhead);
        _minimumAttackDistance = Mathf.Max(0f, _minimumAttackDistance);
        _maximumAttackDistance = Mathf.Max(_minimumAttackDistance, _maximumAttackDistance);
        _attackLaneHalfWidth = Mathf.Max(0.1f, _attackLaneHalfWidth);
        _sectorsPerRing = Mathf.Clamp(_sectorsPerRing, 4, 16);
        _holdingRingSpacing = Mathf.Max(0.5f, _holdingRingSpacing);
        _slotArrivalDistance = Mathf.Max(0.1f, _slotArrivalDistance);
        _targetRelocationDistance = Mathf.Max(1f, _targetRelocationDistance);
        _obstacleProbeRadius = Mathf.Max(0.05f, _obstacleProbeRadius);
        _obstacleProbeHeight = Mathf.Max(_obstacleProbeRadius + 0.05f, _obstacleProbeHeight);
        _yieldDuration = Mathf.Max(0f, _yieldDuration);
        _laneRequestMemory = Mathf.Max(0.1f, _laneRequestMemory);
        ClampRange(ref _preferenceChangeInterval);
        ClampRange(ref _actionHoldInterval);
    }

    private static void ClampRange(ref Vector2 range)
    {
        range.x = Mathf.Max(0.05f, range.x);
        range.y = Mathf.Max(range.x, range.y);
    }

    private void OnDrawGizmosSelected()
    {
        if (!_drawDebugGizmos)
            return;

        foreach (EnemyCore enemy in _enemies)
        {
            if (enemy == null || enemy.TargetTransform == null)
                continue;

            Gizmos.color = enemy == _committedAttacker ? Color.red : Color.cyan;
            Gizmos.DrawLine(enemy.transform.position + Vector3.up * 0.1f,
                enemy.TargetTransform.position + Vector3.up * 0.1f);

            if (_agentStates.TryGetValue(enemy, out AgentState assigned) && assigned.SlotIndex >= 0)
            {
                Vector3 destination = GetSlotPosition(enemy, assigned);
                Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.8f);
                Gizmos.DrawWireSphere(destination + Vector3.up * 0.1f, _slotArrivalDistance);
                Gizmos.DrawLine(enemy.transform.position + Vector3.up * 0.1f,
                    destination + Vector3.up * 0.1f);
            }

            if (_agentStates.TryGetValue(enemy, out AgentState state) && _combatClock < state.YieldUntil)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(enemy.transform.position + Vector3.up * 0.2f, state.YieldDirection * 1.5f);
            }
        }
    }
}
