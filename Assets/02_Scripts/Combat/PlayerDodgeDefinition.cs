using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectR.VFX;

public enum DodgePhase { Dodge, CounterStart, CounterLoop, CounterEnd, Complete }
public enum DodgeSlowExit { Immediate, Fade, UntilAnimation }
public enum DodgeEffectKind { Vignette, Saturation, Exposure, ChromaticAberration, LensDistortion, FieldOfView, CameraShake }
public enum DodgeEffectAnchor { DodgeStart, PerfectSuccess, SlowStart, CounterStart, CounterLoop, CounterEnd, AnimationPose }
public enum DodgeEffectHandoff { Continue, FadeOut, ReplaceWhenCounterEffectStarts }
public enum DodgeAfterimageMode { Legacy, Chromatic, Both, Off }
public enum DodgeAfterimageStart { DodgeStart, PerfectSuccess, SlowStart }

[Serializable]
public sealed class DodgeAfterimageSettings
{
    public DodgeAfterimageMode mode = DodgeAfterimageMode.Legacy;
    public DodgeAfterimageStart start = DodgeAfterimageStart.PerfectSuccess;
    public GameObject prefab;
    [Min(.01f)] public float emissionDuration = .34f;
    [Min(.02f)] public float snapshotInterval = .065f;
    [Min(.1f)] public float snapshotLifetime = .64f;
    [Range(2,24)] public int maxSnapshots = 12;
    [Range(0,1)] public float opacity = .66f;
    [Min(1)] public float noiseScale = 27f;
    [ColorUsage(true,true)] public Color edgeColor = new Color(.12f,.65f,1.8f,1);
    [Range(0,2)] public float edgeStrength = .3f;
    public bool includeRigidMeshes = true;
    [Min(0)] public float fragmentsPerSecond = 720f;
    [Min(0)] public float glintsPerSecond = 90f;
    public bool useRealTime;
    public bool stopOnDodgeExit = true;

    public bool WantsChromatic => mode == DodgeAfterimageMode.Chromatic || mode == DodgeAfterimageMode.Both;
    public bool HasValidPrefab
    {
        get
        {
            var effect = prefab != null ? prefab.GetComponent<ChromaticAfterimageTrail>() : null;
            return effect != null && effect.afterimageMaterial != null && effect.particles != null && effect.particles.visualEffectAsset != null;
        }
    }
    // Old assets and a missing optional prefab keep the original presentation usable.
    public bool UsesLegacy => mode == DodgeAfterimageMode.Legacy || mode == DodgeAfterimageMode.Both ||
        (mode == DodgeAfterimageMode.Chromatic && !HasValidPrefab);

    public void ApplyTo(ChromaticAfterimageTrail effect)
    {
        effect.snapshotInterval = Mathf.Max(.02f, snapshotInterval);
        effect.snapshotLifetime = Mathf.Max(.1f, snapshotLifetime);
        effect.maxSnapshots = Mathf.Clamp(maxSnapshots, 2, 24);
        effect.opacity = Mathf.Clamp01(opacity);
        effect.noiseScale = Mathf.Max(1, noiseScale);
        effect.edgeColor = edgeColor;
        effect.edgeStrength = Mathf.Clamp(edgeStrength, 0, 2);
        effect.includeRigidMeshes = includeRigidMeshes;
        effect.fragmentsPerSecond = Mathf.Max(0, fragmentsPerSecond);
        effect.glintsPerSecond = Mathf.Max(0, glintsPerSecond);
        effect.useUnscaledTime = useRealTime;
    }
}

[Serializable]
public sealed class DodgeMotion
{
    [HideInInspector] public AnimationClip clip;
    [HideInInspector] public string stateName;
    [Min(.01f)] public float speed = 1f;
    [HideInInspector] public float originalStateSpeed = 1f;
    [HideInInspector] public float actionEnd = -1f;
    [Tooltip("기존 완벽 회피 종료 이벤트의 원본 클립 시각입니다. -1은 없음입니다.")] public float perfectEnd = -1f;
    [Min(0f)] public float judgeStart;
    [Min(0f)] public float judgeEnd;
    public bool judgeOverWindow;
    public float Length => clip != null ? Mathf.Max(.001f, clip.length) : 1f;
    public float End => actionEnd >= 0 ? Mathf.Clamp(actionEnd, 0, Length) : Length;
    public float FrameRate => clip != null ? Mathf.Max(1, clip.frameRate) : 60f;
}

