using System;
using System.Collections.Generic;
using UnityEngine;

public class EnemyCore : MonoBehaviour, IDamageable, IHitStopParticipant
{
    [SerializeField, Min(0f)] private float _maxHP = 100f;
    [SerializeField] private float _currentHP;
    [SerializeField] private ParticleSystem _deadVFX;
    [SerializeField] private CapsuleCollider _physicsCollider;

    [SerializeField] private EnemyAnimatorCallback _animatorCallback;
    [SerializeField] private EnemyAttackSO[] _attackSOs;

    [Header("Attack Targeting Debug")]
    [SerializeField] private bool _drawAttackTargetingGizmos = true;
    [SerializeField] private Color _damageFieldColor = new(1f, 0.15f, 0.1f, 0.95f);
    [SerializeField] private Color _perfectDodgeRangeColor = new(1f, 0.8f, 0.1f, 0.95f);
    [SerializeField] private Color _attackWarpPathColor = new(0.1f, 0.85f, 1f, 0.95f);

    [Header("Hit Reaction Resistance")]
    [SerializeField, Tooltip("평상시에 이 레벨 이상의 플레이어 공격이 피격 상태를 발생시킵니다.")]
    private StaggerLevel _minimumStaggerLevel = StaggerLevel.Level1;
    [SerializeField, Tooltip("연속 피격 저항 중에는 이 레벨 이상의 공격만 저항을 뚫고 피격 상태를 발생시킵니다.")]
    private StaggerLevel _resistantMinimumStaggerLevel = StaggerLevel.Level2;
    [SerializeField, Min(1)] private int _minConsecutiveHitReactions = 2;
    [SerializeField, Min(1)] private int _maxConsecutiveHitReactions = 4;
    [SerializeField, Min(0f)] private float _hitReactionChainResetDelay = 1.25f;
    [SerializeField] private Vector2 _hitReactionResistanceDurationRange = new(1.75f, 2.75f);

    [SerializeField] private EnemySFX _enemySFX;
    [SerializeField] private ParticleSystem _frontHitVFX;
    [SerializeField] private ParticleSystem _backHitVFX;

    private DamageData _lastDamageData;

    private EnemyRotator _rotator;
    private EnemyMover _mover;
    private EnemyAnimationEvent _animationEvent;
    private EnemyStateMachine _stateMachine;
    private EnemyTargetDetector _targetDetector;
    private EnemyHitboxPool _hitboxPool;
    private EnemyAttackSimulator _attackSimulator;
    private EnemyAttackTimingController _attackTimingController;
    private EnemyPositioningController _positioningController;
    private EnemyAttackTargetingController _attackTargeting;
    private bool _isHitStopped;
    private float _baseAnimatorSpeed;
    private float _fallbackAttackCooldownRemaining;
    private float _hitReactionChainRemaining;
    private float _hitReactionResistanceRemaining;
    private int _consecutiveHitReactions;
    private int _nextHitReactionThreshold;

    public float CurrentHP => _currentHP;
    public Animator Animator => _animatorCallback.Animator;
    public EnemyRotator Rotator => _rotator;
    public EnemyMover Mover => _mover;
    public EnemyAnimationEvent AnimationEvent => _animationEvent;
    public EnemyStateMachine StateMachine => _stateMachine;
    public EnemyTargetDetector TargetDetector => _targetDetector;
    public EnemyHitboxPool HitboxPool => _hitboxPool;
    public EnemyAttackSimulator AttackSimulator => _attackSimulator;
    public EnemyPositioningController PositioningController => _positioningController;
    public EnemyAttackTargetingController AttackTargeting => _attackTargeting;
    public DamageData LastDamageData => _lastDamageData;
    public bool IsHitStopped => _isHitStopped;
    /// <summary>
    /// True from the start of an attack notice until the attack completes or is interrupted.
    /// UI such as off-screen enemy markers can use this without knowing the active state.
    /// </summary>
    public bool IsAttackWarningActive { get; private set; }
    public Transform TargetTransform => TargetDetector.TargetTransform;
    public bool IsDead => (_currentHP <= 0f);
    public event Action OnDead;

