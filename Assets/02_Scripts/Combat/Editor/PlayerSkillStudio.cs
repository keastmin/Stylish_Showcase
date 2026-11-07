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
using Unity.Cinemachine;
using Object = UnityEngine.Object;

/// <summary>Authors E gameplay data and its separate animation/presentation Timeline.</summary>
public sealed partial class PlayerSkillStudio : EditorWindow
{
    private static readonly Color Accent = SkillStudioGUI.Accent;
    private static readonly Color HitColor = SkillStudioGUI.HitColor;
    private static readonly string[] Tabs = { "타격 · 범위", "클립 · 시퀀스", "연출", "연결 · 실행" };
    [SerializeField] private PlayableDirector _director;
    [SerializeField] private int _tab;
    [SerializeField] private double _time;
    [SerializeField] private bool _snap = true;
    [SerializeField] private bool _showHitboxes = true;
    [SerializeField] private int _handleMode;
    private Vector2 _scroll, _overviewScroll;
    private TimelineClip _clip;
    private TrackAsset _track;
    private IMarker _marker;
    private bool _ownsPreview, _playing;
    private readonly HashSet<Object> _editedAssets = new();
    private readonly HashSet<UnityEngine.SceneManagement.Scene> _editedScenes = new();
    private readonly BoxBoundsHandle _boxHandle = new();
    private TimelineAsset Timeline => _director != null ? _director.playableAsset as TimelineAsset : null;
    private PlayerSkillAttackReceiver Receiver => _director != null ? _director.GetComponent<PlayerSkillAttackReceiver>() : null;
    private PlayerSkillCameraDirector CameraDirector => _director != null ? _director.GetComponent<PlayerSkillCameraDirector>() : null;
    private double FrameRate => Timeline != null ? Math.Max(1, Timeline.editorSettings.frameRate) : 60;
    private double Duration => Data != null ? Data.Duration : Timeline != null ? Math.Max(.01, Timeline.duration) : 1;
    private IEnumerable<TrackAsset> Tracks => Timeline != null ? AllTracks(Timeline) : Enumerable.Empty<TrackAsset>();

    [MenuItem("Tools/Character/Player/E - Skill Studio")]
    public static void Open() => GetWindow<PlayerSkillStudio>("E · Skill Studio").Show();

    public static void OpenDirector(PlayableDirector director)
    {
        var window = GetWindow<PlayerSkillStudio>("E · Skill Studio");
        window.SetDirector(director);
        window.Show();
    }

    private void OnEnable()
    {
        minSize = new Vector2(620, 650);
        _lastTick = EditorApplication.timeSinceStartup;
        Undo.undoRedoPerformed += OnUndo;
        SceneView.duringSceneGui += DuringSceneGUI;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayMode;
        AssemblyReloadEvents.beforeAssemblyReload += EndPreview;
        if (_director == null) FindTarget();
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndo;
        SceneView.duringSceneGui -= DuringSceneGUI;
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        AssemblyReloadEvents.beforeAssemblyReload -= EndPreview;
        EndMarkerDrag();
        EndPreview();
    }

    private void OnPlayMode(PlayModeStateChange state) { EndPreview(); Repaint(); }
    private void OnUndo()
    {
        EndDataMarkerDrag(); ValidateSelection();
        if (Data != null && _dataPreview != null) SampleDataPreview(_time, false);
        Refresh(); Repaint(); SceneView.RepaintAll();
    }
    private void Tick()
    {
        TickPreview();
    }

    public static IEnumerable<TrackAsset> AllTracks(TimelineAsset timeline)
    {
        if (timeline == null) yield break;
        foreach (var root in timeline.GetRootTracks())
            foreach (var track in Descendants(root)) yield return track;
        if (timeline.markerTrack != null) yield return timeline.markerTrack;
    }

    private static IEnumerable<TrackAsset> Descendants(TrackAsset track)
    {
        yield return track;
        foreach (var child in track.GetChildTracks())
            foreach (var nested in Descendants(child)) yield return nested;
    }

    private void FindTarget()
    {
        var selected = Selection.activeGameObject;
        var player = selected != null ? selected.GetComponentInParent<PlayerCore>() : null;
        var receiver = selected != null ? selected.GetComponent<PlayerSkillAttackReceiver>() : null;
        if (receiver == null && player != null) receiver = player.GetComponentInChildren<PlayerSkillAttackReceiver>(true);
        if (receiver == null) receiver = FindObjectsByType<PlayerSkillAttackReceiver>(FindObjectsInactive.Include)
            .FirstOrDefault(item => item.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(item));
        SetDirector(receiver != null ? receiver.GetComponent<PlayableDirector>() : null);
    }

