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
        EditorGUILayout.HelpBox("Skill Hits의 마커가 시간순으로 Hit1~12를 사용합니다. 13번째 이후는 Hit12를 반복 타격합니다. 각 타격의 공격 단계는 Stagger Level로 설정합니다.", MessageType.Info);
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
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();

        var marker = (PlayerSkillHitMarker)target;
        int number = marker.DamageFieldNumber;
        EditorGUILayout.LabelField("사용할 데미지 필드", $"Hit{number}", EditorStyles.boldLabel);
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
