using System;
using UnityEngine;

public enum UltimatePullAnchor { FirstMarker, EachMarker, FollowPlayer }
public enum UltimatePullMotion { Speed, ArrivalTime }

[Serializable]
public sealed class UltimatePull
{
    public string id;
    public string name = "전방 모으기";
    public UltimatePullAnchor anchor;
    public Vector2 offset = new(0f, 2f);
    public UltimatePullMotion motion;
    [Min(0.1f)] public float speed = 8f;
    [Min(0.01f)] public float duration = .4f;
    [Min(0f)] public float radius = .35f;
    [Min(0f)] public float acceleration = 40f;
    [Min(0f)] public float deceleration = 60f;
    [Min(0.01f)] public float arrivalTime = .3f;
    public StaggerLevel strength = StaggerLevel.Level2;
    public int priority;

    public Vector3 Center(Vector3 origin, Quaternion facing)
    {
        Vector3 forward = Vector3.ProjectOnPlane(facing * Vector3.forward, Vector3.up).normalized;
        if (forward == Vector3.zero) forward = Vector3.forward;
        return origin + forward * offset.y + Vector3.Cross(Vector3.up, forward) * offset.x;
    }

    public Vector3 Step(Vector3 position, Vector3 center, Vector3 start, float elapsed, float dt, ref float currentSpeed)
    {
        Vector3 delta = Vector3.ProjectOnPlane(center - position, Vector3.up);
        float distance = delta.magnitude;
        float remaining = Mathf.Max(0f, distance - radius);
        if (remaining <= 0f || dt <= 0f)
        {
            currentSpeed = 0f;
            return Vector3.zero;
        }

        float desiredSpeed = Mathf.Max(.1f, speed);
        if (motion == UltimatePullMotion.ArrivalTime && anchor != UltimatePullAnchor.FollowPlayer)
        {
            float total = Mathf.Max(0f, Vector3.ProjectOnPlane(center - start, Vector3.up).magnitude - radius);
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / Mathf.Max(.01f, arrivalTime));
            desiredSpeed = Mathf.Min(desiredSpeed, Mathf.Max(0f, remaining - total * (1f - progress)) / dt);
            currentSpeed = desiredSpeed;
        }
        else
        {
            if (deceleration > 0f) desiredSpeed = Mathf.Min(desiredSpeed, Mathf.Sqrt(2f * deceleration * remaining));
            float rate = desiredSpeed >= currentSpeed ? acceleration : deceleration;
            currentSpeed = rate > 0f ? Mathf.MoveTowards(currentSpeed, desiredSpeed, rate * dt) : desiredSpeed;
        }
        return delta / distance * Mathf.Min(remaining, currentSpeed * dt);
    }
}
