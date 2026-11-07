using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(PlayableDirector))]
public class PlayerSkillCameraDirector : MonoBehaviour
{
    public enum RecenterModes { Instant, Smooth }

    [Header("Scene Cameras")]
    [SerializeField] private CinemachineCamera _skillCinemachine;
    [SerializeField] private CinemachineOrbitalFollow _playerTPSOrbitFollow;
    [SerializeField] private CinemachineBrain _mainCameraBrain;
    [SerializeField] private Transform _player;
    [Tooltip("Model whose current forward direction defines the TPS return heading. TPS orbit uses World Space.")]
    [SerializeField] private Transform _facingTransform;

    [Header("Placement")]
    [SerializeField] private Vector3 _offsetPosition = new(0f, 5f, -8f);
    [SerializeField] private float _xRotOffset = 16f;
    [Tooltip("Fixed start pose used when scrubbing in Edit mode.")]
    [SerializeField] private Transform _previewSkillOrigin;

    [Header("Sequence Camera Motion")]
    [SerializeField, Tooltip("Timeline 시각별 위치·회전·화각 키를 사용합니다. 끄면 기존 Placement를 사용합니다.")]
    private bool _motionEnabled;
    [SerializeField, Tooltip("켜면 시전 시작 방향을 유지하면서 플레이어 이동량을 따라갑니다.")]
    private bool _followPlayerMovement = true;
    [SerializeField] private List<PlayerSkillCameraKey> _motionKeys = new();

    [Header("Timeline Signals")]
    [SerializeField] private SignalAsset _positionSignal;
    [SerializeField] private SignalAsset _recenterSignal;
    [SerializeField] private TimelineActivationTrack _cameraActivationTrack;

    [Header("TPS Return")]
    [SerializeField] private RecenterModes _recenterMode = RecenterModes.Instant;
    [SerializeField, Min(0.001f)] private float _recenterDuration = 0.15f;

    private PlayableDirector _director;
    private Vector3 _startPosition;
    private Vector3 _startPlayerPosition;
    private Quaternion _startRotation;
    private bool _hasStart;
    private bool _following;
    private int _sessionVersion;
    private double _positionedAt = double.NaN;
    private double _recenteredAt = double.NaN;
    private float _recenterStartX, _recenterStartY, _recenterGoalX;
    private bool _recenterComplete;
    private CinemachineInputAxisController _input;
    private bool _inputWasEnabled, _inputLocked;
    private PreviewSnapshot _preview;
    private bool _previewSuspended;
    private int _previewOverride = -1;
    private CameraPoseSnapshot _runtimeCameraPose;
    private static readonly HashSet<PlayerSkillCameraDirector> Previews = new();

    public CinemachineCamera SkillCamera => _skillCinemachine;
    public CinemachineOrbitalFollow TPSOrbit => _playerTPSOrbitFollow;
    public Transform PreviewOrigin => _previewSkillOrigin;
    public SignalAsset PositionSignal => _positionSignal;
    public SignalAsset RecenterSignal => _recenterSignal;
    public TimelineActivationTrack ActivationTrack => _cameraActivationTrack;
    public int SessionVersion => _sessionVersion;
    public RecenterModes RecenterMode { get => _recenterMode; set => _recenterMode = value; }
    public float RecenterDuration { get => _recenterDuration; set => _recenterDuration = Mathf.Max(0.001f, value); }
    public bool MotionEnabled { get => _motionEnabled; set => _motionEnabled = value; }
    public bool FollowPlayerMovement { get => _followPlayerMovement; set => _followPlayerMovement = value; }
    public List<PlayerSkillCameraKey> MotionKeys => _motionKeys ??= new List<PlayerSkillCameraKey>();
    private CinemachineCamera TPSCamera => _playerTPSOrbitFollow != null
        ? _playerTPSOrbitFollow.GetComponent<CinemachineCamera>() : null;
    private Vector3 WorldUp => _mainCameraBrain != null ? _mainCameraBrain.DefaultWorldUp : Vector3.up;

