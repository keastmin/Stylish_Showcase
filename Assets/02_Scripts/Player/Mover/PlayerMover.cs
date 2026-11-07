using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
[DefaultExecutionOrder(100)] // Consume the FSM's command in the same physics tick.
public class PlayerMover : MonoBehaviour
{
    private const float GroundProbeStartHeight = 1f;
    private const float GroundProbeRadius = 0.1f;
    private const float MaxHoverSpeed = 10f;

    [Header("Collider")]
    [SerializeField] private float _height = 2f;
    [SerializeField] private float _thickness = 1f;
    [SerializeField] private Vector3 _offset = Vector3.zero;
    [SerializeField] private float _stepHeight = 0.3f;

    [Header("Ground Hover")]
    [SerializeField] private LayerMask _groundLayerMask = 1 << 7;
    [SerializeField][Min(0f)] private float _groundProbeExtraDistance = 2f;
    [SerializeField][Min(0f)] private float _groundHoverSharpness = 20f;

    private Rigidbody _rigidbody;
    private CapsuleCollider _capsuleCollider;

    private Vector3 _inputVelocity = Vector3.zero;
    private bool _isHitStopped;
    private RigidbodyConstraints _constraintsBeforeHitStop;
    private RigidbodyInterpolation _interpolationBeforeHitStop;
    private bool _rootMotionHeight;
    private bool _settlingFromRootMotion;
    private float _landingVelocity;
    public Vector3 PhysicsPosition => _rigidbody != null ? _rigidbody.position : transform.position;
    public bool UsesRootMotionHeight => _rootMotionHeight;

    #region MonoBehaviour

    private void OnValidate()
    {
        InitComponents();
        SetColliderDimention();
    }

    private void Awake()
    {
        OnValidate();
    }

    private void FixedUpdate()
    {
        if (_isHitStopped)
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _inputVelocity = Vector3.zero;
            return;
        }

