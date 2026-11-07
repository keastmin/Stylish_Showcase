using UnityEngine;

public class EnemyCloseAttackNoticeState : EnemyAttackNoticeState
{
    private const EnemyAttackID _attackID1 = EnemyAttackID.CloseAttack1Hit;
    private EnemyAttackSO _attackSO1;

    private float _currentTime = 0f;

    public EnemyCloseAttackNoticeState(EnemyCore core) : base(core)
    {
        _attackSO1 = core.AttackDataDictionary[_attackID1];
    }

    public override void Enter()
    {
        Core.BeginAttackTargeting(_attackSO1);
        base.Enter();
        _currentTime = 0f;
    }

    public override void UpdateTick()
    {
        base.UpdateTick();

        if (Core.TargetTransform == null)
        {
            Core.StateMachine.Transition(Core.StateMachine.IdleState);
            return;
        }

        Core.UpdateAttackTargeting();
        Core.Rotator.RotateToward(
            Core.AttackTargeting.TargetForward,
            _attackSO1.AttackRotationAnglePerSecond);

        _currentTime += CombatTimeController.DeltaTime;
        if(_currentTime >= _attackSO1.AttackAnimationTransitionTime)
        {
            Core.LockAttackTargeting();
            Core.StateMachine.Transition(Core.StateMachine.CloseAttackState);
            return;
        }

    }

    public override void Exit()
    {
        base.Exit();
    }
}
