using UnityEngine;

public abstract class EnemyAttackNoticeState : EnemyStateBase
{
    public EnemyAttackNoticeState(EnemyCore core) : base(core)
    {

    }

    public override void Enter()
    {
        Core.OnDamaged += SetDamaged;

        // 공격 알림 VFX 재생
        Core.AnimationEvent.AttackNoticeVFX();

        // 공격 알림 SFX 재생
        Core.AttackNoticeSFX();

        // 애니메이션 재생
        Core.Animator.SetTrigger("IsIdle");
    }

    public override void Exit()
    {
        Core.OnDamaged -= SetDamaged;

        // 애니메이션 초기화
        Core.Animator.ResetTrigger("IsIdle");
    }

    private void SetDamaged(DamageData data)
    {
        if (!Core.ShouldEnterHitReaction(data))
            return;

        Core.EndAttackTargeting();
        Core.ReleaseAttackPermission();

        HitDirectionType type = HitDirectionCalculator.GetHitDirection(
            data,
            Core.transform.position,
            Core.Rotator.FacingDirection);

        if (type == HitDirectionType.Front)
            Core.StateMachine.Transition(Core.StateMachine.FrontHitState);
        else if (type == HitDirectionType.Back)
            Core.StateMachine.Transition(Core.StateMachine.BackHitState);
    }
}