    private void SetDirector(PlayableDirector director)
    {
        EndPreview();
        _director = director; _clip = null; _track = null; _marker = null; _time = 0;
        _animationClip = null; _animationTrack = null; _dataHit = 0;
        ValidateSelection();
        Repaint();
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawDataConnection();
        if (Data != null)
        {
            DrawDataStudio();
            return;
        }
        if (Timeline == null || Receiver == null)
        {
            EditorGUILayout.HelpBox("Player의 Skill Director를 선택하세요. E 스킬의 Timeline과 Player Skill Attack Receiver가 연결된 Director가 필요합니다.", MessageType.Info);
            return;
        }
        ValidateSelection();
        DrawSequenceStrip();
        DrawTransport();
        DrawHitTimeline();
        _tab = GUILayout.Toolbar(_tab, Tabs);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (_tab == 0) DrawHits();
            else if (_tab == 1) DrawSequence();
            else if (_tab == 2)
            {
                _presentationTab = GUILayout.Toolbar(_presentationTab, new[] { "이펙트", "카메라" });
                if (_presentationTab == 0) DrawEffects(); else DrawCamera();
            }
            else DrawSetup();
        }
        EditorGUILayout.EndScrollView();
        DrawFooter();
    }

    private void DrawHeader()
    {
        SkillStudioGUI.Header("E · SKILL STUDIO", Data != null
            ? "이동 · 타격 · 화면 효과 · 입력 복귀를 E 데이터에서 조절하세요."
            : "클립과 타격 마커를 선택하고 실제 스킬의 범위 · 연출을 조절하세요.");
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            var next = (PlayableDirector)EditorGUILayout.ObjectField("E 스킬 연결", _director, typeof(PlayableDirector), true);
            if (EditorGUI.EndChangeCheck()) SetDirector(next);
            if (GUILayout.Button("찾기", GUILayout.Width(44))) FindTarget();
        }
        if (Timeline != null)
            EditorGUILayout.LabelField($"● {_director.gameObject.scene.name} / {_director.name} · {Timeline.name} · 다음 시전부터 반영", EditorStyles.miniLabel);
    }

    private void DrawTransport()
    {
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(_previewView != null ? "Scene 다시 보기" : "Scene 미리보기", EditorStyles.toolbarButton, GUILayout.Width(112))) StartPreview();
                if ((_ownsPreview || _previewView != null) && GUILayout.Button("닫기", EditorStyles.toolbarButton, GUILayout.Width(38))) EndPreview();
                if (GUILayout.Button(_playing ? "Ⅱ 정지" : "▶ 재생", EditorStyles.toolbarButton, GUILayout.Width(68)))
                {
                    if (_playing) _playing = false;
                    else
                    {
                        if (!_ownsPreview) StartPreview();
                        if (_time < PreviewStart || _time >= PreviewEnd) SamplePreview(PreviewStart, false);
                        _lastTick = EditorApplication.timeSinceStartup;
                        _playing = true;
                    }
                }
                if (GUILayout.Button("|◀", EditorStyles.toolbarButton, GUILayout.Width(30))) Seek(PreviewStart);
                if (GUILayout.Button("−1f", EditorStyles.toolbarButton, GUILayout.Width(38))) Seek(_time - 1 / FrameRate);
                if (GUILayout.Button("+1f", EditorStyles.toolbarButton, GUILayout.Width(38))) Seek(_time + 1 / FrameRate);
                _loop = GUILayout.Toggle(_loop, "반복", EditorStyles.toolbarButton, GUILayout.Width(42));
                GUILayout.FlexibleSpace();
                GUILayout.Label("관찰 배속", EditorStyles.miniLabel);
                _previewSpeed = Mathf.Clamp((float)Finite(EditorGUILayout.FloatField(_previewSpeed, GUILayout.Width(42)), 1), .05f, 3f);
            }
            using (new EditorGUI.DisabledScope(_animationClip == null))
            {
                int mode = GUILayout.Toolbar(_focusSelectedClip ? 0 : 1, new[] { "선택 클립 집중 재생", "전체 시퀀스 재생" });
                if (_focusSelectedClip != (mode == 0)) { _focusSelectedClip = mode == 0; _playing = false; }
            }
            EditorGUI.BeginChangeCheck();
            double time = EditorGUILayout.Slider("전체 재생 위치", (float)_time, 0, (float)Duration);
            if (EditorGUI.EndChangeCheck()) Seek(time);
            if (_animationClip != null)
            {
                EditorGUI.BeginChangeCheck();
                double sourceTime = EditorGUILayout.Slider("편집 클립 원본 시간", (float)TimelineToSourceTime(_animationClip, _time),
                    (float)_animationClip.clipIn, (float)(_animationClip.clipIn + _animationClip.duration * _animationClip.timeScale));
                if (EditorGUI.EndChangeCheck()) Seek(SourceToTimelineTime(_animationClip, sourceTime));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"전체 {_time:0.000} / {Duration:0.000}초 · {Math.Round(_time * FrameRate)} / {Math.Round(Duration * FrameRate)}f · {FrameRate:0.##}fps", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                _snap = GUILayout.Toggle(_snap, "프레임 스냅", GUILayout.Width(92));
            }
        }
    }

    private TimelineEditorWindow OpenTimeline()
    {
        var window = TimelineEditor.GetOrCreateWindow();
        if (TimelineEditor.inspectedDirector != _director) window.SetTimeline(_director);
        return window;
    }

    public void Seek(double time)
    {
        _playing = false;
        SamplePreview(time, true);
    }

    private void EndPreview()
    {
        EndMarkerDrag();
        EndDataPreview();
        if (_ownsPreview && !EditorApplication.isPlaying && TimelineEditor.inspectedDirector == _director)
        {
            var window = TimelineEditor.GetWindow();
            if (window != null) { window.playbackControls.Pause(); window.ClearTimeline(); }
        }
        _ownsPreview = false; _playing = false;
        if (_previewView != null) { _previewView.Close(); _previewView = null; }
        _previewHadView = false;
    }

    private double Snap(double time) => _snap ? Math.Round(time * FrameRate) / FrameRate : time;

    private void DrawOverview()
    {
        var tracks = Tracks.Where(track => track.hasClips || track.GetMarkerCount() > 0).ToArray();
        _overviewScroll = EditorGUILayout.BeginScrollView(_overviewScroll, GUILayout.Height(Math.Min(218, 24 + tracks.Length * 24)));
        foreach (var track in tracks)
        {
            Rect row = GUILayoutUtility.GetRect(0, 23, GUILayout.ExpandWidth(true));
            Rect label = new(row.x, row.y, 145, row.height);
            Rect lane = new(row.x + 150, row.y + 2, Math.Max(60, row.width - 157), row.height - 4);
            GUI.Label(label, new GUIContent(track.name, track.GetType().Name), EditorStyles.miniLabel);
            EditorGUI.DrawRect(lane, new Color(.13f, .15f, .19f));
            foreach (var clip in track.GetClips())
            {
                Rect block = new(lane.x + (float)(clip.start / Duration) * lane.width, lane.y,
                    Mathf.Max(4, (float)(clip.duration / Duration) * lane.width), lane.height);
                var old = GUI.backgroundColor;
                GUI.backgroundColor = _clip == clip ? Accent : new Color(.55f, .65f, .8f);
                if (GUI.Button(block, new GUIContent(block.width > 50 ? clip.displayName : "", $"{clip.displayName}: {clip.start:0.000}–{clip.end:0.000}초"), EditorStyles.miniButton))
                    SelectClip(track, clip);
                GUI.backgroundColor = old;
            }
            foreach (var marker in track.GetMarkers())
            {
                float x = lane.x + (float)(marker.time / Duration) * lane.width;
                var old = GUI.color; GUI.color = marker is PlayerSkillHitMarker ? HitColor : Accent;
                if (GUI.Button(new Rect(x - 6, row.y + 1, 13, 21), new GUIContent("◆", $"{MarkerName(marker)} · {marker.time:0.000}초"), EditorStyles.miniLabel))
                    SelectMarker(track, marker);
                GUI.color = old;
            }
            EditorGUI.DrawRect(new Rect(lane.x + (float)(_time / Duration) * lane.width, lane.y, 1, lane.height), Color.white);
        }
        EditorGUILayout.EndScrollView();
    }

    private void SelectClip(TrackAsset track, TimelineClip clip)
    {
        _track = track; _clip = clip; _marker = null;
        _tab = IsCameraTrack(track) || IsEffectTrack(track) ? 2 : 1;
        _presentationTab = IsCameraTrack(track) ? 1 : 0;
        if (track is AnimationTrack) { _animationTrack = track; _animationClip = clip; }
        if (!EditorApplication.isPlayingOrWillChangePlaymode) Seek(clip.start);
        Repaint();
    }

    private void SelectMarker(TrackAsset track, IMarker marker)
    {
        _track = track; _marker = marker; _clip = null;
        _tab = marker is PlayerSkillHitMarker ? 0 : IsCameraTrack(track) ? 2 : 1;
        if (IsCameraTrack(track)) _presentationTab = 1;
        if (!EditorApplication.isPlayingOrWillChangePlaymode) Seek(marker.time);
        Repaint(); SceneView.RepaintAll();
    }

    private static bool IsCameraTrack(TrackAsset track) => track is CameraNoiseTrack || track is SkillCameraSignalTrack || track.name.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) >= 0;
    private static bool IsEffectTrack(TrackAsset track) => !IsCameraTrack(track) && (track is ControlTrack || track is PlayerOpacityTrack || track is TimelineActivationTrack);
    private static string MarkerName(IMarker marker) => marker is PlayerSkillHitMarker hit ? $"Hit{hit.DamageFieldNumber}" : marker is SignalEmitter signal && signal.asset != null ? signal.asset.name : (marker as Object)?.name ?? "Marker";

    private void DrawSequence()
    {
        DrawTimingList(Tracks.Where(track => !(track is PlayerSkillHitTrack)), "클립 · 시그널 구성");
        if (_clip != null) DrawClipDetails();
        else if (_marker != null) DrawMarkerDetails();
        else EditorGUILayout.HelpBox("클립 선택 띠 또는 목록에서 편집할 클립·시그널을 선택하세요.", MessageType.Info);
        EditorGUILayout.HelpBox("타격·이펙트·카메라 시간은 전체 Timeline의 초입니다. 원본 시작·배속·클립 위치를 바꿔도 다른 트랙은 이동하지 않으므로 타격 마커와 연출 구간을 함께 확인하세요.", MessageType.None);
        _showOverview = EditorGUILayout.Foldout(_showOverview, "전체 연출 트랙 보기", true);
        if (_showOverview) DrawOverview();
        if (GUILayout.Button("Unity Timeline에서 상세 편집", GUILayout.Height(26))) OpenTimeline();
    }

    private void DrawSetup()
    {
        Heading("실제 플레이어 연결");
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Skill Director", _director, typeof(PlayableDirector), true);
            EditorGUILayout.ObjectField("공유 Timeline", Timeline, typeof(TimelineAsset), false);
            EditorGUILayout.ObjectField("타격 실행", Receiver, typeof(PlayerSkillAttackReceiver), true);
            EditorGUILayout.ObjectField("카메라 실행", CameraDirector, typeof(PlayerSkillCameraDirector), true);
        }
        Heading("스킬 종료와 복귀");
        var settings = _director.GetComponent<PlayerSkillSequenceSettings>();
        if (settings != null) DrawProperties(settings, ("_useTimelineDuration", "Timeline 길이로 종료"), ("_endTime", "수동 종료 시각 (초)"), ("_recoveryDuration", "연출 후 복귀 대기 (초)"));
        else
        {
            EditorGUILayout.HelpBox("종료 시간을 Timeline과 연결하려면 시퀀스 설정을 추가하세요.", MessageType.Info);
            if (GUILayout.Button("시퀀스 설정 연결")) { settings = Undo.AddComponent<PlayerSkillSequenceSettings>(_director.gameObject); Mark(settings); }
        }
        if (settings != null) EditorGUILayout.LabelField($"연출 종료: {settings.GetContentEndTime(_director):0.000}초 · 복귀 대기: {settings.RecoveryDuration:0.000}초 · 전체: {settings.GetEndTime(_director):0.000}초", EditorStyles.miniLabel);
        Heading("타격 연결 상태");
        var hitTracks = Tracks.OfType<PlayerSkillHitTrack>().ToArray();
        foreach (var track in hitTracks)
        {
            var binding = _director.GetGenericBinding(track) as PlayerSkillAttackReceiver;
            EditorGUILayout.LabelField($"{track.name} · {track.GetMarkers().OfType<PlayerSkillHitMarker>().Count()}개 마커 · {(binding != null ? binding.name : "실행 연결 없음")}", EditorStyles.miniLabel);
            if (binding == null) EditorGUILayout.HelpBox("타격 트랙에 Player Skill Attack Receiver가 연결되어 있지 않습니다. Timeline에서 트랙 바인딩을 연결하세요.", MessageType.Warning);
        }
        EditorGUILayout.HelpBox("E는 현재 연결된 Timeline을 실행합니다. 재생·관찰 배속은 미리보기에만 적용되며, Play Mode에서는 설정을 변경할 수 없습니다. 타격 행 순서는 표시 순서이며 실행은 마커 시각을 따릅니다.", MessageType.None);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("플레이어 찾기")) FindTarget();
            if (GUILayout.Button("Timeline 연결 확인")) OpenTimeline();
            if (GUILayout.Button("Director Inspector")) Selection.activeGameObject = _director.gameObject;
        }
    }

    private void DrawTimingList(IEnumerable<TrackAsset> tracks, string title)
    {
        Heading(title);
        var options = new List<(TrackAsset track, TimelineClip clip, IMarker marker, string label)>();
        foreach (var track in tracks)
        {
            foreach (var clip in track.GetClips()) options.Add((track, clip, null, $"{track.name} / {clip.displayName} [{clip.start:0.000}–{clip.end:0.000}초]"));
            foreach (var marker in track.GetMarkers()) options.Add((track, null, marker, $"{track.name} / ◆ {MarkerName(marker)} [{marker.time:0.000}초]"));
        }
        if (options.Count == 0) { EditorGUILayout.LabelField("연결된 항목 없음"); return; }
        int selected = options.FindIndex(item => item.clip != null ? item.clip == _clip : item.marker == _marker);
        EditorGUI.BeginChangeCheck();
        int next = EditorGUILayout.Popup("편집할 항목", Math.Max(0, selected), options.Select(item => item.label).ToArray());
        if (EditorGUI.EndChangeCheck() || selected < 0)
        {
            var item = options[next]; _track = item.track; _clip = item.clip; _marker = item.marker;
            if (item.track is AnimationTrack && item.clip != null) { _animationTrack = item.track; _animationClip = item.clip; }
        }
    }

    private void DrawClipDetails()
    {
        if (_clip == null || _track == null) return;
        Heading($"{_track.name} · {_clip.displayName}");
        DrawTrackEnabled(_track);
        EditorGUI.BeginChangeCheck();
        string name = EditorGUILayout.TextField("표시 이름", _clip.displayName);
        double start = EditorGUILayout.DoubleField("시작 (초)", _clip.start);
        int frame = EditorGUILayout.IntField("시작 프레임", (int)Math.Round(_clip.start * FrameRate));
        double duration = EditorGUILayout.DoubleField("길이 (초)", _clip.duration);
        double clipIn, speed, fadeIn, fadeOut;
        using (new EditorGUI.DisabledScope((_clip.clipCaps & ClipCaps.ClipIn) == 0))
            clipIn = EditorGUILayout.DoubleField("원본 시작 (초)", _clip.clipIn);
        using (new EditorGUI.DisabledScope((_clip.clipCaps & ClipCaps.SpeedMultiplier) == 0))
            speed = EditorGUILayout.DoubleField("클립 배속", _clip.timeScale);
        using (new EditorGUI.DisabledScope((_clip.clipCaps & ClipCaps.Blending) == 0))
        {
            fadeIn = EditorGUILayout.DoubleField("진입 블렌드 (초)", _clip.easeInDuration);
            fadeOut = EditorGUILayout.DoubleField("종료 블렌드 (초)", _clip.easeOutDuration);
        }
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_track, "Edit E skill clip timing");
            _clip.displayName = name;
            _clip.start = Math.Max(0, frame != (int)Math.Round(_clip.start * FrameRate) ? frame / FrameRate : Snap(Finite(start, _clip.start)));
            _clip.duration = Math.Max(1 / FrameRate, Finite(duration, _clip.duration));
            _clip.clipIn = Math.Max(0, Finite(clipIn, _clip.clipIn));
            _clip.timeScale = Math.Clamp(Finite(speed, _clip.timeScale), .001, 1000);
            _clip.easeInDuration = Math.Clamp(Finite(fadeIn, 0), 0, _clip.duration);
            _clip.easeOutDuration = Math.Clamp(Finite(fadeOut, 0), 0, _clip.duration);
            Mark(_track); Refresh();
        }
        EditorGUILayout.LabelField($"종료 {_clip.end:0.000}초 / {Math.Round(_clip.end * FrameRate)}f", EditorStyles.miniLabel);
        if (_clip.asset is AnimationPlayableAsset)
            EditorGUILayout.LabelField($"원본 {_clip.clipIn:0.000}–{_clip.clipIn + _clip.duration * _clip.timeScale:0.000}초 → 전체 {_clip.start:0.000}–{_clip.end:0.000}초", EditorStyles.wordWrappedMiniLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("클립 시작으로 이동")) Seek(_clip.start);
            if (GUILayout.Button("Timeline에서 선택")) { OpenTimeline(); TimelineEditor.selectedClip = _clip; }
        }
        if (_clip.asset is AnimationPlayableAsset animation)
            DrawProperties(animation, ("m_Clip", "애니메이션"), ("m_Position", "이동 오프셋"), ("m_EulerAngles", "회전 오프셋"));
        else if (_clip.asset is CameraNoiseClip || _clip.asset is PlayerOpacityClip)
            DrawAllProperties(_clip.asset);
    }

    private void DrawMarkerDetails()
    {
        if (_marker == null || _marker is not Object owner) return;
        Heading($"{MarkerName(_marker)} · 타이밍");
        DrawTrackEnabled(_track);
        EditorGUI.BeginChangeCheck();
        double time = EditorGUILayout.DoubleField("발생 시각 (초)", _marker.time);
        int frame = EditorGUILayout.IntField("발생 프레임", (int)Math.Round(_marker.time * FrameRate));
        if (EditorGUI.EndChangeCheck())
        {
            PlayerSkillTimelineUtility.SetMarkerTime(_marker, Math.Max(0, frame != (int)Math.Round(_marker.time * FrameRate) ? frame / FrameRate : Snap(Finite(time, _marker.time))));
            Mark(owner); Refresh(); Seek(_marker.time);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("−1f")) MoveMarker(-1);
            if (GUILayout.Button("마커로 이동")) Seek(_marker.time);
            if (GUILayout.Button("+1f")) MoveMarker(1);
            if (GUILayout.Button("Inspector에서 선택")) { OpenTimeline(); Selection.activeObject = owner; }
        }
        if (_marker is SignalEmitter emitter)
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("실행할 Signal", emitter.asset, typeof(SignalAsset), false);
    }

    private void MoveMarker(int frames)
    {
        PlayerSkillTimelineUtility.SetMarkerTime(_marker, Math.Max(0, _marker.time + frames / FrameRate));
        Mark((Object)_marker); Refresh(); Seek(_marker.time);
    }

    private void DrawTrackEnabled(TrackAsset track)
    {
        if (track == null) return;
        EditorGUI.BeginChangeCheck();
        bool enabled = EditorGUILayout.Toggle("트랙 사용", !track.muted);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(track, "Toggle E skill track");
            track.muted = !enabled;
            Mark(track); Refresh(true);
        }
        if (track.mutedInHierarchy)
            EditorGUILayout.HelpBox(track.muted ? "이 트랙은 꺼져 있어 실제 E 시전에서 실행되지 않습니다." : "상위 그룹이 꺼져 있습니다. Timeline에서 그룹을 켜세요.", MessageType.Warning);
    }

    private void DrawHits()
    {
        var hitTracks = Tracks.OfType<PlayerSkillHitTrack>().ToArray();
        var hits = hitTracks.SelectMany(PlayerSkillTimelineUtility.OrderedHits).ToArray();
        Heading("타격 선택");
        if (hits.Length > 0)
        {
            int current = Array.FindIndex(hits, item => ReferenceEquals(item, _marker));
            if (current < 0) { _marker = hits[0]; _track = hits[0].parent; _clip = null; current = 0; }
            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup("편집할 타격", current, hits.Select((item, index) => $"{index + 1:00} · {MarkerName(item)} · {Math.Round(item.time * FrameRate)}f{(item.Enabled ? "" : " · 꺼짐")}").ToArray());
            if (EditorGUI.EndChangeCheck()) SelectMarker(hits[next].parent, hits[next]);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("현재 시각에 타격 추가")) { AddHit((_marker as PlayerSkillHitMarker)?.parent as PlayerSkillHitTrack ?? hitTracks.FirstOrDefault()); return; }
            using (new EditorGUI.DisabledScope(_marker is not PlayerSkillHitMarker))
                if (GUILayout.Button("선택 타격 삭제")) { RemoveHit((PlayerSkillHitMarker)_marker); return; }
        }
        if (_marker is not PlayerSkillHitMarker hit)
        {
            EditorGUILayout.HelpBox("Skill Hits 트랙의 타격 마커가 필요합니다.", MessageType.Warning); return;
        }
        DrawMarkerDetails();
        EditorGUI.BeginChangeCheck();
        bool enabled = EditorGUILayout.Toggle("이 타격 사용", hit.Enabled);
        int fieldNumber = EditorGUILayout.IntSlider("연결 데미지 필드", hit.DamageFieldNumber, 1, PlayerSkillAttackReceiver.DamageFieldCount);
        if (EditorGUI.EndChangeCheck())
        {
            PlayerSkillTimelineUtility.StabilizeHitFields(hit.parent);
            Undo.RecordObject(hit, "Edit E skill hit identity");
            hit.Enabled = enabled; hit.ExplicitDamageFieldNumber = fieldNumber; Mark(hit); Refresh();
        }
        EditorGUILayout.HelpBox($"Hit{hit.DamageFieldNumber}의 피해와 범위를 사용합니다. 같은 번호의 마커는 설정을 공유하며, 마커를 옮기거나 행 순서를 바꿔도 연결 번호는 유지됩니다.", MessageType.None);
        var receiver = _director.GetGenericBinding(hit.parent) as PlayerSkillAttackReceiver ?? Receiver;
        if (receiver == null) return;
        var so = new SerializedObject(receiver); so.Update();
        var fields = so.FindProperty("_damageFields");
        if (fields == null || fields.arraySize < hit.DamageFieldNumber) return;
        var field = fields.GetArrayElementAtIndex(hit.DamageFieldNumber - 1);
        Heading($"Hit{hit.DamageFieldNumber} · 피해와 판정");
        Property(field, "_hitbox", "히트박스");
        Property(field, "_damage", "피해량");
        Property(field, "_staggerLevel", "경직 단계");
        Property(field, "_hitStopFrame", "히트스톱 (60Hz 프레임)");
        Property(field, "_hitStopMode", "히트스톱 대상");
        Property(field, "_targetLayers", "피격 대상 레이어");
        Property(field, "_triggerInteraction", "Trigger 판정");
        Property(field, "_skillGaugeAdditive", "명중당 게이지 증가");
        if (so.ApplyModifiedProperties()) Mark(receiver);
        DrawCollider(receiver.GetDamageField(hit.DamageFieldNumber)?.Hitbox);
    }

    private void AddHit(PlayerSkillHitTrack track)
    {
        if (track == null)
        {
            ShowNotification(new GUIContent("Unity Timeline에 Skill Hit Track을 먼저 연결하세요.")); return;
        }
        PlayerSkillTimelineUtility.StabilizeHitFields(track);
        int fieldNumber = PlayerSkillTimelineUtility.NextUnusedField(track);
        int displayOrder = PlayerSkillTimelineUtility.OrderedHits(track).Select(hit => hit.DisplayOrder).DefaultIfEmpty(-1).Max() + 1;
        Undo.RegisterCompleteObjectUndo(track, "Add E skill hit");
        var marker = track.CreateMarker<PlayerSkillHitMarker>(Math.Max(0, Snap(_time)));
        marker.name = "Skill Hit";
        marker.ExplicitDamageFieldNumber = fieldNumber; marker.DisplayOrder = displayOrder;
        _track = track; _marker = marker; _clip = null;
        Mark(track); Mark(marker); Refresh(true);
    }

    private void RemoveHit(PlayerSkillHitMarker hit)
    {
        var track = hit.parent;
        PlayerSkillTimelineUtility.StabilizeHitFields(track);
        Undo.RegisterCompleteObjectUndo(track, "Remove E skill hit");
        Undo.RegisterCompleteObjectUndo(hit, "Remove E skill hit");
        track.DeleteMarker(hit);
        _marker = track.GetMarkers().OfType<PlayerSkillHitMarker>().FirstOrDefault();
        Mark(track); Refresh(true);
    }

    private Collider SelectedCollider => _marker is PlayerSkillHitMarker hit && Receiver != null
        ? (_director.GetGenericBinding(hit.parent) as PlayerSkillAttackReceiver ?? Receiver).GetDamageField(hit.DamageFieldNumber)?.Hitbox : null;

    private void DrawCollider(Collider collider)
    {
        if (collider == null) { EditorGUILayout.HelpBox("히트박스 Collider를 연결하세요.", MessageType.Warning); return; }
        Heading("히트박스 범위 · Scene 핸들");
        _showHitboxes = EditorGUILayout.Toggle("Scene에 범위 표시", _showHitboxes);
        _handleMode = GUILayout.Toolbar(_handleMode, new[] { "중심 이동", "회전", "범위" });
        DrawTransform(collider.transform);
        if (collider is BoxCollider) DrawProperties(collider, ("m_Center", "중심"), ("m_Size", "전체 크기 (폭·높이·깊이)"));
        else if (collider is SphereCollider) DrawProperties(collider, ("m_Center", "중심"), ("m_Radius", "반경"));
        else if (collider is CapsuleCollider) DrawProperties(collider, ("m_Center", "중심"), ("m_Radius", "반경"), ("m_Height", "높이"), ("m_Direction", "축 (0=X, 1=Y, 2=Z)"));
        else EditorGUILayout.HelpBox("Box / Sphere / Capsule Collider를 사용하세요.", MessageType.Warning);
        if (GUILayout.Button("Scene에서 범위 보기"))
        {
            Seek(_marker.time);
            if (SceneView.lastActiveSceneView == null) GetWindow<SceneView>().Show();
            var size = collider is BoxCollider box ? Vector3.Scale(box.size, Abs(box.transform.lossyScale)) : Vector3.one * 4;
            SceneView.lastActiveSceneView?.Frame(new Bounds(collider.transform.TransformPoint(ColliderCenter(collider)), size + Vector3.one), false);
            SceneView.RepaintAll();
        }
        EditorGUILayout.HelpBox("Scene 범위와 핸들은 선택한 타격 마커의 프레임에서만 표시합니다. ‘마커로 이동’ 후 범위를 조절하세요. 히트박스는 충돌을 켜지 않고 타격 순간에 한 번 검사합니다.", MessageType.None);
    }

    private void DrawEffects()
    {
        DrawTimingList(Tracks.Where(IsEffectTrack).Where(track => Data == null || !IsDataImpactTrack(track)), "VFX · 투명도");
        if (_clip == null || _track == null || !IsEffectTrack(_track)) return;
        DrawClipDetails();
        if (_clip.asset is ControlPlayableAsset control)
        {
            var source = control.sourceGameObject.Resolve(_director);
            EditorGUI.BeginChangeCheck();
            var next = (GameObject)EditorGUILayout.ObjectField("연결된 이펙트 오브젝트", source, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObjects(new Object[] { control, _director }, "Bind E skill effect");
                var reference = control.sourceGameObject;
                if (PropertyName.IsNullOrEmpty(reference.exposedName)) reference.exposedName = new PropertyName(Guid.NewGuid().ToString("N"));
                _director.SetReferenceValue(reference.exposedName, next); control.sourceGameObject = reference;
                Mark(control); Mark(_director); Refresh(); source = next;
            }
            DrawProperties(control, ("active", "클립 중 활성화"), ("postPlayback", "클립 종료 후 상태"), ("searchHierarchy", "자식 이펙트 포함"), ("particleRandomSeed", "파티클 랜덤 시드"));
            if (source != null)
            {
                DrawTransform(source.transform);
                EditorGUILayout.LabelField($"파티클 {source.GetComponentsInChildren<ParticleSystem>(true).Length}개", EditorStyles.miniLabel);
                if (GUILayout.Button("이펙트 상세 Inspector 열기")) { Selection.activeGameObject = source; EditorGUIUtility.PingObject(source); }
            }
            EditorGUILayout.HelpBox("클립 시작·길이·배속과 오브젝트 위치·회전·크기가 실제 E 연출에 적용됩니다. 파티클 세부 모양은 이펙트 Inspector에서 조절합니다.", MessageType.None);
        }
        var target = _director.GetGenericBinding(_track);
        if (target is PlayerSkillImpactFrame impact)
            DrawProperties(impact, ("_centerOffset", "플레이어 기준 중심"), ("_sourceDuration", "원본 이펙트 길이"));
    }

    private void DrawCamera()
    {
        var camera = CameraDirector;
        if (camera == null) { EditorGUILayout.HelpBox("Player Skill Camera Director 연결이 필요합니다.", MessageType.Warning); return; }
        Heading("관찰 카메라 · 이동 경로");
        DrawProperties(camera, ("_offsetPosition", "기본 위치 (시전 방향 기준)"), ("_xRotOffset", "기본 내려다보기 각도"),
            ("_followPlayerMovement", "플레이어 이동 추적"), ("_motionEnabled", "시간별 카메라 이동 사용"), ("_motionKeys", "카메라 이동 키 (초 · 위치 · 회전 · FOV)"),
            ("_recenterMode", "TPS 복귀 방식"), ("_recenterDuration", "TPS 리센터 시간"));
        using (new EditorGUI.DisabledScope(camera.MotionKeys.Count != 0))
            if (GUILayout.Button("현재 구도로 시작·종료 카메라 키 만들기")) CreateCameraKeys(camera);
        EditorGUILayout.HelpBox("카메라 키의 시간은 E Timeline의 초입니다. 위치·회전은 시전 방향 기준이며 FOV는 화각입니다. 카메라 활성·복귀 신호·진동 구간은 아래에서 조절합니다.", MessageType.None);
        DrawTimingList(Tracks.Where(IsCameraTrack), "카메라 활성 · 진동 · 시그널");
        if (_clip != null && _track != null && IsCameraTrack(_track)) DrawClipDetails();
        else if (_marker != null && _track != null && IsCameraTrack(_track)) DrawMarkerDetails();
        Heading("씬 카메라 연결");
        DrawProperties(camera, ("_skillCinemachine", "Skill Camera"), ("_playerTPSOrbitFollow", "TPS Orbit"), ("_mainCameraBrain", "Main Camera Brain"), ("_previewSkillOrigin", "미리보기 시작점"));
        var cameraProperties = new SerializedObject(camera);
        var brain = cameraProperties.FindProperty("_mainCameraBrain").objectReferenceValue as CinemachineBrain;
        if (brain != null && brain.CustomBlends != null)
        {
            Heading("카메라 진입 · 복귀 블렌드");
            DrawProperties(brain.CustomBlends, ("CustomBlends", "카메라별 전환 설정"));
            EditorGUILayout.HelpBox("공유 Custom Blends 에셋입니다. Skill Camera와 TPS 사이 항목의 시간·곡선을 조절하세요.", MessageType.None);
        }
        if (camera.SkillCamera == null || camera.TPSOrbit == null)
            EditorGUILayout.HelpBox("Skill Camera와 TPS Orbit이 연결되어야 실제 카메라 전환이 동작합니다.", MessageType.Warning);
        var activation = camera.ActivationTrack?.GetClips().FirstOrDefault();
        var signals = Tracks.OfType<SkillCameraSignalTrack>().SelectMany(item => item.GetMarkers()).OfType<SignalEmitter>();
        var position = signals.FirstOrDefault(item => item.asset == camera.PositionSignal);
        var recenter = signals.FirstOrDefault(item => item.asset == camera.RecenterSignal);
        if (activation != null && (position == null || position.time >= activation.start))
            EditorGUILayout.HelpBox("위치 설정 Signal을 카메라 활성 클립 시작보다 앞에 두세요.", MessageType.Warning);
        if (activation != null && (recenter == null || recenter.time >= activation.end ||
            camera.RecenterMode == PlayerSkillCameraDirector.RecenterModes.Smooth && recenter.time + camera.RecenterDuration > activation.end))
            EditorGUILayout.HelpBox("TPS 리센터가 카메라 활성 클립 종료 전 완료되도록 복귀 Signal 시간을 조절하세요.", MessageType.Warning);
    }

    private void CreateCameraKeys(PlayerSkillCameraDirector camera)
    {
        if (camera.MotionKeys.Count != 0) return;
        var so = new SerializedObject(camera);
        Vector3 offset = so.FindProperty("_offsetPosition").vector3Value;
        float pitch = so.FindProperty("_xRotOffset").floatValue;
        float fov = camera.SkillCamera != null ? camera.SkillCamera.Lens.FieldOfView : 60f;
        var active = camera.ActivationTrack?.GetClips().FirstOrDefault();
        Undo.RecordObject(camera, "Create E skill camera keys");
        foreach (float time in new[] { (float)(active?.start ?? 0), (float)(active?.end ?? Duration) })
            camera.MotionKeys.Add(new PlayerSkillCameraKey { time = time, offset = offset, euler = new Vector3(pitch, 0, 0), fieldOfView = fov });
        camera.MotionEnabled = true;
        Mark(camera); Refresh();
    }

    private void DrawTransform(Transform transform)
    {
        EditorGUI.BeginChangeCheck();
        Vector3 position = EditorGUILayout.Vector3Field("로컬 위치", transform.localPosition);
        Vector3 rotation = EditorGUILayout.Vector3Field("로컬 회전", transform.localEulerAngles);
        Vector3 scale = EditorGUILayout.Vector3Field("로컬 크기", transform.localScale);
        if (!EditorGUI.EndChangeCheck()) return;
        Undo.RecordObject(transform, "Edit E skill spatial settings");
        transform.localPosition = position; transform.localEulerAngles = rotation; transform.localScale = scale;
        Mark(transform); Refresh(); SceneView.RepaintAll();
    }

    private void DrawProperties(Object target, params (string property, string label)[] names)
    {
        var so = new SerializedObject(target); so.Update();
        foreach (var name in names)
        {
            var property = so.FindProperty(name.property);
            if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(name.label), true);
        }
        if (so.ApplyModifiedProperties()) { Mark(target); Refresh(); SceneView.RepaintAll(); }
    }

    private void DrawAllProperties(Object target)
    {
        var so = new SerializedObject(target); so.Update();
        var property = so.GetIterator(); bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (property.name != "m_Script") EditorGUILayout.PropertyField(property, true);
        }
        if (so.ApplyModifiedProperties()) { Mark(target); Refresh(); }
    }

    private static void Property(SerializedProperty parent, string name, string label)
    {
        var child = parent.FindPropertyRelative(name);
        if (child != null) EditorGUILayout.PropertyField(child, new GUIContent(label), true);
    }

    private void DrawFooter()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            bool readOnly = EditorApplication.isPlayingOrWillChangePlaymode;
            string status = readOnly ? "Play Mode · 읽기 전용" : HasEditsToSave() ? "● 저장하지 않은 변경" : "✓ 저장됨";
            GUILayout.Label(new GUIContent(status, "시간·클립은 공유 Timeline 에셋에, 피해·범위·카메라 연결은 선택한 플레이어 씬에 저장됩니다."), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(readOnly))
            {
                if (GUILayout.Button("Undo", EditorStyles.toolbarButton, GUILayout.Width(46)))
                    EditorApplication.delayCall += () =>
                    {
                        if (this == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
                        EndMarkerDrag(); Undo.PerformUndo(); Repaint();
                    };
                if (GUILayout.Button(new GUIContent("저장", "Timeline과 연결된 씬의 변경을 저장합니다."), EditorStyles.toolbarButton, GUILayout.Width(46)))
                    EditorApplication.delayCall += () => { if (this != null) Save(); };
                if (GUILayout.Button("Timeline 열기", EditorStyles.toolbarButton, GUILayout.Width(100))) OpenTimeline();
            }
        }
    }

    private bool HasEditsToSave()
    {
        if (Data != null && EditorUtility.IsDirty(Data)) return true;
        if (Timeline != null && EditorUtility.IsDirty(Timeline)) return true;
        if (_editedAssets.Any(asset => asset != null && EditorUtility.IsDirty(asset))) return true;
        if (_editedScenes.Any(scene => scene.IsValid() && scene.isLoaded && scene.isDirty)) return true;
        if (_director != null && _director.gameObject.scene.IsValid() && _director.gameObject.scene.isDirty) return true;
        var camera = CameraDirector;
        if (camera == null) return false;
        if (camera.SkillCamera != null && camera.SkillCamera.gameObject.scene.IsValid() && camera.SkillCamera.gameObject.scene.isDirty) return true;
        var cameraProperties = new SerializedObject(camera);
        var brain = cameraProperties.FindProperty("_mainCameraBrain")?.objectReferenceValue as CinemachineBrain;
        return brain != null && brain.CustomBlends != null && EditorUtility.IsDirty(brain.CustomBlends);
    }

    public void Save()
    {
        if ((Timeline == null && Data == null) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        EndPreview();
        if (Data != null) AssetDatabase.SaveAssetIfDirty(Data);
        if (Timeline != null) AssetDatabase.SaveAssetIfDirty(Timeline);
        foreach (var asset in _editedAssets) if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
        var scenes = new HashSet<UnityEngine.SceneManagement.Scene>(_editedScenes) { _director.gameObject.scene };
        var camera = CameraDirector;
        if (camera != null && camera.SkillCamera != null) scenes.Add(camera.SkillCamera.gameObject.scene);
        if (camera != null)
        {
            var cameraProperties = new SerializedObject(camera);
            var brain = cameraProperties.FindProperty("_mainCameraBrain").objectReferenceValue as CinemachineBrain;
            if (brain != null && brain.CustomBlends != null) AssetDatabase.SaveAssetIfDirty(brain.CustomBlends);
        }
        bool saved = true;
        foreach (var scene in scenes)
            if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                saved &= EditorSceneManager.SaveScene(scene);
        if (saved) { _editedAssets.Clear(); _editedScenes.Clear(); }
        ShowNotification(new GUIContent(saved ? "E 스킬 설정 저장됨" : "씬 저장이 완료되지 않았습니다"));
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
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
            _editedScenes.Add(component.gameObject.scene);
        }
    }

    private static void Refresh(bool structure = false) => TimelineEditor.Refresh(structure
        ? RefreshReason.ContentsAddedOrRemoved | RefreshReason.SceneNeedsUpdate
        : RefreshReason.ContentsModified | RefreshReason.SceneNeedsUpdate);
    private static void Heading(string text) => SkillStudioGUI.Section(text);
    private static double Finite(double value, double fallback) => double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
    private static Vector3 Abs(Vector3 vector) => new(Mathf.Abs(vector.x), Mathf.Abs(vector.y), Mathf.Abs(vector.z));
    private static Vector3 ColliderCenter(Collider collider) => collider is BoxCollider box ? box.center : collider is SphereCollider sphere ? sphere.center : collider is CapsuleCollider capsule ? capsule.center : Vector3.zero;

    private void DuringSceneGUI(SceneView view)
    {
        if (Data != null) { DuringDataSceneGUI(view); return; }
        if (!_showHitboxes || _tab != 0 || !_ownsPreview || !AnimationMode.InAnimationMode() || TimelineEditor.inspectedDirector != _director ||
            _marker is not PlayerSkillHitMarker selectedHit || !IsHitActive(selectedHit) || SelectedCollider is not Collider collider) return;
        var transform = collider.transform;
        Vector3 center = transform.TransformPoint(ColliderCenter(collider));
        var previous = Handles.color; Handles.color = HitColor;
        Handles.Label(center, $"{MarkerName(_marker)} · {_marker.time:0.000}초");
        using (new Handles.DrawingScope(Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale)))
            if (collider is BoxCollider drawBox) Handles.DrawWireCube(drawBox.center, drawBox.size);
        if (collider is SphereCollider drawSphere)
        {
            float radius = drawSphere.radius * Mathf.Max(Abs(transform.lossyScale).x, Abs(transform.lossyScale).y, Abs(transform.lossyScale).z);
            DrawSphere(center, radius);
        }
        if (collider is CapsuleCollider capsule) DrawCapsule(capsule);
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Handles.color = previous; return; }
        EditorGUI.BeginChangeCheck();
        if (_handleMode == 0)
        {
            Vector3 next = Handles.PositionHandle(center, transform.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(collider, "Move E skill hitbox center");
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
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(transform, "Rotate E skill hitbox"); transform.rotation = rotation; Mark(transform); Repaint(); }
        }
        else if (collider is BoxCollider box)
        {
            using (new Handles.DrawingScope(Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale)))
            {
                _boxHandle.center = box.center; _boxHandle.size = box.size; _boxHandle.DrawHandle();
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(box, "Resize E skill hitbox"); box.center = _boxHandle.center; box.size = _boxHandle.size; Mark(box); Repaint(); }
            }
        }
        else if (collider is SphereCollider sphere)
        {
            Vector3 scale = Abs(transform.lossyScale); float multiplier = Mathf.Max(.0001f, scale.x, scale.y, scale.z);
            float radius = Handles.RadiusHandle(Quaternion.identity, center, sphere.radius * multiplier);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(sphere, "Resize E skill hitbox"); sphere.radius = Mathf.Max(.005f, radius / multiplier); Mark(sphere); Repaint(); }
        }
        else EditorGUI.EndChangeCheck();
        Handles.color = previous;
    }

    private static void DrawSphere(Vector3 center, float radius)
    {
        Handles.DrawWireDisc(center, Vector3.up, radius); Handles.DrawWireDisc(center, Vector3.right, radius); Handles.DrawWireDisc(center, Vector3.forward, radius);
    }

    private static void DrawCapsule(CapsuleCollider capsule)
    {
        var transform = capsule.transform; Vector3 scale = Abs(transform.lossyScale);
        Vector3 localAxis = capsule.direction == 0 ? Vector3.right : capsule.direction == 2 ? Vector3.forward : Vector3.up;
        float axisScale = capsule.direction == 0 ? scale.x : capsule.direction == 2 ? scale.z : scale.y;
        float radiusScale = capsule.direction == 0 ? Mathf.Max(scale.y, scale.z) : capsule.direction == 2 ? Mathf.Max(scale.x, scale.y) : Mathf.Max(scale.x, scale.z);
        float radius = capsule.radius * radiusScale;
        Vector3 axis = transform.TransformDirection(localAxis).normalized;
        Vector3 center = transform.TransformPoint(capsule.center);
        Vector3 halfLine = axis * Mathf.Max(0, capsule.height * axisScale * .5f - radius);
        Vector3 a = center + halfLine, b = center - halfLine;
        Handles.DrawWireDisc(a, axis, radius); Handles.DrawWireDisc(b, axis, radius);
        Vector3 tangent = Vector3.Cross(axis, Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > .9f ? Vector3.right : Vector3.up).normalized * radius;
        Vector3 other = Vector3.Cross(axis, tangent).normalized * radius;
        foreach (var offset in new[] { tangent, -tangent, other, -other }) Handles.DrawLine(a + offset, b + offset);
        DrawSphere(a, radius); DrawSphere(b, radius);
    }
}
