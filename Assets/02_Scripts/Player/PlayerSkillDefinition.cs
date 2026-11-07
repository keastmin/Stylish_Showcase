using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>E gameplay authoring data. The Timeline only supplies animation and presentation.</summary>
[CreateAssetMenu(menuName = "Project R/Player/E Skill", fileName = "PlayerSkill")]
public sealed class PlayerSkillDefinition : ScriptableObject
{
    [Min(.01f)] public float duration = 2.683333f;
    [Min(.05f)] public float playbackSpeed = 1f;
    public bool invulnerable = true;
    public bool playPresentation = true;
    public bool useInputCancelTime;
    [Min(0f)] public float inputCancelTime = 2.2f;
    [Min(0f)] public float recoveryDuration;
    [Min(0f), Tooltip("자연 종료 또는 입력 취소 시 다음 동작으로 자세를 연결하는 전투 시간입니다. 추가 후딜레이가 아닙니다.")]
    public float exitBlendDuration = .12f;
    public PlayerSkillMotion motion = new();
    public List<PlayerSkillHit> hits = new();
    public List<PlayerSkillScreenEffect> screenEffects = new();
    // The bound Impact Frame adapter is driven by these intervals, independently of Timeline clips.
    public List<PlayerSkillImpactWindow> impactFrames = new();

    public float Duration => Mathf.Max(.01f, Finite(duration, 2.683333f));
    public float PlaybackSpeed => Mathf.Max(.05f, Finite(playbackSpeed, 1f));
    public float RecoveryDuration => Mathf.Max(0f, Finite(recoveryDuration, 0f));
    public float ExitBlendDuration => Mathf.Max(0f, Finite(exitBlendDuration, .12f));
    public float InputCancelTime => useInputCancelTime
        ? Mathf.Clamp(Finite(inputCancelTime, Duration), 0f, Duration) : Duration;
    public float RuntimeDuration => Duration / PlaybackSpeed + RecoveryDuration;
    public Vector3 EvaluateMotion(float time) => motion != null ? motion.Evaluate(time) : Vector3.zero;
    public static float Finite(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

    public void Normalize()
    {
        duration = Duration; playbackSpeed = PlaybackSpeed; recoveryDuration = RecoveryDuration;
        exitBlendDuration = ExitBlendDuration;
        inputCancelTime = Mathf.Clamp(Finite(inputCancelTime, Duration), 0f, Duration);
        motion ??= new PlayerSkillMotion();
        motion.start = Mathf.Max(0f, Finite(motion.start, 0f));
        motion.end = Mathf.Max(motion.start, Finite(motion.end, Duration));
        motion.distance = Finite(motion.distance, 0f);
        hits ??= new List<PlayerSkillHit>(); screenEffects ??= new List<PlayerSkillScreenEffect>();
        impactFrames ??= new List<PlayerSkillImpactWindow>();
        int nextOrder = 0;
        foreach (var hit in hits) if (hit != null) nextOrder = Mathf.Max(nextOrder, hit.executionOrder + 1);
        var orders = new HashSet<int>();
        foreach (var hit in hits)
        {
            if (hit == null) continue;
            if (hit.executionOrder < 0 || !orders.Add(hit.executionOrder))
            { hit.executionOrder = nextOrder++; orders.Add(hit.executionOrder); }
            hit.time = Mathf.Max(0f, Finite(hit.time, 0f));
            hit.size = new Vector3(Positive(hit.size.x), Positive(hit.size.y), Positive(hit.size.z));
            hit.radius = Positive(hit.radius); hit.height = Mathf.Max(hit.radius * 2f, Positive(hit.height));
            hit.damage = Mathf.Max(0f, Finite(hit.damage, 0f)); hit.hitStopFrames = Mathf.Max(0, hit.hitStopFrames);
            hit.shakeForce = Mathf.Max(0f, Finite(hit.shakeForce, 0f));
            hit.capsuleDirection = Mathf.Clamp(hit.capsuleDirection, 0, 2);
        }
        foreach (var fx in screenEffects)
        {
            if (fx == null) continue;
            fx.start = Mathf.Max(0f, Finite(fx.start, 0f));
            fx.duration = Mathf.Max(.001f, Finite(fx.duration, .1f));
            fx.weight = Mathf.Clamp01(Finite(fx.weight, 1f));
        }
        foreach (var window in impactFrames)
        {
            if (window == null) continue;
            window.start = Mathf.Max(0f, Finite(window.start, 0f));
            window.duration = Mathf.Max(.001f, Finite(window.duration, .3f));
        }
    }
    private static float Positive(float value) => Mathf.Max(.005f, Finite(value, 1f));
    private void OnValidate() => Normalize();
}

[Serializable]
public sealed class PlayerSkillMotion
{
    public bool enabled = true;
    [Min(0f)] public float start = .8166667f;
    [Min(0f)] public float end = 1.1166667f;
    public float distance = 9.7776f;
    public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    public Vector3 Evaluate(float time)
    {
        if (!enabled || time < start) return Vector3.zero;
        float t = end <= start ? 1f : Mathf.Clamp01((time - start) / (end - start));
        float first = curve != null && curve.length > 0 ? curve.Evaluate(0f) : 0f;
        float last = curve != null && curve.length > 0 ? curve.Evaluate(1f) : 1f;
        float value = curve != null && curve.length > 0 ? curve.Evaluate(t) : t;
        float progress = Mathf.Abs(last - first) > .00001f ? (value - first) / (last - first) : t;
        return Vector3.forward * (PlayerSkillDefinition.Finite(distance, 0f) * progress);
    }
}

public enum PlayerSkillHitShape { Box, Sphere, Capsule }

[Serializable]
public sealed class PlayerSkillHit
{
    public string name = "Hit";
    [HideInInspector] public int executionOrder = -1;
    public bool enabled = true;
    [Min(0f)] public float time = .85f;
    public PlayerSkillHitShape shape;
    public Vector3 position = new(0f, 1f, 1.5f);
    public Vector3 euler;
    public Vector3 size = new(3f, 2f, 3f);
    [Min(.005f)] public float radius = 1f;
    [Min(.01f)] public float height = 2f;
    [Range(0, 2)] public int capsuleDirection = 1;
    [Min(0f)] public float damage = 10f;
    [Min(0)] public int hitStopFrames;
    public AttackHitStopMode hitStopMode;
    public StaggerLevel stagger;
    public LayerMask targetLayers = ~0;
    public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;
    public float skillGaugeAdditive;
    [Min(0f)] public float shakeForce = 1.8f;