    private void OnEnable()
    {
        _director = GetComponent<PlayableDirector>();
        _director.stopped += OnStopped;
    }
    private void OnDisable()
    {
        if (_director != null)
            _director.stopped -= OnStopped;
        EndTimelineSession();
    }
    private void OnDestroy() => EndTimelineSession();
    private void OnStopped(PlayableDirector director) => EndTimelineSession();

    public void Configure(CinemachineCamera skill, CinemachineOrbitalFollow tps, CinemachineBrain brain,
        Transform player, Transform facing, Transform previewOrigin, SignalAsset positionSignal,
        SignalAsset recenterSignal, TimelineActivationTrack activationTrack)
    {
        EndTimelineSession();
        _skillCinemachine = skill;
        _playerTPSOrbitFollow = tps;
        _mainCameraBrain = brain;
        _player = player;
        _facingTransform = facing;
        _previewSkillOrigin = previewOrigin;
        _positionSignal = positionSignal;
        _recenterSignal = recenterSignal;
        _cameraActivationTrack = activationTrack;
    }

    // Capture the model's dash direction before Director.Play.
    public void CaptureSkillStart(Vector3 position, Quaternion rotation)
    {
        if (Application.isPlaying && _skillCinemachine != null)
        {
            _runtimeCameraPose?.Restore();
            _runtimeCameraPose = new CameraPoseSnapshot(_skillCinemachine);
        }
        ++_sessionVersion;
        _startPosition = position;
        _startPlayerPosition = _player != null ? _player.position : position;
        _startRotation = HorizontalRotation(rotation);
        _hasStart = true;
        _following = false;
        _positionedAt = _recenteredAt = double.NaN;
        _recenterComplete = false;
    }

    // Parameterless entry point for the standard SignalReceiver.
    public void PositionSkillCamera()
    {
        if (!Application.isPlaying)
        {
            BeginPreview();
            if (_previewSuspended)
                return;
            Transform origin = _previewSkillOrigin != null ? _previewSkillOrigin : _player;
            if (origin != null)
            {
                SkillCameraPositioning(origin.position, origin.rotation);
                _following = true;
            }
            return;
        }
        if (!_hasStart && _player != null)
            CaptureSkillStart(_player.position, _facingTransform != null ? _facingTransform.rotation : _player.rotation);
        if (_hasStart)
        {
            SkillCameraPositioning(_startPosition, _startRotation);
            _following = true;
        }
    }

    public void SkillCameraPositioning(Vector3 playerPos, Quaternion playerRot)
        => PositionCamera(playerPos, playerRot, _director != null ? _director.time : 0d);

    private void PositionCamera(Vector3 playerPos, Quaternion playerRot, double time)
    {
        if (_skillCinemachine == null)
            return;
        Quaternion facing = HorizontalRotation(playerRot);
        PlayerSkillCameraPose pose = EvaluateMotion(time);
        _skillCinemachine.transform.SetPositionAndRotation(playerPos + facing * pose.offset, facing * pose.rotation);
        var lens = _skillCinemachine.Lens;
        lens.FieldOfView = pose.fieldOfView;
        _skillCinemachine.Lens = lens;
        RefreshCamera(_skillCinemachine);
    }

