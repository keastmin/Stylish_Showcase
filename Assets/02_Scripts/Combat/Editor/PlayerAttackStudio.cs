using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

/// <summary>Shared authoring surface for the existing basic combo and ordinary dodge/run attack.</summary>
public abstract class PlayerAttackStudio : EditorWindow
{
    protected abstract bool OrdinaryDodge { get; }
    [SerializeField] private PlayerCore _player;
    [SerializeField] private int _action, _tab;
    [SerializeField] private double _time;
    [SerializeField] private float _previewSpeed = 1;
    [SerializeField] private bool _loop = true, _snap = true, _showFields = true;
    [SerializeField] private int _handleMode;
    private List<PlayerAttackStudioContext> _actions = new();
    private List<PlayerAttackStudioSignal> _signals = new();
    private SignalEmitter _selectedMarker;
    private TimelineClip _effectClip;
    private TrackAsset _effectTrack;
    private Vector2 _scroll, _timelineScroll;
    private PlayerAttackStudioPreview _preview;
    private SceneView _previewView;
    private bool _playing, _hadPreviewView;
    private double _lastTick;
    private PlayerAttackStudioSignal _dragSignal;
    private int _dragUndo = -1, _dragControl;
    private float _dragX;
    private double _dragTime, _dragDuration;
    private string _message;
    private readonly HashSet<Object> _editedAssets = new();
    private readonly HashSet<UnityEngine.SceneManagement.Scene> _editedScenes = new();
    private readonly BoxBoundsHandle _boxHandle = new();
    private PlayerAttackStudioContext Context => _action >= 0 && _action < _actions.Count ? _actions[_action] : null;
    private PlayerAttackStudioSignal Selected => _signals.FirstOrDefault(row => row.Marker == _selectedMarker);
    private double Duration => Math.Max(.01, Context?.Duration ?? 1);
    private double FrameRate => Math.Max(1, Context?.Timeline != null ? Context.Timeline.editorSettings.frameRate : 60);
    private bool ReadOnly => EditorApplication.isPlayingOrWillChangePlaymode;
    private string DisplayTitle => OrdinaryDodge ? "DODGE ATTACK STUDIO" : "BASIC ATTACK STUDIO";

    protected virtual void OnEnable()
    {
        minSize = new Vector2(620, 650);
        _lastTick = EditorApplication.timeSinceStartup;
        Undo.undoRedoPerformed += OnUndo;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayMode;
        AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
        SceneView.duringSceneGui += DuringSceneGUI;
        FindPlayer();
    }

