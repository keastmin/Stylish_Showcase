using UnityEngine;

public class EnemyEngageState : EnemyCompositeState
{
    private const EnemyAttackID PrimaryAttackID = EnemyAttackID.CloseAttack1Hit;

    // 상태 정의
    private readonly EnemyCombatHoldState _holdState;
    private readonly EnemyCombatMoveLeftState _moveLeftState;
    private readonly EnemyCombatMoveRightState _moveRightState;
    private readonly EnemyCombatMoveForwardState _moveForwardState;
    private readonly EnemyCombatMoveBackwardState _moveBackwardState;

    private float _decisionTime;
    private bool _isDamaged = false;
    private readonly EnemyAttackSO _primaryAttack;

    public EnemyEngageState(EnemyCore core) : base(core)
    {
        core.AttackDataDictionary.TryGetValue(PrimaryAttackID, out _primaryAttack);

        // 상태 생성
        _holdState = new EnemyCombatHoldState(core, this);
        _moveLeftState = new EnemyCombatMoveLeftState(core, this);
        _moveRightState = new EnemyCombatMoveRightState(core, this);
        _moveForwardState = new EnemyCombatMoveForwardState(core, this);
        _moveBackwardState = new EnemyCombatMoveBackwardState(core, this);
    }

    public override void Enter()
    {
        // 초기화
        _isDamaged = false;
        // 이벤트 연결
        Core.OnDamaged += SetDamaged;

        ResetDecisionTime();
        TransitionSubState(_holdState);
    }

    public override void UpdateTick()
    {
        base.UpdateTick();

        if (_isDamaged)
            return;

        if (Core.TargetTransform == null)
        {
            Core.StateMachine.Transition(Core.StateMachine.IdleState);
            return;
        }

        Vector3 direction =
            Core.TargetTransform.position - Core.transform.position;

        direction.y = 0f;
        Core.Rotator.RotateToward(direction);

        _decisionTime -= CombatTimeController.DeltaTime;

        if (_decisionTime <= 0f)
        {
            SelectNextAction();
            ResetDecisionTime();
        }

    }

    public override void Exit()
    {
        // 초기화
        _isDamaged = false;
        // 이벤트 해제
        Core.OnDamaged -= SetDamaged;

        base.Exit();
    }

    private void SelectNextAction()
    {
        if (Core.TryBeginAttack(_primaryAttack))
        {
            Core.StateMachine.Transition(Core.StateMachine.CloseAttackNoticeState);
            return;
        }

        if (Core.PositioningController != null)
        {
            SelectPositioningAction(Core.PositioningController.GetRecommendedAction(Core));
            return;
        }

        float distance = Vector3.Distance(
            Core.transform.position,
            Core.TargetTransform.position);

        if (distance < 2f)
        {
            TransitionSubState(_moveBackwardState);
            return;
        }

        if (distance > 5f)
        {
            TransitionSubState(_moveForwardState);
            return;
        }

        float value = Random.value;

        if (value < 0.25f)
            TransitionSubState(_holdState);
        else if (value < 0.60f)
            TransitionSubState(_moveLeftState);
        else
            TransitionSubState(_moveRightState);
    }

    private void SelectPositioningAction(EnemyPositioningAction action)
    {
        switch (action)
        {
            case EnemyPositioningAction.Advance:
                TransitionSubState(_moveForwardState);
                break;
            case EnemyPositioningAction.Retreat:
                TransitionSubState(_moveBackwardState);
                break;
            case EnemyPositioningAction.StrafeLeft:
                TransitionSubState(_moveLeftState);
                break;
            case EnemyPositioningAction.StrafeRight:
                TransitionSubState(_moveRightState);
                break;
            default:
                TransitionSubState(_holdState);
                break;
        }
    }

    private void ResetDecisionTime()
    {
        _decisionTime = Core.PositioningController != null
            ? Random.Range(0.2f, 0.45f)
            : Random.Range(0.4f, 1.1f);
    }

    private void SetDamaged(DamageData damageData)
    {
        if (!Core.ShouldEnterHitReaction(damageData))
            return;

        _isDamaged = true;

        HitDirectionType type = HitDirectionCalculator.GetHitDirection(damageData, Core.transform.position, Core.Rotator.FacingDirection);
        if (type == HitDirectionType.Front)
            Core.StateMachine.Transition(Core.StateMachine.FrontHitState);
        else if (type == HitDirectionType.Back)
            Core.StateMachine.Transition(Core.StateMachine.BackHitState);
    }
}
