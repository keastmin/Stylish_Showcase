using UnityEngine;

public class EnemyCompositeState : EnemyStateBase
{
    protected EnemyStateBase CurrentSubState { get; private set; }

    protected EnemyCompositeState(EnemyCore core) : base(core) { }

    protected void TransitionSubState(EnemyStateBase nextState)
    {
        if (nextState == null || ReferenceEquals(CurrentSubState, nextState))
            return;

        CurrentSubState?.ClearAccumulatedMotion();
        CurrentSubState?.Exit();
        CurrentSubState = nextState;
        CurrentSubState.ClearAccumulatedMotion();
        CurrentSubState.Enter();
    }

    public override void UpdateTick()
    {
        base.UpdateTick();

        CurrentSubState?.UpdateTick();
    }

    public override void FixedTick()
    {
        CurrentSubState?.FixedTick();
    }

    public override void LateTick()
    {
        CurrentSubState?.LateTick();
    }

    public override void AnimatorTick()
    {
        CurrentSubState?.AnimatorTick();
    }

    public override void Exit()
    {
        CurrentSubState?.ClearAccumulatedMotion();
        CurrentSubState?.Exit();
        CurrentSubState = null;
        base.Exit();
    }

    public override void ClearAccumulatedMotion()
    {
        base.ClearAccumulatedMotion();
        CurrentSubState?.ClearAccumulatedMotion();
    }
}
