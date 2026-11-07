using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One committed attack per encounter, with a fair, bounded preparation turn before it.
/// Cooldowns and turn age use the same combat clock as the enemy state machines.
/// </summary>
[DefaultExecutionOrder(-100)]
public class EnemyAttackTimingController : MonoBehaviour
{
    [Header("Participants")]
    [SerializeField] private List<EnemyCore> _enemies = new();
    [SerializeField] private bool _autoDiscoverEnemies = true;

    [Header("Attack Rhythm")]
    [SerializeField] private Vector2 _initialAttackDelayRange = new(1.5f, 3.5f);
    [SerializeField] private Vector2 _repeatAttackDelayRange = new(3.5f, 6.5f);
    [SerializeField] private Vector2 _betweenAttackGapRange = new(0.35f, 1.1f);
    [SerializeField] private Vector2 _urgentAttackDelayRange = new(0f, 0.25f);

    [Header("Group Turn Selection")]
    [SerializeField, Min(0.05f)] private float _intentCollectionDelay = 0.08f;
    [SerializeField, Min(0.2f)] private float _preparationLeaseDuration = 1.2f;
    [SerializeField, Min(0.1f)] private float _failedPreparationBackoff = 0.6f;
    [SerializeField, Min(0.5f)] private float _intentMemory = 1.25f;
    [SerializeField, Min(0f)] private float _readyPositionBonus = 1.5f;
    [SerializeField, Min(0f)] private float _visibleAttackerBonus = 0.65f;
    [SerializeField, Min(0f)] private float _offscreenPreparationDelay = 0.8f;
    [SerializeField, Tooltip("Optional gameplay camera. Falls back to Camera.main.")]
    private Camera _combatCamera;

    private sealed class AttackIntent
    {
        public Transform Target;
        public float WaitingSince;
        public float LastRequestedAt;
        public float RetryAfter;
        public float MaximumReach;
    }

    private static readonly List<EnemyAttackTimingController> ActiveControllers = new();
    private readonly HashSet<EnemyCore> _configuredEnemies = new();
    private readonly Dictionary<EnemyCore, float> _nextAttackTimes = new();
    private readonly HashSet<EnemyCore> _repeatCooldownLockedEnemies = new();
    private readonly Dictionary<EnemyCore, AttackIntent> _intents = new();
    private readonly List<EnemyCore> _cleanupBuffer = new();

    private EnemyCore _activeAttacker;
    private EnemyCore _selectedAttacker;
    private EnemyCore _lastAttacker;
    private float _combatTime;
    private float _nextGlobalAttackTime;
    private float _selectionExpiresAt;
    private float _grantedAt;
    private float _nextDiscoveryTime;
    private bool _capturedConfiguration;

    public EnemyCore ActiveAttacker => _activeAttacker;
    public EnemyCore SelectedAttacker => _selectedAttacker;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ActiveControllers.Clear();

    private void OnEnable()
    {
        if (!_capturedConfiguration)
        {
            _capturedConfiguration = true;
            if (_enemies != null)
                foreach (EnemyCore enemy in _enemies)
                    if (enemy != null) _configuredEnemies.Add(enemy);
        }
        if (!ActiveControllers.Contains(this))
            ActiveControllers.Add(this);
        RegisterConfiguredEnemies();
        // Wait until Update so every controller has completed OnEnable before
        // assigning discovered enemies to an encounter.
        _nextDiscoveryTime = _combatTime;
    }

    private void Update() => Tick(CombatTimeController.DeltaTime);

