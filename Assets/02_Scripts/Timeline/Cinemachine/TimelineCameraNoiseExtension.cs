using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

[ExecuteAlways, AddComponentMenu("Cinemachine/Extensions/Timeline Camera Noise")]
public sealed class TimelineCameraNoiseExtension : CinemachineExtension
{
    private readonly Dictionary<object, Vector3> _requests = new();
    public Vector3 Offset
    {
        get
        {
            Vector3 result = Vector3.zero;
            foreach (Vector3 offset in _requests.Values)
                result += offset;
            return result;
        }
    }

    public void SetOffset(object owner, Vector3 offset)
    {
        if (owner != null)
            _requests[owner] = offset;
    }
    public void Release(object owner)
    {
        if (owner != null)
            _requests.Remove(owner);
    }

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        // Scrubbing passes negative deltaTime. The sampled offset is still valid.
        if (stage == CinemachineCore.Stage.Noise)
            state.PositionCorrection += state.GetCorrectedOrientation() * Offset;
    }
}
