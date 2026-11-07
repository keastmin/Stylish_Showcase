using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>Connects E timing and preserves authored hit identities for independent marker editing.</summary>
public static class PlayerSkillStudioSetup
{
    public const string PlayerPath = "Assets/08_Prefabs/Combat/Player.prefab";
    public const string TimelinePath = "Assets/04_Timelines/Player/SkillTimeline.playable";

    [MenuItem("Tools/Character/Player/Connect E Skill Studio")]
    public static void Connect()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("E 스킬 연결은 편집 모드에서 실행하세요.");

        var prefab = PrefabUtility.LoadPrefabContents(PlayerPath);
        var timelines = new HashSet<TimelineAsset>();
        int added = 0;
        try
        {
            foreach (var receiver in prefab.GetComponentsInChildren<PlayerSkillAttackReceiver>(true))
            {
                CollectTimeline(receiver, timelines);
                if (receiver.GetComponent<PlayerSkillSequenceSettings>() != null) continue;
                receiver.gameObject.AddComponent<PlayerSkillSequenceSettings>();
                added++;
            }
            if (added > 0) PrefabUtility.SaveAsPrefabAsset(prefab, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        // Prefab instances inherit the component. Unpacked scene players get it with Undo.
        foreach (var receiver in UnityEngine.Object.FindObjectsByType<PlayerSkillAttackReceiver>(FindObjectsInactive.Include))
        {
            if (!receiver.gameObject.scene.IsValid() || EditorSceneManager.IsPreviewScene(receiver.gameObject.scene)) continue;
            CollectTimeline(receiver, timelines);
            if (receiver.GetComponent<PlayerSkillSequenceSettings>() != null) continue;
            Undo.AddComponent<PlayerSkillSequenceSettings>(receiver.gameObject);
            EditorSceneManager.MarkSceneDirty(receiver.gameObject.scene);
            added++;
        }
        var authored = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        if (authored != null) timelines.Add(authored);
        int stabilized = timelines.Sum(timeline => UpgradeTimeline(timeline, true));
        Debug.Log($"E Skill Studio 연결 완료 (설정 {added}개 추가, 타격 {stabilized}개 연결 고정). 기존 타격 시각·피해·이펙트·카메라 설정을 유지합니다.");
    }

    /// <summary>Idempotent migration; resolves legacy hit fields before adding row metadata.</summary>
    public static int UpgradeTimeline(TimelineAsset timeline, bool save = false)
    {
        if (timeline == null) return 0;
        int changed = 0;
        foreach (var track in timeline.GetOutputTracks().OfType<PlayerSkillHitTrack>())
            changed += PlayerSkillTimelineUtility.StabilizeHitFields(track, "Connect E Skill Studio hit rows");
        if (changed > 0 && save && AssetDatabase.Contains(timeline)) AssetDatabase.SaveAssetIfDirty(timeline);
        return changed;
    }

    private static void CollectTimeline(PlayerSkillAttackReceiver receiver, HashSet<TimelineAsset> timelines)
    {
        var director = receiver.GetComponent<PlayableDirector>();
        if (director != null && director.playableAsset is TimelineAsset timeline) timelines.Add(timeline);
    }

    public static PlayableDirector FindDirector()
    {
        var selected = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<PlayerCore>() : null;
        var receiver = selected != null ? selected.GetComponentInChildren<PlayerSkillAttackReceiver>(true) : null;
        if (receiver == null)
            receiver = UnityEngine.Object.FindObjectsByType<PlayerSkillAttackReceiver>(FindObjectsInactive.Include)
                .FirstOrDefault(item => item.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewScene(item.gameObject.scene));
        return receiver != null ? receiver.GetComponent<PlayableDirector>() : null;
    }
}
