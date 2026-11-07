using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>One source of truth for the Q skill, its editor, and its runtime.</summary>
[CreateAssetMenu(menuName = "Project R/Player/Ultimate", fileName = "PlayerUltimate")]
public sealed class PlayerUltimateDefinition : ScriptableObject
{
    [Tooltip("Q 시전 직후 전투를 멈추고 재생할 가상 스튜디오 컷씬. 비우면 바로 공격합니다.")]
    public VirtualCutsceneDefinition cutscene;
    public List<UltimatePull> pulls = new();
    public UltimatePull FindPull(string id) => string.IsNullOrEmpty(id) ? null : pulls?.Find(pull => pull != null && pull.id == id);

    public List<UltimateSequenceClip> sequence = new();
    public AnimationClip clip;
    [Min(0.05f)] public float playbackSpeed = 1f;
    [FormerlySerializedAs("useCustomEndTime"), Tooltip("지정한 시각부터 다른 유효한 입력으로 Q를 조기 종료할 수 있습니다. 입력이 없으면 끝까지 재생합니다.")]
    public bool useInputCancelTime;
    [FormerlySerializedAs("endTime"), Min(0.01f), Tooltip("입력 취소가 가능해지는 전체 시퀀스 시각(초). 개별 클립 배속을 반영하며 컷씬 시간은 포함하지 않습니다.")]
    public float inputCancelTime = 3f;
    [Min(0f), Tooltip("종료 자세에서 다음 동작으로 블렌드하는 전투 시간(초). 조작을 막는 후딜레이가 아닙니다.")]
    public float exitBlendDuration = .15f;
    [Min(0f), Tooltip("Game seconds, counted from cast start.")] public float cooldown = 8f;
    public bool invulnerable = true;
    public LayerMask targetLayers = 1 << 6;
    public List<UltimateHitField> hits = new();
    public UltimateMotionWarp warp = new();
    public List<UltimateCue> cues = new();

    public bool HasSequence => sequence != null && sequence.Count > 0;
    public bool Ready
    {
        get
        {
            if (!HasSequence) return clip != null;
            foreach (UltimateSequenceClip entry in sequence)
                if (entry == null || entry.clip == null || entry.SourceEnd <= entry.SourceStart ||
                    (entry.rootMotion && (entry.rootSamples == null || entry.rootSamples.Count < 2))) return false;
            return true;
        }
    }
    // Sequence time includes each segment's speed; global playbackSpeed is applied at runtime.
    // Hit/cue timestamps inside a segment always remain in that original clip's seconds.
    public float Duration => HasSequence ? UltimateSequenceUtility.GetDuration(this) : clip != null ? Mathf.Max(0.01f, clip.length) : 3f;
    public float InputCancelTime => useInputCancelTime
        ? Mathf.Clamp(FiniteTiming(inputCancelTime, Duration), .01f, Duration) : Duration;
    public float ExitBlendDuration => Mathf.Max(0f, FiniteTiming(exitBlendDuration, .15f));
    public float RuntimeDuration => Duration / Mathf.Max(0.05f, playbackSpeed);

    private static float FiniteTiming(float value, float fallback)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