        ApplyVelocity(_rootMotionHeight ? ClampRootDescent(_inputVelocity) :
            _settlingFromRootMotion ? ApplyLanding(_inputVelocity) : ApplyGroundHover(_inputVelocity));
        UpdateCleanup();
    }

    private void OnDisable()
    {
        EndRootMotionHeight();
        SetHitStopped(false);
        // If disabled in the air, continue settling when enabled again.
    }

    #endregion

    #region API

    public void Move(Vector3 velocity)
    {
        if (_isHitStopped)
            return;

        _inputVelocity = velocity;
    }

    public void BeginRootMotionHeight()
    {
        _rootMotionHeight = true;
        _settlingFromRootMotion = false;
        _landingVelocity = 0f;
    }

    public void EndRootMotionHeight()
    {
        if (!_rootMotionHeight) return;
        _rootMotionHeight = false;
        _settlingFromRootMotion = true;
        _landingVelocity = _rigidbody != null ? Mathf.Min(0f, _rigidbody.linearVelocity.y) : 0f;
    }

    // Transfer a command that has not reached the next physics tick yet.
    public Vector3 ConsumePendingDisplacement()
    {
        Vector3 displacement = _inputVelocity * Time.fixedDeltaTime;
        _inputVelocity = Vector3.zero;
        return displacement;
    }

    public void SetHitStopped(bool stopped)
    {
        if (_isHitStopped == stopped)
            return;

        // Constraints can sync the interpolated render Transform back into
        // PhysX. Preserve the completed physics step instead of rewinding it.
        Vector3 position = _rigidbody.position;
        Quaternion rotation = _rigidbody.rotation;
        _isHitStopped = stopped;
        _inputVelocity = Vector3.zero;
        _rigidbody.linearVelocity = Vector3.zero;

        if (stopped)
        {
            _constraintsBeforeHitStop = _rigidbody.constraints;
            _interpolationBeforeHitStop = _rigidbody.interpolation;
            _rigidbody.interpolation = RigidbodyInterpolation.None;
            _rigidbody.constraints = RigidbodyConstraints.FreezeAll;
        }
        else
        {
            _rigidbody.constraints = _constraintsBeforeHitStop;
            _rigidbody.interpolation = _interpolationBeforeHitStop;
        }
        _rigidbody.position = position;
        _rigidbody.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
    }

    #endregion

    #region Core

    private void InitComponents()
    {
        TryGetComponent(out _rigidbody);
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rigidbody.useGravity = false;
        _rigidbody.freezeRotation = true;

        TryGetComponent(out _capsuleCollider);
    }

    private void SetColliderDimention()
    {
        SetHeight();
        SetThickness();
    }

    private void SetHeight()
    {
        if (_stepHeight > _height) _stepHeight = _height;
        Vector3 center = _offset + new Vector3(0f, _height / 2f, 0f);
        center.y += _stepHeight / 2f;
        _capsuleCollider.center = center;
        _capsuleCollider.height = _height - _stepHeight;
        LimitRadius();
    }

    private void SetThickness()
    {
        float radius = _thickness / 2f;
        _capsuleCollider.radius = radius;
        LimitRadius();
    }

    private void LimitRadius()
    {
        if (_capsuleCollider.radius * 2f > _capsuleCollider.height) 
            _capsuleCollider.radius = _capsuleCollider.height / 2f;
    }

    private void ApplyVelocity(Vector3 velocity)
    {
        _rigidbody.linearVelocity = velocity;
    }

    private Vector3 ApplyGroundHover(Vector3 velocity)
    {
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        Vector3 probeOrigin = _rigidbody.position + Vector3.up * GroundProbeStartHeight;
        float probeDistance = GroundProbeStartHeight + _groundProbeExtraDistance;

        if (!Physics.SphereCast(probeOrigin, GroundProbeRadius, Vector3.down,
                out RaycastHit groundHit, probeDistance, _groundLayerMask, QueryTriggerInteraction.Ignore) ||
            groundHit.normal.y <= 0f)
        {
            return horizontalVelocity;
        }

        float heightError = Vector3.Dot(groundHit.point - _rigidbody.position, Vector3.up);
        float hoverSpeed = Mathf.Clamp(heightError * _groundHoverSharpness, -MaxHoverSpeed, MaxHoverSpeed);
        return horizontalVelocity + Vector3.up * hoverSpeed;
    }

    private Vector3 ClampRootDescent(Vector3 velocity)
    {
        if (velocity.y >= 0f) return velocity;
        float step = Mathf.Max(0.0001f, Time.fixedDeltaTime);
        if (TryGetLandingGround(Mathf.Max(_groundProbeExtraDistance, -velocity.y * step + 0.1f), out RaycastHit hit))
            velocity.y = Mathf.Max(velocity.y, (hit.point.y - _rigidbody.position.y) / step);
        return velocity;
    }

    private Vector3 ApplyLanding(Vector3 velocity)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up);
        float step = Mathf.Max(0.0001f, Time.fixedDeltaTime);
        _landingVelocity = Mathf.Max(-MaxHoverSpeed, _landingVelocity + Physics.gravity.y * step);
        if (TryGetLandingGround(Mathf.Max(_groundProbeExtraDistance, -_landingVelocity * step + 0.1f), out RaycastHit hit))
        {
            float height = _rigidbody.position.y - hit.point.y;
            if (height <= 0.04f)
            {
                _settlingFromRootMotion = false;
                _landingVelocity = 0f;
                return ApplyGroundHover(horizontal);
            }
            _landingVelocity = Mathf.Max(_landingVelocity, -height / step);
        }
        return horizontal + Vector3.up * _landingVelocity;
    }

    private bool TryGetLandingGround(float distance, out RaycastHit hit)
        => Physics.SphereCast(_rigidbody.position + Vector3.up * GroundProbeStartHeight, GroundProbeRadius,
            Vector3.down, out hit, GroundProbeStartHeight + distance, _groundLayerMask, QueryTriggerInteraction.Ignore) && hit.normal.y > 0f;

    private void UpdateCleanup()
    {
        _inputVelocity = Vector3.zero;
    }

    #endregion
}
