using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(100)]
public class EnemyMover : MonoBehaviour
{
    private const float GroundProbeStartHeight = 1f;
    private const float GroundProbeRadius = 0.1f;
    private const float MaxHoverSpeed = 10f;

    [Header("Ground Hover")]
    [SerializeField] private LayerMask _groundLayerMask = 1 << 7;
    [SerializeField][Min(0f)] private float _groundProbeExtraDistance = 2f;
    [SerializeField][Min(0f)] private float _groundHoverSharpness = 20f;

    private Rigidbody _rigidbody;
    public Vector3 PhysicsPosition => _rigidbody.position;

    private Vector3 _inputVelocity = Vector3.zero;
    private bool _isHitStopped;
    private RigidbodyConstraints _constraintsBeforeHitStop;
    private RigidbodyInterpolation _interpolationBeforeHitStop;

    private void Awake()
    {
        TryGetComponent(out _rigidbody);
        _rigidbody.freezeRotation = true;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rigidbody.useGravity = false;
    }

    private void FixedUpdate()
    {
        if (_isHitStopped)
        {
            _rigidbody.linearVelocity = Vector3.zero;
            _inputVelocity = Vector3.zero;
            return;
        }

        _rigidbody.linearVelocity = ApplyGroundHover(_inputVelocity);
        _inputVelocity = Vector3.zero;
    }

    private void OnDisable()
    {
        SetHitStopped(false);
    }

    public void Move(Vector3 velocity)
    {
        if (_isHitStopped)
            return;

        _inputVelocity = velocity;
    }

    public void WarpTo(Vector3 worldPosition)
    {
        if (_isHitStopped)
            return;

        _inputVelocity = Vector3.zero;
        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.position = worldPosition;
        transform.position = worldPosition;
    }

    public void SetHitStopped(bool stopped)
    {
        if (_isHitStopped == stopped)
            return;

        // Freeze the physics pose, not the interpolated render pose.
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
}
