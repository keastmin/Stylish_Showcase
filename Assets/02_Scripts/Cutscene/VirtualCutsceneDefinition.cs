using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Presentation only. All times are real seconds; this asset never deals damage.</summary>
[CreateAssetMenu(menuName = "Project R/Cutscene/Virtual Studio Cutscene", fileName = "VirtualCutscene")]
public sealed class VirtualCutsceneDefinition : ScriptableObject
{
    public bool enabled = true;
    [Min(.1f)] public float duration = 1.8f;
    [Tooltip("비워 두면 현재 플레이어의 외형을 복제합니다. 게임 로직은 복제하지 않습니다.")]
    public GameObject actorPrefab;
    public Vector3 actorPosition;
    public Vector3 actorEuler;
    [Min(.01f)] public float actorScale = 1f;
    public Color backgroundColor = new(.025f, .035f, .065f);
    public GameObject backgroundPrefab;
    public Vector3 backgroundPosition;
    public Vector3 backgroundEuler;
    public Vector3 backgroundScale = Vector3.one;
    public Material skybox;
    public bool showFloor = true;
    public Material floorMaterial;
    public Color floorColor = new(.055f, .075f, .11f);
    public Color keyLightColor = new(.75f, .87f, 1f);
    [Min(0)] public float keyLightIntensity = 2.2f;
    public Vector3 keyLightEuler = new(35, -35, 0);
    public Color fillLightColor = new(.6f, .4f, 1f);
    [Min(0)] public float fillLightIntensity = 1.2f;
    [Range(0, .24f)] public float letterbox = .065f;
    [Min(0)] public float fadeIn = .06f;
    [Min(0)] public float fadeOut = .08f;
    public bool allowSkip;
    [HideInInspector] public int renderLayer = 30;
    public List<VirtualCutsceneAnimation> animations = new();
    public List<VirtualCutsceneShot> shots = new() { new VirtualCutsceneShot() };
    public List<VirtualCutsceneEffect> effects = new();
    public List<VirtualCutsceneSound> sounds = new();

    public float Duration => Mathf.Max(.1f, duration);
    public bool Ready => enabled && shots != null && shots.Exists(s => s != null);

    public float BlackOpacity(float time)
    {
        float start = fadeIn > 0 ? 1 - Mathf.Clamp01(time / fadeIn) : 0;
        float end = fadeOut > 0 ? 1 - Mathf.Clamp01((Duration - time) / fadeOut) : 0;
        return Mathf.Max(start, end);
    }

    public VirtualCutsceneShot ShotAt(float time)
    {
        VirtualCutsceneShot selected = null;
        if (shots == null) return null;
        foreach (var shot in shots)
            if (shot != null && shot.start <= time && (selected == null || shot.start >= selected.start)) selected = shot;
        if (selected != null) return selected;
        foreach (var shot in shots)
            if (shot != null && (selected == null || shot.start < selected.start)) selected = shot;
        return selected;
    }

    private void OnValidate()
    {
        animations ??= new List<VirtualCutsceneAnimation>();
        shots ??= new List<VirtualCutsceneShot>();
        effects ??= new List<VirtualCutsceneEffect>();
        sounds ??= new List<VirtualCutsceneSound>();
        duration = Duration;
        actorScale = Mathf.Max(.01f, actorScale);
        fadeIn = Mathf.Clamp(fadeIn, 0, Duration);
        fadeOut = Mathf.Clamp(fadeOut, 0, Duration);
        renderLayer = Mathf.Clamp(renderLayer, 8, 31);
    }
}

[Serializable]
public sealed class VirtualCutsceneAnimation
{
    public string name = "캐릭터 동작";
    public AnimationClip clip;
    [Min(0)] public float start;
    [Min(.01f)] public float duration = 1.8f;
    [Min(0)] public float sourceStart;
    [Min(.01f)] public float speed = 1f;
    public bool loop;

    public float SourceTime(float time)
    {
        if (clip == null) return 0;
        float source = sourceStart + Mathf.Clamp(time - start, 0, Mathf.Max(.01f, duration)) * Mathf.Max(.01f, speed);
        return loop ? Mathf.Repeat(source, Mathf.Max(.001f, clip.length)) : Mathf.Clamp(source, 0, Mathf.Max(0, clip.length - .00001f));
    }
}

[Serializable]
public sealed class VirtualCutsceneShot
{
    public string name = "01 · 히어로 샷";
    [Min(0)] public float start;
    [Min(.01f)] public float duration = 1.8f;
    public Vector3 position = new(.9f, 1.4f, 2.8f);
    public Vector3 lookAt = new(0, 1.15f, 0);
    public Vector3 endPosition = new(.35f, 1.5f, 2.1f);
    public Vector3 endLookAt = new(0, 1.25f, 0);
    [Range(5, 120)] public float fieldOfView = 40;
    [Range(5, 120)] public float endFieldOfView = 32;
    public bool smooth = true;
    [Range(-45, 45)] public float roll;
    [Range(0, .2f)] public float shake;
    [Min(0)] public float shakeFrequency = 18;

    public void Evaluate(float time, out Vector3 cameraPosition, out Vector3 target, out float fov)
    {
        float t = Mathf.Clamp01((time - start) / Mathf.Max(.01f, duration));
        if (smooth) t = t * t * (3 - 2 * t);
        cameraPosition = Vector3.Lerp(position, endPosition, t);
        target = Vector3.Lerp(lookAt, endLookAt, t);
        float clock = Mathf.Max(0, time - start) * shakeFrequency;
        cameraPosition += new Vector3(Mathf.Sin(clock * 2.17f), Mathf.Sin(clock * 3.13f), 0) * shake;
        fov = Mathf.Lerp(fieldOfView, endFieldOfView, t);
    }
}

[Serializable]
public sealed class VirtualCutsceneEffect
{
    public string name = "이펙트";
    public GameObject prefab;
    [Min(0)] public float start = .3f;
    [Min(.01f)] public float duration = 1;
    public Vector3 position = new(0, 1, 0);
    public Vector3 euler;
    public Vector3 scale = Vector3.one;
    public bool relativeToActor = true;
}

[Serializable]
public sealed class VirtualCutsceneSound
{
    public string name = "사운드";
    public AudioClip clip;
    [Min(0)] public float start;
    [Min(.01f)] public float duration = 1;
    [Min(0)] public float sourceStart;
    [Range(0, 1)] public float volume = 1;
    [Range(.1f, 3)] public float pitch = 1;
    [Min(0)] public float fadeIn;
    [Min(0)] public float fadeOut = .05f;
    public bool loop;

    public float VolumeAt(float time)
    {
        float elapsed = time - start;
        if (elapsed < 0 || elapsed >= duration) return 0;
        float gain = fadeIn > 0 ? Mathf.Clamp01(elapsed / fadeIn) : 1;
        if (fadeOut > 0) gain = Mathf.Min(gain, Mathf.Clamp01((duration - elapsed) / fadeOut));
        return volume * gain;
    }
}
