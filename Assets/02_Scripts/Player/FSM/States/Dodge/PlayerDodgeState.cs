using UnityEngine;

public abstract class PlayerDodgeState : PlayerStateBase
{
    protected abstract string AnimationTrigger { get; }

    public PlayerDodgeState(PlayerCore core) : base(core) { }

    protected bool IsPerfectDodge = false;
    protected bool IsPerfectDodgeEnd = false;

    public override void Enter()
    {
        base.Enter();
        Core.AnimationEvent.OnPerfectDodgeEnd += HandlePerfectDodgeEnd;
        // 초기화
        IsPerfectDodge = false;
        IsPerfectDodgeEnd = false;

        // 애니메이션 재생
        Core.Animator.SetTrigger(AnimationTrigger);

        if (Core.Dodge?.Configured == true)
        {
            Core.Dodge.BeginDodge(this is PlayerBackDodgeState);
            return;
        }

        // 회피 판별
        if (Core.IsPerfectDodge(out EnemyCore enemy))
        {
            IsPerfectDodge = true;

            Core.BeginPerfectDodge(enemy);
        }
    }

    public override void FixedTick()
    {
        if (IsDamaged)
            return;

        Core.Mover.Move(AnimDeltaPos / Time.fixedDeltaTime);
        AnimDeltaPos = Vector3.zero;
    }

    public override void AnimatorTick()
    {
        if (IsDamaged)
            return;

        AnimDeltaPos += Core.Animator.deltaPosition;
    }

    public override void Exit()
    {
        Core.AnimationEvent.OnPerfectDodgeEnd -= HandlePerfectDodgeEnd;
        // 자연 종료 후 진행 중인 FadeOut은 유지하고, 도중에 다른 상태로 나가면 정리합니다.
        if (Core.Dodge?.Configured != true && Core.IsPerfectDodgeActive)
            Core.EndPerfectDodge(true);
        // 초기화
        IsPerfectDodge = false;
        IsPerfectDodgeEnd = false;

        // 애니메이션 트리거 리셋
        Core.Animator.ResetTrigger(AnimationTrigger);

        base.Exit();
    }

    private void HandlePerfectDodgeEnd()
    {
        if (Core.Dodge?.Configured == true) { Core.Dodge.AnimationPerfectEnd(); return; }
        if (!IsPerfectDodge || IsPerfectDodgeEnd)
            return;
        IsPerfectDodgeEnd = true;
        Core.EndPerfectDodge();
    }
}