    private void Tick(float deltaTime)
    {
        if (VirtualCutscenePlayback.IsPlaying || ProjectR.UI.PauseSettingsController.InputBlocked)
            return;

        _combatTime += Mathf.Max(0f, deltaTime);
        CleanupParticipants();

        if (_autoDiscoverEnemies && _combatTime >= _nextDiscoveryTime)
        {
            _nextDiscoveryTime = _combatTime + 1f;
            DiscoverEnemies();
        }

        // A completed/cancelled state must not strand the token. Never expire a live
        // attack: granting a second one while its hitbox can still fire is unsafe.
        if (_activeAttacker != null &&
            (!IsAlive(_activeAttacker) ||
             (!_activeAttacker.IsInAttackSequence && _combatTime > _grantedAt + 0.15f)))
        {
            ReleaseAttack(_activeAttacker);
        }

        // Re-enabling a coordinator during an existing attack preserves exclusion.
        if (_activeAttacker == null)
        {
            foreach (EnemyCore enemy in _nextAttackTimes.Keys)
            {
                if (!IsAlive(enemy) || !enemy.IsInAttackSequence)
                    continue;
                _activeAttacker = enemy;
                _grantedAt = _combatTime;
                _intents.Remove(enemy);
                break;
            }
        }

        PruneIntents();
        if (_activeAttacker != null)
        {
            _selectedAttacker = null;
            return;
        }

        if (_selectedAttacker != null &&
            (!HasAttackIntent(_selectedAttacker) || _combatTime >= _selectionExpiresAt))
        {
            if (_intents.TryGetValue(_selectedAttacker, out AttackIntent expired))
            {
                expired.RetryAfter = _combatTime + _failedPreparationBackoff;
                // An unsuccessful approach has had its fair turn. Retaining an
                // arbitrarily old queue age would let an unreachable enemy keep
                // monopolizing preparation while ready neighbours wait.
                expired.WaitingSince = _combatTime;
            }
            _selectedAttacker = null;
        }

        if (_selectedAttacker == null && _combatTime >= _nextGlobalAttackTime)
            SelectNextAttacker();
    }

    private void OnDisable()
    {
        ActiveControllers.Remove(this);
        // Keep participant bindings while disabled: permission checks fail closed
        // instead of silently reverting the whole group to independent attacks.
        // A currently committed attack may finish and release its token normally.
        _intents.Clear();
        _selectedAttacker = null;
    }

    private void OnDestroy()
    {
        foreach (EnemyCore enemy in _nextAttackTimes.Keys)
        {
            if (enemy != null)
                enemy.ClearAttackTimingController(this);
        }
        _nextAttackTimes.Clear();
        _repeatCooldownLockedEnemies.Clear();
        _activeAttacker = null;
    }

    public static EnemyAttackTimingController FindFor(EnemyCore enemy)
    {
        if (enemy == null)
            return null;
        EnemyAttackTimingController current = enemy.AttackTimingController;
        if (current != null && current.isActiveAndEnabled &&
            current.gameObject.scene == enemy.gameObject.scene)
            return current;

        EnemyAttackTimingController closest = null;
        float closestDistance = float.PositiveInfinity;
        bool hasConfiguredOwner = false;
        for (int i = ActiveControllers.Count - 1; i >= 0; i--)
        {
            EnemyAttackTimingController controller = ActiveControllers[i];
            if (controller == null || !controller.isActiveAndEnabled)
            {
                ActiveControllers.RemoveAt(i);
                continue;
            }
            if (controller.gameObject.scene != enemy.gameObject.scene)
                continue;
            bool configured = controller._configuredEnemies.Contains(enemy);
            if (hasConfiguredOwner && !configured)
                continue;
            float distance = (controller.transform.position - enemy.transform.position).sqrMagnitude;
            if (configured && !hasConfiguredOwner)
                closestDistance = float.PositiveInfinity;
            if (distance >= closestDistance)
                continue;
            closest = controller;
            closestDistance = distance;
            hasConfiguredOwner = configured;
        }
        return closest;
    }

    public void Register(EnemyCore enemy)
    {
        if (enemy == null || !isActiveAndEnabled)
            return;
        if (!_enemies.Contains(enemy))
            _enemies.Add(enemy);
        if (!_nextAttackTimes.ContainsKey(enemy))
            _nextAttackTimes.Add(enemy, _combatTime + RandomInRange(_initialAttackDelayRange));
        enemy.SetAttackTimingController(this);
    }

    public void Unregister(EnemyCore enemy)
    {
        if (ReferenceEquals(enemy, null))
            return;
        ReleaseAttack(enemy);
        if (ReferenceEquals(_selectedAttacker, enemy))
            _selectedAttacker = null;
        _intents.Remove(enemy);
        _nextAttackTimes.Remove(enemy);
        _repeatCooldownLockedEnemies.Remove(enemy);
        _enemies.Remove(enemy);
        if (enemy != null)
            enemy.ClearAttackTimingController(this);
    }

