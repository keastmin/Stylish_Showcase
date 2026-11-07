using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

internal sealed class VirtualCutscenePreview : IDisposable
{
    private PreviewRenderUtility _render;
    public VirtualCutsceneStage Stage { get; private set; }
    public Texture LastTexture { get; private set; }

    public void Build(VirtualCutsceneDefinition definition, PlayerCore player)
    {
        Dispose();
        if (definition == null) return;
        try
        {
            _render = new PreviewRenderUtility();
            _render.camera.cameraType = CameraType.Game;
            foreach (var light in _render.lights) light.intensity = 0;
            _render.ambientColor = new Color(.08f, .09f, .13f);
            Stage = new VirtualCutsceneStage(definition, player != null ? player.Animator : null, _render.camera);
            _render.AddSingleGO(Stage.Root);
        }
        catch { Dispose(); throw; }
    }

    public Texture Render(Rect rect, float time, bool inspect, Quaternion orbit, float distance, Vector3 focus)
    {
        if (_render == null || Stage == null || Event.current.type != EventType.Repaint || rect.width < 16 || rect.height < 16) return LastTexture;
        Stage.Sample(time);
        if (inspect)
            _render.camera.transform.SetPositionAndRotation(focus - orbit * Vector3.forward * distance, orbit);
        _render.camera.aspect = rect.width / rect.height;
        _render.BeginPreview(rect, GUIStyle.none);
        try { _render.Render(true); }
        finally { LastTexture = _render.EndPreview(); }
        return LastTexture;
    }

    public void SaveFrame(string path)
    {
        if (LastTexture == null) throw new InvalidOperationException("먼저 미리보기를 재생하세요.");
        var buffer = RenderTexture.GetTemporary(LastTexture.width, LastTexture.height, 0);
        var previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            Graphics.Blit(LastTexture, buffer);
            RenderTexture.active = buffer;
            image = new Texture2D(buffer.width, buffer.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, buffer.width, buffer.height), 0, 0);
            image.Apply();
            System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(buffer);
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
        }
    }

    public void Dispose()
    {
        Stage?.Dispose(); Stage = null;
        _render?.Cleanup(); _render = null;
        LastTexture = null;
    }
}

/// <summary>Editor audio audition without adding any AudioSources to the user's scene.</summary>
internal static class VirtualCutsceneAudioPreview
{
    private static readonly Type AudioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
    private static readonly MethodInfo Play = AudioUtil?.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
        null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
    private static readonly MethodInfo Stop = AudioUtil?.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    public static bool Available => Play != null && Stop != null;
    public static AudioClip BuildMix(VirtualCutsceneDefinition definition)
    {
        if (!Available || definition == null || definition.sounds == null) return null;
        const int rate = 22050;
        int frames = Mathf.Max(1, Mathf.CeilToInt(Mathf.Min(120, definition.Duration) * rate));
        var mixed = new float[frames * 2];
        bool hasAudio = false;
        foreach (var entry in definition.sounds)
        {
            var clip = entry?.clip;
            if (clip == null || clip.loadType == AudioClipLoadType.Streaming || clip.samples <= 0) continue;
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            if (clip.loadState != AudioDataLoadState.Loaded) continue;
            var samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0)) continue;
            int begin = Mathf.Clamp(Mathf.CeilToInt(entry.start * rate), 0, frames);
            int end = Mathf.Clamp(Mathf.CeilToInt((entry.start + entry.duration) * rate), begin, frames);
            for (int frame = begin; frame < end; frame++)
            {
                float time = frame / (float)rate;
                float source = (entry.sourceStart + (time - entry.start) * entry.pitch) * clip.frequency;
                if (entry.loop) source = Mathf.Repeat(source, clip.samples);
                else if (source < 0 || source >= clip.samples) continue;
                int first = Mathf.Clamp(Mathf.FloorToInt(source), 0, clip.samples - 1);
                int next = entry.loop ? (first + 1) % clip.samples : Mathf.Min(first + 1, clip.samples - 1);
                float weight = source - first, volume = entry.VolumeAt(time);
                for (int channel = 0; channel < 2; channel++)
                {
                    int sourceChannel = Mathf.Min(channel, clip.channels - 1);
                    mixed[frame * 2 + channel] += Mathf.Lerp(samples[first * clip.channels + sourceChannel], samples[next * clip.channels + sourceChannel], weight) * volume;
                }
            }
            hasAudio = true;
        }
        if (!hasAudio) return null;
        for (int i = 0; i < mixed.Length; i++) mixed[i] = Mathf.Clamp(mixed[i], -1, 1);
        var result = AudioClip.Create("Cutscene Studio • preview mix", frames, 2, rate, false);
        result.hideFlags = HideFlags.HideAndDontSave;
        result.SetData(mixed, 0);
        return result;
    }
    public static void PlayClip(AudioClip clip, float sourceSeconds = 0, bool loop = false)
    {
        if (clip == null || !Available) return;
        int sample = Mathf.Clamp(Mathf.RoundToInt(sourceSeconds * clip.frequency), 0, Mathf.Max(0, clip.samples - 1));
        Play.Invoke(null, new object[] { clip, sample, loop });
    }
    public static void StopAll() { if (Available) Stop.Invoke(null, null); }
}