    /// <summary>Absolute Timeline seconds; evaluation is independent of playback direction and frame rate.</summary>
    public PlayerSkillCameraPose EvaluateMotion(double time)
    {
        var fallback = new PlayerSkillCameraPose
        {
            offset = _offsetPosition,
            rotation = Quaternion.Euler(_xRotOffset, 0f, 0f),
            fieldOfView = _preview != null ? _preview.SkillFieldOfView : _runtimeCameraPose != null
                ? _runtimeCameraPose.FieldOfView : _skillCinemachine != null ? _skillCinemachine.Lens.FieldOfView : 60f
        };
        if (!_motionEnabled || _motionKeys == null || double.IsNaN(time)) return fallback;
        PlayerSkillCameraKey previous = null, next = null;
        foreach (PlayerSkillCameraKey key in _motionKeys)
        {
            if (key == null || float.IsNaN(key.time) || float.IsInfinity(key.time)) continue;
            // Unsorted authoring is valid. Equal-time keys consistently use the last list entry.
            if (key.time <= time && (previous == null || key.time >= previous.time)) previous = key;
            else if (key.time > time && (next == null || key.time <= next.time)) next = key;
        }
        if (previous == null) return next != null ? next.Pose : fallback;
        if (next == null) return previous.Pose;
        float amount = Mathf.Clamp01((float)((time - previous.time) / (next.time - previous.time)));
        if (previous.interpolation == PlayerSkillCameraInterpolation.SmoothStep) amount = amount * amount * (3f - 2f * amount);
        PlayerSkillCameraPose a = previous.Pose, b = next.Pose;
        return new PlayerSkillCameraPose
        {
            offset = Vector3.LerpUnclamped(a.offset, b.offset, amount),
            rotation = Quaternion.SlerpUnclamped(a.rotation, b.rotation, amount),
            fieldOfView = Mathf.LerpUnclamped(a.fieldOfView, b.fieldOfView, amount)
        };
    }

    public void PlayerTPSCameraRecenter()
    {
        if (!Application.isPlaying)
            BeginPreview(); // The signal track samples the complete preview state.
        else
        {
            double time = _director != null ? _director.time : 0d;
            if (_director != null && _director.playableAsset is TimelineAsset timeline)
                foreach (var track in timeline.GetOutputTracks())
                    if (track is SkillCameraSignalTrack signals)
                    {
                        double markerTime = signals.LatestSignalTime(_recenterSignal, time);
                        if (!double.IsNaN(markerTime))
                            time = markerTime;
                    }
            if (_recenteredAt != time)
                StartRecentering(time);
        }
    }

    // For non-Timeline callers. The activation clip owns this during a skill.
    public void SkillCameraActivation(bool active)
    {
        if (_skillCinemachine != null)
            _skillCinemachine.gameObject.SetActive(active);
    }

    public void EvaluateTimeline(SkillCameraSignalTrack track, double time)
    {
        if (!isActiveAndEnabled || _director == null || !_director.isActiveAndEnabled)
        {
            EndTimelineSession();
            return;
        }
        if (!Application.isPlaying)
        {
            BeginPreview();
            if (_previewSuspended || _preview == null)
                return;
        }
        double positionTime = track.LatestSignalTime(_positionSignal, time);
        double recenterTime = track.LatestSignalTime(_recenterSignal, time);
        if (!double.IsNaN(positionTime))
        {
            if (!Application.isPlaying || _positionedAt != positionTime)
                PositionSkillCamera();
            _positionedAt = positionTime;
        }
        else if (!Application.isPlaying)
        {
            _following = false;
            _preview.RestoreSkillPose();
        }

        UpdateCameraPose(time);

        if (!Application.isPlaying)
        {
            _preview.RestoreAxes();
            if (!double.IsNaN(recenterTime))
            {
                StartRecentering(recenterTime);
                ApplyRecentering(time);
            }
            else
                RefreshCamera(TPSCamera);
            RefreshPreviewOutput(time);
        }
        else
        {
            if (!double.IsNaN(recenterTime) && _recenteredAt != recenterTime)
                StartRecentering(recenterTime);
            if (!double.IsNaN(_recenteredAt))
                ApplyRecentering(time);
            if (_skillCinemachine != null && _skillCinemachine.gameObject.activeSelf)
                LockInput();
        }
    }