    public bool HasAttackIntent(EnemyCore enemy)
    {
        return IsEligible(enemy) && _intents.TryGetValue(enemy, out AttackIntent intent) &&
               intent.Target == enemy.TargetTransform &&
               _combatTime - intent.LastRequestedAt <= _intentMemory;
    }

    public bool IsReadyToAttack(EnemyCore enemy, float maximumAttackReach = 0f)
    {
        if (!isActiveAndEnabled || VirtualCutscenePlayback.IsPlaying ||
            ProjectR.UI.PauseSettingsController.InputBlocked || !IsEligible(enemy))
            return false;
        if (!_nextAttackTimes.ContainsKey(enemy))
            Register(enemy);
        if (_combatTime < _nextAttackTimes[enemy])
            return false;

        if (!_intents.TryGetValue(enemy, out AttackIntent intent) || intent.Target != enemy.TargetTransform)
        {
            intent = new AttackIntent { Target = enemy.TargetTransform, WaitingSince = _combatTime };
            _intents[enemy] = intent;
        }
        intent.LastRequestedAt = _combatTime;
        intent.MaximumReach = Mathf.Max(0f, maximumAttackReach);

        // Selection occurs on the coordinator's next tick after collecting requests,
        // so MonoBehaviour update order cannot make the first caller win every turn.
        return _activeAttacker == null && _selectedAttacker == enemy &&
               _combatTime < _selectionExpiresAt && _combatTime >= _nextGlobalAttackTime;
    }

    public bool TryBeginAttack(EnemyCore enemy)
    {
        if (!isActiveAndEnabled || VirtualCutscenePlayback.IsPlaying ||
            ProjectR.UI.PauseSettingsController.InputBlocked || !HasAttackIntent(enemy) || _activeAttacker != null ||
            _selectedAttacker != enemy || _combatTime >= _selectionExpiresAt ||
            _combatTime < _nextGlobalAttackTime || _combatTime < _nextAttackTimes[enemy])
            return false;

        _activeAttacker = enemy;
        _lastAttacker = enemy;
        _grantedAt = _combatTime;
        _selectedAttacker = null;
        _intents.Remove(enemy);
        _repeatCooldownLockedEnemies.Remove(enemy);
        return true;
    }

    public void ReleaseAttack(EnemyCore enemy)
    {
        if (ReferenceEquals(enemy, null) || !ReferenceEquals(_activeAttacker, enemy))
            return;
        _activeAttacker = null;
        _nextGlobalAttackTime = _combatTime + RandomInRange(_betweenAttackGapRange);
        if (_nextAttackTimes.ContainsKey(enemy))
        {
            _nextAttackTimes[enemy] = _combatTime + RandomInRange(_repeatAttackDelayRange);
            _repeatCooldownLockedEnemies.Add(enemy);
        }
    }

    public void PrioritizeAttack(EnemyCore enemy)
    {
        if (enemy == null || !isActiveAndEnabled)
            return;
        if (!_nextAttackTimes.ContainsKey(enemy))
            Register(enemy);

        // A resistance counter may accelerate initial readiness, never a paid repeat
        // cooldown, the shared recovery gap, or another enemy's committed attack.
        if (_repeatCooldownLockedEnemies.Contains(enemy) && _combatTime < _nextAttackTimes[enemy])
            return;
        float urgentAttackTime = _combatTime + RandomInRange(_urgentAttackDelayRange);
        _nextAttackTimes[enemy] = Mathf.Min(_nextAttackTimes[enemy], urgentAttackTime);
    }

