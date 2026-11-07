using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

public sealed partial class PlayerSkillStudio
{
    [SerializeField] private int _dataTab, _dataHit, _dataPresentationTab;
    private PlayerSkillHit _dataDragHit;
    private int _dataDragControl, _dataDragUndo = -1;
    private float _dataDragX, _dataDragTime;
    private Vector2 _dataTimelineScroll;
    private ESkillDataPreview _dataPreview;
    private PlayerSkillDefinition Data => _director != null
        ? _director.GetComponent<PlayerSkillSequenceSettings>()?.Definition : null;

    private void DrawDataConnection()
    {
        if (_director == null) return;
        var settings = _director.GetComponent<PlayerSkillSequenceSettings>();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            EditorGUI.BeginChangeCheck();
            var next = (PlayerSkillDefinition)EditorGUILayout.ObjectField("E 조정 데이터", Data, typeof(PlayerSkillDefinition), false);
            if (EditorGUI.EndChangeCheck())
            {
                EndPreview();
                if (settings == null) settings = Undo.AddComponent<PlayerSkillSequenceSettings>(_director.gameObject);
                Undo.RecordObject(settings, "Connect E gameplay data");
                settings.Definition = next; Mark(settings); Repaint();
            }
            if (Data == null)
            {
                EditorGUILayout.HelpBox("E 이동·타격·화면 효과·입력 시간을 데이터로 분리합니다. 기존 Timeline은 애니메이션과 연출에 사용합니다.", MessageType.Info);
                if (GUILayout.Button("기존 E 설정을 데이터로 분리 연결"))
                {
                    EndPreview();
                    PlayerSkillDataMigration.Connect();
                    Repaint();
                    GUIUtility.ExitGUI();
                }
            }
        }
    }

    private void DrawDataStudio()
    {
        if (Data == null) return;
        if (Data.hits != null && Data.hits.Count > 0) _dataHit = Mathf.Clamp(_dataHit, 0, Data.hits.Count - 1);
        DrawDataTransport();
        DrawDataHitTimeline();
        _dataTab = GUILayout.Toolbar(_dataTab, new[] { "타격 · 범위", "이동 · 입력", "화면 효과", "연출 · 실행" });
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        float previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = Mathf.Min(270f, position.width * .43f);
        try
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (_dataTab == 0) DrawDataHits();
                else if (_dataTab == 1) DrawDataMotionAndInput();
                else if (_dataTab == 2) DrawDataScreenEffects();
                else DrawDataPresentation();
            }
            if (_dataTab == 3) DrawDataPlayControls();
        }
        finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
        EditorGUILayout.EndScrollView();
        DrawDataFooter();
    }

    private void DrawDataTransport()
    {
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(_dataPreview != null ? "모델 다시 보기" : "모델 미리보기", EditorStyles.toolbarButton, GUILayout.Width(105))) StartDataPreview(true);
                if (_dataPreview != null && GUILayout.Button("닫기", EditorStyles.toolbarButton, GUILayout.Width(38))) EndPreview();
                if (GUILayout.Button(_playing ? "Ⅱ 정지" : "▶ 재생", EditorStyles.toolbarButton, GUILayout.Width(65)))
                {
                    if (_playing) _playing = false;
                    else
                    {
                        if (_dataPreview == null) StartDataPreview(false);
                        if (_time >= Duration) SampleDataPreview(0, false);
                        _lastTick = EditorApplication.timeSinceStartup; _playing = true;
                    }
                }
                if (GUILayout.Button("|◀", EditorStyles.toolbarButton, GUILayout.Width(28))) Seek(0);
                if (GUILayout.Button("−1f", EditorStyles.toolbarButton, GUILayout.Width(36))) Seek(_time - 1 / FrameRate);
                if (GUILayout.Button("+1f", EditorStyles.toolbarButton, GUILayout.Width(36))) Seek(_time + 1 / FrameRate);
                _loop = GUILayout.Toggle(_loop, "반복", EditorStyles.toolbarButton, GUILayout.Width(39));
                GUILayout.FlexibleSpace();
                GUILayout.Label("관찰 배속", EditorStyles.miniLabel);
                _previewSpeed = Mathf.Clamp((float)Finite(EditorGUILayout.FloatField(_previewSpeed, GUILayout.Width(38)), 1), .05f, 3f);
            }
            EditorGUI.BeginChangeCheck();
            double next = EditorGUILayout.Slider("E 데이터 시각", (float)_time, 0, (float)Duration);
            if (EditorGUI.EndChangeCheck()) Seek(next);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{_time:0.000} / {Duration:0.000}초 · {Math.Round(_time * FrameRate)}f / {Math.Round(Duration * FrameRate)}f · {FrameRate:0.##}fps", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                _snap = GUILayout.Toggle(_snap, "프레임 스냅", GUILayout.Width(92));
            }
        }
    }

    private void DrawDataHitTimeline()
    {
        var hits = Data.hits;
        int count = hits?.Count ?? 0;
        float height = 52 + 24 * Math.Max(1, count);
        _dataTimelineScroll = EditorGUILayout.BeginScrollView(_dataTimelineScroll,
            GUILayout.Height(Mathf.Min(height, Mathf.Clamp(position.height * .25f, 120f, 220f))));
        Rect area = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
        float left = area.x + SkillStudioGUI.TimelineLabelWidth;
        float width = Mathf.Max(1, area.width - SkillStudioGUI.TimelineLabelWidth - 14);
        EditorGUI.DrawRect(area, SkillStudioGUI.TimelineBackground);
        for (int i = 0; i <= 8; i++)
        {
            float x = left + width * i / 8;
            EditorGUI.DrawRect(new Rect(x, area.y + 19, 1, area.height - 19), SkillStudioGUI.TimelineGrid);
            GUI.Label(new Rect(x - 10, area.y, 45, 18), (Duration * i / 8).ToString("0.00"), EditorStyles.miniLabel);
        }
        GUI.Label(new Rect(area.x + 5, area.y, 155, 18), "E 타격 · 데이터 시각", EditorStyles.miniLabel);
        GUI.Label(new Rect(area.x + 5, area.y + 24, 155, 18), "이동 / 입력 허용", EditorStyles.miniLabel);
        if (Data.motion != null && Data.motion.enabled)
        {
            float startX = left + Mathf.Clamp01(Data.motion.start / Data.Duration) * width;
            float endX = left + Mathf.Clamp01(Data.motion.end / Data.Duration) * width;
            EditorGUI.DrawRect(new Rect(startX, area.y + 29, Mathf.Max(2, endX - startX), 9), SkillStudioGUI.PathColor);
        }
        if (Data.useInputCancelTime)
        {
            float x = left + Data.InputCancelTime / Data.Duration * width;
            EditorGUI.DrawRect(new Rect(x, area.y + 20, 2, area.height - 20), Color.yellow);
        }
        int control = GUIUtility.GetControlID(FocusType.Passive);
        Event ev = Event.current;
        bool editable = !EditorApplication.isPlayingOrWillChangePlaymode;
        for (int i = 0; i < count; i++)
        {
            PlayerSkillHit hit = hits[i];
            if (hit == null) continue;
            float y = area.y + 48 + i * 24;
            if (GUI.Button(new Rect(area.x + 5, y, 105, 21), $"{i + 1:00}  {hit.name}", _dataHit == i ? EditorStyles.toolbarButton : EditorStyles.miniLabel))
                SelectDataHit(i);
            using (new EditorGUI.DisabledScope(!editable || i == 0 || _dataDragHit != null))
                if (GUI.Button(new Rect(area.x + 112, y, 21, 21), "↑", EditorStyles.miniButtonLeft)) MoveDataHit(i, -1);
            using (new EditorGUI.DisabledScope(!editable || i == count - 1 || _dataDragHit != null))
                if (GUI.Button(new Rect(area.x + 134, y, 21, 21), "↓", EditorStyles.miniButtonRight)) MoveDataHit(i, 1);
            float markerX = left + Mathf.Clamp01(hit.time / Data.Duration) * width;
            EditorGUI.DrawRect(new Rect(left, y + 11, width, 1), SkillStudioGUI.TimelineGrid);
            Rect marker = new(markerX - 9, y, 18, 22);
            Color color = !hit.enabled ? Color.gray : IsDataHitActive(hit) ? Color.white : _dataHit == i ? HitColor : Accent;
            var style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 20, normal = { textColor = color } };
            GUI.Label(marker, new GUIContent("◆", $"{hit.name} · {hit.time:0.000}초 · {Math.Round(hit.time * FrameRate)}f · 드래그해서 시점 조절"), style);
            GUI.Label(new Rect(markerX + 12 > area.xMax - 44 ? markerX - 46 : markerX + 12, y + 3, 44, 18), $"{Math.Round(hit.time * FrameRate)}f", EditorStyles.miniLabel);
            if (editable) EditorGUIUtility.AddCursorRect(marker, MouseCursor.SlideArrow);
            if (editable && ev.type == EventType.MouseDown && ev.button == 0 && marker.Contains(ev.mousePosition))
            {
                SelectDataHit(i);
                _dataDragHit = hit; _dataDragX = ev.mousePosition.x; _dataDragTime = hit.time;
                Undo.IncrementCurrentGroup(); _dataDragUndo = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Move E data hit marker");
                _dataDragControl = control; GUIUtility.hotControl = control; ev.Use();
            }
        }
        if (_dataDragHit != null && GUIUtility.hotControl == _dataDragControl)
        {
            if (ev.type == EventType.MouseDrag)
            {
                Undo.RecordObject(Data, "Move E data hit marker");
                float time = (float)Math.Clamp(Snap(_dataDragTime + (ev.mousePosition.x - _dataDragX) / width * Duration), 0, Duration);
                _dataDragHit.time = time; DataChanged(); Seek(time); ev.Use();
            }
            else if (ev.type == EventType.MouseUp) { EndDataMarkerDrag(); ev.Use(); }
        }
        if (_dataDragHit != null && (ev.rawType == EventType.MouseUp || ev.type == EventType.Ignore || GUIUtility.hotControl != _dataDragControl)) EndDataMarkerDrag();
        if (editable && ev.type == EventType.MouseDown && ev.button == 0 && new Rect(left, area.y, width, 20).Contains(ev.mousePosition))
        { Seek((ev.mousePosition.x - left) / width * Duration); ev.Use(); }
        EditorGUI.DrawRect(new Rect(left + Mathf.Clamp01((float)(_time / Duration)) * width, area.y + 19, 2, area.height - 19), HitColor);
        EditorGUILayout.EndScrollView();
    }

    private void EndDataMarkerDrag()
    {
        if (_dataDragUndo >= 0) Undo.CollapseUndoOperations(_dataDragUndo);
        if (_dataDragControl != 0 && GUIUtility.hotControl == _dataDragControl) GUIUtility.hotControl = 0;
        _dataDragHit = null; _dataDragUndo = -1; _dataDragControl = 0;
    }

    private void SelectDataHit(int index)
    {
        _dataHit = index; _dataTab = 0;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && Data.hits[index] != null) Seek(Data.hits[index].time);
        Repaint(); SceneView.RepaintAll();
    }

    private void MoveDataHit(int index, int direction)
    {
        int next = index + direction;
        if (next < 0 || next >= Data.hits.Count) return;
        Undo.RecordObject(Data, "Reorder E data hit rows");
        (Data.hits[index], Data.hits[next]) = (Data.hits[next], Data.hits[index]);
        _dataHit = next; DataChanged();
    }

    private void DrawDataHits()
    {
        Heading("타격 마커");
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("현재 시각에 타격 추가"))
            {
                Undo.RecordObject(Data, "Add E data hit");
                Data.hits ??= new List<PlayerSkillHit>();
                Data.hits.Add(new PlayerSkillHit { name = $"Hit {Data.hits.Count + 1}", time = (float)_time });
                _dataHit = Data.hits.Count - 1; DataChanged();
            }
            using (new EditorGUI.DisabledScope(Data.hits == null || Data.hits.Count == 0))
            {
                if (GUILayout.Button("선택 복제"))
                {
                    Undo.RecordObject(Data, "Duplicate E data hit");
                    var clone = JsonUtility.FromJson<PlayerSkillHit>(JsonUtility.ToJson(Data.hits[_dataHit]));
                    clone.name += " 복제"; Data.hits.Insert(_dataHit + 1, clone); _dataHit++; DataChanged();
                }
                if (GUILayout.Button("선택 삭제"))
                {
                    Undo.RecordObject(Data, "Delete E data hit");
                    Data.hits.RemoveAt(_dataHit); _dataHit = Mathf.Max(0, _dataHit - 1); DataChanged();
                }
            }
        }
        if (Data.hits == null || Data.hits.Count == 0) return;
        _dataHit = EditorGUILayout.Popup("편집할 타격", _dataHit, Data.hits.Select((hit, i) => $"{i + 1:00} · {hit?.name ?? "비어 있음"}").ToArray());
        var so = new SerializedObject(Data); so.Update();
        var hit = so.FindProperty("hits").GetArrayElementAtIndex(_dataHit);
        Property(hit, "name", "이름"); Property(hit, "enabled", "타격 사용");
        Property(hit, "time", "타격 시각 (E 데이터 초)");
        EditorGUI.BeginChangeCheck();
        int frame = EditorGUILayout.IntField("타격 프레임", (int)Math.Round(hit.FindPropertyRelative("time").floatValue * FrameRate));
        if (EditorGUI.EndChangeCheck()) hit.FindPropertyRelative("time").floatValue = (float)Math.Clamp(frame / FrameRate, 0, Duration);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("−1f")) hit.FindPropertyRelative("time").floatValue = Mathf.Max(0, hit.FindPropertyRelative("time").floatValue - (float)(1 / FrameRate));
            if (GUILayout.Button("마커로 이동")) Seek(hit.FindPropertyRelative("time").floatValue);
            if (GUILayout.Button("+1f")) hit.FindPropertyRelative("time").floatValue = Mathf.Min(Data.Duration, hit.FindPropertyRelative("time").floatValue + (float)(1 / FrameRate));
        }
        Heading("피해 · 타격 반응");
        Property(hit, "damage", "피해량"); Property(hit, "stagger", "경직 단계");
        Property(hit, "hitStopFrames", "히트스톱 (60Hz 프레임)"); Property(hit, "hitStopMode", "히트스톱 대상");
        Property(hit, "skillGaugeAdditive", "명중당 스킬 게이지"); Property(hit, "shakeForce", "타격 순간 카메라 진동");
        Property(hit, "targetLayers", "대상 레이어"); Property(hit, "triggerInteraction", "Trigger 검사");
        Heading("히트박스 · 시전 방향 기준 미터");
        Property(hit, "shape", "형태"); Property(hit, "position", "중심 (좌우 · 높이 · 전방)"); Property(hit, "euler", "회전 (도)");
        var shape = (PlayerSkillHitShape)hit.FindPropertyRelative("shape").enumValueIndex;
        if (shape == PlayerSkillHitShape.Box) Property(hit, "size", "크기 (폭 · 높이 · 깊이)");
        else
        {
            Property(hit, "radius", "반경");
            if (shape == PlayerSkillHitShape.Capsule)
            {
                Property(hit, "height", "전체 높이");
                var axis = hit.FindPropertyRelative("capsuleDirection");
                axis.intValue = EditorGUILayout.Popup("캡슐 축", axis.intValue, new[] { "X (좌우)", "Y (높이)", "Z (전방)" });
            }
        }
        ApplyDataProperties(so);
        _showHitboxes = EditorGUILayout.Toggle("Scene에 범위 표시", _showHitboxes);
        _handleMode = GUILayout.Toolbar(_handleMode, new[] { "중심 이동", "회전", "범위" });
        if (GUILayout.Button("선택 타격을 3D에서 조절")) { Seek(Data.hits[_dataHit].time); StartDataPreview(true); FrameDataHit(); }
        EditorGUILayout.HelpBox("마커 프레임에서 한 번 검사합니다. 행 순서는 표시 순서이며 실행은 시각 순서입니다. 진동은 빗나간 타격에서도 발생합니다.", MessageType.None);
    }

    private void DrawDataMotionAndInput()
    {
        var so = new SerializedObject(Data); so.Update();
        Heading("재생 시간");
        DataProperty(so, "duration", "E 동작 길이 (초)"); DataProperty(so, "playbackSpeed", "실제 스킬 배속");
        DataProperty(so, "invulnerable", "스킬 중 무적");
        Heading("이동 거리 · 타이밍");
        var motion = so.FindProperty("motion");
        Property(motion, "enabled", "데이터 이동 사용"); Property(motion, "start", "이동 시작 (E 데이터 초)");
        Property(motion, "end", "이동 종료 (E 데이터 초)"); Property(motion, "distance", "총 전방 이동 거리 (m)");
        Property(motion, "curve", "이동 진행 곡선 (0 → 1)");
        Heading("입력 허용 · 후딜레이");
        DataProperty(so, "useInputCancelTime", "종료 전 입력 취소 허용");
        using (new EditorGUI.DisabledScope(!so.FindProperty("useInputCancelTime").boolValue))
            DataProperty(so, "inputCancelTime", "입력 허용 시점 (E 데이터 초)");
        DataProperty(so, "recoveryDuration", "동작 종료 후 입력 대기 (전투 초)");
        DataProperty(so, "exitBlendDuration", "다음 동작 블렌드 (전투 초)");
        ApplyDataProperties(so);
        EditorGUILayout.LabelField($"동작 {Data.Duration / Data.PlaybackSpeed:0.000}초 + 후딜 {Data.RecoveryDuration:0.000}초 = 자연 복귀 {Data.RuntimeDuration:0.000}초", EditorStyles.miniLabel);
        if (Data.useInputCancelTime)
            EditorGUILayout.LabelField($"입력 취소 창: 시전 후 {Data.InputCancelTime / Data.PlaybackSpeed:0.000}초부터", EditorStyles.miniLabel);
        EditorGUILayout.HelpBox("입력 허용 시점에는 유효한 이동·공격·회피 입력이 있을 때만 동작을 취소합니다. 입력이 없으면 끝까지 재생한 뒤 후딜을 거칩니다. 히트스톱 중에는 이동·스킬 시각·후딜이 멈춥니다. Q는 기존 즉시 발동 규칙을 따릅니다.", MessageType.None);
        EditorGUILayout.HelpBox("이동 거리와 곡선은 Timeline 루트 이동과 독립적입니다. Scene 미리보기는 별도 모델을 사용하며 벽·바닥에 의한 차단은 Play Mode에서 확인합니다.", MessageType.None);
    }

    private void DrawDataScreenEffects()
    {
        Heading("임팩트 프레임");
        EditorGUILayout.HelpBox("기존 E 임팩트 이펙트의 활성 시각과 길이입니다. Timeline의 임팩트 클립 대신 이 데이터를 실행합니다.", MessageType.None);
        DrawDataEffectList("impactFrames", true);
        var impact = _director.GetComponent<PlayerSkillImpactFrame>();
        if (impact != null) DrawProperties(impact, ("_centerOffset", "임팩트 중심"), ("_sourceDuration", "원본 파티클 길이"),
            ("_coreScreenScale", "중심 링 크기"), ("_playerProtectionPadding", "캐릭터 보호 여유"));
        else EditorGUILayout.HelpBox("이 Director에 Player Skill Impact Frame 연결이 없습니다. 임팩트 프레임은 연결 후 재생됩니다.", MessageType.Warning);
        Heading("화면 후처리");
        EditorGUILayout.HelpBox("Volume Profile의 색·블룸·노출 등을 지정한 구간에 적용합니다. 강도 곡선은 해당 구간의 0~1 진행도입니다. 임팩트·후처리·카메라 결과는 Play Mode에서 확인하세요.", MessageType.None);
        DrawDataEffectList("screenEffects", false);
    }

    private void DrawDataEffectList(string propertyName, bool impact)
    {
        var so = new SerializedObject(Data); so.Update();
        var list = so.FindProperty(propertyName);
        for (int i = 0; i < list.arraySize; i++)
        {
            var item = list.GetArrayElementAtIndex(i);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    item.isExpanded = EditorGUILayout.Foldout(item.isExpanded, $"{i + 1:00} · {item.FindPropertyRelative("name").stringValue}", true);
                    if (GUILayout.Button("삭제", GUILayout.Width(42)))
                    {
                        list.DeleteArrayElementAtIndex(i); ApplyDataProperties(so); GUIUtility.ExitGUI();
                    }
                }
                if (item.isExpanded)
                {
                    Property(item, "name", "이름"); Property(item, "enabled", "사용");
                    Property(item, "start", "시작 (E 데이터 초)"); Property(item, "duration", "지속시간 (E 데이터 초)");
                    if (!impact)
                    {
                        Property(item, "profile", "Volume Profile"); Property(item, "weight", "최대 강도");
                        Property(item, "envelope", "강도 곡선"); Property(item, "priority", "Volume 우선순위");
                    }
                }
            }
        }
        ApplyDataProperties(so);
        if (GUILayout.Button(impact ? "임팩트 프레임 추가" : "화면 후처리 추가"))
        {
            Undo.RecordObject(Data, "Add E screen effect");
            if (impact) Data.impactFrames.Add(new PlayerSkillImpactWindow { start = (float)_time });
            else Data.screenEffects.Add(new PlayerSkillScreenEffect { start = (float)_time });
            DataChanged();
        }
    }

    private bool IsDataImpactTrack(TrackAsset track) => _director != null && _director.GetGenericBinding(track) is PlayerSkillImpactFrame;

    private void DrawDataPresentation()
    {
        Heading("애니메이션 · 연출 전용 Timeline");
        var so = new SerializedObject(Data); so.Update();
        DataProperty(so, "playPresentation", "기존 애니메이션 · 연출 재생"); ApplyDataProperties(so);
        EditorGUILayout.HelpBox("이동·타격·입력·임팩트 활성은 E 데이터가 실행합니다. 아래 Timeline은 자세·VFX·투명도·카메라·사운드를 편집합니다. Timeline 길이를 바꿔도 E 동작 길이는 자동 변경되지 않습니다.", MessageType.None);
        if (Timeline == null) { EditorGUILayout.HelpBox("연출 Timeline이 비어 있습니다. 데이터 이동·타격은 사용할 수 있습니다.", MessageType.Info); return; }
        _dataPresentationTab = GUILayout.Toolbar(_dataPresentationTab, new[] { "애니메이션", "VFX · 투명도", "카메라" });
        if (_dataPresentationTab == 0)
        {
            DrawTimingList(Tracks.OfType<AnimationTrack>().Where(track => !IsCameraTrack(track)), "애니메이션 클립");
            if (_clip != null && _track is AnimationTrack) DrawClipDetails();
        }
        else if (_dataPresentationTab == 1) DrawEffects();
        else DrawCamera();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Timeline 상세 편집")) { EndPreview(); OpenTimeline(); }
            if (GUILayout.Button("조정 데이터 Inspector")) Selection.activeObject = Data;
        }
    }

    private void DrawDataPlayControls()
    {
        Heading("실제 E 실행");
        var player = _director != null ? _director.GetComponentInParent<PlayerCore>() : null;
        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || player == null))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("테스트용 게이지 채우기")) player.AddSkillGauge(player.MaxSkillGauge);
                using (new EditorGUI.DisabledScope(player == null || !player.IsSkillEnable || player.StateMachine.IsSkillActive))
                    if (GUILayout.Button("E 시전")) player.StateMachine.Transition(player.StateMachine.SkillState);
            }
        }
        EditorGUILayout.HelpBox("Play Mode에서 E 시전으로 이동·실제 피해·카메라·화면 효과와 입력 복귀를 확인합니다. 실행 중 편집은 잠기며 저장된 값은 다음 시전에 적용됩니다.", MessageType.None);
    }

    private void DrawDataFooter()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            bool readOnly = EditorApplication.isPlayingOrWillChangePlaymode;
            GUILayout.Label(readOnly ? "Play Mode · 읽기 전용" : HasEditsToSave() ? "● 저장하지 않은 변경" : "✓ 저장됨", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(readOnly))
            {
                if (GUILayout.Button("Undo", EditorStyles.toolbarButton, GUILayout.Width(46)))
                    EditorApplication.delayCall += () => { if (this != null && !EditorApplication.isPlayingOrWillChangePlaymode) { EndDataMarkerDrag(); Undo.PerformUndo(); Repaint(); } };
                if (GUILayout.Button(new GUIContent("저장", "E 데이터와 편집한 연출·씬 설정을 저장합니다."), EditorStyles.toolbarButton, GUILayout.Width(46)))
                    EditorApplication.delayCall += () => { if (this != null) Save(); };
                if (GUILayout.Button("데이터 Inspector", EditorStyles.toolbarButton, GUILayout.Width(105))) Selection.activeObject = Data;
            }
        }
    }

    private static void DataProperty(SerializedObject so, string name, string label)
        => EditorGUILayout.PropertyField(so.FindProperty(name), new GUIContent(label), true);

    private void ApplyDataProperties(SerializedObject so)
    {
        if (so.ApplyModifiedProperties()) DataChanged();
    }

    private void DataChanged()
    {
        if (Data == null) return;
        Data.Normalize(); Mark(Data);
        if (_dataPreview != null) _dataPreview.Sample((float)_time, Data);
        Repaint(); SceneView.RepaintAll();
    }

    private bool IsDataHitActive(PlayerSkillHit hit) => hit != null && hit.enabled && Math.Round(_time * FrameRate) == Math.Round(hit.time * FrameRate);

    private void SampleDataPreview(double time, bool snap)
    {
        if (Data == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        _time = Math.Clamp(snap ? Snap(Finite(time, 0)) : Finite(time, 0), 0, Duration);
        var player = _director.GetComponentInParent<PlayerCore>();
        if (_dataPreview != null && !_dataPreview.UsesSource(player, _director, Timeline))
        { _dataPreview.Dispose(); _dataPreview = null; }
        if (_dataPreview == null)
        {
            if (player != null)
            {
                _dataPreview = new ESkillDataPreview();
                _dataPreview.Build(player, _director, Timeline);
            }
        }
        _dataPreview?.Sample((float)_time, Data);
        Repaint(); SceneView.RepaintAll();
    }

    private void StartDataPreview(bool focus)
    {
        SampleDataPreview(_time, false);
        if (_dataPreview == null || !_dataPreview.IsReady || !focus) return;
        if (_previewView == null)
        {
            _previewView = CreateInstance<SceneView>(); _previewView.Show();
            _previewView.titleContent = new GUIContent("E Data · 3D Preview"); _previewHadView = true;
        }
        float length = Mathf.Abs(Data.motion?.distance ?? 0f);
        Vector3 mid = _dataPreview.Origin + _dataPreview.Facing * (Data.EvaluateMotion(Data.Duration) * .5f);
        _previewView.Frame(new Bounds(mid + Vector3.up, new Vector3(Mathf.Max(5, length + 3), 5, Mathf.Max(5, length + 3))), false);
        _previewView.Repaint(); Focus();
    }

    private void EndDataPreview()
    {
        EndDataMarkerDrag();
        _dataPreview?.Dispose(); _dataPreview = null;
    }

    private void FrameDataHit()
    {
        if (_dataPreview == null || Data.hits == null || Data.hits.Count == 0) return;
        var hit = Data.hits[_dataHit];
        Vector3 center = _dataPreview.Origin + _dataPreview.Facing * (Data.EvaluateMotion(hit.time) + hit.position);
        var size = hit.shape == PlayerSkillHitShape.Box ? hit.size : Vector3.one * Mathf.Max(hit.radius * 2, hit.height);
        _previewView?.Frame(new Bounds(center, size + Vector3.one * 2), false);
    }

    private void DuringDataSceneGUI(SceneView view)
    {
        if (_dataPreview == null || !_dataPreview.IsReady || Data == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Vector3 origin = _dataPreview.Origin;
        Quaternion facing = _dataPreview.Facing;
        using (new Handles.DrawingScope(SkillStudioGUI.PathColor))
        {
            var points = new Vector3[65];
            for (int i = 0; i < points.Length; i++) points[i] = origin + facing * Data.EvaluateMotion(Data.Duration * i / (points.Length - 1));
            Handles.DrawAAPolyLine(3, points);
            Handles.Label(origin + Vector3.up * .1f, "E 데이터 시작");
            Handles.Label(points[^1] + Vector3.up * .1f, $"이동 끝 · {Data.motion?.distance ?? 0:0.###}m");
        }
        if (!_showHitboxes || Data.hits == null) return;
        for (int i = 0; i < Data.hits.Count; i++)
        {
            var hit = Data.hits[i];
            if (!IsDataHitActive(hit)) continue;
            Vector3 center = origin + facing * (Data.EvaluateMotion((float)_time) + hit.position);
            Quaternion rotation = facing * Quaternion.Euler(hit.euler);
            using (new Handles.DrawingScope(i == _dataHit ? HitColor : Accent))
            {
                DrawDataField(hit, center, rotation);
                Handles.Label(center, $"{hit.name} · {hit.time:0.000}초");
                if (i != _dataHit || _dataTab != 0) continue;
                EditorGUI.BeginChangeCheck();
                if (_handleMode == 0)
                {
                    Vector3 next = Handles.PositionHandle(center, facing);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(Data, "Move E data hitbox");
                        hit.position += Quaternion.Inverse(facing) * (next - center); DataChanged();
                    }
                }
                else if (_handleMode == 1)
                {
                    Quaternion next = Handles.RotationHandle(rotation, center);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(Data, "Rotate E data hitbox"); hit.euler = (Quaternion.Inverse(facing) * next).eulerAngles; DataChanged();
                    }
                }
                else if (hit.shape == PlayerSkillHitShape.Box)
                {
                    using (new Handles.DrawingScope(Matrix4x4.TRS(center, rotation, Vector3.one)))
                    {
                        _boxHandle.center = Vector3.zero; _boxHandle.size = hit.size; _boxHandle.DrawHandle();
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(Data, "Resize E data hitbox");
                            hit.position += Quaternion.Inverse(facing) * (rotation * _boxHandle.center);
                            hit.size = _boxHandle.size; DataChanged();
                        }
                    }
                }
                else
                {
                    float radius = Handles.RadiusHandle(rotation, center, hit.radius);
                    Vector3 axis = rotation * (hit.capsuleDirection == 0 ? Vector3.right : hit.capsuleDirection == 2 ? Vector3.forward : Vector3.up);
                    Vector3 top = center + axis * (hit.height * .5f);
                    Vector3 nextTop = hit.shape == PlayerSkillHitShape.Capsule
                        ? Handles.Slider(top, axis, HandleUtility.GetHandleSize(top) * .08f, Handles.CubeHandleCap, 0f) : top;
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(Data, "Resize E data hitbox"); hit.radius = radius;
                        if (hit.shape == PlayerSkillHitShape.Capsule) hit.height = Mathf.Max(radius * 2, Vector3.Dot(nextTop - center, axis) * 2);
                        DataChanged();
                    }
                }
            }
        }
    }

    private static void DrawDataField(PlayerSkillHit hit, Vector3 center, Quaternion rotation)
    {
        if (hit.shape == PlayerSkillHitShape.Box)
        {
            using (new Handles.DrawingScope(Matrix4x4.TRS(center, rotation, Vector3.one))) Handles.DrawWireCube(Vector3.zero, hit.size);
            return;
        }
        if (hit.shape == PlayerSkillHitShape.Sphere) { DrawSphere(center, hit.radius); return; }
        Vector3 axis = rotation * (hit.capsuleDirection == 0 ? Vector3.right : hit.capsuleDirection == 2 ? Vector3.forward : Vector3.up);
        Vector3 line = axis * Mathf.Max(0, hit.height * .5f - hit.radius);
        Vector3 a = center + line, b = center - line;
        Handles.DrawWireDisc(a, axis, hit.radius); Handles.DrawWireDisc(b, axis, hit.radius);
        Vector3 tangent = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > .9f ? Vector3.right : Vector3.up).normalized * hit.radius;
        Vector3 other = Vector3.Cross(axis, tangent).normalized * hit.radius;
        foreach (var offset in new[] { tangent, -tangent, other, -other }) Handles.DrawLine(a + offset, b + offset);
        DrawSphere(a, hit.radius); DrawSphere(b, hit.radius);
    }
}

