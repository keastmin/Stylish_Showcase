using System;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit, idempotent migration of the original Ultimate prototype.</summary>
public static class UltimateSequenceSetup
{
    public const string JumpStart = "Assets/Download Assets/Scythe Animation Pack/Animations/Humanoid/05_Jump/02_Jump_Combat/01_Jump_Combat_0/Jump_Combat_Start_0.anim";
    public const string AirAttack = "Assets/Download Assets/Scythe Animation Pack/Animations/Humanoid/02_Attack/07_Combo_Attack_Air/Combo_Attack_Air_All.anim";
    public const string Landing = "Assets/Download Assets/Scythe Animation Pack/Animations/Humanoid/05_Jump/02_Jump_Combat/01_Jump_Combat_0/Jump_End_F_0.anim";

    public static string Migrate(PlayerUltimateDefinition definition, PlayerCore player)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        if (definition.HasSequence) return "기존 시퀀스를 유지했습니다.";
        var jump = Load(JumpStart, "점프 시작");
        var attack = Load(AirAttack, "공중 콤보");
        var landing = Load(Landing, "착지");
        Undo.RegisterCompleteObjectUndo(definition, "Create Ultimate animation sequence");
        definition.sequence.Add(jump);
        definition.sequence.Add(attack);
        definition.sequence.Add(landing);
        // The hand-merged clip inserted one extra frame at each join. Preserve source-pose
        // timestamps, not the merged clip's leading duration or the new sequence duration.
        const float oldAttackStart = 26f / 60f;
        const float oldLandingStart = 212f / 60f;
        if (definition.hits != null)
            foreach (var hit in definition.hits)
            {
                if (hit == null) continue;
                var target = hit.time >= oldLandingStart ? landing : hit.time >= oldAttackStart ? attack : jump;
                float offset = target == landing ? oldLandingStart : target == attack ? oldAttackStart : 0;
                var copy = JsonUtility.FromJson<UltimateHitField>(JsonUtility.ToJson(hit));
                copy.time = Mathf.Clamp(hit.time - offset, 0, target.clip.length);
                target.hits.Add(copy);
            }
        if (definition.cues != null)
            foreach (var cue in definition.cues)
            {
                if (cue == null) continue;
                var target = cue.time >= oldLandingStart ? landing : cue.time >= oldAttackStart ? attack : jump;
                float offset = target == landing ? oldLandingStart : target == attack ? oldAttackStart : 0;
                var copy = JsonUtility.FromJson<UltimateCue>(JsonUtility.ToJson(cue));
                copy.time = Mathf.Clamp(cue.time - offset, 0, target.clip.length);
                target.cues.Add(copy);
            }
        var result = UltimateRootMotionBaker.Bake(definition, player);
        EditorUtility.SetDirty(definition);
        return "점프 → 공중 콤보 → 착지. " + result;
    }

    private static UltimateSequenceClip Load(string path, string label)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) throw new InvalidOperationException(path + " 클립을 찾을 수 없습니다.");
        return new UltimateSequenceClip { id = Guid.NewGuid().ToString("N"), label = label, clip = clip,
            sourceStart = 0, sourceEnd = -1, speed = 1, rootMotion = true,
            applyRootRotation = true, applyRootHeight = false, poseBlendIn = 0 };
    }
}
