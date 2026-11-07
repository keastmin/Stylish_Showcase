using UnityEngine;
using UnityEngine.Playables;

public class PlayerSkillState : PlayerStateBase
{
    public override bool IsInvulnerable => Runtime?.IsActive == true ? Runtime.ActiveDefinition.invulnerable : true;
    public PlayerSkillRuntime Runtime { get; private set; }
    private bool _dataMode;
    private bool _blendOnExit;
    private PlayableDirector _director;
    private PlayerSkillCameraDirector _cameraDirector;
    private bool _directorStopped;
    private bool _recovering;
    private bool _completionMotionPending;
    private double _contentEndTime;
    private float _recoveryRemaining;

    public PlayerSkillState(PlayerCore core) : base(core)
    {
        _director = core.DirectorContainer.Directors[DirectorID.Skill];
        _cameraDirector = _director.GetComponent<PlayerSkillCameraDirector>();
        Runtime = new PlayerSkillRuntime(core, _director);
    }

    public override void Enter()
    {
        _directorStopped = false;
        _blendOnExit = false;
        _recovering = false;
        _completionMotionPending = false;
        var settings = _director.GetComponent<PlayerSkillSequenceSettings>();
        _dataMode = settings != null && settings.Definition != null;
        _contentEndTime = settings != null ? settings.GetContentEndTime(_director) : PlayerSkillSequenceSettings.TimelineDuration(_director);
        _recoveryRemaining = settings != null ? settings.RecoveryDuration : 0f;
        if (!_dataMode) _director.stopped += OnDirectorStopped;

        // 스킬 사용
        Core.UseSkillGauge();

        // 회전
        Collider target = Core.TargetDetector.AcquireBasicAttackTarget();
        Vector3 targetDirection = target != null
            ? Vector3.ProjectOnPlane(target.transform.position - Core.transform.position, Vector3.up)
            : Core.InputCollector.IsInputMove && Core.MainCamera != null
                ? Core.DirCalculator.GetTargetDirection(Core.InputCollector.MoveValue, Core.MainCamera.transform)
                : Core.Rotator.FacingDirection;
        Core.Rotator.RotateImmediately(targetDirection);

        // The model's rotation is the dash direction; the player root stays unrotated.
        base.Enter();
        if (_dataMode) Runtime.Begin(settings.Definition);
        else
        {
            _cameraDirector?.CaptureSkillStart(Core.transform.position, Core.Rotator.FacingRotation);
            Core.DirectorContainer.Play(DirectorID.Skill);
        }
    }

    public override void UpdateTick()
    {
        if (_dataMode)
        {
            if (!Runtime.IsActive) { Core.StateMachine.Transition(Core.StateMachine.IdleState); return; }
            Runtime.Tick(CombatTimeController.DeltaTime);
            bool complete = Runtime.IsComplete;
            if (!complete && !Runtime.CanCancelWithInput) return;
            var input = Core.InputCollector; var states = Core.StateMachine;
            PlayerStateBase next = null;
            if (input.IsInputDodge && Core.IsDodgeAvailable)
                next = input.IsInputMove ? states.FrontDodgeState : states.BackDodgeState;
            else if (input.IsInputAttack) next = states.BasicAttack1State;
            else if (input.IsInputMove) next = states.RunStartState;
            else if (complete) next = states.IdleState;
            if (next != null)
            {
                _blendOnExit = true;
                if (!states.Transition(next)) _blendOnExit = false;
            }
            return;
        }
        // A hitstop pauses the director; only its actual end or Stop event finishes
        // the sequence. Cache authored timing at cast start so a live edit affects
        // the next cast rather than unexpectedly cancelling the current one.
        if (!_recovering && (_directorStopped || _director.time >= _contentEndTime))
        {
            // The Timeline runs between gameplay ticks. Keep its final root delta
            // until the motor has consumed it; exiting here used to clear that
            // delta whenever rendering reached the endpoint before physics did.
            double overshoot = System.Math.Max(0d, _director.time - _contentEndTime);
            _recovering = true;
            AnimDeltaPos += Core.Mover.ConsumePendingDisplacement();
            _completionMotionPending = AnimDeltaPos.sqrMagnitude > 0f;
            _recoveryRemaining -= (float)overshoot;
            if (!_directorStopped) _director.Stop();
            FinishIfReady();
            return;
        }
        if (!_recovering) return;
        _recoveryRemaining -= CombatTimeController.DeltaTime;
        FinishIfReady();
    }

    public override void FixedTick()
    {
        if (_dataMode) { Runtime.FixedTick(); return; }
        Core.Mover.Move(AnimDeltaPos / Time.fixedDeltaTime);
        AnimDeltaPos = Vector3.zero;
        // PlayerMover runs after the FSM in this same physics tick. A subsequent
        // Update may now leave the state without discarding the final command.
        if (_recovering) _completionMotionPending = false;
    }

    public override void AnimatorTick()
    {
        if (_dataMode) return;
        // Stop releases the Timeline's animator binding. Recovery must not consume
        // root motion from whichever controller animation becomes active next.
        if (!_recovering) AnimDeltaPos += Core.Animator.deltaPosition;
    }

    public override void OnHitStopStarted()
    {
        if (_dataMode) { Runtime.PreservePendingMotion(); return; }
        // Hit notifications arrive between animation and physics updates. Keep
        // this frame's root motion, plus any movement awaiting a physics tick,
        // so repeated skill hits pause the dash instead of deleting its distance.
        AnimDeltaPos += Core.Mover.ConsumePendingDisplacement();
        if (_recovering && AnimDeltaPos.sqrMagnitude > 0f) _completionMotionPending = true;
    }

    public override void Exit()
    {
        // Capture before Stop releases the Timeline and restores the controller.
        // The accepted destination still starts immediately in this same tick.
        if (_blendOnExit && Runtime.OwnsModelAnimation && Runtime.ActiveDefinition != null)
            PlayerUltimateExitBlend.Begin(Core.Animator, Runtime.ActiveDefinition.ExitBlendDuration, Core);
        _blendOnExit = false;
        Runtime.Stop();
        _dataMode = false;
        _director.stopped -= OnDirectorStopped;
        _directorStopped = false;
        _recovering = false;
        _completionMotionPending = false;

        // 타임라인 초기화
        // Paused timelines also own temporary model-fade materials.
        _director.Stop();

        base.Exit();
    }

    // PlayerCore calls this when only its component is disabled (the FSM has no Exit then).
    public void StopRuntime()
    {
        Runtime.Stop();
        if (_director != null) _director.Stop();
    }

    private void OnDirectorStopped(PlayableDirector director) => _directorStopped = true;

    private void FinishIfReady()
    {
        if (!_completionMotionPending && _recoveryRemaining <= 0f)
            Core.StateMachine.Transition(Core.StateMachine.IdleState);
    }
}