    private void LateUpdate()
    {
        if (_director == null || !_director.isActiveAndEnabled)
        {
            EndTimelineSession();
            return;
        }
        // CinemachineBrain runs at order 100, after this frame's player movement.
        UpdateCameraPose(_director.time);
        if (Application.isPlaying && _inputLocked &&
            (_skillCinemachine == null || !_skillCinemachine.gameObject.activeSelf) &&
            (_mainCameraBrain == null || (!_mainCameraBrain.IsBlending && _mainCameraBrain.ActiveVirtualCamera as Object == TPSCamera)))
            RestoreInput();
    }

    private void UpdateCameraPose(double time)
    {
        if (!_following || _player == null || _skillCinemachine == null || _previewSuspended)
            return;
        if (Application.isPlaying)
        {
            Vector3 displacement = _followPlayerMovement ? _player.position - _startPlayerPosition : Vector3.zero;
            PositionCamera(_startPosition + displacement, _startRotation, time);
            return;
        }
        if (_preview == null)
            return;
        Transform origin = _previewSkillOrigin != null ? _previewSkillOrigin : _player;
        Vector3 previewDisplacement = _followPlayerMovement ? _player.position - _preview.PlayerPosition : Vector3.zero;
        // When Player itself is the preview origin, its current position already includes the movement.
        Vector3 previewOrigin = origin == _player ? _preview.PlayerPosition + previewDisplacement : origin.position + previewDisplacement;
        PositionCamera(previewOrigin, origin.rotation, time);
    }

    private void StartRecentering(double time)
    {
        if (_playerTPSOrbitFollow == null || TPSCamera == null)
            return;
        if (Application.isPlaying)
            LockInput();
        _recenterStartX = _playerTPSOrbitFollow.HorizontalAxis.Value;
        _recenterStartY = _playerTPSOrbitFollow.VerticalAxis.Value;
        // In World Space, orbit yaw is measured from world forward. Capture the
        // model's current heading at the signal, independently of the Follow anchor.
        Transform facing = _facingTransform != null ? _facingTransform : _player;
        Vector3 forward = facing != null ? Vector3.ProjectOnPlane(facing.forward, WorldUp) : Vector3.zero;
        _recenterGoalX = forward.sqrMagnitude > 0.0001f
            ? _playerTPSOrbitFollow.HorizontalAxis.ClampValue(Vector3.SignedAngle(Vector3.forward, forward, WorldUp))
            : _recenterStartX;
        _recenteredAt = time;
        _recenterComplete = false;
        ApplyRecentering(time);
    }

    private void ApplyRecentering(double time)
    {
        if (_playerTPSOrbitFollow == null || double.IsNaN(_recenteredAt))
            return;
        if (Application.isPlaying && _recenterComplete)
            return;
        float amount = _recenterMode == RecenterModes.Instant ? 1f :
            Mathf.SmoothStep(0f, 1f, (float)(time - _recenteredAt) / Mathf.Max(0.001f, _recenterDuration));
        _playerTPSOrbitFollow.HorizontalAxis.Value = _playerTPSOrbitFollow.HorizontalAxis.ClampValue(
            Mathf.LerpAngle(_recenterStartX, _recenterGoalX, amount));
        _playerTPSOrbitFollow.VerticalAxis.Value = _playerTPSOrbitFollow.VerticalAxis.ClampValue(
            Mathf.Lerp(_recenterStartY, _playerTPSOrbitFollow.VerticalAxis.Center, amount));
        _playerTPSOrbitFollow.HorizontalAxis.CancelRecentering();
        _playerTPSOrbitFollow.VerticalAxis.CancelRecentering();
        bool enabledX = _playerTPSOrbitFollow.HorizontalAxis.Recentering.Enabled;
        bool enabledY = _playerTPSOrbitFollow.VerticalAxis.Recentering.Enabled;
        _playerTPSOrbitFollow.HorizontalAxis.Recentering.Enabled = false;
        _playerTPSOrbitFollow.VerticalAxis.Recentering.Enabled = false;
        RefreshCamera(TPSCamera);
        _playerTPSOrbitFollow.HorizontalAxis.Recentering.Enabled = enabledX;
        _playerTPSOrbitFollow.VerticalAxis.Recentering.Enabled = enabledY;
        _recenterComplete = amount >= 1f;
    }

