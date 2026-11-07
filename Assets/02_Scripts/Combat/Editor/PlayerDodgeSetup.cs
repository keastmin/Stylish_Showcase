using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlayerDodgeSetup
{
    public const string DefinitionPath = "Assets/07_Datas/Player/PlayerDodge.asset";
    public const string PlayerPath = "Assets/08_Prefabs/Combat/Player.prefab";

    [MenuItem("Tools/Character/Player/Set Up Dodge Studio")]
    public static void SetUp()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode에서 연결하세요.");
        var scenePlayer = UnityEngine.Object.FindAnyObjectByType<PlayerCore>();
        var prefab = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var source = scenePlayer != null ? scenePlayer : prefab.GetComponent<PlayerCore>();
            var definition = AssetDatabase.LoadAssetAtPath<PlayerDodgeDefinition>(DefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<PlayerDodgeDefinition>();
                var legacy = (PerfectDodgeSettings)typeof(PlayerCore).GetField("_perfectDodge", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);
                definition.slow = JsonUtility.FromJson<PerfectDodgeSettings>(JsonUtility.ToJson(legacy));
                definition.counterOpenDelay = legacy.StartDelay;
                definition.counterWindowDuration = legacy.MaxDuration;
                ReadMotion(definition.front, source.Animator);
                ReadMotion(definition.back, source.Animator);
                ReadMotion(definition.counterStart, source.Animator);
                ReadMotion(definition.counterLoop, source.Animator);
                ReadMotion(definition.counterEnd, source.Animator);
                var volume = UnityEngine.Object.FindAnyObjectByType<GlobalVolumeEffectController>();
                if (volume != null)
                {
                    var so = new SerializedObject(volume);
                    var cue = definition.dodgeEffects[0];
                    cue.strength = so.FindProperty("_vignetteIntensity").floatValue;
                    cue.fadeIn = so.FindProperty("_vignetteFadeInDuration").floatValue;
                    cue.hold = so.FindProperty("_vignetteDuration").floatValue;
                    cue.fadeOut = so.FindProperty("_vignetteFadeOutDuration").floatValue;
                    cue.inCurve = so.FindProperty("_vignetteFadeInCurve").animationCurveValue;
                    cue.outCurve = so.FindProperty("_vignetteFadeOutCurve").animationCurveValue;
                }
                if (!AssetDatabase.IsValidFolder("Assets/07_Datas/Player")) AssetDatabase.CreateFolder("Assets/07_Datas", "Player");
                AssetDatabase.CreateAsset(definition, DefinitionPath);
            }
            Connect(prefab.GetComponent<PlayerCore>(), definition, false);
            PrefabUtility.SaveAsPrefabAsset(prefab, PlayerPath);
            if (scenePlayer != null) Connect(scenePlayer, definition, true);
            AssetDatabase.SaveAssetIfDirty(definition);
            PlayerDodgeStudio.OpenDefinition(definition);
            Debug.Log("Dodge Studio 연결 완료. 설정 연결을 해제하면 기존 회피로 돌아갑니다.");
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
    }

    private static void Connect(PlayerCore core, PlayerDodgeDefinition definition, bool undo)
    {
        if (undo) Undo.RecordObject(core, "Connect Dodge Studio");
        var serialized = new SerializedObject(core);
        serialized.FindProperty("_dodgeDefinition").objectReferenceValue = definition;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (core.GetComponent<PlayerDodgeScreenEffects>() == null)
        {
            if (undo) Undo.AddComponent<PlayerDodgeScreenEffects>(core.gameObject);
            else core.gameObject.AddComponent<PlayerDodgeScreenEffects>();
        }
        if (undo)
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(core);
            EditorSceneManager.MarkSceneDirty(core.gameObject.scene);
        }
    }

    internal static void ReadMotion(DodgeMotion motion, Animator animator)
    {
        var controller = animator.runtimeAnimatorController as AnimatorController;
        if (controller == null) throw new InvalidOperationException("AnimatorController를 확인하세요.");
        AnimatorState state = null;
        foreach (var layer in controller.layers) { state = Find(layer.stateMachine, motion.stateName); if (state != null) break; }
        if (state == null || !(state.motion is AnimationClip)) throw new InvalidOperationException(motion.stateName + "의 클립을 찾지 못했습니다.");
        motion.clip = (AnimationClip)state.motion;
        motion.speed = motion.originalStateSpeed = state.speed;
        var events = AnimationUtility.GetAnimationEvents(motion.clip);
        motion.perfectEnd = events.Where(e => e.functionName == "OnPerfectDodgeEndInvoke").Select(e => e.time).DefaultIfEmpty(-1).First();
        motion.actionEnd = events.Where(e => e.functionName == "OnAnimationEndInvoke").Select(e => e.time).DefaultIfEmpty(motion.clip.length).First();
    }

    private static AnimatorState Find(AnimatorStateMachine machine, string name)
    {
        foreach (var state in machine.states) if (state.state.name == name) return state.state;
        foreach (var child in machine.stateMachines) { var result = Find(child.stateMachine, name); if (result != null) return result; }
        return null;
    }

    [MenuItem("Tools/Character/Player/Use Legacy Dodge (Selected Player)")]
    public static void Disconnect()
    {
        var core = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<PlayerCore>() : null;
        if (core == null) core = UnityEngine.Object.FindAnyObjectByType<PlayerCore>();
        if (core == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Undo.RecordObject(core, "Use Legacy Dodge");
        var so = new SerializedObject(core);
        so.FindProperty("_dodgeDefinition").objectReferenceValue = null;
        so.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(core);
        EditorSceneManager.MarkSceneDirty(core.gameObject.scene);
    }
}