    private void OnValidate()
    {
        playbackSpeed = Mathf.Max(0.05f, playbackSpeed);
        cooldown = Mathf.Max(0f, cooldown);
        inputCancelTime = Mathf.Max(.01f, FiniteTiming(inputCancelTime, Duration));
        exitBlendDuration = ExitBlendDuration;
        sequence ??= new List<UltimateSequenceClip>();
        foreach (UltimateSequenceClip entry in sequence) entry?.Normalize();
        hits ??= new List<UltimateHitField>();
        cues ??= new List<UltimateCue>();
        warp ??= new UltimateMotionWarp();
        float legacyDuration = clip != null ? Mathf.Max(0.01f, clip.length) : 3f;
        foreach (UltimateHitField hit in hits)
        {
            if (hit == null) continue;
            hit.time = Mathf.Clamp(hit.time, 0f, legacyDuration);
            hit.size = new Vector3(Mathf.Max(0.01f, hit.size.x), Mathf.Max(0.01f, hit.size.y), Mathf.Max(0.01f, hit.size.z));
            hit.radius = Mathf.Max(0.005f, hit.radius);
            hit.damage = Mathf.Max(0f, hit.damage);
            hit.hitStopFrames = Mathf.Max(0, hit.hitStopFrames);
        }
        warp.start = Mathf.Clamp(warp.start, 0f, legacyDuration);
        warp.end = Mathf.Clamp(warp.end, warp.start, legacyDuration);
        warp.Normalize(legacyDuration);
        foreach (UltimateCue cue in cues)
        {
            if (cue == null) continue;
            cue.time = Mathf.Clamp(cue.time, 0f, legacyDuration);
            cue.lifetime = Mathf.Max(0.05f, cue.lifetime);
        }
    }
}

public enum UltimateFieldShape { Box, Sphere }

[Serializable]
public sealed class UltimateHitField
{
    public string name = "Hit";
    public string pullId;
    public string releasePullId;
    public bool enabled = true;
    [FormerlySerializedAs("start"), Min(0f)] public float time = 0.5f;
    public UltimateFieldShape shape;
    [Tooltip("Meters from the player root, in cast-facing coordinates.")]
    public Vector3 position = new(0f, 1f, 1.5f);
    public Vector3 euler;
    [Tooltip("Full box dimensions, meters.")] public Vector3 size = new(3f, 2f, 3f);
    [Min(0.005f)] public float radius = 2f;
    [Min(0f)] public float damage = 30f;
    [Min(0)] public int hitStopFrames = 3;
    public StaggerLevel stagger = StaggerLevel.Level2;
    public AttackHitStopMode hitStopMode = AttackHitStopMode.AttackerAndVictims;
}

public enum UltimateMotionWarpMode { Forward, Path }
public enum UltimatePathInterpolation { Linear, SmoothStep }

[Serializable]
public sealed class UltimateMotionWaypoint
{
    [Min(0.001f), Tooltip("Source clip seconds. The fixed starting point at time zero is implicit.")]
    public float time = 0.5f;
    [Tooltip("Meters from the cast starting point in its facing coordinates. Ground XZ only; Y is always zero.")]
    public Vector3 position = Vector3.forward;
}

[Serializable]
public sealed class UltimateMotionWarp
{
    public bool enabled;
    public UltimateMotionWarpMode mode;
    [Min(0f)] public float start = 0.1f;
    [Min(0f)] public float end = 0.5f;
    [Tooltip("Forward travel in meters; this prototype does not lock onto a moving target.")]
    public float distance = 2f;
    public AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    public UltimatePathInterpolation interpolation = UltimatePathInterpolation.SmoothStep;
    public List<UltimateMotionWaypoint> points = new();

    public Vector3 EvaluateOffset(float time)
    {
        if (!enabled || float.IsNaN(time)) return Vector3.zero;
        if (mode == UltimateMotionWarpMode.Path) return EvaluatePath(time);
        if (time < start) return Vector3.zero;
        float t = end <= start ? 1f : Mathf.Clamp01((time - start) / (end - start));
        float progress = curve != null && curve.length > 0 ? curve.Evaluate(t) : t;
        return Vector3.forward * (distance * progress);
    }