    private void LockInput()
    {
        if (_inputLocked || _playerTPSOrbitFollow == null)
            return;
        _input = _playerTPSOrbitFollow.GetComponent<CinemachineInputAxisController>();
        if (_input == null)
            return;
        _inputWasEnabled = _input.enabled;
        _inputLocked = true;
        foreach (var controller in _input.Controllers)
        {
            if (controller.Owner != _playerTPSOrbitFollow)
                continue;
            if (controller.Name == "Look Orbit X")
                controller.Driver.CancelCurrentInput(ref _playerTPSOrbitFollow.HorizontalAxis);
            else if (controller.Name == "Look Orbit Y")
                controller.Driver.CancelCurrentInput(ref _playerTPSOrbitFollow.VerticalAxis);
        }
        _input.enabled = false;
    }
    private void RestoreInput()
    {
        if (_inputLocked && _input != null)
            _input.enabled = _inputWasEnabled;
        _inputLocked = false;
    }
    private void RefreshCamera(CinemachineCamera camera)
    {
        if (camera == null)
            return;
        camera.PreviousStateIsValid = false;
        camera.InternalUpdateCameraState(WorldUp, -1f);
    }
    private static Quaternion HorizontalRotation(Quaternion rotation)
    {
        Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
        return forward.sqrMagnitude > 0.000001f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity;
    }

    private void BeginPreview()
    {
        if (Application.isPlaying || _preview != null || _skillCinemachine == null)
            return;
        _preview = new PreviewSnapshot(this);
        Previews.Add(this);
    }
    public void EndTimelineSession()
    {
        RestoreInput();
        if (_mainCameraBrain != null && _previewOverride >= 0)
            _mainCameraBrain.ReleaseCameraOverride(_previewOverride);
        _previewOverride = -1;
        _preview?.Restore();
        _preview = null;
        _runtimeCameraPose?.Restore();
        _runtimeCameraPose = null;
        _previewSuspended = false;
        Previews.Remove(this);
        _positionedAt = _recenteredAt = double.NaN;
        _hasStart = false;
        _following = false;
    }

    public float PreviewSkillWeight(double time)
    {
        if (_cameraActivationTrack == null || _cameraActivationTrack.mutedInHierarchy)
            return 0f;
        float result = 0f;
        foreach (TimelineClip clip in _cameraActivationTrack.GetClips())
        {
            if (time < clip.start)
                continue;
            var blend = BlendDefinition(time < clip.end);
            float elapsed = (float)(time - (time < clip.end ? clip.start : clip.end));
            float amount = blend.Time > 0f && blend.BlendCurve != null
                ? blend.BlendCurve.Evaluate(Mathf.Clamp01(elapsed / blend.Time)) : 1f;
            result = Mathf.Max(result, time < clip.end ? amount : 1f - amount);
        }
        return Mathf.Clamp01(result);
    }
    private CinemachineBlendDefinition BlendDefinition(bool entering)
    {
        if (_mainCameraBrain == null || TPSCamera == null || _skillCinemachine == null)
            return new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        string from = entering ? TPSCamera.Name : _skillCinemachine.Name;
        string to = entering ? _skillCinemachine.Name : TPSCamera.Name;
        return _mainCameraBrain.CustomBlends != null
            ? _mainCameraBrain.CustomBlends.GetBlendForVirtualCameras(from, to, _mainCameraBrain.DefaultBlend)
            : _mainCameraBrain.DefaultBlend;
    }
    public void RefreshPreviewOutput(double time)
    {
        if (Application.isPlaying || _preview == null || _previewSuspended ||
            _mainCameraBrain == null || TPSCamera == null || _skillCinemachine == null)
            return;
        UpdateCameraPose(time);
        RefreshCamera(_skillCinemachine);
        _previewOverride = _mainCameraBrain.SetCameraOverride(_previewOverride, 100,
            TPSCamera, _skillCinemachine, PreviewSkillWeight(time), -1f);
        var update = _mainCameraBrain.UpdateMethod;
        _mainCameraBrain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
        try { _mainCameraBrain.ManualUpdate(); }
        finally { _mainCameraBrain.UpdateMethod = update; }
    }

