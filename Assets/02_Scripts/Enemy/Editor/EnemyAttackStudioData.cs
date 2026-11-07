using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>Reads the same assets and animation events used by the enemy runtime.</summary>
internal static class EnemyAttackStudioData
{
    public static EnemyAttackSO[] FindAttacks(EnemyCore enemy)
    {
        if (enemy == null) return Array.Empty<EnemyAttackSO>();
        var serialized = new SerializedObject(enemy);
        var attacks = serialized.FindProperty("_attackSOs");
        if (attacks == null || !attacks.isArray) return Array.Empty<EnemyAttackSO>();

        var result = new List<EnemyAttackSO>();
        for (int i = 0; i < attacks.arraySize; i++)
        {
            var attack = attacks.GetArrayElementAtIndex(i).objectReferenceValue as EnemyAttackSO;
            if (attack != null && !result.Contains(attack)) result.Add(attack);
        }
        return result.ToArray();
    }

    public static AnimationClip[] FindClips(EnemyCore enemy, EnemyAttackSO attack)
    {
        if (enemy == null || attack == null) return Array.Empty<AnimationClip>();
        var animator = enemy.GetComponentInChildren<Animator>(true);
        var controller = animator != null ? animator.runtimeAnimatorController : null;
        if (controller == null) return Array.Empty<AnimationClip>();

        var candidates = new List<AnimationClip>();
        if (controller is AnimatorOverrideController overrides)
        {
            // Only inspect effective clips: an overridden base clip no longer
            // supplies the events that will run on this enemy.
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrides.overridesCount);
            overrides.GetOverrides(pairs);
            foreach (var pair in pairs) candidates.Add(pair.Value != null ? pair.Value : pair.Key);
        }
        else candidates.AddRange(controller.animationClips);

        return candidates.Where(clip => clip != null && FindAttackEventIndex(clip, attack) >= 0)
            .Distinct().OrderBy(clip => clip.name, StringComparer.Ordinal).ToArray();
    }

    public static int FindAttackEventIndex(AnimationClip clip, EnemyAttackSO attack)
    {
        if (clip == null || attack == null) return -1;
        var events = AnimationUtility.GetAnimationEvents(clip);
        for (int i = 0; i < events.Length; i++)
            if (events[i].functionName == "OnAttack" && events[i].objectReferenceParameter == attack)
                return i;
        return -1;
    }

    /// <returns>The source-clip time in seconds, or -1 when no matching event exists.</returns>
    public static float GetHitTime(AnimationClip clip, EnemyAttackSO attack)
    {
        int index = FindAttackEventIndex(clip, attack);
        return index >= 0 ? AnimationUtility.GetAnimationEvents(clip)[index].time : -1f;
    }

    /// <summary>
    /// Earliest unconditional transition start, expressed in the effective clip's
    /// source seconds. A hit after this point may be skipped or blend-dependent.
    /// BlendTree timing is deliberately unknown because its duration is dynamic.
    /// </summary>
    public static float GetEarliestExitTime(EnemyCore enemy, AnimationClip clip)
    {
        if (enemy == null || clip == null) return -1f;
        var animator = enemy.GetComponentInChildren<Animator>(true);
        RuntimeAnimatorController runtime = animator != null ? animator.runtimeAnimatorController : null;
        var overrides = new List<List<KeyValuePair<AnimationClip, AnimationClip>>>();
        var visited = new HashSet<RuntimeAnimatorController>();
        while (runtime is AnimatorOverrideController replacement && visited.Add(runtime))
        {
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>(replacement.overridesCount);
            replacement.GetOverrides(pairs);
            overrides.Add(pairs);
            runtime = replacement.runtimeAnimatorController;
        }
        if (!(runtime is AnimatorController controller)) return -1f;

        float earliest = float.PositiveInfinity;
        foreach (var layer in controller.layers)
            FindEarliestExit(layer.stateMachine, clip, overrides, ref earliest);
        return float.IsPositiveInfinity(earliest) ? -1f : earliest;
    }

    private static void FindEarliestExit(AnimatorStateMachine machine, AnimationClip clip,
        List<List<KeyValuePair<AnimationClip, AnimationClip>>> overrides, ref float earliest)
    {
        if (machine == null) return;
        foreach (var child in machine.states)
        {
            var state = child.state;
            if (state == null || !(state.motion is AnimationClip original)) continue;
            AnimationClip effective = original;
            // Walk from the base controller outward. Null overrides preserve the
            // effective motion inherited from the underlying controller.
            for (int i = overrides.Count - 1; i >= 0; i--)
            {
                var pair = overrides[i].FirstOrDefault(item => item.Key == effective);
                if (pair.Key == null) pair = overrides[i].FirstOrDefault(item => item.Key == original);
                if (pair.Value != null) effective = pair.Value;
            }
            if (effective != clip) continue;
            foreach (var transition in state.transitions)
            {
                if (transition == null || transition.mute || !transition.hasExitTime ||
                    transition.conditions.Length != 0 || transition.exitTime < 0f || transition.exitTime > 1f)
                    continue;
                earliest = Mathf.Min(earliest, transition.exitTime * clip.length);
            }
        }
        foreach (var child in machine.stateMachines)
            FindEarliestExit(child.stateMachine, clip, overrides, ref earliest);
    }

    public static bool CanEditEvents(AnimationClip clip)
    {
        if (clip == null || !AssetDatabase.Contains(clip)) return false;
        string path = AssetDatabase.GetAssetPath(clip);
        if (!path.StartsWith("Assets/", StringComparison.Ordinal) ||
            !string.Equals(Path.GetExtension(path), ".anim", StringComparison.OrdinalIgnoreCase) ||
            AssetDatabase.LoadMainAssetAtPath(path) != clip || !AssetDatabase.IsOpenForEdit(clip))
            return false;

        string absolute = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, path);
        try
        {
            return File.Exists(absolute) && (File.GetAttributes(absolute) & FileAttributes.ReadOnly) == 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static bool SetAttackEventTime(AnimationClip clip, EnemyAttackSO attack, float time)
    {
        if (!CanEditEvents(clip) || float.IsNaN(time) || float.IsInfinity(time)) return false;
        int index = FindAttackEventIndex(clip, attack);
        if (index < 0) return false;

        var events = AnimationUtility.GetAnimationEvents(clip);
        float clamped = Mathf.Clamp(time, 0f, clip.length);
        if (Mathf.Approximately(events[index].time, clamped)) return true;

        Undo.RegisterCompleteObjectUndo(clip, "Change enemy attack hit time");
        events[index].time = clamped;
        // OrderBy is stable so events sharing a timestamp retain their order.
        // Every event payload, including unrelated SFX/trail/end events, survives.
        AnimationUtility.SetAnimationEvents(clip, events.OrderBy(item => item.time).ToArray());
        EditorUtility.SetDirty(clip);
        return true;
    }
}