    public Dictionary<EnemyAttackID, EnemyAttackSO> AttackDataDictionary;
    public event Action<DamageData> OnDamaged;
    public event Action<float, float> OnHealthChange; // 체력이 변경되면 호출, <최대 체력, 현재 체력>

    private void Awake()
    {
        _baseAnimatorSpeed = Animator.speed;
        // 공격 데이터 저장
        AttackDataDictionary = new Dictionary<EnemyAttackID, EnemyAttackSO>();
        foreach (var so in _attackSOs)
            AttackDataDictionary.Add(so.AttackID, so);

        TryGetComponent(out _rotator);
        TryGetComponent(out _mover);
        TryGetComponent(out _animationEvent);
        TryGetComponent(out _targetDetector);
        TryGetComponent(out _hitboxPool);
        TryGetComponent(out _attackSimulator);
        _attackTargeting = new EnemyAttackTargetingController(this);

        _currentHP = _maxHP;
        _fallbackAttackCooldownRemaining = UnityEngine.Random.Range(1.5f, 3.5f);
        RollNextHitReactionThreshold();

        _stateMachine = new EnemyStateMachine(this);
        _animatorCallback.OnAnimatorMoveAction += AnimatorUpdate;
        _animationEvent.OnAttack += HandleAttack;
        CombatVfxTime.RegisterHierarchy(gameObject);
    }

    private void OnEnable()
    {
        CombatTimeController.ScaleChanged += ApplyCombatSpeed;
        ApplyCombatSpeed(CombatTimeController.Scale);
        ResolvePositioningController();
    }

    private void ApplyCombatSpeed(float scale)
    {
        Animator.speed = _baseAnimatorSpeed * (_isHitStopped ? 0f : scale);
    }

    private void Start()
    {
        _stateMachine.InitEnemyStateMachine(_stateMachine.IdleState);
    }

    private void Update()
    {
        TickCombatTimers();

        if (_isHitStopped)
            return;

        StateMachine.UpdateTick();
    }

    private void FixedUpdate()
    {
        if (_isHitStopped)
            return;

        StateMachine.FixedTick();
    }

    private void LateUpdate()
    {
        if (_isHitStopped)
            return;

        StateMachine.LateTick();
    }

    private void AnimatorUpdate()
    {
        if (_isHitStopped)
            return;

        StateMachine.AnimatorTick();
    }

    private void OnDisable()
    {
        EndAttackTargeting();
        ReleaseAttackPermission();
        _positioningController?.Unregister(this);
        EndHitStop();
        CombatTimeController.ScaleChanged -= ApplyCombatSpeed;
        ApplyCombatSpeed(1f);
    }

    public bool TryTakeDamage(DamageData damageData)
    {
        if (damageData.DamageAmount <= 0f || _currentHP <= 0f)
            return false;

        _currentHP = Mathf.Max(_currentHP - damageData.DamageAmount, 0f);
        _lastDamageData = damageData;
        OnDamaged?.Invoke(damageData);
        OnHealthChange?.Invoke(_maxHP, _currentHP);

        HitDirectionType type = HitDirectionCalculator.GetHitDirection(
            damageData,
            transform.position,
            Rotator.FacingDirection);

        if (type == HitDirectionType.Front)
            FrontHitVFXOn();
        else if (type == HitDirectionType.Back)
            BackHitVFXOn();

        return true;
    }