    public void GatherPreviewProperties(IPropertyCollector collector)
    {
        if (_skillCinemachine != null)
        {
            collector.AddFromComponent(_skillCinemachine.gameObject, _skillCinemachine.transform);
            collector.AddFromComponent(_skillCinemachine.gameObject, _skillCinemachine);
        }
        if (_playerTPSOrbitFollow != null)
        {
            collector.AddFromComponent(_playerTPSOrbitFollow.gameObject, _playerTPSOrbitFollow);
            collector.AddFromComponent(_playerTPSOrbitFollow.gameObject, _playerTPSOrbitFollow.transform);
        }
        if (_mainCameraBrain != null && _mainCameraBrain.OutputCamera != null)
        {
            collector.AddFromComponent(_mainCameraBrain.gameObject, _mainCameraBrain.OutputCamera);
            collector.AddFromComponent(_mainCameraBrain.gameObject, _mainCameraBrain.transform);
        }
    }

    public static void RestoreAllPreviews()
    {
        foreach (var controller in new List<PlayerSkillCameraDirector>(Previews))
            if (controller != null)
                controller.EndTimelineSession();
        Previews.Clear();
    }
    public static void SuspendPreviewsForSave()
    {
        foreach (var controller in Previews)
        {
            if (controller == null)
                continue;
            controller._previewSuspended = true;
            if (controller._mainCameraBrain != null && controller._previewOverride >= 0)
                controller._mainCameraBrain.ReleaseCameraOverride(controller._previewOverride);
            controller._previewOverride = -1;
            controller._preview?.Restore();
        }
    }
    public static void ResumePreviewsAfterSave()
    {
        foreach (var controller in new List<PlayerSkillCameraDirector>(Previews))
        {
            if (controller == null || controller._director == null)
                continue;
            controller._previewSuspended = false;
            if (controller._director.playableAsset is TimelineAsset timeline)
                foreach (var track in timeline.GetOutputTracks())
                    if (track is SkillCameraSignalTrack signals &&
                        controller._director.GetGenericBinding(track) is SignalReceiver receiver &&
                        receiver.GetComponent<PlayerSkillCameraDirector>() == controller)
                        controller.EvaluateTimeline(signals, controller._director.time);
        }
    }
    public static void RefreshPreviewOutputs()
    {
        foreach (var controller in new List<PlayerSkillCameraDirector>(Previews))
            if (controller != null && controller._director != null)
                controller.RefreshPreviewOutput(controller._director.time);
    }

