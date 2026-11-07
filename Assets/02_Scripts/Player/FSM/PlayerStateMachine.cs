using UnityEngine;

public class PlayerStateMachine
{
    public PlayerIdleState IdleState;
    public PlayerRunStartState RunStartState;
    public PlayerRunLoopState RunLoopState;
    public PlayerRunStopLeftState RunStopLeftState;
    public PlayerRunStopRightState RunStopRightState;
    public PlayerFastRunLoopState FastRunLoopState;
    public PlayerFastRunStopState FastRunStopState;
    public PlayerBasicAttack1State BasicAttack1State;
    public PlayerBasicAttack2State BasicAttack2State;
    public PlayerBasicAttack3State BasicAttack3State;
    public PlayerBasicAttack4State BasicAttack4State;
    public PlayerFrontDodgeState FrontDodgeState;
    public PlayerFrontDodgeStopState FrontDodgeStopState;
    public PlayerBackDodgeState BackDodgeState;
    public PlayerRunAttackState RunAttackState;
    public PlayerFastRunTurnState FastRunTurnState;
    public PlayerHitFrontState HitFrontState;
    public PlayerHitBackState HitBackState;
    public PlayerHitLargeState HitLargeState;
    public PlayerDodgeAttackStartState DodgeAttackStartState;
    public PlayerDodgeAttackLoopState DodgeAttackLoopState;
    public PlayerDodgeAttackEndState DodgeAttackEndState;
    public PlayerSkillState SkillState;
    public PlayerUltimateState UltimateState;

    private PlayerStateBase _currentState;
    private readonly PlayerCore _core;
    private Animator _playerAnimator;

    public bool IsInvulnerable => _currentState?.IsInvulnerable == true;
    public bool IsSkillActive => _currentState is PlayerSkillState;
    // Q is a priority action in every initialized gameplay state; an active cast
    // cannot restart itself. Cooldown and pause are checked by PlayerCore.
    public bool CanStartUltimate => _currentState != null && _currentState is not PlayerUltimateState;

    public PlayerStateMachine(PlayerCore player)
    {
        _core = player;
        _playerAnimator = player.Animator;
        IdleState = new PlayerIdleState(player);
        RunStartState = new PlayerRunStartState(player);
        RunLoopState = new PlayerRunLoopState(player);
        RunStopLeftState = new PlayerRunStopLeftState(player);
        RunStopRightState = new PlayerRunStopRightState(player);
        FastRunLoopState = new PlayerFastRunLoopState(player);
        FastRunStopState = new PlayerFastRunStopState(player);
        BasicAttack1State = new PlayerBasicAttack1State(player);
        BasicAttack2State = new PlayerBasicAttack2State(player);
        BasicAttack3State = new PlayerBasicAttack3State(player);
        BasicAttack4State = new PlayerBasicAttack4State(player);
        FrontDodgeState = new PlayerFrontDodgeState(player);
        FrontDodgeStopState = new PlayerFrontDodgeStopState(player);
        BackDodgeState = new PlayerBackDodgeState(player);
        RunAttackState = new PlayerRunAttackState(player);
        FastRunTurnState = new PlayerFastRunTurnState(player);
        HitFrontState = new PlayerHitFrontState(player);
        HitBackState = new PlayerHitBackState(player);
        HitLargeState = new PlayerHitLargeState(player);
        DodgeAttackStartState = new PlayerDodgeAttackStartState(player);
        DodgeAttackLoopState = new PlayerDodgeAttackLoopState(player);
        DodgeAttackEndState = new PlayerDodgeAttackEndState(player);
        SkillState = new PlayerSkillState(player);
        UltimateState = new PlayerUltimateState(player);
    }

    public void InitStateMachine(PlayerStateBase initState)
    {
        if (IsNewDodge(initState) && !_core.TryConsumeDodgeCharge()) return;
        _currentState = initState;
        _currentState?.Enter();
    }

    public void UpdateTick()
    {
        if (_core.InputCollector.IsInputUltimate && _core.TryBeginUltimate()) return;
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

    public void OnHitStopStarted()
    {
        _currentState?.OnHitStopStarted();
    }

    public bool Transition(PlayerStateBase nextState)
    {
        if (nextState is PlayerUltimateState && !_core.CanBeginUltimate) return false;
        // Stop is a continuation and counterattacks are separate actions.
        // Reject before Exit so a depleted press cannot cancel attacks, reset
        // animation/event state, clear targets, or restart the cooldown.
        if (IsNewDodge(nextState) && !_core.TryConsumeDodgeCharge()) return false;
        _core.Dodge?.BeforeTransition(_currentState, nextState);
        _currentState?.Exit();
        if (nextState is PlayerUltimateState)
            PlayerUltimateExitBlend.Cancel(_core.Animator);
        if (_currentState is PlayerBasicAttackState && nextState is not PlayerBasicAttackState)
            _core.TargetDetector.ClearBasicAttackTarget();
        if (_currentState is PlayerDodgeAttackState && nextState is not PlayerDodgeAttackState)
            _core.ClearDodgeAttackTarget();
        _currentState = nextState;
        _currentState?.Enter();
        return true;
    }

    private static bool IsNewDodge(PlayerStateBase state)
        => state is PlayerFrontDodgeState || state is PlayerBackDodgeState;
}
