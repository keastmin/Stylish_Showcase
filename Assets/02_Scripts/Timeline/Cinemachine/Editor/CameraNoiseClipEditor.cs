using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CameraNoiseClip))]
public sealed class CameraNoiseClipEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var property = serializedObject.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (property.name == nameof(CameraNoiseClip.Profile))
            {
                // Cinemachine's NoiseSettings drawer only supplies UI Toolkit UI;
                // Timeline embeds this clip inspector through IMGUI.
                property.objectReferenceValue = EditorGUILayout.ObjectField(
                    new GUIContent(property.displayName, property.tooltip), property.objectReferenceValue,
                    typeof(NoiseSettings), false);
            }
            else
            {
                using (new EditorGUI.DisabledScope(property.name == "m_Script"))
                    EditorGUILayout.PropertyField(property, true);
            }
        }
        serializedObject.ApplyModifiedProperties();
        var clip = (CameraNoiseClip)target;
        EditorGUILayout.HelpBox("구간은 Timeline 클립의 Start/Duration으로 조절합니다. Direction=(0,0,1)은 카메라 기준 앞뒤 진동입니다. 같은 시간은 같은 진동으로 미리보기됩니다.", MessageType.Info);
        if (clip.Waveform == CameraNoiseClip.NoiseWaveform.Profile)
            EditorGUILayout.HelpBox("Profile의 위치 Z 진동 × Amplitude를 사용합니다. Frequency는 프로필 주파수의 배율입니다. 회전 진동은 적용하지 않습니다.", MessageType.Info);
        if (clip.FinisherEnabled)
            EditorGUILayout.HelpBox("Finisher Position은 클립 진행률입니다. 충격 지속 시간은 클립 끝을 넘지 않도록 제한됩니다.", MessageType.Info);
    }
}