    private Vector3 EvaluatePath(float time)
    {
        // The path always begins at the cast origin, even with an invalid authored t=0 key.
        if (time <= 0f || points == null || points.Count == 0) return Vector3.zero;
        float previousTime = 0f;
        Vector3 previousPosition = Vector3.zero;
        UltimateMotionWaypoint next = null;
        foreach (UltimateMotionWaypoint point in points)
        {
            if (point == null || !IsFinite(point.time) || point.time <= 0f) continue;
            // Evaluation remains safe before OnValidate, including while editing the list.
            // Equal-time keys use the last list entry, avoiding a zero-length division.
            if (point.time <= time && point.time >= previousTime)
            {
                previousTime = point.time;
                previousPosition = GroundPosition(point.position);
            }
            else if (point.time > time && (next == null || point.time <= next.time))
                next = point;
        }
        if (next == null) return previousPosition;
        float t = Mathf.Clamp01((time - previousTime) / (next.time - previousTime));
        if (interpolation == UltimatePathInterpolation.SmoothStep) t = t * t * (3f - 2f * t);
        return Vector3.LerpUnclamped(previousPosition, GroundPosition(next.position), t);
    }

    public void Normalize(float duration)
    {
        points ??= new List<UltimateMotionWaypoint>();
        float lastTime = IsFinite(duration) ? Mathf.Max(0.01f, duration) : 0.01f;
        for (int i = points.Count - 1; i >= 0; i--)
        {
            UltimateMotionWaypoint point = points[i];
            if (point == null || !IsFinite(point.time))
            {
                points.RemoveAt(i);
                continue;
            }
            point.time = Mathf.Clamp(point.time, 0.001f, lastTime);
            point.position = GroundPosition(point.position);
        }
        // Stable ordering preserves the explicit last-key-wins rule for duplicate times.
        for (int i = 1; i < points.Count; i++)
        {
            UltimateMotionWaypoint point = points[i];
            int previous = i - 1;
            while (previous >= 0 && points[previous].time > point.time)
            {
                points[previous + 1] = points[previous];
                previous--;
            }
            points[previous + 1] = point;
        }
    }

    private static Vector3 GroundPosition(Vector3 position)
        => new(IsFinite(position.x) ? position.x : 0f, 0f, IsFinite(position.z) ? position.z : 0f);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

[Serializable]
public sealed class UltimateCue
{
    public string name = "Effect";
    [Min(0f)] public float time = 0.5f;
    public GameObject prefab;
    public Vector3 position;
    public Vector3 euler;
    [Min(0.05f)] public float lifetime = 3f;
}

public struct UltimateFieldPose
{
    public Vector3 center;
    public Quaternion rotation;
    public Vector3 halfExtents;
    public float radius;
}

public static class UltimateFieldGeometry
{
    public static UltimateFieldPose Evaluate(UltimateHitField field, Vector3 origin, Quaternion facing)
    {
        return new UltimateFieldPose
        {
            center = origin + facing * field.position,
            rotation = facing * Quaternion.Euler(field.euler),
            halfExtents = new Vector3(Mathf.Max(0.005f, field.size.x * 0.5f), Mathf.Max(0.005f, field.size.y * 0.5f), Mathf.Max(0.005f, field.size.z * 0.5f)),
            radius = Mathf.Max(0.005f, field.radius)
        };
    }

    // A marker occupies one source-clip frame in the visual editor, not a damage window.
    public static bool IsActive(UltimateHitField field, float time, float frameRate = 60f)
        => field != null && field.enabled &&
           Mathf.RoundToInt(time * Mathf.Max(1f, frameRate)) == Mathf.RoundToInt(field.time * Mathf.Max(1f, frameRate));

    // Include the newly reached endpoint, but never fire again from an already reached time.
    // Cast initialization passes a negative previous time so markers at zero fire once too.
    public static bool Crossed(UltimateHitField field, float from, float to)
        => field != null && field.enabled && to >= from && from < field.time && field.time <= to;

    public static Collider[] Overlap(UltimateHitField field, Vector3 origin, Quaternion facing, int layerMask)
    {
        UltimateFieldPose pose = Evaluate(field, origin, facing);
        return field.shape == UltimateFieldShape.Sphere
            ? Physics.OverlapSphere(pose.center, pose.radius, layerMask, QueryTriggerInteraction.Collide)
            : Physics.OverlapBox(pose.center, pose.halfExtents, pose.rotation, layerMask, QueryTriggerInteraction.Collide);
    }
}
