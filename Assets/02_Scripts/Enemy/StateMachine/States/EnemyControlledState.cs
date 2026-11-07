using System;
using UnityEngine;

public sealed class EnemyControlledState : EnemyStateBase
{
    private static readonly int Front = Animator.StringToHash("Base Layer.Hit.Hit Front");
    private static readonly int Back = Animator.StringToHash("Base Layer.Hit.Hit Back");

    private object _cast;
    private UltimatePull _pull;
    private Func<Vector3> _follow;
    private Vector3 _center, _start;
    private float _remaining, _elapsed, _speed;
    private bool _ending;

    public bool Active => _pull != null;

    public EnemyControlledState(EnemyCore core) : base(core) { }

    public bool Apply(object cast, UltimatePull pull, Vector3 center, Func<Vector3> follow, DamageData hit)
    {
        if (Active && ReferenceEquals(_cast, cast) && _pull == pull)
        {
            _remaining = Mathf.Max(_remaining, Mathf.Max(.01f, pull.duration));
            if (pull.anchor == UltimatePullAnchor.EachMarker && center != _center)
            {
                _start = Core.Mover.PhysicsPosition;
                _elapsed = 0f;
                _center = center;
            }
            _ending = false;
            return true;
        }
        if (Active && pull.priority < _pull.priority) return false;
        if (!Core.ShouldEnterHitReaction(new DamageData(hit.Sender, hit.DamageAmount, 0, pull.strength))) return false;

        bool wasActive = Active;
        _cast = cast;
        _pull = pull;
        _center = center;
        _follow = follow;
        _start = Core.Mover.PhysicsPosition;
        _elapsed = _speed = 0f;
        _remaining = Mathf.Max(.01f, pull.duration);
        _ending = false;
        if (!wasActive)
        {
            Core.StateMachine.Transition(this);
            var direction = HitDirectionCalculator.GetHitDirection(hit, Core.transform.position, Core.Rotator.FacingDirection);
            Core.PlayHitReaction(direction == HitDirectionType.Back ? Back : Front);
        }
        return true;
    }

    public override void Enter()
    {
        Core.EndAttackTargeting();
        Core.ReleaseAttackPermission();
        Core.Mover.Move(Vector3.zero);
    }

    public override void FixedTick()
    {
        if (Core.IsDead) return;
        if (_ending)
        {
            Release();
            return;
        }
        float dt = Mathf.Min(_remaining, Time.fixedDeltaTime * CombatTimeController.Scale);
        _remaining -= dt;
        _elapsed += dt;
        Vector3 movement = _pull.Step(Core.Mover.PhysicsPosition, _follow?.Invoke() ?? _center, _start, _elapsed, dt, ref _speed);
        Core.Mover.Move(movement / Time.fixedDeltaTime);
        _ending = _remaining <= 0f;
    }

    public void Release(object cast, UltimatePull pull)
    {
        if (Active && ReferenceEquals(_cast, cast) && (pull == null || pull == _pull)) Release();
    }

    public void Release()
    {
        if (Active) Core.StateMachine.Transition(Core.IsDead ? Core.StateMachine.DeadState : Core.StateMachine.IdleState);
    }

    public override void Exit()
    {
        _cast = null;
        _pull = null;
        _follow = null;
        Core.Mover.Move(Vector3.zero);
    }
}
