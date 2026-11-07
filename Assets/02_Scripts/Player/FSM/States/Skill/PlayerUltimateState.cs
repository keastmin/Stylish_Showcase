using UnityEngine;

public sealed class PlayerUltimateState : PlayerStateBase
{
    private bool _blendOnExit;
    public PlayerUltimateState(PlayerCore core) : base(core) { }
    public override bool IsInvulnerable => Core.Ultimate?.ActiveDefinition?.invulnerable == true;

    public override void Enter()
    {
        base.Enter();
        _blendOnExit = false;
        HitstopCoordinator.Cancel(Core);
        Core.EndPerfectDodge(true);
        // A paused Timeline still owns its animation/camera/effect bindings.
        foreach (var director in Core.DirectorContainer.Directors.Values)
            if (director != null) director.Stop();
        foreach (var parameter in Core.Animator.parameters)
            if (parameter.type == AnimatorControllerParameterType.Trigger)
                Core.Animator.ResetTrigger(parameter.nameHash);
        Collider target = Core.TargetDetector.AcquireBasicAttackTarget();
        Vector3 direction = target != null
            ? Vector3.ProjectOnPlane(target.transform.position - Core.transform.position, Vector3.up)
            : Core.InputCollector.IsInputMove && Core.MainCamera != null
                ? Core.DirCalculator.GetTargetDirection(Core.InputCollector.MoveValue, Core.MainCamera.transform)
                : Core.Rotator.FacingDirection;
        Core.Rotator.RotateImmediately(direction);
        Core.Mover.ConsumePendingDisplacement();
        Core.Ultimate.Begin();
    }

    public override void UpdateTick()
    {
        if (!Core.Ultimate.IsActive)
        {
            Core.StateMachine.Transition(Core.StateMachine.IdleState);
            return;
        }
        Core.Ultimate.Tick(CombatTimeController.DeltaTime);
        bool complete = Core.Ultimate.IsComplete;
        if (!complete && !Core.Ultimate.CanCancelWithInput) return;
        var input = Core.InputCollector;
        var states = Core.StateMachine;
        PlayerStateBase next = null;
        if (input.IsInputDodge && Core.IsDodgeAvailable)
            next = input.IsInputMove ? states.FrontDodgeState : states.BackDodgeState;
        else if (input.IsInputSkill && Core.IsSkillEnable) next = states.SkillState;
        else if (input.IsInputAttack) next = states.BasicAttack1State;
        else if (input.IsInputMove) next = states.RunStartState;
        else if (complete) next = states.IdleState;
        // Opening the window never ends Q by itself. A rejected action must
        // leave the cast running; the accepted input enters its state this tick.
        if (next == null) return;
        _blendOnExit = true;
        if (!states.Transition(next)) _blendOnExit = false;
    }

    public override void FixedTick() => Core.Ultimate.FixedTick();

    public override void OnHitStopStarted() => Core.Ultimate.PreservePendingMotion();

    public override void Exit()
    {
        if (_blendOnExit && Core.Ultimate.ActiveDefinition != null)
            PlayerUltimateExitBlend.Begin(Core.Animator, Core.Ultimate.ActiveDefinition.ExitBlendDuration, Core);
        _blendOnExit = false;
        Core.Ultimate.Stop();
        Core.Mover.ConsumePendingDisplacement();
        Core.TargetDetector.ClearBasicAttackTarget();
        base.Exit();
    }
}
