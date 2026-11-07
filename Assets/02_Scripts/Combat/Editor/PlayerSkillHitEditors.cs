using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEngine;

[CustomEditor(typeof(PlayerSkillAttackReceiver))]
public sealed class PlayerSkillAttackReceiverEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_attackContainer"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_hitboxRoot"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_skillHitImpulse"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_skillHitShakeForce"));
        EditorGUILayout.HelpBox("Skill Hits 마커의 필드 번호가 Hit1~12의 피해와 범위를 선택합니다. Studio 연결 후에는 시간·행 순서를 바꿔도 필드 연결을 유지합니다. 같은 필드를 고른 마커는 아래 설정을 공유합니다.", MessageType.Info);
        SerializedProperty fields = serializedObject.FindProperty("_damageFields");
        fields.arraySize = PlayerSkillAttackReceiver.DamageFieldCount;
        for (int i = 0; i < fields.arraySize; i++)
            EditorGUILayout.PropertyField(fields.GetArrayElementAtIndex(i), new GUIContent($"Hit{i + 1}"), true);
        serializedObject.ApplyModifiedProperties();

        if (GUILayout.Button("Skill Hitbox의 Hit1~Hit12 다시 연결"))
        {
            var root = serializedObject.FindProperty("_hitboxRoot").objectReferenceValue as Transform;
            if (root == null)
                return;
            Undo.RecordObject(target, "Assign Skill Hitboxes");
            var receiver = (PlayerSkillAttackReceiver)target;
            for (int i = 1; i <= PlayerSkillAttackReceiver.DamageFieldCount; i++)
            {
                Transform hit = root.Find($"Hit{i}");
                if (hit != null && hit.TryGetComponent(out Collider collider))
                    receiver.GetDamageField(i)?.AssignHitbox($"Skill Hit{i}", collider);
            }
            EditorUtility.SetDirty(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            EditorSceneManager.MarkSceneDirty(receiver.gameObject.scene);
        }
    }
}

[CustomEditor(typeof(PlayerSkillHitMarker))]
public sealed class PlayerSkillHitMarkerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var marker = (PlayerSkillHitMarker)target;
        EditorGUI.BeginChangeCheck();
        double time = EditorGUILayout.DoubleField("발생 시각 (초)", marker.time);
        if (EditorGUI.EndChangeCheck()) PlayerSkillTimelineUtility.SetMarkerTime(marker, time);
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_enabled"), new GUIContent("타격 사용"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("_damageFieldNumber"), new GUIContent("데미지 필드 (0 = 자동)"));
        serializedObject.ApplyModifiedProperties();

        int number = marker.DamageFieldNumber;
        EditorGUILayout.LabelField("사용할 데미지 필드", $"Hit{number}", EditorStyles.boldLabel);
        if (!marker.Enabled) EditorGUILayout.HelpBox("이 마커의 타격은 실행하지 않습니다. 연결된 필드와 다른 마커는 유지합니다.", MessageType.Info);
        var director = TimelineEditor.inspectedDirector;
        var receiver = director != null && marker.parent != null
            ? director.GetGenericBinding(marker.parent) as PlayerSkillAttackReceiver : null;
        if (receiver == null)
        {
            EditorGUILayout.HelpBox("씬의 Skill Director를 선택하여 Timeline을 열면 이곳에서 Hit 설정도 편집할 수 있습니다.", MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox($"아래 설정은 Skill Director의 Hit{number}를 사용하는 모든 마커가 공유합니다.", MessageType.Info);
        var receiverProperties = new SerializedObject(receiver);
        receiverProperties.Update();
        var fields = receiverProperties.FindProperty("_damageFields");
        if (number <= fields.arraySize)
            EditorGUILayout.PropertyField(fields.GetArrayElementAtIndex(number - 1), new GUIContent($"Hit{number} 설정"), true);
        receiverProperties.ApplyModifiedProperties();
    }
}