[Serializable]
public sealed class DodgeCounterLink
{
    public DodgeSlowExit slowExit = DodgeSlowExit.Immediate;
    [Min(0f)] public float fadeDuration = .08f;
    public AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public DodgePhase releasePhase = DodgePhase.CounterStart;
    [Min(0f)] public float releaseSourceTime;
    [Min(.01f)] public float maxHold = 1f;
    public bool invulnerable = true;
}

[Serializable]
public sealed class DodgeScreenCue
{
    public string name = "비네트";
    public bool enabled = true;
    public DodgeEffectKind kind;
    public DodgeEffectAnchor anchor = DodgeEffectAnchor.SlowStart;
    public DodgePhase posePhase;
    [Min(0f)] public float sourceTime;
    [Min(0f)] public float delay;
    public float strength = .45f;
    public Color color = Color.black;
    [Min(0f)] public float fadeIn = .3f;
    [Min(0f)] public float hold = .5f;
    [Min(0f)] public float fadeOut = .3f;
    public AnimationCurve inCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public AnimationCurve outCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public DodgeEffectHandoff onCounter;
    [Min(0f)] public float counterFade = .1f;
    public float Duration => Mathf.Max(0, fadeIn) + Mathf.Max(0, hold) + Mathf.Max(0, fadeOut);

    // Shared by new definitions and the studio's add-effect preset.
    public static DodgeScreenCue PerfectDodgeDesaturation() => new DodgeScreenCue
    {
        name = "완벽 회피 · 부분 흑백",
        kind = DodgeEffectKind.Saturation,
        anchor = DodgeEffectAnchor.PerfectSuccess,
        strength = -65f,
        fadeIn = .06f,
        hold = .24f,
        fadeOut = .28f,
        onCounter = DodgeEffectHandoff.Continue
    };
}

/// <summary>Optional authoring data. A player without this asset uses the original dodge implementation.</summary>
[CreateAssetMenu(menuName = "Combat/Player Dodge Studio", fileName = "PlayerDodge")]
public sealed class PlayerDodgeDefinition : ScriptableObject
{
    public DodgeMotion front = new DodgeMotion { stateName = "Front Dodge", speed = 1.3f, originalStateSpeed = 1.3f };
    public DodgeMotion back = new DodgeMotion { stateName = "Back Dodge", speed = 1.3f, originalStateSpeed = 1.3f };
    public DodgeMotion counterStart = new DodgeMotion { stateName = "Dodge Attack Start" };
    public DodgeMotion counterLoop = new DodgeMotion { stateName = "Dodge Attack Loop" };
    public DodgeMotion counterEnd = new DodgeMotion { stateName = "Dodge Attack End" };
    public PerfectDodgeSettings slow = new PerfectDodgeSettings();
    [Min(0f)] public float counterOpenDelay = .07f;
    [Min(.01f)] public float counterWindowDuration = 1.65f;
    public bool closeAtAnimationMarker = true;
    public bool releaseSlowWithWindow = true;
    public bool invulnerableOnSuccess = true;
    [Min(0f)] public float invulnerabilityDelay;
    [Tooltip("0이면 반격 입력 구간이 닫힐 때까지. 양수이면 성공 후 실제 초입니다.")]
    [Min(0f)] public float invulnerabilityDuration;
    public DodgeCounterLink counter = new DodgeCounterLink();
    public List<DodgeScreenCue> dodgeEffects = new List<DodgeScreenCue> { new DodgeScreenCue(), DodgeScreenCue.PerfectDodgeDesaturation() };
    public List<DodgeScreenCue> counterEffects = new List<DodgeScreenCue>();
    public DodgeAfterimageSettings afterimage = new DodgeAfterimageSettings();

    public DodgeMotion Motion(DodgePhase phase, bool backwards)
    {
        switch (phase)
        {
            case DodgePhase.CounterStart: return counterStart;
            case DodgePhase.CounterLoop: return counterLoop;
            case DodgePhase.CounterEnd: return counterEnd;
            default: return backwards ? back : front;
        }
    }
}
