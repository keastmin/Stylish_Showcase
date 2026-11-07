using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public sealed class CameraNoiseClip : PlayableAsset, ITimelineClipAsset
{
    public enum NoiseWaveform { Sine, Perlin, Profile }

    [Header("Continuous Noise")]
    public NoiseWaveform Waveform = NoiseWaveform.Sine;
    [Tooltip("Optional Cinemachine noise profile. Its Z signal is used along Direction.")]
    public NoiseSettings Profile;
    [Min(0f)] public float Amplitude = 0.03f;
    [Min(0f)] public float Frequency = 9f;
    [Tooltip("Direction in camera space. Forward produces front/back motion.")]
    public Vector3 Direction = Vector3.forward;
    public int Seed = 17;
    [Min(0f)] public float FadeIn = 0.05f;
    [Min(0f)] public float FadeOut = 0.05f;

    [Header("Final Impact")]
    public bool FinisherEnabled = true;
    [Range(0f, 1f)] public float FinisherPosition = 0.85f;
    [Min(0f)] public float FinisherAmplitude = 0.18f;
    [Min(0.001f)] public float FinisherDuration = 0.12f;
    public AnimationCurve FinisherCurve = new(
        new Keyframe(0f, 0f), new Keyframe(0.2f, -1f),
        new Keyframe(0.55f, 0.3f), new Keyframe(1f, 0f));

    public ClipCaps clipCaps => ClipCaps.Blending;
    public override double duration => 1d;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        var playable = ScriptPlayable<CameraNoiseBehaviour>.Create(graph);
        playable.GetBehaviour().Settings = this;
        return playable;
    }

    public Vector3 Sample(double localTime, double clipDuration)
    {
        if (clipDuration <= 0d || localTime < 0d || localTime >= clipDuration)
            return Vector3.zero;
        float t = (float)localTime;
        float length = (float)clipDuration;
        float frequency = Mathf.Max(0f, Frequency);
        float signal;
        if (Waveform == NoiseWaveform.Profile && Profile != null)
        {
            var offsets = new Vector3(Seed * 0.173f, Seed * 0.317f, Seed * 0.619f);
            signal = NoiseSettings.GetCombinedFilterResults(Profile.PositionNoise, t * frequency, offsets).z;
        }
        else if (Waveform == NoiseWaveform.Perlin)
            signal = Mathf.PerlinNoise(Seed * 0.173f, t * frequency + Seed * 0.619f) * 2f - 1f;
        else
            signal = Mathf.Sin(t * frequency * Mathf.PI * 2f);

        float envelope = FadeIn > 0f ? Mathf.SmoothStep(0f, 1f, t / FadeIn) : 1f;
        envelope *= FadeOut > 0f ? Mathf.SmoothStep(0f, 1f, (length - t) / FadeOut) : 1f;
        float displacement = signal * Mathf.Max(0f, Amplitude) * envelope;
        if (FinisherEnabled)
        {
            float start = length * Mathf.Clamp01(FinisherPosition);
            float impactLength = Mathf.Min(Mathf.Max(0.001f, FinisherDuration), length - start);
            float impactTime = t - start;
            if (impactLength > 0f && impactTime >= 0f && impactTime < impactLength && FinisherCurve != null)
                displacement += FinisherCurve.Evaluate(impactTime / impactLength) * Mathf.Max(0f, FinisherAmplitude);
        }
        return Direction.sqrMagnitude > 0.000001f ? Direction.normalized * displacement : Vector3.zero;
    }
}

public sealed class CameraNoiseBehaviour : PlayableBehaviour
{
    public CameraNoiseClip Settings;
}