    private void SelectNextAttacker()
    {
        Camera camera = _combatCamera != null ? _combatCamera : Camera.main;
        EnemyCore best = null;
        float bestScore = float.NegativeInfinity;
        foreach (var pair in _intents)
        {
            EnemyCore enemy = pair.Key;
            AttackIntent intent = pair.Value;
            float waiting = _combatTime - intent.WaitingSince;
            if (!HasAttackIntent(enemy) || _combatTime < intent.RetryAfter ||
                _combatTime < _nextAttackTimes[enemy] || waiting < _intentCollectionDelay)
                continue;

            bool visible = IsVisible(enemy, camera);
            if (!visible && waiting < _offscreenPreparationDelay)
                continue;

            bool positionReady = enemy.PositioningController == null ||
                enemy.PositioningController.IsAttackPositionReady(enemy, intent.MaximumReach);
            // Finite readiness/visibility preferences: age keeps growing so a rear
            // or blocked enemy eventually receives a preparation opportunity too.
            float score = waiting + (positionReady ? _readyPositionBonus : 0f) +
                          (visible ? _visibleAttackerBonus : 0f) - (enemy == _lastAttacker ? 0.5f : 0f);
            if (score < bestScore ||
                (Mathf.Approximately(score, bestScore) && best != null && enemy.GetEntityId().GetHashCode() > best.GetEntityId().GetHashCode()))
                continue;
            best = enemy;
            bestScore = score;
        }
        _selectedAttacker = best;
        if (best != null)
            _selectionExpiresAt = _combatTime + _preparationLeaseDuration;
    }

    private static bool IsVisible(EnemyCore enemy, Camera camera)
    {
        if (camera == null)
            return true;
        Vector3 viewport = camera.WorldToViewportPoint(enemy.transform.position + Vector3.up);
        return viewport.z > 0f && viewport.x >= 0.02f && viewport.x <= 0.98f &&
               viewport.y >= 0.02f && viewport.y <= 0.98f;
    }

    private static bool IsAlive(EnemyCore enemy) =>
        enemy != null && enemy.isActiveAndEnabled && !enemy.IsDead;

    private static bool IsEligible(EnemyCore enemy) =>
        IsAlive(enemy) && enemy.IsEngagedForCoordination && enemy.TargetTransform != null;

    private void PruneIntents()
    {
        _cleanupBuffer.Clear();
        foreach (EnemyCore enemy in _intents.Keys)
        {
            if (!HasAttackIntent(enemy))
                _cleanupBuffer.Add(enemy);
        }
        foreach (EnemyCore enemy in _cleanupBuffer)
            _intents.Remove(enemy);
    }

    private void CleanupParticipants()
    {
        _cleanupBuffer.Clear();
        foreach (EnemyCore enemy in _nextAttackTimes.Keys)
        {
            if (!IsAlive(enemy))
                _cleanupBuffer.Add(enemy);
        }
        foreach (EnemyCore enemy in _cleanupBuffer)
            Unregister(enemy);
    }

    private void RegisterConfiguredEnemies()
    {
        if (_enemies == null)
            _enemies = new List<EnemyCore>();
        for (int i = _enemies.Count - 1; i >= 0; i--)
        {
            EnemyCore enemy = _enemies[i];
            if (enemy == null)
                _enemies.RemoveAt(i);
            else
                Register(enemy);
        }
    }

    private void DiscoverEnemies()
    {
        if (!_autoDiscoverEnemies)
            return;
        foreach (EnemyCore enemy in FindObjectsByType<EnemyCore>())
        {
            if (IsAlive(enemy) && FindFor(enemy) == this)
                Register(enemy);
        }
    }

    private void OnValidate()
    {
        ClampRange(ref _initialAttackDelayRange);
        ClampRange(ref _repeatAttackDelayRange);
        ClampRange(ref _betweenAttackGapRange);
        ClampRange(ref _urgentAttackDelayRange);
        _intentCollectionDelay = Mathf.Max(0.05f, _intentCollectionDelay);
        _preparationLeaseDuration = Mathf.Max(0.2f, _preparationLeaseDuration);
        _failedPreparationBackoff = Mathf.Max(0.1f, _failedPreparationBackoff);
        _intentMemory = Mathf.Max(0.5f, _intentMemory);
        _readyPositionBonus = Mathf.Max(0f, _readyPositionBonus);
        _visibleAttackerBonus = Mathf.Max(0f, _visibleAttackerBonus);
        _offscreenPreparationDelay = Mathf.Max(0f, _offscreenPreparationDelay);
    }

    private static void ClampRange(ref Vector2 range)
    {
        range.x = Mathf.Max(0f, range.x);
        range.y = Mathf.Max(range.x, range.y);
    }

    private static float RandomInRange(Vector2 range) => Random.Range(range.x, range.y);
}
