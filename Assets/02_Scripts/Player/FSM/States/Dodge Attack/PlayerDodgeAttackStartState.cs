using UnityEngine;

public class PlayerDodgeAttackStartState : PlayerDodgeAttackState
{
    private static readonly int StartStateHash = Animator.StringToHash("Dodge Attack Start");
    private const float MaxApproachSpeed = 20f;

    protected override DirectorID AttackDirectorID => DirectorID.DodgeAttackStart;

    protected override string AnimationTrigger => "IsDodgeAttack";

    protected override PlayerStateBase NextState => Core.StateMachine.DodgeAttackLoopState;

    public PlayerDodgeAttackStartState(PlayerCore core) : base(core)
    {
    }

    protected override float WarpForwardDelta(float forwardDelta, float remaining)
    {
        Animator animator = Core.Animator;
        AnimatorStateInfo state = animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0)
            : animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != StartStateHash)
            return forwardDelta;

        // Close the gap over the remaining windup, while limiting extra travel
        // for distant attackers. The shared movement step clamps to the hurtbox.
        float animationStep = Time.fixedDeltaTime * animator.speed * state.speed * state.speedMultiplier;
        if (animationStep <= 0f)
            return forwardDelta;

        float timeLeft = state.length * (1f - Mathf.Clamp01(state.normalizedTime));
        float approachDelta = remaining * animationStep / Mathf.Max(timeLeft, animationStep);
        return Mathf.Max(forwardDelta, Mathf.Min(approachDelta, MaxApproachSpeed * animationStep));
    }
}
