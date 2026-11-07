using UnityEngine;
using UnityEngine.Playables;

public abstract class PlayerDodgeAttackState : PlayerStateBase
{
    private const float HurtboxClearance = 0.85f;
    private Collider _targetHurtbox;

    public override bool IsInvulnerable => Core.Dodge?.Configured == true ? Core.Dodge.Invulnerable : true;
    protected abstract DirectorID AttackDirectorID { get; }
    protected abstract string AnimationTrigger { get; }
    protected abstract PlayerStateBase NextState{ get; }
    private PlayableDirector _director;
    private bool _isAnimationEnd = false;

    public PlayerDodgeAttackState(PlayerCore core) : base(core) 
    {
        _director = core.DirectorContainer.Directors[AttackDirectorID];
    }

    public override void Enter()
    {
        base.Enter();
        if (Core.Dodge?.Configured == true)
            Core.Dodge.EnterCounterPhase(this is PlayerDodgeAttackStartState ? DodgePhase.CounterStart :
                this is PlayerDodgeAttackLoopState ? DodgePhase.CounterLoop : DodgePhase.CounterEnd);

        // 초기화
        _isAnimationEnd = false;
        EnemyCore target = Core.DodgeAttackTarget;
        _targetHurtbox = FindHurtbox(target);
        if (TryGetTargetOffset(out Vector3 offset, out _))
            Core.Rotator.RotateImmediately(offset);

        // 이벤트 연결
        Core.AnimationEvent.OnAnimationEnd += HandleAnimationEnd;

        // 애니메이션 재생
        Core.Animator.SetTrigger(AnimationTrigger);

        // 타임라인 재생
        Core.DirectorContainer.Play(AttackDirectorID);
    }

    public override void UpdateTick()
    {
        if (IsDamaged)
            return;
        if (_isAnimationEnd)
        {
            Core.StateMachine.Transition(NextState);
            return;
        }
    }

    public override void FixedTick()
    {
        if (IsDamaged)
            return;
        if (!TryGetTargetOffset(out Vector3 offset, out float clearance))
        {
            MoveRootMotionAlongFacingDirection();
            return;
        }

        float distance = offset.magnitude;
        float forwardDelta = Vector3.Dot(AnimDeltaPos, Core.Rotator.FacingDirection);
        AnimDeltaPos = Vector3.zero;
        if (distance <= 0.001f)
        {
            Core.Mover.Move(Vector3.zero);
            return;
        }

        Vector3 direction = offset / distance;
        Core.Rotator.RotateImmediately(direction);
        float remaining = Mathf.Max(0f, distance - clearance);
        float displacement = Mathf.Min(WarpForwardDelta(forwardDelta, remaining), remaining);
        Core.Mover.Move(direction * (displacement / Time.fixedDeltaTime));
    }

    protected virtual float WarpForwardDelta(float forwardDelta, float remaining) => forwardDelta;

    private bool TryGetTargetOffset(out Vector3 offset, out float clearance)
    {
        offset = Vector3.zero;
        clearance = HurtboxClearance;
        EnemyCore target = Core.DodgeAttackTarget;
        if (target == null || !target.isActiveAndEnabled || target.IsDead)
            return false;

        Vector3 position = Core.Mover.PhysicsPosition;
        if (_targetHurtbox != null && _targetHurtbox.enabled && _targetHurtbox.gameObject.activeInHierarchy)
        {
            Vector3 center = _targetHurtbox.bounds.center;
            Vector3 nearSide = _targetHurtbox.ClosestPoint(new Vector3(position.x, center.y, position.z));
            offset = Vector3.ProjectOnPlane(center - position, Vector3.up);
            clearance += Vector3.Dot(Vector3.ProjectOnPlane(center - nearSide, Vector3.up), offset.normalized);
        }
        else
        {
            offset = Vector3.ProjectOnPlane(target.Mover.PhysicsPosition - position, Vector3.up);
        }

        return true;
    }

    private static Collider FindHurtbox(EnemyCore target)
    {
        if (target == null || !target.isActiveAndEnabled || target.IsDead)
            return null;

        int hurtboxLayer = LayerMask.NameToLayer("Enemy Hurtbox");
        foreach (Collider collider in target.GetComponentsInChildren<Collider>())
        {
            if (collider.enabled && collider.gameObject.layer == hurtboxLayer)
                return collider;
        }

        return null;
    }

    public override void AnimatorTick()
    {
        if (IsDamaged)
            return;
        AnimDeltaPos += Core.Animator.deltaPosition;
    }

    public override void Exit()
    {
        _targetHurtbox = null;
        Core.AttackInstanceContainer.ClearDamagedTargets();
        // 초기화
        _isAnimationEnd = false;

        // 이벤트 해제
        Core.AnimationEvent.OnAnimationEnd -= HandleAnimationEnd;

        // 애니메이션 초기화
        Core.Animator.ResetTrigger(AnimationTrigger);

        // 타임라인 초기 중단
        if (_director.state == PlayState.Playing)
            _director.Stop();

        base.Exit();
    }

    private void HandleAnimationEnd()
    {
        _isAnimationEnd = true;
    }

}