/// <summary>Samples only a disposable model. World travel comes exclusively from E data.</summary>
internal sealed class ESkillDataPreview : IDisposable
{
    private GameObject _root, _model;
    private PlayableDirector _director;
    private TimelineAsset _timeline;
    private readonly List<Object> _ownedTracks = new();
    private Animator _source;
    private string _sourceSignature;
    private Transform[] _poseTransforms;
    private Vector3[] _posePositions, _poseScales;
    private Quaternion[] _poseRotations;
    public Vector3 Origin { get; private set; }
    public Quaternion Facing { get; private set; }
    public bool IsReady => _root != null;

    public void Build(PlayerCore player, PlayableDirector director, TimelineAsset timeline)
    {
        Dispose();
        var source = player.Animator != null ? player.Animator : player.GetComponentInChildren<Animator>(true);
        if (source == null) return;
        try
        {
            _source = source;
            _sourceSignature = SourceSignature(timeline, director, source);
            Origin = player.transform.position + source.transform.rotation * Vector3.right * 6f;
            Facing = source.transform.rotation;
            _root = UltimatePreviewModel.Create(source, out _model, out var animator);
            _root.name = "E Skill Studio · disposable data preview";
            _model.name = "E visual model";
            CaptureStationaryPose();
            if (timeline == null || !BoundAnimationTracks(timeline, director, source).Any()) return;
            _timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _timeline.name = "E Studio · private animation presentation";
            _timeline.hideFlags = HideFlags.HideAndDontSave;
            _director = _root.AddComponent<PlayableDirector>();
            _director.playOnAwake = false;
            _director.timeUpdateMode = DirectorUpdateMode.Manual;
            _director.extrapolationMode = DirectorWrapMode.Hold;
            _director.playableAsset = _timeline;
            PlayerSkillDataMigration.CopyAnimationTracks(timeline, director, source, _timeline, _director, animator, _ownedTracks);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static IEnumerable<AnimationTrack> BoundAnimationTracks(TimelineAsset timeline, PlayableDirector director, Animator source)
    {
        if (timeline == null || director == null || source == null) yield break;
        foreach (var track in PlayerSkillStudio.AllTracks(timeline).OfType<AnimationTrack>())
        {
            var root = track;
            while (root.parent is AnimationTrack parent) root = parent;
            if (!track.mutedInHierarchy && director.GetGenericBinding(root) == source) yield return track;
        }
    }

    private static string SourceSignature(TimelineAsset timeline, PlayableDirector director, Animator source)
    {
        // Unsaved authoring edits matter too: clip ranges, blends, custom curves, masks,
        // extrapolation, offsets, track layers and AnimationPlayableAsset properties.
        return string.Join("\n", BoundAnimationTracks(timeline, director, source).Select(track =>
            track.GetEntityId() + ":" + EditorJsonUtility.ToJson(track) + ":" +
            string.Join("|", track.GetClips().Select(clip => clip.asset != null ? EditorJsonUtility.ToJson(clip.asset) : ""))));
    }

    public bool UsesSource(PlayerCore player, PlayableDirector director, TimelineAsset timeline)
    {
        if (!IsReady || player == null) return false;
        var source = player.Animator != null ? player.Animator : player.GetComponentInChildren<Animator>(true);
        return source == _source && _sourceSignature == SourceSignature(timeline, director, source);
    }

    private void CaptureStationaryPose()
    {
        _poseTransforms = _model.GetComponentsInChildren<Transform>(true);
        _posePositions = _poseTransforms.Select(item => item.localPosition).ToArray();
        _poseRotations = _poseTransforms.Select(item => item.localRotation).ToArray();
        _poseScales = _poseTransforms.Select(item => item.localScale).ToArray();
    }

    private void RestoreStationaryPose()
    {
        for (int i = 0; i < _poseTransforms.Length; i++)
        {
            if (_poseTransforms[i] == null) continue;
            _poseTransforms[i].SetLocalPositionAndRotation(_posePositions[i], _poseRotations[i]);
            _poseTransforms[i].localScale = _poseScales[i];
        }
    }

    public void Sample(float time, PlayerSkillDefinition definition)
    {
        if (!IsReady || definition == null) return;
        if (definition.playPresentation && _director != null)
        {
            if (!_director.playableGraph.IsValid())
            {
                _director.Play();
                _director.Pause();
            }
            _director.time = Mathf.Max(0f, time);
            _director.Evaluate();
        }
        else RestoreStationaryPose();
        // The private Timeline supplies only the pose. E data owns world travel;
        // discard authored animation-root translation/rotation after every sample.
        _root.transform.SetPositionAndRotation(Origin + Facing * definition.EvaluateMotion(time), Facing);
        _model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    public void Dispose()
    {
        if (_director != null) _director.Stop();
        if (_root != null) Object.DestroyImmediate(_root);
        // Track containers are private copies; their AnimationPlayableAsset and
        // AnimationClip references still belong to the original authoring assets.
        foreach (var track in _ownedTracks) if (track != null) Object.DestroyImmediate(track);
        _ownedTracks.Clear();
        if (_timeline != null) Object.DestroyImmediate(_timeline);
        _root = _model = null;
        _director = null; _timeline = null; _source = null; _sourceSignature = null;
        _poseTransforms = null; _posePositions = _poseScales = null; _poseRotations = null;
    }
}
