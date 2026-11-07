using System;
using UnityEditor;
using UnityEngine;

public static class VirtualCutsceneSetup
{
    public const string DefaultPath = "Assets/07_Datas/Player/UltimateCutscene.asset";
    public const string FloorPath = "Assets/07_Datas/Player/UltimateCutsceneFloor.mat";

    [MenuItem("Tools/Character/Player/Set Up Q Cutscene Studio")]
    public static void SetupMenu() { var definition = EnsureDefault(); VirtualCutsceneStudio.OpenDefinition(definition); }

    public static VirtualCutsceneDefinition EnsureDefault()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("컷씬 연결은 Edit Mode에서 실행하세요.");
        var ultimate = AssetDatabase.LoadAssetAtPath<PlayerUltimateDefinition>(PlayerUltimateSetup.DefinitionPath);
        if (ultimate == null) throw new InvalidOperationException("기존 PlayerUltimate 에셋이 없습니다.");
        if (ultimate.cutscene != null) return ultimate.cutscene;
        var definition = AssetDatabase.LoadAssetAtPath<VirtualCutsceneDefinition>(DefaultPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<VirtualCutsceneDefinition>();
            definition.renderLayer = EnsureLayer();
            var clip = ultimate.HasSequence ? ultimate.sequence[0].clip : ultimate.clip;
            if (clip != null)
                definition.animations.Add(new VirtualCutsceneAnimation { clip = clip, name = clip.name,
                    sourceStart = ultimate.HasSequence ? ultimate.sequence[0].SourceStart : 0, duration = definition.duration, speed = .7f });
            definition.shots[0].name = "01 · 얼굴에서 무기로";
            definition.shots[0].duration = 1.05f;
            definition.shots[0].position = new Vector3(.45f, 1.65f, 1.9f);
            definition.shots[0].lookAt = new Vector3(0, 1.35f, 0);
            definition.shots[0].endPosition = new Vector3(.8f, 1.4f, 2.5f);
            definition.shots[0].endLookAt = new Vector3(0, 1.1f, 0);
            definition.shots[0].fieldOfView = 36;
            definition.shots[0].endFieldOfView = 42;
            definition.shots.Add(new VirtualCutsceneShot { name = "02 · 전신 / 복귀", start = 1.05f, duration = .75f,
                position = new Vector3(-1.1f, 1.15f, 3.5f), endPosition = new Vector3(-.5f, 1.25f, 2.8f),
                lookAt = new Vector3(0, .95f, 0), endLookAt = new Vector3(0, 1.05f, 0), fieldOfView = 48, endFieldOfView = 40 });
            var sound = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/12_SFX/Player Skill/Impact_sfx_2.wav");
            if (sound != null) definition.sounds.Add(new VirtualCutsceneSound { clip = sound, name = "전환 임팩트", start = 1.05f,
                duration = .7f, volume = .45f, fadeOut = .15f });
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                var floor = AssetDatabase.LoadAssetAtPath<Material>(FloorPath);
                if (floor == null)
                {
                    floor = new Material(shader) { name = "Ultimate Cutscene Floor", color = definition.floorColor };
                    AssetDatabase.CreateAsset(floor, FloorPath);
                }
                definition.floorMaterial = floor;
            }
            AssetDatabase.CreateAsset(definition, DefaultPath);
        }
        Undo.RecordObject(ultimate, "Connect Q Cutscene");
        ultimate.cutscene = definition;
        EditorUtility.SetDirty(ultimate);
        AssetDatabase.SaveAssetIfDirty(ultimate);
        AssetDatabase.SaveAssetIfDirty(definition);
        return definition;
    }

    public static int EnsureLayer()
    {
        const string label = "Cutscene Studio";
        int existing = LayerMask.NameToLayer(label);
        if (existing >= 8) return existing;
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = settings.FindProperty("layers");
        int selected = -1;
        if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(30).stringValue)) selected = 30;
        else for (int i = 31; i >= 8; i--) if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) { selected = i; break; }
        if (selected < 0) throw new InvalidOperationException("컷씬 전용 레이어를 만들 빈 사용자 레이어가 없습니다.");
        layers.GetArrayElementAtIndex(selected).stringValue = label;
        settings.ApplyModifiedProperties();
        return selected;
    }
}