    protected virtual void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndo;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
        SceneView.duringSceneGui -= DuringSceneGUI;
        StopPreview();
    }

    private void OnPlayMode(PlayModeStateChange state)
    {
        StopPreview();
        if (state == PlayModeStateChange.EnteredEditMode) { _player = null; FindPlayer(); }
        Repaint();
    }

    private void FindPlayer()
    {
        if (_player == null && Selection.activeGameObject != null)
            _player = Selection.activeGameObject.GetComponentInParent<PlayerCore>();
        if (_player == null)
            _player = FindObjectsByType<PlayerCore>(FindObjectsInactive.Include).FirstOrDefault(player =>
                player.gameObject.scene.IsValid() && !EditorSceneManager.IsPreviewScene(player.gameObject.scene) && !EditorUtility.IsPersistent(player));
        RefreshData();
    }

    private void RefreshData()
    {
        _actions = _player != null ? PlayerAttackStudioData.GetActions(_player, OrdinaryDodge) : new();
        _action = Mathf.Clamp(_action, 0, Math.Max(0, _actions.Count - 1));
        RefreshSignals();
    }

    private void RefreshSignals()
    {
        _signals = Context != null ? PlayerAttackStudioData.GetSignals(Context) : new();
        if (!_signals.Any(row => row.Marker == _selectedMarker))
            _selectedMarker = (_signals.FirstOrDefault(row => row.IsHit) ?? _signals.FirstOrDefault())?.Marker;
        _time = Math.Clamp(_time, 0, Duration);
    }

    private void OnUndo()
    {
        EndDrag(); RefreshData(); RebuildPreview(); Repaint(); SceneView.RepaintAll();
    }

    private void SelectAction(int index)
    {
        StopPreview(); _action = index; _time = 0; _selectedMarker = null;
        _effectClip = null; _effectTrack = null; _message = null; RefreshData(); Repaint();
    }

    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        double delta = Math.Clamp(now - _lastTick, 0, .1);
        _lastTick = now;
        if (_hadPreviewView && _previewView == null) { StopPreview(); Repaint(); }
        if (ReadOnly) return;
        if (!_playing || Context == null) return;
        _time += delta * _previewSpeed;
        if (_time >= Duration)
        {
            if (_loop) _time %= Duration;
            else { _time = Duration; _playing = false; }
        }
        _preview?.Sample(_time); Repaint(); SceneView.RepaintAll();
    }

    private void OnGUI()
    {
        DrawHeader();
        if (Context == null || Context.Director == null || Context.Timeline == null)
        {
            EditorGUILayout.HelpBox("플레이어와 해당 공격의 Timeline 연결을 확인하세요. 현재 씬에서 플레이어를 찾거나 위에서 선택할 수 있습니다.", MessageType.Info);
            DrawFooter(); return;
        }
        DrawActionStrip();
        DrawTransport();
        DrawSignalTimeline();
        _tab = GUILayout.Toolbar(_tab, new[] { "타격 · 범위", "클립 · 타이밍", "연출", "연결 · 실행" });
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        using (new EditorGUI.DisabledScope(ReadOnly))
        {
            if (_tab == 0) DrawHitDetails();
            else if (_tab == 1) DrawAnimationAndGates();
            else if (_tab == 2) DrawEffects();
            else DrawConnections();
        }
        if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.Warning);
        EditorGUILayout.EndScrollView();
        DrawFooter();
    }

    private void DrawHeader()
    {
        SkillStudioGUI.Header(DisplayTitle, OrdinaryDodge
            ? "일반 회피 후 이어지는 공격의 타격 · 범위 · 연출을 조절하세요."
            : "기본 공격 1–4단의 타격 · 콤보 입력 구간 · 연출을 조절하세요.");
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            var player = (PlayerCore)EditorGUILayout.ObjectField("편집할 플레이어", _player, typeof(PlayerCore), true);
            if (EditorGUI.EndChangeCheck()) { StopPreview(); _player = player; _action = 0; _selectedMarker = null; RefreshData(); }
            if (GUILayout.Button("찾기", GUILayout.Width(44))) { StopPreview(); _player = null; FindPlayer(); }
        }
        EditorGUILayout.LabelField(Context?.Timeline != null
            ? $"● {_player.gameObject.scene.name} / {Context.Director.name} · {Context.Timeline.name} · 실제 실행 데이터"
            : "플레이어 연결 대기", EditorStyles.miniLabel);
    }

    private void DrawActionStrip()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup("편집할 공격", _action, _actions.Select(action => action.Label).ToArray());
            if (EditorGUI.EndChangeCheck()) SelectAction(next);
            if (GUILayout.Button("클립 구성", GUILayout.Width(75))) _tab = 1;
        }
        Rect strip = GUILayoutUtility.GetRect(0, 39, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(strip, SkillStudioGUI.StripBackground);
        float width = strip.width / Math.Max(1, _actions.Count);
        for (int i = 0; i < _actions.Count; i++)
        {
            Color old = GUI.backgroundColor;
            GUI.backgroundColor = i == _action ? SkillStudioGUI.Accent : new Color(.6f, .62f, .72f);
            var action = _actions[i];
            var rect = new Rect(strip.x + width * i + 1, strip.y + 2, width - 2, strip.height - 4);
            if (GUI.Button(rect, new GUIContent($"{action.Label}\n{action.AnimationDuration:0.00}초", action.Clip != null ? action.Clip.name : "애니메이션 미연결"))) SelectAction(i);
            GUI.backgroundColor = old;
        }
    }

    private void DrawTransport()
    {
        using (new EditorGUI.DisabledScope(ReadOnly || Context?.Clip == null))
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(_preview?.IsReady == true ? "Scene 다시 보기" : "Scene 미리보기", EditorStyles.toolbarButton, GUILayout.Width(112))) StartPreview();
                if (_preview != null && GUILayout.Button("닫기", EditorStyles.toolbarButton, GUILayout.Width(38))) StopPreview();
                if (GUILayout.Button(_playing ? "Ⅱ 정지" : "▶ 재생", EditorStyles.toolbarButton, GUILayout.Width(68)))
                {
                    if (_playing) _playing = false;
                    else { if (_preview?.IsReady != true) StartPreview(); if (_time >= Duration) _time = 0; _lastTick = EditorApplication.timeSinceStartup; _playing = _preview?.IsReady == true; }
                }
                if (GUILayout.Button("|◀", EditorStyles.toolbarButton, GUILayout.Width(30))) Seek(0);
                if (GUILayout.Button("−1f", EditorStyles.toolbarButton, GUILayout.Width(38))) Seek(_time - 1 / FrameRate);
                if (GUILayout.Button("+1f", EditorStyles.toolbarButton, GUILayout.Width(38))) Seek(_time + 1 / FrameRate);
                _loop = GUILayout.Toggle(_loop, "반복", EditorStyles.toolbarButton, GUILayout.Width(42));
                GUILayout.FlexibleSpace(); GUILayout.Label("관찰 배속", EditorStyles.miniLabel);
                float speed = EditorGUILayout.FloatField(_previewSpeed, GUILayout.Width(42));
                _previewSpeed = float.IsNaN(speed) || float.IsInfinity(speed) ? 1 : Mathf.Clamp(speed, .05f, 3f);
            }
            EditorGUI.BeginChangeCheck();
            double next = EditorGUILayout.Slider("선택 공격 재생 위치", (float)_time, 0, (float)Duration);
            if (EditorGUI.EndChangeCheck()) Seek(next);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label($"전체 {_time:0.000} / {Duration:0.000}초 · {Math.Round(_time * FrameRate)}f · 원본 {_time * (Context?.Speed ?? 1):0.000}초", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace(); _snap = GUILayout.Toggle(_snap, "프레임 스냅", GUILayout.Width(92));
        }
    }

    private void DrawSignalTimeline()
    {
        float height = 28 + 24 * Math.Max(1, _signals.Count);
        _timelineScroll = EditorGUILayout.BeginScrollView(_timelineScroll, GUILayout.Height(Mathf.Min(height, Mathf.Clamp(position.height * .27f, 124, 244))));
        Rect area = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
        float left = area.x + SkillStudioGUI.TimelineLabelWidth, width = Mathf.Max(1, area.width - SkillStudioGUI.TimelineLabelWidth - 14);
        EditorGUI.DrawRect(area, SkillStudioGUI.TimelineBackground);
        for (int i = 0; i <= 10; i++)
        {
            float x = left + width * i / 10;
            EditorGUI.DrawRect(new Rect(x, area.y + 19, 1, area.height - 19), SkillStudioGUI.TimelineGrid);
            GUI.Label(new Rect(x - 12, area.y, 44, 18), (Duration * i / 10).ToString("0.0"), EditorStyles.miniLabel);
        }
        GUI.Label(new Rect(area.x + 5, area.y, 155, 18), "타격 · 행동 시그널 (초)", EditorStyles.miniLabel);
        Event ev = Event.current;
        int control = GUIUtility.GetControlID(FocusType.Passive);
        for (int i = 0; i < _signals.Count; i++)
        {
            var row = _signals[i];
            float y = area.y + 23 + i * 24;
            bool selected = row.Marker == _selectedMarker;
            if (GUI.Button(new Rect(area.x + 5, y, 151, 21), new GUIContent(row.Label, row.Method), selected ? EditorStyles.toolbarButton : EditorStyles.miniLabel)) SelectSignal(row);
            float x = left + Mathf.Clamp01((float)(row.Marker.time / Duration)) * width;
            Rect marker = new(x - 9, y, 18, 22);
            EditorGUI.DrawRect(new Rect(left, y + 11, width, 1), SkillStudioGUI.TimelineGrid);
            Color color = !row.IsBound || row.Track.mutedInHierarchy ? Color.gray : selected ? SkillStudioGUI.HitColor : row.IsHit ? SkillStudioGUI.Accent : SkillStudioGUI.PathColor;
            GUI.Label(marker, new GUIContent("◆", $"{row.Label} · {Math.Round(row.Marker.time * FrameRate)}f · 드래그해서 시각 이동"),
                new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 20, normal = { textColor = color } });
            GUI.Label(new Rect(x + 12 > area.xMax - 42 ? x - 46 : x + 12, y + 3, 42, 18), $"{Math.Round(row.Marker.time * FrameRate)}f", EditorStyles.miniLabel);
            if (!ReadOnly) EditorGUIUtility.AddCursorRect(marker, MouseCursor.SlideArrow);
            if (!ReadOnly && ev.type == EventType.MouseDown && ev.button == 0 && marker.Contains(ev.mousePosition))
            {
                SelectSignal(row); _dragSignal = row; _dragTime = row.Marker.time; _dragDuration = Duration; _dragX = ev.mousePosition.x;
                Undo.IncrementCurrentGroup(); _dragUndo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Move player attack marker");
                _dragControl = control; GUIUtility.hotControl = control; ev.Use();
            }
        }
        if (_signals.Count == 0) GUI.Label(new Rect(area.x + 5, area.y + 25, area.width - 10, 20), "연결된 시그널이 없습니다. Timeline의 Signal Track 연결을 확인하세요.", EditorStyles.miniLabel);
        if (_dragSignal != null && GUIUtility.hotControl == _dragControl && ev.type == EventType.MouseDrag && !ReadOnly)
        {
            double next = Math.Clamp(Snap(_dragTime + (ev.mousePosition.x - _dragX) / width * _dragDuration), 0, _dragDuration);
            PlayerAttackStudioData.SetMarkerTime(_dragSignal, next); Mark(_dragSignal.Marker); Seek(next); ev.Use();
        }
        if (_dragSignal != null && (ev.rawType == EventType.MouseUp || ev.type == EventType.Ignore || GUIUtility.hotControl != _dragControl)) EndDrag();
        if (!ReadOnly && ev.type == EventType.MouseDown && ev.button == 0 && new Rect(left, area.y, width, 20).Contains(ev.mousePosition))
        { Seek((ev.mousePosition.x - left) / width * Duration); ev.Use(); }
        EditorGUI.DrawRect(new Rect(left + Mathf.Clamp01((float)(_time / Duration)) * width, area.y + 19, 2, area.height - 19), SkillStudioGUI.HitColor);
        EditorGUILayout.EndScrollView();
    }

    private void SelectSignal(PlayerAttackStudioSignal signal)
    {
        _selectedMarker = signal.Marker; _tab = signal.IsHit ? 0 : 1; Seek(signal.Marker.time); Repaint();
    }

    private void EndDrag()
    {
        if (_dragUndo >= 0) Undo.CollapseUndoOperations(_dragUndo);
        if (_dragControl != 0 && GUIUtility.hotControl == _dragControl) GUIUtility.hotControl = 0;
        _dragSignal = null; _dragUndo = -1; _dragControl = 0;
    }

    private void DrawHitDetails()
    {
        var hits = _signals.Where(row => row.IsHit).ToArray();
        SkillStudioGUI.Section("타격 선택");
        if (hits.Length == 0) { EditorGUILayout.HelpBox("데미지 필드에 연결된 타격 시그널이 없습니다. Timeline 또는 연결 탭에서 시그널 수신기를 확인하세요.", MessageType.Warning); return; }
        if (Selected?.IsHit != true) _selectedMarker = hits[0].Marker;
        int current = Array.FindIndex(hits, row => row.Marker == _selectedMarker);
        EditorGUI.BeginChangeCheck();
        int next = EditorGUILayout.Popup("편집할 타격", Math.Max(0, current), hits.Select(row => $"{row.Label} · {Math.Round(row.Marker.time * FrameRate)}f").ToArray());
        if (EditorGUI.EndChangeCheck()) SelectSignal(hits[next]);
        var selected = Selected;
        if (selected == null) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("현재 시각에 타격 복제"))
            {
                _selectedMarker = PlayerAttackStudioData.CloneSignal(selected, Snap(_time))?.Marker;
                Mark(selected.Track); RefreshSignals(); return;
            }
            if (GUILayout.Button("선택 타격 삭제"))
            { PlayerAttackStudioData.RemoveSignal(selected); Mark(selected.Track); _selectedMarker = null; RefreshSignals(); return; }
        }
        DrawSignalTime(selected);
        if (!string.IsNullOrEmpty(selected.BindingIssue)) return;
        if (selected.Owner == null || selected.Field == null || string.IsNullOrEmpty(selected.DamageFieldProperty))
        { EditorGUILayout.HelpBox("실제 공격 컨테이너와 피해 필드 연결을 확인하세요.", MessageType.Warning); return; }
        var so = new SerializedObject(selected.Owner); so.Update();
        var field = so.FindProperty(selected.DamageFieldProperty);
        if (field == null) return;
        SkillStudioGUI.Section("피해와 판정");
        Property(field, "_hitbox", "히트박스"); Property(field, "_damage", "피해량");
        Property(field, "_staggerLevel", "경직 단계"); Property(field, "_hitStopFrame", "히트스톱 (60Hz 프레임)");
        Property(field, "_hitStopMode", "히트스톱 대상"); Property(field, "_targetLayers", "피격 대상 레이어");
        Property(field, "_triggerInteraction", "Trigger 판정"); Property(field, "_skillGaugeAdditive", "명중당 게이지 증가");
        if (so.ApplyModifiedProperties()) { Mark(selected.Owner); RefreshSignals(); RebuildPreview(); }
        EditorGUILayout.HelpBox("같은 피해 필드에 연결된 시그널은 피해량과 범위를 공유합니다. 시그널을 복제해도 기존의 연속 타격·중복 타격 방지 방식은 그대로 적용됩니다.", MessageType.None);
        DrawCollider(Selected?.Field?.Hitbox);
    }

    private void DrawSignalTime(PlayerAttackStudioSignal row)
    {
        SkillStudioGUI.Section(row.Label + " · 타이밍");
        DrawTrackToggle(row.Track);
        EditorGUI.BeginChangeCheck();
        double next = EditorGUILayout.DoubleField("발생 시각 (초)", row.Marker.time);
        int frame = EditorGUILayout.IntField("발생 프레임", (int)Math.Round(row.Marker.time * FrameRate));
        if (EditorGUI.EndChangeCheck())
        {
            double value = frame != (int)Math.Round(row.Marker.time * FrameRate) ? frame / FrameRate : Snap(Finite(next, row.Marker.time));
            PlayerAttackStudioData.SetMarkerTime(row, Math.Max(0, value)); Mark(row.Marker); Seek(row.Marker.time);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("−1f")) MoveSignal(row, -1);
            if (GUILayout.Button("마커로 이동")) Seek(row.Marker.time);
            if (GUILayout.Button("+1f")) MoveSignal(row, 1);
            if (GUILayout.Button("Inspector")) Selection.activeObject = row.Marker;
        }
        if (!row.IsBound) EditorGUILayout.HelpBox("이 시그널의 실행 연결이 없습니다. Signal Receiver의 반응을 연결하세요.", MessageType.Warning);
        else if (!string.IsNullOrEmpty(row.BindingIssue)) EditorGUILayout.HelpBox(row.BindingIssue, MessageType.Warning);
    }

    private void MoveSignal(PlayerAttackStudioSignal row, int frames)
    {
        PlayerAttackStudioData.SetMarkerTime(row, Math.Max(0, row.Marker.time + frames / FrameRate)); Mark(row.Marker); Seek(row.Marker.time);
    }

    private void DrawAnimationAndGates()
    {
        SkillStudioGUI.Section("실제 재생 애니메이션");
        var context = Context;
        using (new EditorGUI.DisabledScope(!context.CanEditAnimation))
        {
            EditorGUI.BeginChangeCheck();
            var clip = (AnimationClip)EditorGUILayout.ObjectField("원본 애니메이션", context.Clip, typeof(AnimationClip), false);
            float speed = EditorGUILayout.FloatField("실제 공격 배속", context.Speed);
            if (EditorGUI.EndChangeCheck() && clip != null)
            {
                try
                {
                    PlayerAttackStudioData.SetAnimation(context, clip, speed);
                    Mark(context.State); Mark(context.Timeline); RefreshData(); RebuildPreview(); _message = null;
                }
                catch (Exception exception) { _message = exception.Message; }
            }
        }
        if (!string.IsNullOrEmpty(context.AnimationIssue)) EditorGUILayout.HelpBox(context.AnimationIssue, MessageType.Info);
        EditorGUILayout.LabelField($"원본 {context.Clip?.length ?? 0:0.000}초 · 실전 애니메이션 {context.AnimationDuration:0.000}초", EditorStyles.miniLabel);
        EditorGUILayout.HelpBox("애니메이션은 실제 Animator 상태와 Timeline의 참조 클립을 함께 바꿉니다. 타격·행동·이펙트 시각은 공격 시작부터의 초를 유지하므로, 클립이나 배속을 바꾸면 아래 마커 시각을 함께 확인하세요.", MessageType.None);
        if (!OrdinaryDodge)
            EditorGUILayout.LabelField($"기본 공격 자동 종료 기준: 애니메이션 92% · 약 {context.AnimationDuration * .92:0.000}초", EditorStyles.miniLabel);
        var gates = _signals.Where(row => !row.IsHit).ToArray();
        SkillStudioGUI.Section("콤보 · 행동 전환 · 기타 시그널");
        if (gates.Length > 0)
        {
            int index = Array.FindIndex(gates, row => row.Marker == _selectedMarker);
            if (index < 0) index = 0;
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup("편집할 시그널", index, gates.Select(row => $"{row.Label} · {Math.Round(row.Marker.time * FrameRate)}f").ToArray());
            if (EditorGUI.EndChangeCheck()) SelectSignal(gates[next]);
            DrawSignalTime(gates[next]);
        }
        else EditorGUILayout.LabelField("연결된 행동 시그널 없음", EditorStyles.miniLabel);
        if (!OrdinaryDodge) EditorGUILayout.HelpBox("기본 공격은 콤보 입력 허용 구간에서 공격 입력을 받아 다음 단으로 이어집니다. 상단 단계 선택은 편집 대상 선택이며, 4단을 자동으로 이어 재생하지 않습니다.", MessageType.None);
    }

    private void DrawEffects()
    {
        var entries = PlayerAttackStudioData.EnumerateTracks(Context.Timeline).SelectMany(track => track.GetClips()
            .Where(clip => clip.asset is ControlPlayableAsset || clip.asset is AudioPlayableAsset || clip.asset is PlayerOpacityClip)
            .Select(clip => (track, clip))).ToArray();
        SkillStudioGUI.Section("이펙트 · 사운드 클립");
        if (entries.Length == 0) { EditorGUILayout.LabelField("연결된 연출 클립 없음"); return; }
        int index = Array.FindIndex(entries, item => item.clip == _effectClip);
        bool staleSelection = index < 0;
        if (staleSelection) index = 0;
        EditorGUI.BeginChangeCheck();
        int next = EditorGUILayout.Popup("편집할 연출", index, entries.Select(item => $"{item.track.name} / {item.clip.displayName}").ToArray());
        if (EditorGUI.EndChangeCheck() || staleSelection) { _effectClip = entries[next].clip; _effectTrack = entries[next].track; }
        var clip = _effectClip;
        DrawTrackToggle(_effectTrack);
        EditorGUI.BeginChangeCheck();
        double start = EditorGUILayout.DoubleField("시작 (초)", clip.start);
        double duration = EditorGUILayout.DoubleField("길이 (초)", clip.duration);
        double source = clip.clipIn, speed = clip.timeScale;
        using (new EditorGUI.DisabledScope((clip.clipCaps & ClipCaps.ClipIn) == 0)) source = EditorGUILayout.DoubleField("원본 시작 (초)", source);
        using (new EditorGUI.DisabledScope((clip.clipCaps & ClipCaps.SpeedMultiplier) == 0)) speed = EditorGUILayout.DoubleField("클립 배속", speed);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_effectTrack, "Edit player attack presentation timing");
            clip.start = Math.Max(0, Snap(Finite(start, clip.start))); clip.duration = Math.Max(1 / FrameRate, Finite(duration, clip.duration));
            clip.clipIn = Math.Max(0, Finite(source, clip.clipIn)); clip.timeScale = Math.Clamp(Finite(speed, clip.timeScale), .001, 1000);
            Mark(_effectTrack); RefreshTimeline(); RebuildPreview();
        }
        if (GUILayout.Button("연출 시작으로 이동")) Seek(clip.start);
        if (clip.asset is ControlPlayableAsset control)
        {
            var sourceObject = control.sourceGameObject.Resolve(Context.Director);
            EditorGUI.BeginChangeCheck();
            var nextObject = (GameObject)EditorGUILayout.ObjectField("연결 이펙트 오브젝트", sourceObject, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObjects(new Object[] { control, Context.Director }, "Bind player attack effect");
                var reference = control.sourceGameObject;
                if (PropertyName.IsNullOrEmpty(reference.exposedName)) reference.exposedName = new PropertyName(Guid.NewGuid().ToString("N"));
                Context.Director.SetReferenceValue(reference.exposedName, nextObject); control.sourceGameObject = reference;
                Mark(control); Mark(Context.Director); sourceObject = nextObject; RebuildPreview();
            }
            DrawProperties(control, ("active", "구간 중 활성화"), ("postPlayback", "구간 종료 후 상태"), ("searchHierarchy", "자식 이펙트 포함"), ("particleRandomSeed", "파티클 랜덤 시드"));
            if (sourceObject != null) DrawTransform(sourceObject.transform);
        }
        else if (clip.asset is AudioPlayableAsset audio)
        {
            DrawProperties(audio, ("m_Clip", "사운드 클립"), ("m_Loop", "반복"), ("m_bufferingTime", "버퍼 시간"));
            EditorGUILayout.HelpBox("사운드는 실제 Timeline에서 재생됩니다. Scene 미리보기에서는 소리를 실행하지 않습니다.", MessageType.None);
        }
        else DrawProperties(clip.asset, ("Opacity", "투명도"));
    }

    private void DrawConnections()
    {
        SkillStudioGUI.Section("실제 실행 연결");
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("플레이어", _player, typeof(PlayerCore), true);
            EditorGUILayout.ObjectField("공격 Director", Context.Director, typeof(PlayableDirector), true);
            EditorGUILayout.ObjectField("공유 Timeline", Context.Timeline, typeof(TimelineAsset), false);
            EditorGUILayout.ObjectField("Animator 상태", Context.State, typeof(UnityEditor.Animations.AnimatorState), false);
        }
        EditorGUILayout.LabelField($"타격 {_signals.Count(row => row.IsHit)}개 · 기타 시그널 {_signals.Count(row => !row.IsHit)}개 · 미연결 {_signals.Count(row => !row.IsBound)}개", EditorStyles.miniLabel);
        EditorGUILayout.HelpBox(OrdinaryDodge
            ? "일반 회피 중 공격 입력으로 진입하는 RunAttack을 편집합니다. 달리기 중 공격도 같은 동작을 사용하므로 변경값을 공유합니다. 완벽 회피 후 반격 Start/Loop/End는 이 툴의 편집 대상이 아닙니다."
            : "기본 공격 1–4단의 실제 Timeline 시그널과 공격 컨테이너를 편집합니다. 다음 단 입력 허용·차단은 클립·타이밍 탭에서 조절합니다.", MessageType.Info);
        EditorGUILayout.HelpBox("미리보기는 독립된 모델과 이펙트를 사용하며 피해·행동 시그널을 실행하지 않습니다. 실제 적 추적·벽 충돌·콤보 입력은 Play Mode에서 확인하세요. Timeline의 꺼진 애니메이션 트랙은 참조용이며 실전 자세는 Animator가 재생합니다.", MessageType.None);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Timeline 열기")) OpenTimeline();
            if (GUILayout.Button("Director Inspector")) Selection.activeGameObject = Context.Director.gameObject;
            if (GUILayout.Button("연결 다시 읽기")) { RefreshData(); RebuildPreview(); }
        }
    }

    private void DrawTrackToggle(TrackAsset track)
    {
        if (track == null) return;
        EditorGUI.BeginChangeCheck(); bool enabled = EditorGUILayout.Toggle("트랙 사용", !track.muted);
        if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(track, "Toggle player attack track"); track.muted = !enabled; Mark(track); RefreshTimeline(); RebuildPreview(); }
        if (track.mutedInHierarchy) EditorGUILayout.HelpBox("이 트랙 또는 상위 그룹이 꺼져 있어 실제 게임에서 실행하지 않습니다.", MessageType.Warning);
    }

    private void DrawCollider(Collider collider)
    {
        SkillStudioGUI.Section("히트박스 · Scene 핸들");
        if (collider == null) { EditorGUILayout.HelpBox("피해 필드의 히트박스 Collider를 연결하세요.", MessageType.Warning); return; }
        _showFields = EditorGUILayout.Toggle("Scene에 범위 표시", _showFields);
        _handleMode = GUILayout.Toolbar(_handleMode, new[] { "중심 이동", "회전", "범위" });
        if (collider is BoxCollider) DrawProperties(collider, ("m_Center", "중심"), ("m_Size", "전체 크기"));
        else if (collider is SphereCollider) DrawProperties(collider, ("m_Center", "중심"), ("m_Radius", "반경"));
        else if (collider is CapsuleCollider) DrawProperties(collider, ("m_Center", "중심"), ("m_Radius", "반경"), ("m_Height", "전체 높이"), ("m_Direction", "축"));
        DrawTransform(collider.transform);
        if (GUILayout.Button("Scene에서 범위 보기")) { StartPreview(); Seek(Selected.Marker.time); FrameSelectedCollider(); }
        EditorGUILayout.HelpBox("선택한 타격의 해당 프레임에서만 범위와 핸들을 표시합니다. 중심·크기는 실제 Collider 로컬 단위입니다.", MessageType.None);
    }

    private void DrawTransform(Transform target)
    {
        EditorGUI.BeginChangeCheck();
        Vector3 position = EditorGUILayout.Vector3Field("로컬 위치", target.localPosition);
        Vector3 rotation = EditorGUILayout.Vector3Field("로컬 회전", target.localEulerAngles);
        Vector3 scale = EditorGUILayout.Vector3Field("로컬 크기", target.localScale);
        if (!EditorGUI.EndChangeCheck()) return;
        Undo.RecordObject(target, "Edit player attack spatial settings");
        target.localPosition = position; target.localEulerAngles = rotation; target.localScale = scale;
        Mark(target); RebuildPreview();
    }

    private void DrawProperties(Object target, params (string property, string label)[] properties)
    {
        var so = new SerializedObject(target); so.Update();
        foreach (var item in properties) { var property = so.FindProperty(item.property); if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(item.label), true); }
        if (so.ApplyModifiedProperties()) { Mark(target); RefreshTimeline(); RebuildPreview(); }
    }

    private static void Property(SerializedProperty field, string name, string label)
    { var property = field.FindPropertyRelative(name); if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label), true); }

    private void DrawFooter()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            bool dirty = _editedAssets.Any(asset => asset != null && EditorUtility.IsDirty(asset)) ||
                _actions.Any(action => action.Timeline != null && EditorUtility.IsDirty(action.Timeline)) ||
                _editedScenes.Any(scene => scene.IsValid() && scene.isLoaded && scene.isDirty) ||
                _player != null && _player.gameObject.scene.isDirty;
            GUILayout.Label(new GUIContent(ReadOnly ? "Play Mode · 읽기 전용" : dirty ? "● 저장하지 않은 변경" : "✓ 저장됨",
                "클립·시그널·Animator 상태는 공유 에셋에, 피해·범위·이펙트 연결은 선택한 플레이어 씬에 저장합니다."), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(ReadOnly))
            {
                if (GUILayout.Button("Undo", EditorStyles.toolbarButton, GUILayout.Width(46))) EditorApplication.delayCall += () => { if (this != null && !ReadOnly) { EndDrag(); Undo.PerformUndo(); } };
                if (GUILayout.Button("저장", EditorStyles.toolbarButton, GUILayout.Width(46))) EditorApplication.delayCall += () => { if (this != null) Save(); };
                using (new EditorGUI.DisabledScope(Context?.Director == null))
                    if (GUILayout.Button("Timeline 열기", EditorStyles.toolbarButton, GUILayout.Width(100))) OpenTimeline();
            }
        }
    }

    public void Save()
    {
        if (ReadOnly) return;
        StopPreview();
        foreach (var action in _actions)
        {
            if (action.Timeline != null) AssetDatabase.SaveAssetIfDirty(action.Timeline);
            if (action.State != null) AssetDatabase.SaveAssetIfDirty(action.State);
        }
        foreach (var asset in _editedAssets) if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
        var scenes = new HashSet<UnityEngine.SceneManagement.Scene>(_editedScenes);
        if (_player != null) scenes.Add(_player.gameObject.scene);
        bool saved = true;
        foreach (var scene in scenes) if (scene.IsValid() && scene.isLoaded && scene.isDirty) saved &= EditorSceneManager.SaveScene(scene);
        if (saved) { _editedAssets.Clear(); _editedScenes.Clear(); }
        ShowNotification(new GUIContent(saved ? "공격 설정 저장됨" : "씬 저장이 완료되지 않았습니다")); Repaint();
    }

    private void Mark(Object target)
    {
        if (target == null) return;
        EditorUtility.SetDirty(target);
        if (EditorUtility.IsPersistent(target))
        {
            var main = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(target));
            if (main != null) { EditorUtility.SetDirty(main); _editedAssets.Add(main); }
        }
        if (target is Component component && component.gameObject.scene.IsValid())
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene); _editedScenes.Add(component.gameObject.scene);
        }
    }

    private void OpenTimeline()
    { if (Context?.Director != null) { var window = TimelineEditor.GetOrCreateWindow(); window.SetTimeline(Context.Director); window.Focus(); } }
    private static void RefreshTimeline() => TimelineEditor.Refresh(RefreshReason.ContentsModified | RefreshReason.SceneNeedsUpdate);
    private double Snap(double time) => _snap ? Math.Round(time * FrameRate) / FrameRate : time;
    private static double Finite(double value, double fallback) => double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;

    public void Seek(double time)
    {
        if (ReadOnly) return;
        _playing = false; _time = Math.Clamp(Snap(Finite(time, 0)), 0, Duration);
        if (_preview?.IsReady != true) StartPreview(false);
        _preview?.Sample(_time); SceneView.RepaintAll(); Repaint();
    }

    private void StartPreview(bool focus = true)
    {
        if (ReadOnly || _player == null || Context?.Director == null || Context.Clip == null) return;
        _preview ??= new PlayerAttackStudioPreview();
        try { _preview.Build(_player, Context.Director, Context.Clip, Context.Speed); _preview.Sample(_time); _message = null; }
        catch (Exception exception) { _message = exception.Message; StopPreview(); return; }
        if (!focus || !_preview.IsReady) return;
        if (_previewView == null)
        {
            _previewView = CreateInstance<SceneView>(); _previewView.Show(); _hadPreviewView = true;
            _previewView.drawGizmos = false;
            _previewView.titleContent = new GUIContent(OrdinaryDodge ? "Dodge Attack · 3D Preview" : "Basic Attack · 3D Preview");
            float pixels = Mathf.Max(1, EditorGUIUtility.pixelsPerPoint);
            float desktopWidth = Mathf.Max(1280, Screen.currentResolution.width / pixels);
            float desktopHeight = Mathf.Max(790, Screen.currentResolution.height / pixels);
            float monitorLeft = Mathf.Floor(Mathf.Max(0, position.x) / desktopWidth) * desktopWidth;
            float studioWidth = Mathf.Clamp(position.width, 620, 720);
            float height = Mathf.Clamp(position.height, 680, desktopHeight - 110);
            float previewWidth = Mathf.Min(980, desktopWidth - studioWidth - 40);
            float y = Mathf.Clamp(position.y, 50, Mathf.Max(50, desktopHeight - height - 40));
            position = new Rect(monitorLeft + previewWidth + 24, y, studioWidth, height);
            _previewView.position = new Rect(monitorLeft + 12, y, previewWidth, height);
        }
        _previewView.LookAt(_preview.CurrentPosition + Vector3.up, Quaternion.Euler(25, -35, 0), 4.2f, false, true);
        _previewView.Repaint(); Focus();
    }

    private void RebuildPreview()
    { if (_preview?.IsReady == true) { bool playing = _playing; StartPreview(false); _playing = playing && _preview?.IsReady == true; } SceneView.RepaintAll(); }

    private void StopPreview()
    {
        EndDrag(); _playing = false; _preview?.Dispose(); _preview = null;
        if (_previewView != null) { _previewView.Close(); _previewView = null; }
        _hadPreviewView = false; SceneView.RepaintAll();
    }

    private bool IsSelectedHitVisible()
        => !ReadOnly && _preview?.IsReady == true && Selected?.IsHit == true && Selected.IsBound && string.IsNullOrEmpty(Selected.BindingIssue) &&
            !Selected.Track.mutedInHierarchy && Math.Round(_time * FrameRate) == Math.Round(Selected.Marker.time * FrameRate);

    private void FrameSelectedCollider()
    {
        var collider = Selected?.Field?.Hitbox;
        var transform = collider != null ? _preview?.ResolveTransform(collider.transform) : null;
        if (transform != null) _previewView?.Frame(new Bounds(transform.TransformPoint(Center(collider)), Vector3.one * 5), false);
    }

    private void DuringSceneGUI(SceneView view)
    {
        if (!_showFields || _tab != 0 || !IsSelectedHitVisible() || Selected.Field?.Hitbox is not Collider collider) return;
        var transform = _preview.ResolveTransform(collider.transform);
        if (transform == null) return;
        Color old = Handles.color; Handles.color = SkillStudioGUI.HitColor;
        Vector3 center = transform.TransformPoint(Center(collider));
        Handles.Label(center, $"{Selected.Label} · {Math.Round(Selected.Marker.time * FrameRate)}f");
        using (new Handles.DrawingScope(Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale)))
            if (collider is BoxCollider box) Handles.DrawWireCube(box.center, box.size);
        if (collider is SphereCollider sphereOutline) DrawSphere(center, sphereOutline.radius * MaximumScale(transform));
        if (collider is CapsuleCollider capsule) DrawCapsule(capsule, transform);
        EditorGUI.BeginChangeCheck();
        if (_handleMode == 0)
        {
            Vector3 next = Handles.PositionHandle(center, transform.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(collider, "Move player attack hitbox center");
                Vector3 local = transform.InverseTransformPoint(next);
                if (collider is BoxCollider b) b.center = local;
                else if (collider is SphereCollider s) s.center = local;
                else if (collider is CapsuleCollider c) c.center = local;
                Mark(collider); Repaint();
            }
        }
        else if (_handleMode == 1)
        {
            Quaternion rotation = Handles.RotationHandle(transform.rotation, center);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(collider.transform, "Rotate player attack hitbox");
                collider.transform.localRotation = transform.parent != null ? Quaternion.Inverse(transform.parent.rotation) * rotation : rotation;
                transform.localRotation = collider.transform.localRotation; Mark(collider.transform); Repaint();
            }
        }
        else if (collider is BoxCollider box)
        {
            using (new Handles.DrawingScope(Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale)))
            {
                _boxHandle.center = box.center; _boxHandle.size = box.size; _boxHandle.DrawHandle();
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(box, "Resize player attack hitbox"); box.center = _boxHandle.center; box.size = _boxHandle.size; Mark(box); Repaint(); }
            }
        }
        else if (collider is SphereCollider sphere)
        {
            float scale = MaximumScale(transform);
            float radius = Handles.RadiusHandle(Quaternion.identity, center, sphere.radius * scale);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(sphere, "Resize player attack hitbox"); sphere.radius = Mathf.Max(.005f, radius / scale); Mark(sphere); Repaint(); }
        }
        else EditorGUI.EndChangeCheck();
        Handles.color = old;
    }

    private static Vector3 Center(Collider collider) => collider is BoxCollider box ? box.center : collider is SphereCollider sphere ? sphere.center : collider is CapsuleCollider capsule ? capsule.center : Vector3.zero;
    private static Vector3 Abs(Vector3 value) => new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    private static float MaximumScale(Transform transform) { var scale = Abs(transform.lossyScale); return Mathf.Max(.0001f, scale.x, scale.y, scale.z); }
    private static void DrawSphere(Vector3 center, float radius)
    { Handles.DrawWireDisc(center, Vector3.up, radius); Handles.DrawWireDisc(center, Vector3.right, radius); Handles.DrawWireDisc(center, Vector3.forward, radius); }
    private static void DrawCapsule(CapsuleCollider collider, Transform transform)
    {
        Vector3 scale = Abs(transform.lossyScale);
        Vector3 localAxis = collider.direction == 0 ? Vector3.right : collider.direction == 2 ? Vector3.forward : Vector3.up;
        float axisScale = collider.direction == 0 ? scale.x : collider.direction == 2 ? scale.z : scale.y;
        float radiusScale = collider.direction == 0 ? Mathf.Max(scale.y, scale.z) : collider.direction == 2 ? Mathf.Max(scale.x, scale.y) : Mathf.Max(scale.x, scale.z);
        float radius = collider.radius * radiusScale;
        Vector3 axis = transform.TransformDirection(localAxis).normalized;
        Vector3 center = transform.TransformPoint(collider.center);
        Vector3 halfLine = axis * Mathf.Max(0, collider.height * axisScale * .5f - radius);
        Vector3 a = center + halfLine, b = center - halfLine;
        DrawSphere(a, radius); DrawSphere(b, radius);
        Vector3 tangent = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > .9f ? Vector3.right : Vector3.up).normalized * radius;
        Vector3 other = Vector3.Cross(axis, tangent).normalized * radius;
        foreach (var offset in new[] { tangent, -tangent, other, -other }) Handles.DrawLine(a + offset, b + offset);
    }
}
