using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[CustomEditor(typeof(RendererFeatureActivationTarget), true), CanEditMultipleObjects]
public sealed class RendererFeatureActivationTargetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

        SerializedProperty renderer = serializedObject.FindProperty("_rendererData");
        SerializedProperty feature = serializedObject.FindProperty("_feature");
        EditorGUILayout.PropertyField(renderer, new GUIContent("Renderer Data"));

        if (!renderer.hasMultipleDifferentValues && renderer.objectReferenceValue is ScriptableRendererData data)
        {
            var features = data.rendererFeatures.Where(item => item != null).ToArray();
            string[] names = new[] { "None" }.Concat(features.Select(item => item.name)).ToArray();
            int current = System.Array.IndexOf(features, feature.objectReferenceValue) + 1;
            EditorGUI.showMixedValue = feature.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup("Renderer Feature", current, names);
            if (EditorGUI.EndChangeCheck())
                feature.objectReferenceValue = selected == 0 ? null : features[selected - 1];
            EditorGUI.showMixedValue = false;
        }
        else
            EditorGUILayout.PropertyField(feature, new GUIContent("Renderer Feature"));

        serializedObject.ApplyModifiedProperties();
        foreach (RendererFeatureActivationTarget adapter in targets)
        {
            if (!adapter.IsConfigured)
            {
                EditorGUILayout.HelpBox("Renderer Data와 그 안의 Renderer Feature를 선택하세요.", MessageType.Warning);
                break;
            }
        }
        EditorGUILayout.HelpBox("Activation 클립 안에서 ON, 빈 구간에서 OFF입니다. 종료하면 원래 상태로 복원합니다. 이 Renderer를 공유하는 카메라에 적용됩니다.", MessageType.Info);
    }
}