    public bool ShouldEnterHitReaction(DamageData damageData)
    {
        if (_hitReactionResistanceRemaining > 0f)
            return damageData.StaggerLevel >= _resistantMinimumStaggerLevel;

        if (damageData.StaggerLevel < _minimumStaggerLevel)
            return false;

        if (_hitReactionChainRemaining <= 0f)
        {
            _consecutiveHitReactions = 0;
            RollNextHitReactionThreshold();
        }

        _hitReactionChainRemaining = _hitReactionChainResetDelay;

        if (_consecutiveHitReactions >= _nextHitReactionThreshold)
        {
            _consecutiveHitReactions = 0;
            _hitReactionResistanceRemaining = UnityEngine.Random.Range(
                _hitReactionResistanceDurationRange.x,
                _hitReactionResistanceDurationRange.y);

            PrioritizeNextAttack();
            return damageData.StaggerLevel >= _resistantMinimumStaggerLevel;
        }

        _consecutiveHitReactions++;
        return true;
    }

    public bool TryBeginAttack(EnemyAttackSO attackSO = null)
    {
        if (_attackTimingController != null)
        {
            if (!_attackTimingController.IsReadyToAttack(this))
                return false;
        }
        else if (_fallbackAttackCooldownRemaining > 0f)
        {
            return false;
        }

        float maximumAttackReach = attackSO != null
            ? attackSO.GetMaximumAttackReach()
            : 0f;

        if (_positioningController != null &&
            !_positioningController.CanBeginAttack(this, maximumAttackReach))
            return false;

        bool granted;

        if (_attackTimingController != null)
        {
            granted = _attackTimingController.TryBeginAttack(this);
        }
        else
        {
            _fallbackAttackCooldownRemaining = UnityEngine.Random.Range(3.5f, 6.5f);
            granted = true;
        }

        if (granted)
            _positioningController?.NotifyAttackStarted(this);

        return granted;
    }

    public void ReleaseAttackPermission()
    {
        IsAttackWarningActive = false;
        _positioningController?.NotifyAttackEnded(this);

        if (_attackTimingController != null)
            _attackTimingController.ReleaseAttack(this);
    }

    public void SetAttackTimingController(EnemyAttackTimingController controller)
    {
        if (controller == null || _attackTimingController == controller)
            return;

        ReleaseAttackPermission();
        _attackTimingController = controller;
    }

    public void ClearAttackTimingController(EnemyAttackTimingController controller)
    {
        if (_attackTimingController == controller)
            _attackTimingController = null;
    }

    public void SetPositioningController(EnemyPositioningController controller)
    {
        if (controller == null || _positioningController == controller)
            return;

        EnemyPositioningController previous = _positioningController;
        _positioningController = controller;
        previous?.Unregister(this);
    }

    public void ClearPositioningController(EnemyPositioningController controller)
    {
        if (_positioningController == controller)
            _positioningController = null;
    }

    public Vector3 AdjustPositioningMovement(Vector3 desiredVelocity)
    {
        return _positioningController != null
            ? _positioningController.AdjustMovement(this, desiredVelocity)
            : desiredVelocity;
    }

    public void PlayHitReaction(int stateHash)
    {
        Animator.Play(stateHash, 0, 0f);
        Animator.Update(0f);
    }

    public void BeginHitStop()
    {
        if (_isHitStopped)
            return;

        _isHitStopped = true;
        // The hit frame has already been evaluated. Keep its pending root motion
        // so the hit reaction/attack resumes with the same remaining displacement.
        _mover.SetHitStopped(true);

        ApplyCombatSpeed(CombatTimeController.Scale);
    }

    public void EndHitStop()
    {
        if (!_isHitStopped)
            return;

        _isHitStopped = false;
        ApplyCombatSpeed(CombatTimeController.Scale);
        _mover.SetHitStopped(false);
    }

    // 공격
    private void HandleAttack(EnemyAttackSO attackSO)
    {
        CompleteAttackWarpForImpact();
        HitboxPool.SpacingHitboxes(attackSO);
    }

    private void CompleteAttackWarpForImpact()
    {
        if (_attackTargeting == null || !_attackTargeting.IsActive)
            return;

        _attackTargeting.Lock();
        _stateMachine.ClearAccumulatedMotion();
        _rotator.RotateImmediately(_attackTargeting.TargetForward);
        _mover.WarpTo(_attackTargeting.TargetPosition);

        // The hitbox query runs in this same animation-event call. Synchronize the
        // physics broadphase so it observes the completed impact pose immediately.
        Physics.SyncTransforms();
    }

