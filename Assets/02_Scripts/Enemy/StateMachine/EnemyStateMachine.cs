using UnityEngine;

public class EnemyStateMachine
{
    public EnemyIdleState IdleState;
    public EnemyEngageState EngageState;
    public EnemyCloseAttackState CloseAttackState;
    public EnemyFrontHitState FrontHitState;
    public EnemyBackHitState BackHitState;
    public EnemyCloseAttackNoticeState CloseAttackNoticeState;
    public EnemyDeadState DeadState;

    private EnemyStateBase _currentState;
    public EnemyStateBase CurrentState => _currentState;

    private EnemyCore _core;

    public EnemyStateMachine(EnemyCore enemy)
    {
        _core = enemy;
        IdleState = new EnemyIdleState(enemy);
        EngageState = new EnemyEngageState(enemy);
        CloseAttackState = new EnemyCloseAttackState(enemy);
        FrontHitState = new EnemyFrontHitState(enemy);
        BackHitState = new EnemyBackHitState(enemy);
        CloseAttackNoticeState = new EnemyCloseAttackNoticeState(enemy);
        DeadState = new EnemyDeadState(enemy);
    }

    public void InitEnemyStateMachine(EnemyStateBase initState)
    {
        _currentState = initState;
        _currentState?.Enter();
    }

    public void UpdateTick()
    {
        // Handle death before dispatch: a base-state transition must not let the
        // previous derived state continue and enter a fresh attack on this frame.
        if (_core.IsDead && _currentState != DeadState)
        {
            Transition(DeadState);
            return;
        }
        _currentState?.UpdateTick();
    }

    public void FixedTick()
    {
        _currentState?.FixedTick();
    }

    public void LateTick()
    {
        _currentState?.LateTick();
    }

    public void AnimatorTick()
    {
        _currentState?.AnimatorTick();
    }

    public void ClearAccumulatedMotion()
    {
        _currentState?.ClearAccumulatedMotion();
    }

    public void Transition(EnemyStateBase nextState)
    {
        if (nextState == null)
            return;

        // Repeated hits must re-enter to reset reaction flags and restart the animation.
        bool isHitReaction = nextState is EnemyFrontHitState || nextState is EnemyBackHitState;
        if (ReferenceEquals(_currentState, nextState) && !isHitReaction)
            return;

        // A pause preserves pending motion; cancelling a state does not.
        _currentState?.ClearAccumulatedMotion();
        bool leavingAttack = _currentState is EnemyAttackNoticeState || _currentState is EnemyAttackState;
        bool enteringAttack = nextState is EnemyAttackNoticeState || nextState is EnemyAttackState;
        _currentState?.Exit();
        if (leavingAttack && !enteringAttack)
        {
            _core.EndAttackTargeting();
            _core.ReleaseAttackPermission();
        }
        _currentState = nextState;
        _currentState.ClearAccumulatedMotion();
        _currentState?.Enter();
    }
}
