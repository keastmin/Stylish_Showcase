using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>Extracts actual retargeted Animator root motion, without changing source clips or scene objects.</summary>
public static class UltimateRootMotionBaker
{
    private const string Version = "ultimate-root-1";

    public static bool IsCurrent(PlayerUltimateDefinition definition, PlayerCore player)
    {
        if (definition == null || !definition.HasSequence || player == null || player.Animator == null) return false;
        foreach (var entry in definition.sequence)
            if (!IsCurrent(entry, player)) return false;
        return true;
    }

    public static bool IsCurrent(UltimateSequenceClip entry, PlayerCore player)
        => entry != null && entry.clip != null && player != null && player.Animator != null &&
           entry.rootSamples != null && entry.rootSamples.Count > 1 && entry.rootBakeKey == Key(entry.clip, player.Animator);

    public static string Bake(PlayerUltimateDefinition definition, PlayerCore player)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("루트 이동 추출은 Play Mode를 종료한 뒤 실행하세요.");
        if (definition == null || !definition.HasSequence) throw new InvalidOperationException("시퀀스에 애니메이션 클립을 추가하세요.");
        if (player == null || player.Animator == null || player.Animator.avatar == null)
            throw new InvalidOperationException("Avatar가 있는 플레이어를 연결하세요.");
        // Stage all results first: a failed extraction cannot leave a half-updated sequence.
        var results = new Dictionary<AnimationClip, List<UltimateRootSample>>();
        foreach (var entry in definition.sequence)
        {
            if (entry == null || entry.clip == null) throw new InvalidOperationException("비어 있는 시퀀스 클립을 지정하거나 삭제하세요.");
            if (!results.ContainsKey(entry.clip)) results.Add(entry.clip, Extract(entry.clip, player.Animator));
        }
        int sampleCount = 0;
        foreach (var entry in definition.sequence)
        {
            entry.rootSamples = new List<UltimateRootSample>(results[entry.clip]);
            entry.rootBakeKey = Key(entry.clip, player.Animator);
            sampleCount += entry.rootSamples.Count;
        }
        EditorUtility.SetDirty(definition);
        return $"{definition.sequence.Count}개 클립 · {sampleCount}개 루트 샘플 추출 완료";
    }

    public static List<UltimateRootSample> Extract(AnimationClip source, Animator playerAnimator)
    {
        if (source == null || playerAnimator == null) throw new ArgumentNullException();
        GameObject holder = null;
        AnimationClip copy = null;
        PlayableGraph graph = default;
        try
        {
            holder = UltimatePreviewModel.Create(playerAnimator, out var model, out var animator);
            holder.name = "Ultimate root extraction (temporary)";
            holder.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            copy = UnityEngine.Object.Instantiate(source);
            copy.hideFlags = HideFlags.HideAndDontSave;
            var settings = AnimationUtility.GetAnimationClipSettings(copy);
            settings.loopTime = false;
            settings.loopBlend = false;
            settings.startTime = 0f;
            settings.stopTime = source.length;
            AnimationUtility.SetAnimationClipSettings(copy, settings);
            animator.applyRootMotion = true;
            graph = PlayableGraph.Create("Ultimate root extraction");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationClipPlayable.Create(graph, copy);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            playable.SetDuration(source.length);
            var output = AnimationPlayableOutput.Create(graph, "Root sampler", animator);
            output.SetSourcePlayable(playable);
            graph.Play();
            playable.SetTime(0);
            graph.Evaluate(0);
            // Discard clip entry offsets. Root deltas are already in this model's world basis.
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            var samples = new List<UltimateRootSample> { new UltimateRootSample { time = 0, position = position, rotation = rotation } };
            int frames = Mathf.Max(1, Mathf.CeilToInt(source.length * Mathf.Max(60, source.frameRate)));
            float previousTime = 0;
            for (int frame = 1; frame <= frames; frame++)
            {
                float time = source.length * frame / frames;
                graph.Evaluate(time - previousTime);
                Vector3 delta = animator.deltaPosition;
                Quaternion turn = animator.deltaRotation;
                if (!Finite(delta.x) || !Finite(delta.y) || !Finite(delta.z))
                    throw new InvalidOperationException(source.name + ": 루트 이동에 유효하지 않은 값이 있습니다.");
                position += delta;
                rotation = rotation * turn;
                samples.Add(new UltimateRootSample { time = time, position = position, rotation = rotation });
                previousTime = time;
            }
            return samples;
        }
        finally
        {
            if (graph.IsValid()) graph.Destroy();
            if (holder != null) UnityEngine.Object.DestroyImmediate(holder);
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static string Key(AnimationClip clip, Animator animator)
    {
        var scale = animator.transform.lossyScale;
        string clipPath = AssetDatabase.GetAssetPath(clip);
        string avatarPath = AssetDatabase.GetAssetPath(animator.avatar);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string clipGuid, out long clipId);
        return Version + "|" + clipGuid + ":" + clipId + ":" + clip.name + "|" + AssetDatabase.GetAssetDependencyHash(clipPath) +
               "|" + AssetDatabase.AssetPathToGUID(avatarPath) + "|" + AssetDatabase.GetAssetDependencyHash(avatarPath) +
               "|" + animator.avatar.name + "|" + scale.x.ToString("R", CultureInfo.InvariantCulture) + "," +
               scale.y.ToString("R", CultureInfo.InvariantCulture) + "," + scale.z.ToString("R", CultureInfo.InvariantCulture);
    }
}

/// <summary>Build under an inactive parent so copied gameplay behaviours never run.</summary>
internal static class UltimatePreviewModel
{
    public static GameObject Create(Animator source, out GameObject model, out Animator animator)
    {
        var holder = new GameObject("Ultimate Studio • disposable preview") { hideFlags = HideFlags.HideAndDontSave };
        holder.SetActive(false);
        model = UnityEngine.Object.Instantiate(source.gameObject, holder.transform, false);
        model.name = "Ultimate visual model";
        foreach (var item in model.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(item);
        foreach (var item in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(item);
        foreach (var item in model.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(item);
        foreach (var item in model.GetComponentsInChildren<AudioSource>(true)) UnityEngine.Object.DestroyImmediate(item);
        foreach (var item in model.GetComponentsInChildren<AudioListener>(true)) UnityEngine.Object.DestroyImmediate(item);
        foreach (var item in model.GetComponentsInChildren<Camera>(true)) UnityEngine.Object.DestroyImmediate(item);
        foreach (var item in model.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(item);
        foreach (var particles in model.GetComponentsInChildren<ParticleSystem>(true))
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            if (renderer != null) renderer.enabled = false;
        }
        foreach (var item in model.GetComponentsInChildren<TrailRenderer>(true)) item.enabled = false;
        foreach (var item in model.GetComponentsInChildren<LineRenderer>(true)) item.enabled = false;
        foreach (var transform in holder.GetComponentsInChildren<Transform>(true)) transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = source.transform.lossyScale;
        animator = model.GetComponent<Animator>();
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.fireEvents = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.enabled = true;
        model.SetActive(true);
        holder.SetActive(true);
        return holder;
    }
}