    public void BeginAttackTargeting(EnemyAttackSO attackSO)
    {
        if (attackSO != null)
            IsAttackWarningActive = true;

        _attackTargeting?.Begin(attackSO);
    }

    public void UpdateAttackTargeting()
    {
        _attackTargeting?.UpdateTarget(CombatTimeController.DeltaTime);
    }

    public void LockAttackTargeting()
    {
        _attackTargeting?.Lock();
    }

    public void EndAttackTargeting()
    {
        _attackTargeting?.End();
    }

    public bool IsPlayerInEnemyAttackRange()
    {
        return _attackTargeting != null && _attackTargeting.IsPlayerInNoticeRange();
    }

    private void TickCombatTimers()
    {
        if (_isHitStopped)
            return;

        float deltaTime = CombatTimeController.DeltaTime;
        _fallbackAttackCooldownRemaining = Mathf.Max(0f, _fallbackAttackCooldownRemaining - deltaTime);
        _hitReactionChainRemaining = Mathf.Max(0f, _hitReactionChainRemaining - deltaTime);
        _hitReactionResistanceRemaining = Mathf.Max(0f, _hitReactionResistanceRemaining - deltaTime);
    }

    private void ResolvePositioningController()
    {
        EnemyPositioningController controller = EnemyPositioningController.FindFor(this);
        controller?.Register(this);
    }

    private void PrioritizeNextAttack()
    {
        _fallbackAttackCooldownRemaining = 0f;
        _attackTimingController?.PrioritizeAttack(this);
    }

    private void RollNextHitReactionThreshold()
    {
        int min = Mathf.Max(1, _minConsecutiveHitReactions);
        int max = Mathf.Max(min, _maxConsecutiveHitReactions);
        _nextHitReactionThreshold = UnityEngine.Random.Range(min, max + 1);
    }

    public void DeadStart()
    {
        EnemyDeadEventInvoke();
        ColliderDisable();
        DeadVFXPlay();
        DeadSFXOn();
    }

    public void EnemyDeadEventInvoke()
    {
        OnDead?.Invoke();
    }

    public void ColliderDisable()
    {
        _physicsCollider.isTrigger = true;
    }

    public void DeadVFXPlay()
    {
        _deadVFX.Play();
    }

    public void AttackNoticeSFX()
    {
        _enemySFX.OnAttackNoticeSFX();
    }

    public void CloseAttackHitSFX()
    {
        _enemySFX.OnCloseAttackHitSFX();
    }

    public void FrontHitVFXOn()
    {
        _frontHitVFX.time = 0f;
        _frontHitVFX.Play();
    }

    public void BackHitVFXOn()
    {
        _backHitVFX.time = 0f;
        _backHitVFX.Play();
    }

    public void DeadSFXOn()
    {
        _enemySFX.OnDeadSFX();
    }

    private void OnValidate()
    {
        _minConsecutiveHitReactions = Mathf.Max(1, _minConsecutiveHitReactions);
        _maxConsecutiveHitReactions = Mathf.Max(_minConsecutiveHitReactions, _maxConsecutiveHitReactions);
        _hitReactionChainResetDelay = Mathf.Max(0f, _hitReactionChainResetDelay);
        _hitReactionResistanceDurationRange.x = Mathf.Max(0f, _hitReactionResistanceDurationRange.x);
        _hitReactionResistanceDurationRange.y = Mathf.Max(
            _hitReactionResistanceDurationRange.x,
            _hitReactionResistanceDurationRange.y);
    }

    private void OnDrawGizmos()
    {
        if (!_drawAttackTargetingGizmos || !Application.isPlaying)
            return;

        _attackTargeting?.DrawGizmos(
            _damageFieldColor,
            _perfectDodgeRangeColor,
            _attackWarpPathColor);
    }
}