    public Collider[] Overlap(Vector3 origin, Quaternion facing)
    {
        Vector3 center = origin + facing * position;
        Quaternion rotation = facing * Quaternion.Euler(euler);
        if (shape == PlayerSkillHitShape.Sphere)
            return Physics.OverlapSphere(center, radius, targetLayers, triggerInteraction);
        if (shape == PlayerSkillHitShape.Capsule)
        {
            Vector3 axis = rotation * (capsuleDirection == 0 ? Vector3.right : capsuleDirection == 2 ? Vector3.forward : Vector3.up);
            Vector3 line = axis * Mathf.Max(0f, height * .5f - radius);
            return Physics.OverlapCapsule(center + line, center - line, radius, targetLayers, triggerInteraction);
        }
        return Physics.OverlapBox(center, size * .5f, rotation, targetLayers, triggerInteraction);
    }
}

[Serializable]
public sealed class PlayerSkillScreenEffect
{
    public string name = "Screen Effect";
    public bool enabled = true;
    public float start = .5f;
    [Min(.001f)] public float duration = .3f;
    public VolumeProfile profile;
    [Range(0f, 1f)] public float weight = 1f;
    public AnimationCurve envelope = AnimationCurve.Linear(0f, 1f, 1f, 0f);
    public float priority = 1000f;
}

[Serializable]
public sealed class PlayerSkillImpactWindow
{
    public string name = "Impact Frame";
    public bool enabled = true;
    public float start = .5333333f;
    [Min(.001f)] public float duration = .3f;
}