    private sealed class PreviewSnapshot
    {
        public readonly Vector3 PlayerPosition;
        private readonly PlayerSkillCameraDirector _owner;
        private readonly Vector3 _skillPosition, _tpsPosition, _mainPosition;
        private readonly Quaternion _skillRotation, _tpsRotation, _mainRotation;
        private readonly InputAxis _horizontal, _vertical, _radial;
        private readonly float _fieldOfView;
        private readonly LensSettings _skillLens;
        public float SkillFieldOfView => _skillLens.FieldOfView;
        public PreviewSnapshot(PlayerSkillCameraDirector owner)
        {
            _owner = owner;
            PlayerPosition = owner._player != null ? owner._player.position : Vector3.zero;
            _skillPosition = owner._skillCinemachine.transform.position;
            _skillRotation = owner._skillCinemachine.transform.rotation;
            _skillLens = owner._skillCinemachine.Lens;
            if (owner._playerTPSOrbitFollow != null)
            {
                _horizontal = owner._playerTPSOrbitFollow.HorizontalAxis;
                _vertical = owner._playerTPSOrbitFollow.VerticalAxis;
                _radial = owner._playerTPSOrbitFollow.RadialAxis;
                _tpsPosition = owner._playerTPSOrbitFollow.transform.position;
                _tpsRotation = owner._playerTPSOrbitFollow.transform.rotation;
            }
            if (owner._mainCameraBrain != null)
            {
                _mainPosition = owner._mainCameraBrain.transform.position;
                _mainRotation = owner._mainCameraBrain.transform.rotation;
                if (owner._mainCameraBrain.OutputCamera != null)
                    _fieldOfView = owner._mainCameraBrain.OutputCamera.fieldOfView;
            }
        }
        public void RestoreSkillPose()
        {
            if (_owner._skillCinemachine != null)
            {
                _owner._skillCinemachine.transform.SetPositionAndRotation(_skillPosition, _skillRotation);
                _owner._skillCinemachine.Lens = _skillLens;
            }
        }
        public void RestoreAxes()
        {
            if (_owner._playerTPSOrbitFollow == null)
                return;
            _owner._playerTPSOrbitFollow.HorizontalAxis = _horizontal;
            _owner._playerTPSOrbitFollow.VerticalAxis = _vertical;
            _owner._playerTPSOrbitFollow.RadialAxis = _radial;
        }
        public void Restore()
        {
            RestoreSkillPose();
            RestoreAxes();
            if (_owner._playerTPSOrbitFollow != null)
                _owner._playerTPSOrbitFollow.transform.SetPositionAndRotation(_tpsPosition, _tpsRotation);
            if (_owner._mainCameraBrain != null)
            {
                _owner._mainCameraBrain.transform.SetPositionAndRotation(_mainPosition, _mainRotation);
                if (_owner._mainCameraBrain.OutputCamera != null)
                    _owner._mainCameraBrain.OutputCamera.fieldOfView = _fieldOfView;
            }
        }
    }

    private sealed class CameraPoseSnapshot
    {
        private readonly CinemachineCamera _camera;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly LensSettings _lens;
        public float FieldOfView => _lens.FieldOfView;
        public CameraPoseSnapshot(CinemachineCamera camera)
        {
            _camera = camera;
            _position = camera.transform.position;
            _rotation = camera.transform.rotation;
            _lens = camera.Lens;
        }
        public void Restore()
        {
            if (_camera == null) return;
            _camera.transform.SetPositionAndRotation(_position, _rotation);
            _camera.Lens = _lens;
        }
    }
}

public enum PlayerSkillCameraInterpolation { Linear, SmoothStep }

[System.Serializable]
public sealed class PlayerSkillCameraKey
{
    [Min(0f), Tooltip("Skill Timeline 시작부터의 초입니다.")] public float time;
    [Tooltip("시전 방향 기준 카메라 위치: X 오른쪽, Y 위, Z 전방.")] public Vector3 offset = new(0f, 5f, -8f);
    public Vector3 euler = new(16f, 0f, 0f);
    [Range(1f, 179f)] public float fieldOfView = 60f;
    [Tooltip("이 키에서 다음 키로 이동하는 방식입니다.")] public PlayerSkillCameraInterpolation interpolation = PlayerSkillCameraInterpolation.SmoothStep;
    public PlayerSkillCameraPose Pose => new()
    {
        offset = offset,
        rotation = Quaternion.Euler(euler),
        fieldOfView = float.IsNaN(fieldOfView) || float.IsInfinity(fieldOfView) ? 60f : Mathf.Clamp(fieldOfView, 1f, 179f)
    };
}

public struct PlayerSkillCameraPose
{
    public Vector3 offset;
    public Quaternion rotation;
    public float fieldOfView;
}
