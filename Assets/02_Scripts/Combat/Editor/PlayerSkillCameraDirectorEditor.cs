using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

[CustomEditor(typeof(PlayerSkillCameraDirector))]
public sealed class PlayerSkillCameraDirectorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var controller = (PlayerSkillCameraDirector)target;
        if (controller.SkillCamera == null || controller.TPSOrbit == null)
            EditorGUILayout.HelpBox("씬의 Skill Camera와 TPS Orbital Follow를 연결하세요. 외부 씬 카메라 참조는 플레이어 인스턴스에서 지정합니다.", MessageType.Info);
        var player = controller.GetComponentInParent<PlayerCore>();
        if (controller.SkillCamera != null && player != null && controller.SkillCamera.transform.IsChildOf(player.transform))
            EditorGUILayout.HelpBox("Skill Camera는 움직이는 Player 하이어라키 밖에 배치해야 합니다.", MessageType.Warning);
        if (controller.PreviewOrigin == null)
            EditorGUILayout.HelpBox("Preview Skill Origin을 연결하면 스크러빙 중에도 시작 위치와 방향이 고정됩니다.", MessageType.Warning);
        var clip = controller.ActivationTrack?.GetClips().FirstOrDefault();
        var timeline = controller.ActivationTrack?.timelineAsset;
        var track = timeline?.GetOutputTracks().OfType<SkillCameraSignalTrack>().FirstOrDefault();
        if (clip != null && track != null)
        {
            var markers = track.GetMarkers().OfType<SignalEmitter>().ToArray();
            var position = markers.FirstOrDefault(marker => marker.asset == controller.PositionSignal);
            var recenter = markers.FirstOrDefault(marker => marker.asset == controller.RecenterSignal);
            if (position == null || position.time >= clip.start)
                EditorGUILayout.HelpBox("위치 설정 시그널은 카메라 활성 클립보다 앞에 배치하세요.", MessageType.Warning);
            if (recenter == null || recenter.time >= clip.end ||
                (controller.RecenterMode == PlayerSkillCameraDirector.RecenterModes.Smooth &&
                    clip.end - recenter.time < controller.RecenterDuration))
                EditorGUILayout.HelpBox("TPS 리센터가 카메라 활성 클립 종료 전에 완료되도록 시그널 시점을 조절하세요.", MessageType.Warning);
        }
    }
}
