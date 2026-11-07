using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class UltimateSequenceClip
{
    public string id = Guid.NewGuid().ToString("N");
    public string label = "Attack";
    public AnimationClip clip;
    [Min(0f)] public float sourceStart;
    [Tooltip("Negative means the complete source clip.")] public float sourceEnd = -1f;
    [Min(0.05f)] public float speed = 1f;
    public bool rootMotion = true;
    public bool applyRootRotation = true;
    public bool applyRootHeight;
    [Min(0f), Tooltip("Blend from the previous clip's frozen endpoint pose. Does not overlap sequence time.")]
    public float poseBlendIn;
    public List<UltimateHitField> hits = new();
    public List<UltimateCue> cues = new();
    public List<UltimateRootSample> rootSamples = new();
    public string rootBakeKey;

    public float SourceStart => Mathf.Clamp(sourceStart, 0f, clip != null ? clip.length : 0f);
    public float SourceEnd => clip == null ? 0f : Mathf.Clamp(sourceEnd < 0f ? clip.length : sourceEnd, SourceStart, clip.length);
    public float Duration => (SourceEnd - SourceStart) / Mathf.Max(0.05f, speed);
    public float SourceTime(float localElapsed) => Mathf.Clamp(SourceStart + Mathf.Max(0f, localElapsed) * Mathf.Max(0.05f, speed), SourceStart, SourceEnd);

    public void Normalize()
    {
        if (string.IsNullOrEmpty(id)) id = Guid.NewGuid().ToString("N");
        speed = Mathf.Max(0.05f, speed);
        sourceStart = SourceStart;
        if (sourceEnd >= 0f) sourceEnd = SourceEnd;
        poseBlendIn = Mathf.Clamp(poseBlendIn, 0f, Duration);
        hits ??= new List<UltimateHitField>();
        cues ??= new List<UltimateCue>();
        rootSamples ??= new List<UltimateRootSample>();
        foreach (UltimateHitField hit in hits)
        {
            if (hit == null) continue;
            hit.time = Mathf.Max(0f, hit.time);
            hit.size = Vector3.Max(hit.size, Vector3.one * 0.01f);
            hit.radius = Mathf.Max(0.005f, hit.radius);
            hit.damage = Mathf.Max(0f, hit.damage);
            hit.hitStopFrames = Mathf.Max(0, hit.hitStopFrames);
        }
    }
}

[Serializable]
public sealed class UltimateRootSample
{
    public float time;
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
}

public struct UltimateSequenceMotion
{
    public Vector3 position;
    public Quaternion rotation;
    public bool usesHeight;
    public static UltimateSequenceMotion Identity => new() { rotation = Quaternion.identity };
}

public static class UltimateSequenceUtility
{
    public static float GetDuration(PlayerUltimateDefinition definition)
    {
        float duration = 0f;
        if (definition?.sequence != null)
            foreach (UltimateSequenceClip entry in definition.sequence) duration += entry?.Duration ?? 0f;
        return Mathf.Max(0.01f, duration);
    }

    public static float GetSegmentStart(PlayerUltimateDefinition definition, int index)
    {
        float start = 0f;
        if (definition?.sequence == null) return start;
        for (int i = 0; i < Mathf.Min(index, definition.sequence.Count); i++) start += definition.sequence[i]?.Duration ?? 0f;
        return start;
    }

    public static bool GetSegment(PlayerUltimateDefinition definition, float time, out int index, out float elapsed)
    {
        index = -1;
        elapsed = 0f;
        if (definition == null || !definition.HasSequence) return false;
        float remaining = Mathf.Clamp(time, 0f, definition.Duration);
        for (int i = 0; i < definition.sequence.Count; i++)
        {
            UltimateSequenceClip entry = definition.sequence[i];
            if (entry == null || entry.Duration <= 0f) continue;
            if (remaining < entry.Duration || i == definition.sequence.Count - 1)
            {
                index = i;
                elapsed = Mathf.Min(remaining, entry.Duration);
                return true;
            }
            remaining -= entry.Duration;
        }
        return false;
    }

    public static bool UsesRootHeight(PlayerUltimateDefinition definition)
    {
        if (definition == null || !definition.HasSequence) return false;
        foreach (UltimateSequenceClip entry in definition.sequence)
            if (entry != null && entry.rootMotion && entry.applyRootHeight) return true;
        return false;
    }

    public static UltimateSequenceMotion EvaluateMotion(PlayerUltimateDefinition definition, float time)
    {
        UltimateSequenceMotion result = UltimateSequenceMotion.Identity;
        if (definition == null) return result;
        if (!definition.HasSequence)
        {
            result.position = definition.warp?.EvaluateOffset(time) ?? Vector3.zero;
            return result;
        }
        float remaining = Mathf.Clamp(time, 0f, definition.Duration);
        foreach (UltimateSequenceClip entry in definition.sequence)
        {
            if (entry == null || entry.Duration <= 0f) continue;
            float elapsed = Mathf.Min(remaining, entry.Duration);
            if (entry.rootMotion)
            {
                UltimateSequenceMotion start = SampleRoot(entry, entry.SourceStart);
                UltimateSequenceMotion current = SampleRoot(entry, entry.SourceTime(elapsed));
                Quaternion inverseStart = Quaternion.Inverse(start.rotation);
                Vector3 relativePosition = inverseStart * (current.position - start.position);
                Quaternion relativeRotation = inverseStart * current.rotation;
                if (!entry.applyRootHeight) relativePosition.y = 0f;
                result.position += result.rotation * relativePosition;
                if (entry.applyRootRotation)
                    result.rotation *= Quaternion.Euler(0f, relativeRotation.eulerAngles.y, 0f);
                result.usesHeight |= entry.applyRootHeight;
            }
            if (remaining <= entry.Duration) break;
            remaining -= entry.Duration;
        }
        return result;
    }

    public static UltimateSequenceMotion SampleRoot(UltimateSequenceClip entry, float sourceTime)
    {
        UltimateRootSample previous = null;
        UltimateRootSample next = null;
        if (entry?.rootSamples != null)
            foreach (UltimateRootSample sample in entry.rootSamples)
            {
                if (sample == null || float.IsNaN(sample.time) || float.IsInfinity(sample.time)) continue;
                if (sample.time <= sourceTime && (previous == null || sample.time >= previous.time)) previous = sample;
                if (sample.time > sourceTime && (next == null || sample.time <= next.time)) next = sample;
            }
        previous ??= next;
        next ??= previous;
        if (previous == null) return UltimateSequenceMotion.Identity;
        float t = next.time <= previous.time ? 0f : Mathf.Clamp01((sourceTime - previous.time) / (next.time - previous.time));
        return new UltimateSequenceMotion
        {
            position = Vector3.Lerp(previous.position, next.position, t),
            rotation = Quaternion.Slerp(ValidRotation(previous.rotation), ValidRotation(next.rotation), t)
        };
    }

    private static Quaternion ValidRotation(Quaternion q)
        => q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w < 0.0001f ? Quaternion.identity : q.normalized;
}
