using System;
using UnityEditor;
using UnityEngine;

/// <summary>Idempotent first-run setup; existing authored hit markers are preserved.</summary>
public static class PlayerUltimateSetup
{
    public const string DefinitionPath = "Assets/07_Datas/Player/PlayerUltimate.asset";
    public const string PlayerPath = "Assets/08_Prefabs/Combat/Player.prefab";
    public const string ClipPath = "Assets/03_Animations/Player/Clips/Attack/Ultimate.anim";

    [MenuItem("Tools/Character/Player/Set Up Ultimate Prototype")]
    public static void SetUp()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("궁극기 연결은 Edit Mode에서 실행하세요.");
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null) throw new InvalidOperationException("Ultimate 클립을 찾을 수 없습니다.");
        if (!AssetDatabase.IsValidFolder("Assets/07_Datas/Player"))
            AssetDatabase.CreateFolder("Assets/07_Datas", "Player");

        var definition = AssetDatabase.LoadAssetAtPath<PlayerUltimateDefinition>(DefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<PlayerUltimateDefinition>();
            definition.clip = clip;
            definition.hits.Add(new UltimateHitField {
                name = "01 · 베기", time = 0.75f,
                position = new Vector3(0f, 1f, 1.6f), size = new Vector3(3.6f, 2f, 3f),
                euler = new Vector3(0f, -20f, 0f), damage = 35f, hitStopFrames = 2 });
            definition.hits.Add(new UltimateHitField {
                name = "02 · 회전", time = 1.6f,
                shape = UltimateFieldShape.Sphere, position = new Vector3(0f, 1f, 0.6f),
                radius = 2.6f, damage = 45f, hitStopFrames = 3 });
            definition.hits.Add(new UltimateHitField {
                name = "03 · 마무리", time = 2.65f,
                position = new Vector3(0f, 1f, 2f), size = new Vector3(4f, 2.4f, 4.5f),
                damage = 90f, hitStopFrames = 5 });
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }

        var root = PrefabUtility.LoadPrefabContents(PlayerPath);
        try
        {
            var core = root.GetComponent<PlayerCore>();
            var serialized = new SerializedObject(core);
            serialized.FindProperty("_ultimateDefinition").objectReferenceValue = definition;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssetIfDirty(definition);
        Debug.Log("Ultimate E 연결 완료. Tools > Character > Player > E - Ultimate Studio에서 조절하세요.");
    }
}
