using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>Visual authoring for the exact definition used by the Q ultimate runtime.</summary>
public sealed class PlayerUltimateStudio : EditorWindow
{
    private static readonly Color Accent = SkillStudioGUI.Accent;
    private static readonly Color HitColor = SkillStudioGUI.HitColor;
    private static readonly Color PathColor = SkillStudioGUI.PathColor;
    private static readonly Color InputCancelColor = new(1f, .75f, .24f);
    [SerializeField] private PlayerUltimateDefinition _definition;
    [SerializeField] private PlayerCore _player;
    [SerializeField] private int _selected;
    [SerializeField] private float _time;
    [SerializeField] private float _previewSpeed = 1f;
    [SerializeField] private bool _loop = true;
    [SerializeField] private int _handleMode;
    [SerializeField] private int _tab;
    [SerializeField] private int _selectedNode;
    [SerializeField] private bool _showHitFootprints = true;
    [SerializeField] private bool _followCamera;
    [SerializeField] private int _selectedClip;
    [SerializeField] private float _sequenceTime;
    [SerializeField] private bool _focusSelectedClip = true;
    private bool _sampleSelectedClip = true;
    private bool _previewSequenceDirty;
    private AnimationClip _clipToAdd;
    private string _bakeMessage;
    private bool _playing;
    private double _lastTick;
    private Vector2 _scroll;
    private Vector2 _timelineScroll;
    private UltimateStudioPreview _preview;
    private SceneView _previewView;
    private bool _previewHadView;
    private readonly BoxBoundsHandle _boxHandle = new BoxBoundsHandle
    {
        midpointHandleDrawFunction = Handles.CubeHandleCap,
        midpointHandleSizeFunction = point => HandleUtility.GetHandleSize(point) * .07f
    };
    private int _dragHit = -1, _dragNode = -1, _dragUndo;
    private float _dragX, _dragStart;
    private string _savedJson;
    private PlayerUltimateDefinition _savedDefinition;
    private bool SequenceMode => _definition != null && _definition.HasSequence;
    private UltimateSequenceClip Entry => SequenceMode && _selectedClip >= 0 && _selectedClip < _definition.sequence.Count ? _definition.sequence[_selectedClip] : null;
    private AnimationClip EditClip => SequenceMode ? Entry?.clip : _definition != null ? _definition.clip : null;
    private List<UltimateHitField> Hits => SequenceMode ? Entry?.hits : _definition?.hits;
    private List<UltimateCue> Cues => SequenceMode ? Entry?.cues : _definition?.cues;
    private float Duration => EditClip != null ? Mathf.Max(.01f, EditClip.length) : 1f;
    private float SequenceDuration => _definition != null ? Mathf.Max(.01f, _definition.Duration) : 1f;
    private float FrameRate => EditClip != null ? Mathf.Max(1f, EditClip.frameRate) : 60f;
    private UltimateHitField Selected => Hits != null && _selected >= 0 && _selected < Hits.Count ? Hits[_selected] : null;
    private float EntryStart => SequenceMode ? UltimateSequenceUtility.GetSegmentStart(_definition, _selectedClip) : 0f;
    // Source-clip endpoint inspection must show that clip's last pose. Whole-sequence
    // playback keeps the normal half-open interval rule and selects the next clip.
    private float SequenceSampleTime => SequenceMode && _sampleSelectedClip && Entry != null &&
        _selectedClip < _definition.sequence.Count - 1 && Entry.Duration > 0 &&
        _sequenceTime >= EntryStart && _sequenceTime <= EntryStart + Entry.Duration
        ? Mathf.Min(_sequenceTime, EntryStart + Entry.Duration - Mathf.Min(.00001f, Entry.Duration * .001f)) : _sequenceTime;
    private UltimateMotionWarp Motion => _definition != null ? _definition.warp : null;
    private UltimateMotionWaypoint SelectedNode => Motion != null && Motion.points != null && _selectedNode >= 0 && _selectedNode < Motion.points.Count ? Motion.points[_selectedNode] : null;

    [MenuItem("Tools/Character/Player/Q - Ultimate Studio")]
    public static void Open() => GetWindow<PlayerUltimateStudio>("Q · Ultimate Studio").Show();

    public static void OpenDefinition(PlayerUltimateDefinition definition)
    {
        var window = GetWindow<PlayerUltimateStudio>("Q · Ultimate Studio");
        window.ClearPreview();
        window._definition = definition;
        window._selected = 0; window._selectedClip = 0; window._time = 0; window._sequenceTime = 0; window._previewSequenceDirty = true;
        window.CaptureBaseline();
        window.Show();
    }

    private void OnEnable()
    {
        minSize = new Vector2(620, 650);
        _preview = new UltimateStudioPreview();
        _lastTick = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        SceneView.duringSceneGui += DuringSceneGUI;
        Undo.undoRedoPerformed += OnUndo;
        EditorApplication.playModeStateChanged += OnPlayMode;
        AssemblyReloadEvents.beforeAssemblyReload += ClearPreview;
        FindTarget();
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        SceneView.duringSceneGui -= DuringSceneGUI;
        Undo.undoRedoPerformed -= OnUndo;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        AssemblyReloadEvents.beforeAssemblyReload -= ClearPreview;
        ClearPreview();
    }

    private void OnPlayMode(PlayModeStateChange state) { ClearPreview(); _playing = false; Repaint(); }
    private void ClearPreview()
    {
        _preview?.Dispose();
        if (_previewView != null) { _previewView.Close(); _previewView = null; }
        _previewHadView = false;
        SceneView.RepaintAll();
    }
    private void OnUndo()
    {
        _bakeMessage = null;
        _selectedClip = Mathf.Clamp(_selectedClip, 0, Mathf.Max(0, (_definition?.sequence?.Count ?? 0) - 1));
        _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, (Hits?.Count ?? 0) - 1));
        _selectedNode = Mathf.Clamp(_selectedNode, 0, Mathf.Max(0, (Motion?.points?.Count ?? 0) - 1));
        _previewSequenceDirty = true;
        if (SequenceMode) _sequenceTime = SourceToSequenceTime(_time);
        Sample(); Repaint();
    }

    private void FindTarget()
    {
        if (_player == null && Selection.activeGameObject != null) _player = Selection.activeGameObject.GetComponentInParent<PlayerCore>();
        if (_player == null) _player = FindAnyObjectByType<PlayerCore>();
        if (_definition == null && _player != null) _definition = _player.UltimateDefinition;
        if (_definition == null)
        {
            var guid = AssetDatabase.FindAssets("t:PlayerUltimateDefinition").FirstOrDefault();
            if (guid != null) _definition = AssetDatabase.LoadAssetAtPath<PlayerUltimateDefinition>(AssetDatabase.GUIDToAssetPath(guid));
        }
        CaptureBaseline();
    }

    private void CaptureBaseline()
    {
        if (_savedDefinition == _definition) return;
        _savedDefinition = _definition;
        _savedJson = _definition != null ? EditorJsonUtility.ToJson(_definition) : null;
    }

    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)Math.Min(.1, now - _lastTick);
        _lastTick = now;
        if (_previewHadView && _previewView == null)
        {
            _playing = false; ClearPreview(); Repaint();
        }
        if (EditorApplication.isPlaying) Repaint();
        if (!_playing || EditorApplication.isPlayingOrWillChangePlaymode || _definition == null) return;
        if (SequenceMode)
        {
            _sampleSelectedClip = _focusSelectedClip;
            float start = _focusSelectedClip ? EntryStart : 0f;
            float end = _focusSelectedClip && Entry != null ? start + Entry.Duration : SequenceDuration;
            _sequenceTime += dt * _previewSpeed;
            if (_sequenceTime > end)
            {
                if (_loop) _sequenceTime = start + (_sequenceTime - start) % Mathf.Max(.001f, end - start);
                else { _sequenceTime = end; _playing = false; }
            }
            if (_focusSelectedClip && Entry != null) _time = Entry.SourceTime(_sequenceTime - start);
        }
        else
        {
            _time += dt * _previewSpeed;
            if (_time > Duration) { if (_loop) _time %= Duration; else { _time = Duration; _playing = false; } }
        }
        Sample(); Repaint();
    }

    private void Sample()
    {
        _time = Mathf.Clamp(_time, 0f, Duration);
        if (_preview != null && _preview.IsReady && _definition != null)
        {
            if (SequenceMode)
            {
                if (_previewSequenceDirty || !_preview.IsSequence || !_preview.UsesDefinition(_definition)) _preview.BuildSequence(_player, _definition);
                _previewSequenceDirty = false;
                _sequenceTime = Mathf.Clamp(_sequenceTime, 0, SequenceDuration);
                _preview.SampleSequence(SequenceSampleTime);
            }
            else
            {
                if (_preview.IsSequence || !_preview.UsesClip(EditClip)) _preview.Build(_player, EditClip);
                _preview.Sample(_time, _definition.warp != null ? _definition.warp.EvaluateOffset(_time) : Vector3.zero);
            }
            if (_followCamera && _playing && _previewView != null)
                _previewView.pivot = _preview.Origin + _preview.Facing * Offset + Vector3.up;
        }
        SceneView.RepaintAll();
    }

    private void StartPreview(bool focus = true)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || _definition == null) return;
        FindTarget();
        if (SequenceMode) _preview.BuildSequence(_player, _definition);
        else _preview.Build(_player, EditClip);
        _previewSequenceDirty = false;
        Sample();
        if (focus && _preview.IsReady)
        {
            if (_previewView == null)
            {
                _previewView = CreateInstance<SceneView>();
                _previewView.Show();
                _previewHadView = true;
                _previewView.titleContent = new GUIContent("Ultimate · 3D Preview");
                float pixels = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
                float desktopWidth = Mathf.Max(1280f, Screen.currentResolution.width / pixels);
                float desktopHeight = Mathf.Max(790f, Screen.currentResolution.height / pixels);
                float monitorLeft = Mathf.Floor(Mathf.Max(0, position.x) / desktopWidth) * desktopWidth;
                float studioWidth = Mathf.Clamp(position.width, 620f, 720f);
                float height = Mathf.Clamp(position.height, 680f, desktopHeight - 110f);
                float previewWidth = Mathf.Min(980f, desktopWidth - studioWidth - 40f);
                float y = Mathf.Clamp(position.y, 50f, Mathf.Max(50f, desktopHeight - height - 40f));
                position = new Rect(monitorLeft + previewWidth + 24f, y, studioWidth, height);
                _previewView.position = new Rect(monitorLeft + 12f, y, previewWidth, height);
            }
            _previewView.drawGizmos = false;
            FramePath(false);
            Focus();
        }
    }

    private void OnGUI()
    {
        DrawHeader();
        if (_definition == null)
        {
            EditorGUILayout.HelpBox("궁극기 데이터를 선택하거나 연결된 플레이어를 찾아주세요.", MessageType.Info);
            if (GUILayout.Button("플레이어 / 데이터 찾기")) FindTarget();
            return;
        }
        CaptureBaseline();
        if (SequenceMode) DrawSequenceStrip();
        DrawTransport();
        DrawInputCancelTiming();
        DrawTimeline();
        _tab = GUILayout.Toolbar(_tab, new[] { "타격 · 범위", SequenceMode ? "클립 · 루트모션" : "이동 경로", "연출", "연결 · 실행" });
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        if (_tab == 0) { DrawHitSelection(); if (Selected != null) DrawPull(); DrawHitDetails(); DrawSpatialDiagrams(); }
        else if (_tab == 1) { if (SequenceMode) DrawSequenceConfiguration(); else DrawMotionPath(); }
        else if (_tab == 2) DrawCues();
        else DrawSetup();
        EditorGUILayout.EndScrollView();
        DrawFooter();
    }

    private void DrawHeader()
    {
        SkillStudioGUI.Header("Q · ULTIMATE STUDIO", SequenceMode
            ? "원본 애니메이션을 연결하고 각 클립의 타격 마커 · 연출을 조절하세요."
            : "타격 마커와 이동 노드를 시간축에서 선택하고 3D 공간에서 직접 조절하세요.");
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            var next = (PlayerUltimateDefinition)EditorGUILayout.ObjectField("궁극기 데이터", _definition, typeof(PlayerUltimateDefinition), false);
            if (EditorGUI.EndChangeCheck()) { ClearPreview(); _definition = next; _selected = 0; _selectedClip = 0; _time = 0; _sequenceTime = 0; _previewSequenceDirty = true; CaptureBaseline(); }
            if (GUILayout.Button("찾기", GUILayout.Width(44))) FindTarget();
        }
        if (_definition != null)
            EditorGUILayout.LabelField(_player == null ? "플레이어 미연결 · 연결 탭에서 선택" : _player.UltimateDefinition == _definition ? $"● {_player.name}의 궁극기와 연결됨 · 다음 시전부터 반영" : $"⚠ {_player.name}의 실행 데이터와 다름 · 연결 탭 확인", EditorStyles.miniLabel);
    }

    private string EntryName(UltimateSequenceClip entry, int index)
        => entry == null ? $"{index + 1:00} · 비어 있음" : $"{index + 1:00} · {(string.IsNullOrWhiteSpace(entry.label) ? entry.clip != null ? entry.clip.name : "클립 미지정" : entry.label)}";

    private void DrawSequenceStrip()
    {
        _selectedClip = Mathf.Clamp(_selectedClip, 0, _definition.sequence.Count - 1);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            int index = EditorGUILayout.Popup("편집할 클립", _selectedClip, _definition.sequence.Select(EntryName).ToArray());
            if (EditorGUI.EndChangeCheck()) SelectClip(index);
            if (GUILayout.Button("클립 구성", GUILayout.Width(75))) _tab = 1;
        }
        Rect strip = GUILayoutUtility.GetRect(0, 39, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(strip, SkillStudioGUI.StripBackground);
        for (int i = 0; i < _definition.sequence.Count; i++)
        {
            var entry = _definition.sequence[i]; if (entry == null) continue;
            float start = UltimateSequenceUtility.GetSegmentStart(_definition, i);
            Rect rect = new Rect(strip.x + start / SequenceDuration * strip.width, strip.y + 2, Mathf.Max(1, entry.Duration / SequenceDuration * strip.width - 2), strip.height - 4);
            var old = GUI.backgroundColor;
            GUI.backgroundColor = i == _selectedClip ? Accent : new Color(.6f, .62f, .72f);
            string title = rect.width < 95 ? $"{i + 1:00}\n{start:0.00}s" : $"{EntryName(entry, i)}\n{start:0.00}–{start + entry.Duration:0.00}s";
            if (GUI.Button(rect, new GUIContent(title, $"{EntryName(entry, i)} · 클릭해서 편집 대상으로 선택"))) SelectClip(i);
            GUI.backgroundColor = old;
        }
        EditorGUI.DrawRect(new Rect(strip.x + _sequenceTime / SequenceDuration * strip.width, strip.y, 2, strip.height), HitColor);
        if (_definition.useInputCancelTime)
            DrawInputCancelMarker(strip, _definition.InputCancelTime / SequenceDuration, true);
        AcceptClipDrop(strip);
        bool current = _player != null && UltimateRootMotionBaker.IsCurrent(_definition, _player);
        if (!current) EditorGUILayout.HelpBox("원본 이동 데이터가 아직 없거나 바뀌었습니다. ‘클립 · 루트모션’에서 원본 이동 읽기를 실행하세요.", MessageType.Warning);
    }

    private void SelectClip(int index)
    {
        _selectedClip = Mathf.Clamp(index, 0, Mathf.Max(0, _definition.sequence.Count - 1));
        _selected = 0; _playing = false; _sampleSelectedClip = true;
        _time = Entry != null ? Entry.SourceStart : 0;
        _sequenceTime = EntryStart;
        Sample(); Repaint();
    }

    private void DrawSequenceConfiguration()
    {
        EditorGUILayout.LabelField("클립을 순서대로 연결", EditorStyles.boldLabel);
        DrawClipAddition();
        if (!SequenceMode || Entry == null) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_selectedClip == 0))
                if (GUILayout.Button("↑ 앞 순서로")) QueueStudio(() => MoveClip(-1));
            using (new EditorGUI.DisabledScope(_selectedClip >= _definition.sequence.Count - 1))
                if (GUILayout.Button("↓ 뒤 순서로")) QueueStudio(() => MoveClip(1));
            if (GUILayout.Button("복제")) QueueStudio(DuplicateClip);
            if (GUILayout.Button(new GUIContent("삭제", "이 클립의 시퀀스 항목과 전용 타격·연출 설정을 제거합니다. 원본 애니메이션 파일은 유지됩니다."))) QueueStudio(RemoveClip);
        }
        EditorGUILayout.LabelField($"편집 중: {EntryName(Entry, _selectedClip)}", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        var clip = (AnimationClip)EditorGUILayout.ObjectField("원본 애니메이션", Entry.clip, typeof(AnimationClip), false);
        if (EditorGUI.EndChangeCheck()) QueueStudio(() => Change("Replace ultimate sequence clip", () =>
        {
            Entry.clip = clip; Entry.rootSamples.Clear(); Entry.rootBakeKey = string.Empty;
            Entry.sourceStart = 0; Entry.sourceEnd = -1;
            _previewSequenceDirty = true; _sampleSelectedClip = true; _time = 0; _sequenceTime = EntryStart;
            TryAutoBake();
        }));
        var so = new SerializedObject(_definition); so.Update();
        var entryProperty = so.FindProperty("sequence").GetArrayElementAtIndex(_selectedClip);
        Property(entryProperty, "label", "클립 표시 이름");
        Property(entryProperty, "sourceStart", "원본 시작 (초)");
        Property(entryProperty, "sourceEnd", "원본 끝 (초 / -1 = 전체)");
        Property(entryProperty, "speed", "이 클립 배속");
        Property(entryProperty, "rootMotion", "원본 루트 이동 사용");
        using (new EditorGUI.DisabledScope(!Entry.rootMotion))
        {
            Property(entryProperty, "applyRootRotation", "원본 루트 회전 사용");
            Property(entryProperty, "applyRootHeight", "루트 높이 Y 적용");
        }
        Property(entryProperty, "poseBlendIn", "이전 자세와 연결 (초)");
        if (so.ApplyModifiedProperties())
        {
            Entry.Normalize(); _previewSequenceDirty = true;
            _sequenceTime = SourceToSequenceTime(_time); Sample();
        }
        EditorGUILayout.LabelField($"원본 {Entry.SourceStart:0.000}–{Entry.SourceEnd:0.000}초 → 연결 구간 {EntryStart:0.000}–{EntryStart + Entry.Duration:0.000}초", EditorStyles.wordWrappedMiniLabel);
        if (Entry.SourceEnd <= Entry.SourceStart) EditorGUILayout.HelpBox("원본 끝을 시작보다 뒤로 지정해야 이 클립을 재생할 수 있습니다.", MessageType.Warning);
        EditorGUILayout.HelpBox("타격과 연출 시간은 선택한 원본 클립의 초 단위입니다. 클립 순서·배속·잘라 쓰는 범위를 바꿔도 타격 데이터는 해당 클립에 붙어 있습니다. 연결 시 이전 클립의 마지막 위치·방향을 이어받습니다. 루트 높이 Y를 꺼도 본에 들어 있는 도약 자세는 유지됩니다.", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(_player == null || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("원본 이동 읽기 / 갱신", GUILayout.Height(28))) QueueStudio(BakeSequence);
            if (GUILayout.Button("전체 연결 보기", GUILayout.Height(28))) QueueStudio(() => { EnsurePreview(); FramePath(false); });
            if (GUILayout.Button("선택 클립 보기", GUILayout.Height(28))) QueueStudio(() => { EnsurePreview(); FrameSelectedClip(); });
        }
        bool current = _player != null && UltimateRootMotionBaker.IsCurrent(Entry, _player);
        EditorGUILayout.LabelField(current ? "✓ 선택 클립의 원본 이동 데이터가 최신입니다." : "● 선택 클립의 원본 이동 데이터를 갱신해야 합니다.", EditorStyles.miniLabel);
        if (!string.IsNullOrEmpty(_bakeMessage)) EditorGUILayout.HelpBox(_bakeMessage, MessageType.None);
        _showHitFootprints = EditorGUILayout.ToggleLeft("타격 마커 위치 표시", _showHitFootprints);
        _followCamera = EditorGUILayout.ToggleLeft("재생 중 캐릭터 따라가기", _followCamera);
    }

    private void DrawClipAddition()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            _clipToAdd = (AnimationClip)EditorGUILayout.ObjectField("추가할 클립", _clipToAdd, typeof(AnimationClip), false);
            using (new EditorGUI.DisabledScope(_clipToAdd == null))
                if (GUILayout.Button("+ 추가", GUILayout.Width(62)))
                {
                    var clip = _clipToAdd; QueueStudio(() => AddClips(new[] { clip })); _clipToAdd = null;
                }
        }
        Rect drop = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
        GUI.Box(drop, "AnimationClip을 여기로 드래그해서 추가 (여러 개 가능)", EditorStyles.helpBox);
        AcceptClipDrop(drop);
    }

    private void AcceptClipDrop(Rect rect)
    {
        var ev = Event.current;
        if (!rect.Contains(ev.mousePosition) || (ev.type != EventType.DragUpdated && ev.type != EventType.DragPerform)) return;
        var clips = DragAndDrop.objectReferences.OfType<AnimationClip>().ToArray();
        if (clips.Length == 0) return;
        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (ev.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); QueueStudio(() => AddClips(clips)); }
        ev.Use();
    }

    private void AddClips(AnimationClip[] clips)
    {
        Change("Add ultimate sequence clips", () =>
        {
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                _definition.sequence.Add(new UltimateSequenceClip { id = Guid.NewGuid().ToString("N"), label = clip.name, clip = clip });
            }
            _selectedClip = Mathf.Max(0, _definition.sequence.Count - 1); _selected = 0; _time = Entry?.SourceStart ?? 0;
            _sequenceTime = EntryStart; _previewSequenceDirty = true; _sampleSelectedClip = true; _playing = false; _tab = 1;
            TryAutoBake();
        });
    }

    private void DuplicateClip()
    {
        if (Entry == null) return;
        Change("Duplicate ultimate sequence clip", () =>
        {
            var copy = JsonUtility.FromJson<UltimateSequenceClip>(JsonUtility.ToJson(Entry));
            copy.clip = Entry.clip; copy.id = Guid.NewGuid().ToString("N"); copy.label = Entry.label + " 복제";
            _definition.sequence.Insert(_selectedClip + 1, copy); _selectedClip++;
            _selected = 0; _time = copy.SourceStart; _sequenceTime = EntryStart; _previewSequenceDirty = true; _playing = false;
        });
    }

    private void MoveClip(int direction)
    {
        int next = _selectedClip + direction;
        if (Entry == null || next < 0 || next >= _definition.sequence.Count) return;
        Change("Reorder ultimate sequence clip", () =>
        {
            var entry = Entry; _definition.sequence.RemoveAt(_selectedClip); _definition.sequence.Insert(next, entry); _selectedClip = next;
            _sequenceTime = SourceToSequenceTime(_time); _previewSequenceDirty = true; _playing = false;
        });
    }

    private void RemoveClip()
    {
        if (Entry == null) return;
        Change("Remove ultimate sequence clip", () =>
        {
            _definition.sequence.RemoveAt(_selectedClip); _selectedClip = Mathf.Clamp(_selectedClip, 0, Mathf.Max(0, _definition.sequence.Count - 1));
            _selected = 0; _time = Entry?.SourceStart ?? 0; _sequenceTime = EntryStart; _previewSequenceDirty = true; _playing = false;
        });
    }

    private void BakeSequence()
    {
        if (!SequenceMode || _player == null) return;
        Undo.RegisterCompleteObjectUndo(_definition, "Read ultimate original root motion");
        try { _bakeMessage = UltimateRootMotionBaker.Bake(_definition, _player); }
        catch (Exception exception) { _bakeMessage = "원본 이동을 읽지 못했습니다: " + exception.Message; }
        EditorUtility.SetDirty(_definition); _previewSequenceDirty = true; Sample(); Repaint();
    }

    private void TryAutoBake()
    {
        if (!SequenceMode || _player == null || EditorApplication.isPlayingOrWillChangePlaymode)
        { _bakeMessage = "플레이어를 연결하고 Edit Mode에서 원본 이동 읽기를 실행하세요."; return; }
        // Called inside the edit's complete-object Undo, so one Undo also restores the samples.
        try { _bakeMessage = UltimateRootMotionBaker.Bake(_definition, _player); }
        catch (Exception exception) { _bakeMessage = "자동 읽기를 완료하지 못했습니다. 원본 이동 읽기로 다시 시도하세요. " + exception.Message; }
    }

    private SerializedProperty EditProperty(SerializedObject serialized, string name)
        => SequenceMode ? serialized.FindProperty("sequence").GetArrayElementAtIndex(_selectedClip).FindPropertyRelative(name) : serialized.FindProperty(name);

    private void DrawTransport()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditClip == null || _player == null))
            {
                if (GUILayout.Button(_preview.IsReady ? "Scene 다시 보기" : "Scene 미리보기", EditorStyles.toolbarButton, GUILayout.Width(112))) StartPreview();
                if (_preview.IsReady && GUILayout.Button("닫기", EditorStyles.toolbarButton, GUILayout.Width(38))) { _playing = false; ClearPreview(); }
                if (GUILayout.Button(_playing ? "Ⅱ 정지" : "▶ 재생", EditorStyles.toolbarButton, GUILayout.Width(68)))
                {
                    if (!_preview.IsReady) StartPreview();
                    _playing = !_playing;
                    if (SequenceMode)
                    {
                        _sampleSelectedClip = _focusSelectedClip;
                        float start = _focusSelectedClip ? EntryStart : 0f;
                        float end = _focusSelectedClip ? EntryStart + Entry.Duration : SequenceDuration;
                        if (_sequenceTime < start || _sequenceTime >= end) _sequenceTime = start;
                    }
                    else if (_time >= Duration) _time = 0f;
                }
            }
            if (GUILayout.Button("|◀", EditorStyles.toolbarButton, GUILayout.Width(30))) Seek(SequenceMode && Entry != null ? Entry.SourceStart : 0f);
            if (GUILayout.Button("−1f", EditorStyles.toolbarButton, GUILayout.Width(38))) Seek(_time - 1f / FrameRate);
            if (GUILayout.Button("+1f", EditorStyles.toolbarButton, GUILayout.Width(38))) Seek(_time + 1f / FrameRate);
            _loop = GUILayout.Toggle(_loop, "반복", EditorStyles.toolbarButton, GUILayout.Width(42));
            GUILayout.FlexibleSpace();
            GUILayout.Label("관찰 배속", EditorStyles.miniLabel);
            _previewSpeed = EditorGUILayout.FloatField(_previewSpeed, GUILayout.Width(42));
            _previewSpeed = Mathf.Clamp(_previewSpeed, .05f, 3f);
        }
        if (SequenceMode)
        {
            int focus = GUILayout.Toolbar(_focusSelectedClip ? 0 : 1, new[] { "선택 클립 집중 재생", "전체 시퀀스 재생" });
            if (_focusSelectedClip != (focus == 0))
            {
                _focusSelectedClip = focus == 0; _playing = false;
                _sampleSelectedClip = _focusSelectedClip;
                if (_focusSelectedClip) _sequenceTime = SourceToSequenceTime(_time);
                Sample();
            }
            EditorGUI.BeginChangeCheck();
            float sequenceTime = EditorGUILayout.Slider("전체 재생 위치", _sequenceTime, 0, SequenceDuration);
            if (EditorGUI.EndChangeCheck()) { _playing = false; _sampleSelectedClip = false; _sequenceTime = sequenceTime; Sample(); }
            UltimateSequenceUtility.GetSegment(_definition, SequenceSampleTime, out int current, out float elapsed);
            var playingEntry = current >= 0 && current < _definition.sequence.Count ? _definition.sequence[current] : null;
            if (playingEntry != null) EditorGUILayout.LabelField($"재생: {EntryName(playingEntry, current)} · 원본 {playingEntry.SourceTime(elapsed):0.000}초   |   편집: {_selectedClip + 1:00}번 클립", EditorStyles.miniLabel);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            float nextTime = SequenceMode ? EditorGUILayout.Slider("편집 클립 원본 시간", _time, 0f, Duration) : EditorGUILayout.Slider(_time, 0f, Duration);
            if (EditorGUI.EndChangeCheck()) Seek(nextTime);
            GUILayout.Label($"{Mathf.RoundToInt(_time * FrameRate)} f / {Mathf.RoundToInt(Duration * FrameRate)} f", GUILayout.Width(100));
        }
        EditorGUILayout.LabelField(SequenceMode ? $"편집 원본 {_time:0.000}초   ·   전체 {_sequenceTime:0.000} / {SequenceDuration:0.000}초   ·   실전 전체 {_definition.RuntimeDuration:0.00}초" : $"클립 {_time:0.000}초  ·  게임 {_time / Mathf.Max(.05f, _definition.playbackSpeed):0.000}초  ·  실전 길이 {_definition.RuntimeDuration:0.00}초", EditorStyles.miniLabel);
    }

    private float SourceToSequenceTime(float time) => SequenceMode && Entry != null ? EntryStart + Mathf.Clamp((time - Entry.SourceStart) / Mathf.Max(.05f, Entry.speed), 0, Entry.Duration) : time;

    private void DrawInputCancelTiming()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUI.BeginChangeCheck();
            bool custom;
            float cancelTime = _definition.inputCancelTime;
            using (new EditorGUILayout.HorizontalScope())
            {
                custom = EditorGUILayout.ToggleLeft("입력 취소 시점 지정", _definition.useInputCancelTime, GUILayout.Width(142));
                using (new EditorGUI.DisabledScope(!custom))
                    cancelTime = EditorGUILayout.FloatField(new GUIContent("취소 가능 (전체 초)", "이 시각부터 유효한 이동·공격·회피·E 입력으로 Q를 조기 종료합니다. 입력이 없으면 전체 시퀀스를 재생합니다. 구간 배속을 반영하며 컷씬 길이는 포함하지 않습니다."), custom ? cancelTime : _definition.Duration);
            }
            float blend = EditorGUILayout.FloatField(new GUIContent("다음 동작 블렌드 (초)", "입력 취소 또는 자연 종료 시 다음 동작으로 자세를 섞는 시간입니다. 다음 행동은 즉시 시작하며 추가 후딜레이가 아닙니다."), _definition.exitBlendDuration);
            if (EditorGUI.EndChangeCheck())
                Change("Change ultimate input cancel timing", () =>
                {
                    _definition.useInputCancelTime = custom;
                    if (custom) _definition.inputCancelTime = Mathf.Min(_definition.Duration, FiniteTime(cancelTime, _definition.Duration, .01f));
                    _definition.exitBlendDuration = FiniteTime(blend, .15f, 0f);
                });
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("현재 위치부터 입력 취소"))
                    Change("Set ultimate input cancel at playhead", () =>
                    {
                        _definition.useInputCancelTime = true;
                        _definition.inputCancelTime = Mathf.Clamp(SequenceMode ? _sequenceTime : _time, .01f, _definition.Duration);
                        _playing = false;
                    });
                if (GUILayout.Button("취소 위치 보기", GUILayout.Width(110))) SeekInputCancel();
                GUILayout.Label($"자연 종료 {_definition.RuntimeDuration:0.00}초 · 컷씬 제외", EditorStyles.miniLabel);
            }
            EditorGUILayout.HelpBox(custom
                ? $"{_definition.InputCancelTime / Mathf.Max(.05f, _definition.playbackSpeed):0.00}초부터 다른 입력으로 취소할 수 있습니다. 입력이 없으면 끝까지 재생합니다."
                : "입력 취소를 사용하지 않습니다. 전체 시퀀스가 끝나면 다음 행동으로 전환합니다.", MessageType.Info);
            CountEventsAfterInputCancel(out int hits, out int cues);
            if (hits > 0 || cues > 0)
                EditorGUILayout.HelpBox($"취소 가능 시점 뒤에 타격 {hits}개 · 연출 {cues}개가 있습니다. 실제 입력으로 취소한 뒤의 마커만 생략됩니다.", MessageType.Info);
        }
    }

    private static float FiniteTime(float value, float fallback, float minimum)
        => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(minimum, value);

    private void SeekInputCancel()
    {
        _playing = false;
        float end = _definition.InputCancelTime;
        if (SequenceMode)
        {
            _sequenceTime = end;
            _sampleSelectedClip = false;
            if (UltimateSequenceUtility.GetSegment(_definition, end, out int index, out float elapsed))
            {
                _selectedClip = index;
                _selected = 0;
                _time = Entry.SourceTime(elapsed);
            }
            Sample(); Repaint();
        }
        else Seek(end);
    }

    private void CountEventsAfterInputCancel(out int hits, out int cues)
    {
        hits = cues = 0;
        if (!_definition.useInputCancelTime) return;
        float end = _definition.InputCancelTime;
        if (!SequenceMode)
        {
            hits = _definition.hits?.Count(hit => hit != null && hit.enabled && hit.time > end && hit.time <= _definition.Duration) ?? 0;
            cues = _definition.cues?.Count(cue => cue != null && cue.time > end && cue.time <= _definition.Duration) ?? 0;
            return;
        }
        float start = 0f;
        foreach (var entry in _definition.sequence)
        {
            if (entry == null) continue;
            float speed = Mathf.Max(.05f, entry.speed);
            bool IsAfterCancel(float sourceTime) => sourceTime >= entry.SourceStart && sourceTime <= entry.SourceEnd &&
                start + (sourceTime - entry.SourceStart) / speed > end;
            hits += entry.hits?.Count(hit => hit != null && hit.enabled && IsAfterCancel(hit.time)) ?? 0;
            cues += entry.cues?.Count(cue => cue != null && IsAfterCancel(cue.time)) ?? 0;
            start += entry.Duration;
        }
    }

    private static void DrawInputCancelMarker(Rect area, float normalizedTime, bool tintTail)
    {
        float x = Mathf.Lerp(area.x, area.xMax, Mathf.Clamp01(normalizedTime));
        if (tintTail && x < area.xMax)
            EditorGUI.DrawRect(new Rect(x, area.y, area.xMax - x, area.height), new Color(1f, .75f, .24f, .08f));
        EditorGUI.DrawRect(new Rect(Mathf.Min(x, area.xMax - 2), area.y, 2, area.height), InputCancelColor);
        var style = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = InputCancelColor } };
        GUI.Label(new Rect(Mathf.Clamp(x - 54, area.x, area.xMax - 58), area.y, 58, 16), new GUIContent("취소 가능", "이 시점부터 다른 입력으로 Q를 조기 종료할 수 있습니다. 입력이 없으면 계속 재생합니다."), style);
    }

    private void DrawClipInputCancel(Rect area, float left, float width)
    {
        if (!_definition.useInputCancelTime) return;
        Rect track = new(left, area.y + 18, width, area.height - 18);
        float end = _definition.InputCancelTime;
        if (!SequenceMode) { DrawInputCancelMarker(track, end / Duration, true); return; }
        if (Entry == null) return;
        if (end < EntryStart)
        {
            EditorGUI.DrawRect(new Rect(left + Entry.SourceStart / Duration * width, track.y,
                (Entry.SourceEnd - Entry.SourceStart) / Duration * width, track.height), new Color(1f, .75f, .24f, .08f));
            return;
        }
        if (end <= EntryStart + Entry.Duration)
            DrawInputCancelMarker(track, Entry.SourceTime(end - EntryStart) / Duration, true);
    }

    private void Seek(float time)
    {
        _playing = false; _time = Mathf.Clamp(time, 0, Duration);
        if (SequenceMode) { _sampleSelectedClip = true; _sequenceTime = SourceToSequenceTime(_time); }
        Sample(); Repaint();
    }

    private void DrawTimeline()
    {
        int count = Hits?.Count ?? 0;
        int motionRows = SequenceMode ? 0 : 1;
        _timelineScroll = EditorGUILayout.BeginScrollView(_timelineScroll, GUILayout.Height(Mathf.Min(244, 28 + 24 * Mathf.Max(1, count + motionRows))));
        Rect area = GUILayoutUtility.GetRect(0, 28 + 24 * Mathf.Max(1, count + motionRows), GUILayout.ExpandWidth(true));
        float left = area.x + SkillStudioGUI.TimelineLabelWidth, width = Mathf.Max(1, area.width - SkillStudioGUI.TimelineLabelWidth - 14);
        EditorGUI.DrawRect(area, SkillStudioGUI.TimelineBackground);
        for (int i = 0; i <= 10; i++)
        {
            float x = left + width * i / 10f;
            EditorGUI.DrawRect(new Rect(x, area.y + 19, 1, area.height - 19), SkillStudioGUI.TimelineGrid);
            GUI.Label(new Rect(x - 12, area.y, 44, 18), (Duration * i / 10f).ToString("0.0"), EditorStyles.miniLabel);
        }
        var ev = Event.current;
        int control = GUIUtility.GetControlID(FocusType.Passive);
        if (!SequenceMode) DrawPathTimeRow(area, left, width, control);
        else if (Entry != null)
        {
            EditorGUI.DrawRect(new Rect(left, area.y + 19, Entry.SourceStart / Duration * width, area.height - 19), new Color(.3f, .12f, .12f, .3f));
            EditorGUI.DrawRect(new Rect(left + Entry.SourceEnd / Duration * width, area.y + 19, (1 - Entry.SourceEnd / Duration) * width, area.height - 19), new Color(.3f, .12f, .12f, .3f));
        }
        for (int i = 0; i < count; i++)
        {
            var hit = Hits[i];
            if (hit == null) continue;
            float y = area.y + 23 + (i + motionRows) * 24;
            bool selected = i == _selected;
            bool active = FieldIsActiveAtPreviewTime(hit);
            var label = new Rect(area.x + 5, y, 105, 21);
            if (GUI.Button(label, $"{i + 1:00}  {hit.name}", selected ? EditorStyles.toolbarButton : EditorStyles.miniLabel)) { _selected = i; _tab = 0; Seek(hit.time); }
            int row = i;
            using (new EditorGUI.DisabledScope(i == 0 || _dragHit >= 0))
                if (GUI.Button(new Rect(area.x + 112, y, 21, 21), new GUIContent("↑", "타격 행을 위로 이동 · 시각은 유지"), EditorStyles.miniButtonLeft))
                    QueueStudio(() => MoveHit(row, -1));
            using (new EditorGUI.DisabledScope(i == count - 1 || _dragHit >= 0))
                if (GUI.Button(new Rect(area.x + 134, y, 21, 21), new GUIContent("↓", "타격 행을 아래로 이동 · 시각은 유지"), EditorStyles.miniButtonRight))
                    QueueStudio(() => MoveHit(row, 1));
            float markerX = left + Mathf.Clamp01(hit.time / Duration) * width;
            Rect marker = new Rect(markerX - 9, y, 18, 22);
            EditorGUI.DrawRect(new Rect(left, y + 11, width, 1), new Color(.22f, .24f, .27f));
            var pull = _definition.FindPull(hit.pullId);
            if (pull != null)
            {
                float sourceSeconds = pull.duration * _definition.playbackSpeed * (SequenceMode ? Entry.speed : 1f);
                float endX = left + Mathf.Clamp01((hit.time + sourceSeconds) / Duration) * width;
                EditorGUI.DrawRect(new Rect(markerX, y + 20, Mathf.Max(0f, endX - markerX), 2f), PullColor);
            }
            var color = active ? Color.white : pull != null ? PullColor : selected ? HitColor : Accent;
            if (!hit.enabled) color = Color.gray;
            var markerStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 20, normal = { textColor = color } };
            int frame = Mathf.RoundToInt(hit.time * FrameRate);
            GUI.Label(marker, new GUIContent("◆", $"{hit.name}{(pull != null ? $" · {pull.name}" : "")} · {frame}f · 원본 {hit.time:0.000}초 · 드래그해서 프레임 이동"), markerStyle);
            GUI.Label(new Rect(markerX + 12 > area.xMax - 42 ? markerX - 46 : markerX + 12, y + 3, 42, 18), $"{frame}f", EditorStyles.miniLabel);
            EditorGUIUtility.AddCursorRect(marker, MouseCursor.SlideArrow);
            if (ev.type == EventType.MouseDown && ev.button == 0 && marker.Contains(ev.mousePosition))
            {
                _selected = i; _tab = 0; _playing = false; _dragHit = i; _dragNode = -1;
                _dragX = ev.mousePosition.x; _dragStart = hit.time; _time = hit.time;
                if (SequenceMode) { _sampleSelectedClip = true; _sequenceTime = SourceToSequenceTime(_time); }
                Undo.IncrementCurrentGroup(); _dragUndo = Undo.GetCurrentGroup();
                Undo.RegisterCompleteObjectUndo(_definition, "Move ultimate hit marker");
                GUIUtility.hotControl = control;
                Sample(); Repaint();
                ev.Use();
            }
        }
        DrawClipInputCancel(area, left, width);
        float playhead = left + width * (_time / Duration);
        EditorGUI.DrawRect(new Rect(playhead, area.y + 18, 2, area.height - 18), new Color(1f, .3f, .27f));
        if (_dragHit >= 0 && ev.type == EventType.MouseDrag)
        {
            var hit = Hits[_dragHit];
            float delta = (ev.mousePosition.x - _dragX) / width * Duration;
            hit.time = SnapHitTime(_dragStart + delta);
            _time = hit.time;
            if (SequenceMode) { _sampleSelectedClip = true; _sequenceTime = SourceToSequenceTime(_time); }
            EditorUtility.SetDirty(_definition); Sample(); Repaint(); ev.Use();
        }
        if (_dragNode >= 0 && Motion != null && _dragNode < Motion.points.Count && ev.type == EventType.MouseDrag)
        {
            var node = Motion.points[_dragNode];
            node.time = ClampNodeTime(_dragNode, Snap(_dragStart + (ev.mousePosition.x - _dragX) / width * Duration));
            _time = node.time;
            EditorUtility.SetDirty(_definition); Sample(); Repaint(); ev.Use();
        }
        if (_dragHit >= 0 && ev.rawType == EventType.MouseUp) { Undo.CollapseUndoOperations(_dragUndo); _dragHit = -1; GUIUtility.hotControl = 0; Repaint(); }
        if (_dragNode >= 0 && ev.rawType == EventType.MouseUp) { Undo.CollapseUndoOperations(_dragUndo); _dragNode = -1; GUIUtility.hotControl = 0; Repaint(); }
        if (ev.type == EventType.MouseDown && ev.button == 0 && area.Contains(ev.mousePosition) && ev.mousePosition.x >= left)
        { Seek(Snap((ev.mousePosition.x - left) / width * Duration)); ev.Use(); }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.LabelField(SequenceMode ? "◆ 클릭·드래그 = 시점 편집 · ↑↓ = 행 순서 · 노란 선 = 입력 취소 가능" : "타격 ◆ = 한 프레임 판정 · 보라 ◆ = 이동 도착 · 노란 선 = 입력 취소 가능", EditorStyles.miniLabel);
    }

    private void DrawPathTimeRow(Rect area, float left, float width, int control)
    {
        float y = area.y + 23f;
        if (GUI.Button(new Rect(area.x + 5, y, 105, 21), "◆ 이동 경로", _tab == 1 ? EditorStyles.toolbarButton : EditorStyles.miniLabel)) _tab = 1;
        if (Motion == null) return;
        EditorGUI.DrawRect(new Rect(left, y + 10, width, 2), new Color(.26f, .2f, .37f));
        if (Motion.mode == UltimateMotionWarpMode.Forward)
        {
            EditorGUI.DrawRect(new Rect(left + Motion.start / Duration * width, y + 6, Mathf.Max(3, (Motion.end - Motion.start) / Duration * width), 10), Motion.enabled ? PathColor : Color.gray);
            return;
        }
        var ev = Event.current;
        for (int i = 0; i < Motion.points.Count; i++)
        {
            var point = Motion.points[i]; if (point == null) continue;
            float x = left + Mathf.Clamp01(point.time / Duration) * width;
            Rect marker = new Rect(x - 10, y + 1, 20, 22);
            var style = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 20, normal = { textColor = _selectedNode == i && _tab == 1 ? Color.white : PathColor } };
            GUI.Label(marker, new GUIContent("◆", $"노드 {i + 1} · {point.time:0.000}초 · 드래그해서 도착 시간 조절"), style);
            EditorGUIUtility.AddCursorRect(marker, MouseCursor.SlideArrow);
            if (ev.type == EventType.MouseDown && ev.button == 0 && marker.Contains(ev.mousePosition))
            {
                _tab = 1; _selectedNode = i; _dragNode = i; _dragHit = -1; _playing = false;
                _dragX = ev.mousePosition.x; _dragStart = point.time; _time = point.time;
                Undo.IncrementCurrentGroup(); _dragUndo = Undo.GetCurrentGroup();
                Undo.RegisterCompleteObjectUndo(_definition, "Move ultimate path arrival time");
                GUIUtility.hotControl = control; Sample(); Repaint(); ev.Use();
            }
        }
    }

    private float Snap(float value) => Mathf.Round(value * FrameRate) / FrameRate;
    private float SnapHitTime(float value) => Mathf.Clamp(Mathf.Round(value * FrameRate), 0, Mathf.Floor(Duration * FrameRate + .001f)) / FrameRate;

    private void MoveHit(int index, int direction)
    {
        var hits = Hits;
        int next = index + direction;
        if (hits == null || index < 0 || index >= hits.Count || next < 0 || next >= hits.Count) return;
        // Reorder the existing objects only: no timestamps, geometry or damage are rewritten.
        Undo.RegisterCompleteObjectUndo(_definition, "Reorder ultimate hit rows");
        var hit = hits[index];
        hits.RemoveAt(index);
        hits.Insert(next, hit);
        _selected = next;
        _playing = false;
        EditorUtility.SetDirty(_definition);
        Sample(); Repaint();
    }

    private void DrawHitSelection()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("타격 목록", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(Selected == null || _selected <= 0))
                if (GUILayout.Button("↑ 위로", GUILayout.Width(60))) QueueStudio(() => MoveHit(_selected, -1));
            using (new EditorGUI.DisabledScope(Selected == null || _selected >= Hits.Count - 1))
                if (GUILayout.Button("↓ 아래로", GUILayout.Width(66))) QueueStudio(() => MoveHit(_selected, 1));
            if (GUILayout.Button("+ 타격", GUILayout.Width(68))) Change("Add ultimate hit", () =>
            {
                Hits.Add(new UltimateHitField { name = $"Hit {Hits.Count + 1}", time = SnapHitTime(_time) });
                _selected = Hits.Count - 1;
            });
            using (new EditorGUI.DisabledScope(Selected == null))
            {
                if (GUILayout.Button("복제", GUILayout.Width(48))) Change("Duplicate ultimate hit", () =>
                {
                    var hit = JsonUtility.FromJson<UltimateHitField>(JsonUtility.ToJson(Selected));
                    hit.name += " copy"; Hits.Insert(_selected + 1, hit); _selected++;
                });
                if (GUILayout.Button("삭제", GUILayout.Width(48))) Change("Remove ultimate hit", () =>
                { Hits.RemoveAt(_selected); _selected = Mathf.Max(0, _selected - 1); });
            }
        }
        if (Hits.Count > 0)
        {
            EditorGUI.BeginChangeCheck();
            _selected = EditorGUILayout.Popup("선택 타격", Mathf.Clamp(_selected, 0, Hits.Count - 1), Hits.Select((h, i) => h == null ? $"{i + 1:00} (empty)" : $"{i + 1:00}  {h.name}   {Mathf.RoundToInt(h.time * FrameRate)}f / {h.time:0.000}s").ToArray());
            if (EditorGUI.EndChangeCheck() && Selected != null) Seek(Selected.time);
        }
    }

    private static readonly Color PullColor = new(.28f, 1f, .72f);
    [SerializeField] private bool _pullOpen;
    [SerializeField] private bool _pullAssignmentsOpen;
    [SerializeField] private bool _pullAdvanced;
    [SerializeField] private string _editingPull;
    private bool _pullHandle;
    private int _pullHandleUndo = -1;
    private bool EditingPullPosition => _pullHandle && _pullOpen && _definition.FindPull(_editingPull) != null;
    private Vector2 _pullPreviewOffset = new(0f, 4f);

    private IEnumerable<(UltimateHitField hit, float time, string label)> PullMarkers()
    {
        if (!SequenceMode)
        {
            foreach (var hit in _definition.hits)
                if (hit != null) yield return (hit, hit.time, $"{hit.name} / {Mathf.RoundToInt(hit.time * FrameRate)}f");
            yield break;
        }

        float start = 0f;
        for (int i = 0; i < _definition.sequence.Count; i++)
        {
            var clip = _definition.sequence[i];
            if (clip == null) continue;
            foreach (var hit in clip.hits)
                if (hit != null)
                {
                    float time = hit.time < clip.SourceStart || hit.time > clip.SourceEnd
                        ? float.PositiveInfinity : start + (hit.time - clip.SourceStart) / Mathf.Max(.05f, clip.speed);
                    yield return (hit, time, $"{EntryName(clip, i)} / {hit.name} / {Mathf.RoundToInt(hit.time * (clip.clip != null ? clip.clip.frameRate : 60f))}f");
                }
            start += clip.Duration;
        }
    }

    private void DrawPull()
    {
        _pullOpen = EditorGUILayout.Foldout(_pullOpen, "흡인", true);
        if (!_pullOpen) return;
        using var box = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
        var groups = (_definition.pulls ?? new List<UltimatePull>()).Where(p => p != null).ToList();
        var selected = Selected;
        var names = new[] { "없음" }.Concat(groups.Select(p => p.name)).ToArray();
        int linked = groups.FindIndex(p => p.id == selected.pullId) + 1;
        int next = EditorGUILayout.Popup("선택 타격에 적용", linked, names);
        if (next != linked)
        {
            Change("타격 흡인 연결", () => selected.pullId = next == 0 ? null : groups[next - 1].id);
            if (next > 0) _editingPull = selected.pullId;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("새 흡인 설정 만들기")) AddPull(selected, null);
            using (new EditorGUI.DisabledScope(linked == 0))
                if (GUILayout.Button("설정 복제 후 분리")) AddPull(selected, groups[linked - 1]);
        }

        int index = groups.FindIndex(p => p.id == _editingPull) + 1;
        if (index == 0) index = linked;
        index = EditorGUILayout.Popup("공통 설정 편집", index, names);
        if (index == 0) { _editingPull = null; return; }
        var pull = groups[index - 1];
        _editingPull = pull.id;
        var markers = PullMarkers().ToArray();
        _pullAssignmentsOpen = EditorGUILayout.Foldout(_pullAssignmentsOpen,
            $"적용 타격 선택 · 연결 {markers.Count(m => m.hit.pullId == pull.id)}개", true);
        if (_pullAssignmentsOpen)
        {
            foreach (var marker in markers)
            {
                bool assigned = marker.hit.pullId == pull.id;
                string label = marker.label + (!marker.hit.enabled || float.IsInfinity(marker.time) ? " (미실행)" : "");
                var other = _definition.FindPull(marker.hit.pullId);
                if (!assigned && other != null) label += $" [{other.name}]";
                if (EditorGUILayout.ToggleLeft(label, assigned) != assigned)
                    Change("공통 흡인 적용 타격", () => marker.hit.pullId = assigned ? null : pull.id);
            }
        }

        var so = new SerializedObject(_definition);
        var props = so.FindProperty("pulls").GetArrayElementAtIndex(_definition.pulls.IndexOf(pull));
        Property(props, "name", "설정 이름");
        var anchor = props.FindPropertyRelative("anchor");
        anchor.enumValueIndex = EditorGUILayout.Popup("위치 기준", anchor.enumValueIndex,
            new[] { "첫 적용 타격 시 고정", "각 타격 시 새로 지정", "플레이어 따라가기" });
        Property(props, "offset", "좌우 X / 전방 Y (m)");
        _pullHandle = GUILayout.Toolbar(_pullHandle ? 1 : 0, new[] { "공격 범위 핸들", "흡인 위치 핸들" }) == 1;
        if (GUILayout.Button("Scene에서 모을 위치 보기"))
            QueueStudio(() => { _pullHandle = true; Seek(selected.time); EnsurePreview(); FrameCurrent(); });

        bool follow = anchor.enumValueIndex == (int)UltimatePullAnchor.FollowPlayer;
        var motion = props.FindPropertyRelative("motion");
        var speed = props.FindPropertyRelative("speed");
        if (motion.enumValueIndex == 0 || follow)
            speed.floatValue = EditorGUILayout.Slider("흡인 속도 (m/s)", speed.floatValue, .1f, Mathf.Max(30f, speed.floatValue));
        else
            Property(props, "speed", "최대 속도 (m/s)");
        Property(props, "duration", "지속시간 (전투 초)");
        Property(props, "radius", "도착 허용 반경 (m)");
        _pullAdvanced = EditorGUILayout.Foldout(_pullAdvanced, "고급 설정", true);
        if (_pullAdvanced)
        {
            using (new EditorGUI.DisabledScope(follow))
                motion.enumValueIndex = EditorGUILayout.Popup("이동 방식", motion.enumValueIndex, new[] { "속도 지정", "도착시간 지정" });
            if (motion.enumValueIndex == 0 || follow)
            {
                Property(props, "acceleration", "가속도 (m/s²)");
                Property(props, "deceleration", "감속도 (m/s²)");
            }
            else Property(props, "arrivalTime", "목표 도착시간 (전투 초)");
            Property(props, "strength", "저항 판정용 강도");
            Property(props, "priority", "교체 우선순위");

            int release = Array.FindIndex(markers, m => m.hit.releasePullId == pull.id) + 1;
            int releaseNext = EditorGUILayout.Popup("지정 타격 직전 해제", release,
                new[] { "없음" }.Concat(markers.Select(m => m.label)).ToArray());
            if (releaseNext != release)
            {
                so.ApplyModifiedProperties();
                Change("흡인 해제 타격", () =>
                {
                    if (release > 0) markers[release - 1].hit.releasePullId = null;
                    if (releaseNext > 0) markers[releaseNext - 1].hit.releasePullId = pull.id;
                });
                return;
            }
        }
        if (so.ApplyModifiedProperties()) { Sample(); SceneView.RepaintAll(); }
        EditorGUI.BeginChangeCheck();
        _pullPreviewOffset = EditorGUILayout.Vector2Field("예상 적 시작 위치 (좌우 / 전방 m)", _pullPreviewOffset);
        if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
        EditorGUILayout.HelpBox("Scene 예상 궤적은 선택 마커 1회 적중을 가정합니다. 실제 판정·충돌·저항·히트스톱은 Play Mode에서 확인하세요.", MessageType.Info);
    }

    private void AddPull(UltimateHitField hit, UltimatePull source)
    {
        Change("흡인 설정 생성", () =>
        {
            var pull = source == null ? new UltimatePull() : JsonUtility.FromJson<UltimatePull>(JsonUtility.ToJson(source));
            if (source != null) pull.name += " 복제";
            pull.id = Guid.NewGuid().ToString("N");
            _definition.pulls ??= new List<UltimatePull>();
            _definition.pulls.Add(pull);
            hit.pullId = _editingPull = pull.id;
            _pullAssignmentsOpen = true;
        });
    }

    private void DrawPullScene()
    {
        var pull = _definition.FindPull(_editingPull);
        if (_tab != 0 || !_pullOpen || pull == null || Selected == null) return;
        var markers = PullMarkers().ToArray();
        float selectedTime = markers.First(m => m.hit == Selected).time;
        if (float.IsInfinity(selectedTime)) return;
        float anchorTime = selectedTime;
        if (pull.anchor == UltimatePullAnchor.FirstMarker)
            anchorTime = markers.Where(m => m.hit.enabled && m.hit.pullId == pull.id && !float.IsInfinity(m.time))
                .Select(m => m.time).DefaultIfEmpty(selectedTime).Min();

        (Vector3 position, Quaternion rotation) Pose(float time)
        {
            if (!SequenceMode) return (_preview.Origin + _preview.Facing * (_definition.warp?.EvaluateOffset(time) ?? Vector3.zero), _preview.Facing);
            var motion = UltimateSequenceUtility.EvaluateMotion(_definition, time);
            return (_preview.Origin + _preview.Facing * motion.position, _preview.Facing * motion.rotation);
        }

        var (origin, facing) = Pose(anchorTime);
        Vector3 center = pull.Center(origin, facing);
        center.y = _preview.Origin.y + .06f;
        var (startOrigin, startFacing) = Pose(selectedTime);
        Vector3 start = startOrigin + startFacing * new Vector3(_pullPreviewOffset.x, 0f, _pullPreviewOffset.y);
        start.y = center.y;
        Handles.color = PullColor;
        Handles.DrawWireDisc(center, Vector3.up, Mathf.Max(.05f, pull.radius));
        Handles.Label(center + Vector3.up * .2f, pull.name + " · 흡인 중심");
        Vector3 direction = Vector3.ProjectOnPlane(center - start, Vector3.up);
        if (direction.sqrMagnitude > .001f)
            Handles.ArrowHandleCap(0, start, Quaternion.LookRotation(direction), Mathf.Min(1f, direction.magnitude), EventType.Repaint);

        float speed = 0f;
        float duration = Mathf.Max(.01f, pull.duration);
        float elapsed = Mathf.Clamp(((SequenceMode ? _sequenceTime : _time) - selectedTime) / Mathf.Max(.05f, _definition.playbackSpeed), 0f, duration);
        Vector3 position = start;
        Vector3 sample = start;
        const int steps = 48;
        for (int i = 1; i <= steps; i++)
        {
            float t = duration * i / steps;
            Vector3 target = center;
            if (pull.anchor == UltimatePullAnchor.FollowPlayer)
            {
                var pose = Pose(Mathf.Min(_definition.Duration, selectedTime + t * _definition.playbackSpeed));
                target = pull.Center(pose.position, pose.rotation);
            }
            Vector3 next = position + pull.Step(position, target, start, t, duration / steps, ref speed);
            Handles.DrawLine(position, next, 2f);
            if (t <= elapsed) sample = next;
            position = next;
        }
        Handles.SphereHandleCap(0, sample, Quaternion.identity, .24f, EventType.Repaint);
        if (!_pullHandle || _playing) return;
        Vector3 forward = Vector3.ProjectOnPlane(facing * Vector3.forward, Vector3.up).normalized;
        if (forward == Vector3.zero) forward = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        int control = GUIUtility.GetControlID(FocusType.Passive);
        int previousControl = GUIUtility.hotControl;
        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.Slider2D(control, center, Vector3.up, right, forward,
            HandleUtility.GetHandleSize(center) * .12f, Handles.RectangleHandleCap, new Vector2(.05f, .05f));
        bool changed = EditorGUI.EndChangeCheck();
        if (GUIUtility.hotControl == control && previousControl != control)
        {
            Undo.IncrementCurrentGroup();
            _pullHandleUndo = Undo.GetCurrentGroup();
            Undo.RegisterCompleteObjectUndo(_definition, "흡인 중심 이동");
        }
        if (changed)
        {
            pull.offset = new Vector2(Vector3.Dot(moved - origin, right), Vector3.Dot(moved - origin, forward));
            EditorUtility.SetDirty(_definition);
            Repaint();
        }
        if (_pullHandleUndo >= 0 && GUIUtility.hotControl != control)
        {
            Undo.CollapseUndoOperations(_pullHandleUndo);
            _pullHandleUndo = -1;
        }
    }

    private void DrawHitDetails()
    {
        var hit = Selected;
        if (hit == null) { EditorGUILayout.HelpBox("+ 타격으로 데미지 필드를 추가하세요.", MessageType.Info); return; }
        var so = new SerializedObject(_definition);
        so.Update();
        var field = EditProperty(so, "hits").GetArrayElementAtIndex(_selected);
        bool markerTimeChanged = false;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Property(field, "enabled", "사용"); Property(field, "name", "이름");
            }
            var markerTime = field.FindPropertyRelative("time");
            EditorGUI.BeginChangeCheck();
            float seconds = EditorGUILayout.FloatField("타격 시점 (원본 초)", markerTime.floatValue);
            if (EditorGUI.EndChangeCheck()) { markerTime.floatValue = SnapHitTime(seconds); markerTimeChanged = true; }
            EditorGUI.BeginChangeCheck();
            int frame = EditorGUILayout.IntField("타격 프레임 (0부터)", Mathf.RoundToInt(markerTime.floatValue * FrameRate));
            if (EditorGUI.EndChangeCheck()) { markerTime.floatValue = SnapHitTime(frame / FrameRate); markerTimeChanged = true; }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("마커로 이동")) Seek(markerTime.floatValue);
                if (GUILayout.Button("마커 −1f")) { markerTime.floatValue = SnapHitTime(markerTime.floatValue - 1f / FrameRate); markerTimeChanged = true; }
                if (GUILayout.Button("마커 +1f")) { markerTime.floatValue = SnapHitTime(markerTime.floatValue + 1f / FrameRate); markerTimeChanged = true; }
            }
            Property(field, "shape", "판정 모양");
            Property(field, "position", "위치 (전방 기준 m)");
            Property(field, "euler", "회전 (도)");
            if (hit.shape == UltimateFieldShape.Box) Property(field, "size", "전체 크기 XYZ (m)");
            else Property(field, "radius", "반경 (m)");
            using (new EditorGUILayout.HorizontalScope())
            {
                Property(field, "damage", "타격당 피해"); Property(field, "hitStopFrames", "히트스톱 (60fps 프레임)");
            }
            Property(field, "stagger", "경직 단계");
            Property(field, "hitStopMode", "히트스톱 대상");
            _handleMode = GUILayout.Toolbar(_handleMode, new[] { "이동 핸들", "회전 핸들", "크기 핸들" });
            EditorGUILayout.LabelField($"{FrameRate:0.##}fps 원본 기준 · 이 마커 프레임에서 한 번 판정합니다. ‘마커로 이동’에서 필드와 핸들을 편집하세요.", EditorStyles.wordWrappedMiniLabel);
        }
        if (so.ApplyModifiedProperties()) { Sanitize(); if (markerTimeChanged) Seek(hit.time); else Sample(); }
        float damage = Hits.Where(h => h != null && h.enabled && (!SequenceMode || (h.time >= Entry.SourceStart && h.time <= Entry.SourceEnd))).Sum(h => h.damage);
        EditorGUILayout.LabelField($"{(SequenceMode ? "선택 클립 " : "")}전부 적중 시 합계 {damage:0.##} 피해  ·  필드마다 대상 1회 타격", EditorStyles.boldLabel);
        if (SequenceMode && (hit.time < Entry.SourceStart || hit.time > Entry.SourceEnd))
            EditorGUILayout.HelpBox("이 타격 마커는 잘라낸 원본 구간에 있습니다. 설정은 보존되며 이 시점을 다시 포함하면 실행됩니다.", MessageType.Info);
        EditorGUILayout.LabelField("가정: 적 1명, 방어 미반영. 로컬 +Z가 전방입니다. Scene의 박스와 실전 판정은 같은 데이터를 사용합니다.", EditorStyles.wordWrappedMiniLabel);
    }

    private static void Property(SerializedProperty root, string name, string label)
    {
        var property = root.FindPropertyRelative(name);
        if (property != null) EditorGUILayout.PropertyField(property, new GUIContent(label), true);
    }

    private void DrawMotionPath()
    {
        if (Motion == null) return;
        EditorGUILayout.LabelField("이동 경로 · 시전 시작점을 기준으로 편집", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        bool enabled = EditorGUILayout.ToggleLeft("이동 사용 — 캐릭터와 데미지 필드가 함께 이동", Motion.enabled);
        int mode = EditorGUILayout.Popup("이동 방식", (int)Motion.mode, new[] { "전방 이동", "시간별 경로 노드" });
        if (EditorGUI.EndChangeCheck()) Change("Change ultimate motion mode", () => { Motion.enabled = enabled; Motion.mode = (UltimateMotionWarpMode)mode; });
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("전체 경로 보기", GUILayout.Height(25))) QueueStudio(() => { EnsurePreview(); FramePath(false); });
            if (GUILayout.Button("위에서 보기", GUILayout.Height(25))) QueueStudio(() => { EnsurePreview(); FramePath(true); });
            if (GUILayout.Button("현재 위치 보기", GUILayout.Height(25))) QueueStudio(() => { EnsurePreview(); FrameCurrent(); });
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            _followCamera = EditorGUILayout.ToggleLeft("재생 중 캐릭터 따라가기", _followCamera);
            bool footprints = EditorGUILayout.ToggleLeft("타격 마커 발자국 표시", _showHitFootprints);
            if (footprints != _showHitFootprints) { _showHitFootprints = footprints; SceneView.RepaintAll(); }
        }
        if (!Motion.enabled) EditorGUILayout.HelpBox("이동 사용이 꺼져 있습니다. 켜면 설정한 경로를 따라 움직입니다.", MessageType.Warning);
        if (Motion.mode == UltimateMotionWarpMode.Forward)
        {
            var so = new SerializedObject(_definition); so.Update();
            var warp = so.FindProperty("warp");
            Property(warp, "start", "시작 (클립 초)");
            Property(warp, "end", "끝 (클립 초)");
            Property(warp, "distance", "전방 거리 (m)");
            Property(warp, "curve", "이동 진행 곡선");
            if (so.ApplyModifiedProperties()) { Sanitize(); Sample(); }
        }
        else
        {
            EditorGUI.BeginChangeCheck();
            int interpolation = EditorGUILayout.Popup("구간 속도", (int)Motion.interpolation, new[] { "일정 속도", "부드러운 출발 / 도착" });
            if (EditorGUI.EndChangeCheck()) Change("Change ultimate path interpolation", () => Motion.interpolation = (UltimatePathInterpolation)interpolation);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 현재 시점에 노드", GUILayout.Height(25))) QueueStudio(AddNodeAtCurrentTime);
                using (new EditorGUI.DisabledScope(SelectedNode == null))
                    if (GUILayout.Button("선택 노드 삭제", GUILayout.Height(25))) QueueStudio(() => Change("Delete ultimate path node", () =>
                    { Motion.points.RemoveAt(_selectedNode); _selectedNode = Mathf.Max(0, _selectedNode - 1); }));
            }
            if (GUILayout.Button("START   0.000초   X 0 / Z 0  — 고정 시작점", EditorStyles.toolbarButton)) Seek(0);
            for (int i = 0; i < Motion.points.Count; i++)
            {
                var point = Motion.points[i]; if (point == null) continue;
                int index = i;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var color = GUI.color;
                    if (index == _selectedNode) GUI.color = new Color(.85f, .72f, 1f);
                    if (GUILayout.Button($"◆ {index + 1:00}    {point.time:0.000}초    X {point.position.x:0.00} / Z {point.position.z:0.00} m", EditorStyles.toolbarButton)) SelectNode(index);
                    GUI.color = color;
                }
            }
            var node = SelectedNode;
            if (node != null)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"노드 {_selectedNode + 1:00} · 3D 창에서 보라색 점을 드래그", EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    float time = EditorGUILayout.FloatField("도착 (클립 초)", node.time);
                    float x, z;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("시작점에서 위치 (m)", GUILayout.Width(155));
                        GUILayout.Label("X", GUILayout.Width(14)); x = EditorGUILayout.FloatField(node.position.x);
                        GUILayout.Label("Z", GUILayout.Width(14)); z = EditorGUILayout.FloatField(node.position.z);
                    }
                    if (EditorGUI.EndChangeCheck()) Change("Edit ultimate path node", () =>
                    {
                        node.time = ClampNodeTime(_selectedNode, Snap(time));
                        node.position = new Vector3(x, 0, z); _time = node.time;
                    });
                    var previous = _selectedNode == 0 ? null : Motion.points[_selectedNode - 1];
                    float distance = Vector3.Distance(previous != null ? previous.position : Vector3.zero, node.position);
                    float seconds = (node.time - (previous != null ? previous.time : 0f)) / Mathf.Max(.05f, _definition.playbackSpeed);
                    EditorGUILayout.LabelField($"이전 노드에서 {distance:0.00}m / 게임 {seconds:0.000}초 · 평균 {distance / Mathf.Max(.001f, seconds):0.00}m/s", EditorStyles.miniLabel);
                }
            }
            else EditorGUILayout.HelpBox("현재 시점에 노드를 추가하거나 아래 예제를 적용하세요. 시점의 위치를 먼저 계산하므로 노드 추가만으로 기존 경로가 튀지 않습니다.", MessageType.Info);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("전방 설정으로 경로 만들기")) QueueStudio(ConvertForwardToPath);
            if (GUILayout.Button("타격 시점 예제 적용")) QueueStudio(ApplyExamplePath);
        }
        EditorGUILayout.LabelField("위 두 버튼은 이동 경로만 교체합니다. Undo로 되돌릴 수 있습니다.", EditorStyles.wordWrappedMiniLabel);
        float length = RouteLength();
        Vector3 offset = Offset;
        EditorGUILayout.LabelField($"총 경로 {length:0.00}m   ·   현재 X {offset.x:0.00} / Z {offset.z:0.00}m", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("보라색 점 = 도착 위치, 시간축의 ◆ = 도착 시점. 시작점과 1m 격자는 고정됩니다. +Z는 시전 전방, +X는 오른쪽이며 마지막 노드 이후에는 정지합니다. 지면 XZ 경로를 편집하며 원본 클립의 루트 이동을 추출한 경로는 아닙니다. 벽·지형 충돌은 Play Mode에서 확인하세요.", MessageType.Info);
    }

    private void QueueStudio(Action action)
    {
        EditorApplication.delayCall += () => { if (this != null && _definition != null) action(); };
    }

    private void EnsurePreview() { if (_preview == null || !_preview.IsReady || _previewView == null) StartPreview(); }

    private void SelectNode(int index)
    {
        _selectedNode = index; _tab = 1;
        if (SelectedNode != null) Seek(SelectedNode.time);
    }

    private float ClampNodeTime(int index, float value)
    {
        float step = 1f / FrameRate;
        float minimum = index > 0 ? Motion.points[index - 1].time + step : step;
        float maximum = index + 1 < Motion.points.Count ? Motion.points[index + 1].time - step : Duration;
        // Existing assets can contain sub-frame neighbours. Preserve their ordering
        // without forcing a key beyond the clip duration or across its neighbour.
        if (maximum < minimum)
        {
            minimum = index > 0 ? Motion.points[index - 1].time + .0001f : .001f;
            maximum = index + 1 < Motion.points.Count ? Motion.points[index + 1].time - .0001f : Duration;
        }
        if (maximum < minimum) return Mathf.Clamp(Motion.points[index].time, .001f, Duration);
        return Mathf.Clamp(Mathf.Clamp(value, minimum, maximum), .001f, Duration);
    }

    private void AddNodeAtCurrentTime()
    {
        float time = Mathf.Clamp(Snap(_time), 1f / FrameRate, Duration);
        int existing = Motion.points.FindIndex(point => point != null && Mathf.Abs(point.time - time) < 1.001f / FrameRate);
        if (existing >= 0) { SelectNode(existing); ShowNotification(new GUIContent("1프레임 이내의 기존 노드를 선택했습니다.")); return; }
        Vector3 offset = Motion.EvaluateOffset(time);
        Change("Add ultimate path node", () =>
        {
            Motion.mode = UltimateMotionWarpMode.Path; Motion.enabled = true;
            var node = new UltimateMotionWaypoint { time = time, position = offset };
            Motion.points.Add(node); Motion.Normalize(Duration); _selectedNode = Motion.points.IndexOf(node); _time = time;
        });
        EnsurePreview();
    }

    private void ConvertForwardToPath()
    {
        Change("Convert ultimate forward motion to path", () =>
        {
            float start = Mathf.Clamp(Motion.start, 0, Mathf.Max(0, Duration - 1f / FrameRate));
            float end = Mathf.Clamp(Motion.end, start + 1f / FrameRate, Duration);
            Motion.points.Clear();
            if (start >= 1f / FrameRate) Motion.points.Add(new UltimateMotionWaypoint { time = start, position = Vector3.zero });
            Motion.points.Add(new UltimateMotionWaypoint { time = end, position = Vector3.forward * Motion.distance });
            Motion.mode = UltimateMotionWarpMode.Path; Motion.enabled = true; _selectedNode = Motion.points.Count - 1;
        });
        EnsurePreview(); FramePath(false);
    }

    private void ApplyExamplePath()
    {
        Change("Apply editable ultimate path example", () =>
        {
            var times = Hits.Where(hit => hit != null && hit.enabled)
                .Select(hit => Snap(hit.time)).Where(time => time > 0f).Distinct().OrderBy(time => time).Take(3).ToArray();
            if (times.Length == 0) times = new[] { Snap(Duration * .2f), Snap(Duration * .45f), Snap(Duration * .7f) };
            Motion.points.Clear();
            for (int i = 0; i < times.Length; i++) Motion.points.Add(new UltimateMotionWaypoint
            { time = times[i], position = new Vector3(i % 2 == 1 ? .8f : 0f, 0f, 1.6f + i * 1.2f) });
            Motion.enabled = true; Motion.mode = UltimateMotionWarpMode.Path; _selectedNode = 0; _tab = 1; _time = 0;
        });
        EnsurePreview(); FramePath(false);
        ShowNotification(new GUIContent("조절 가능한 이동 예제를 적용했습니다. 타격 설정은 유지됩니다."));
    }

    private float RouteLength()
    {
        if (Motion == null) return 0;
        var samples = RouteSamples(SequenceMode ? SequenceDuration : Duration, 64);
        float length = 0;
        for (int i = 1; i < samples.Length; i++)
        {
            length += Vector3.Distance(samples[i - 1], samples[i]);
        }
        return length;
    }

    private Vector3[] RouteSamples(float until, int subdivisions)
    {
        if (SequenceMode)
        {
            var boundaries = _definition.sequence.Select((entry, index) => UltimateSequenceUtility.GetSegmentStart(_definition, index));
            return Enumerable.Range(0, subdivisions + 1).Select(i => until * i / subdivisions)
                .Concat(boundaries.Where(time => time >= 0 && time <= until)).Distinct().OrderBy(time => time)
                .Select(time => UltimateSequenceUtility.EvaluateMotion(_definition, time).position).ToArray();
        }
        if (Motion == null) return new[] { Vector3.zero };
        // Include exact authored keys so even a very short segment appears in the route.
        var keys = Motion.mode == UltimateMotionWarpMode.Path
            ? Motion.points.Where(point => point != null).Select(point => point.time)
            : new[] { Motion.start, Motion.end }.AsEnumerable();
        return Enumerable.Range(0, subdivisions + 1).Select(i => until * i / subdivisions)
            .Concat(keys.Where(time => time >= 0 && time <= until)).Distinct().OrderBy(time => time)
            .Select(time => Motion.EvaluateOffset(time)).ToArray();
    }

    private void FramePath(bool top)
    {
        if (_previewView == null || _preview == null || !_preview.IsReady) return;
        _followCamera = false;
        Bounds bounds = new Bounds(_preview.Origin + Vector3.up, new Vector3(2, 2.5f, 2));
        foreach (Vector3 local in RouteSamples(SequenceMode ? SequenceDuration : Duration, 64))
        {
            Vector3 point = _preview.Origin + _preview.Facing * local;
            bounds.Encapsulate(point + new Vector3(-1, 0, -1)); bounds.Encapsulate(point + new Vector3(1, 2.5f, 1));
        }
        if (!SequenceMode && Motion != null && Motion.mode == UltimateMotionWarpMode.Path) foreach (var node in Motion.points)
            if (node != null) bounds.Encapsulate(_preview.Origin + _preview.Facing * node.position + Vector3.up);
        _previewView.LookAt(bounds.center, top ? Quaternion.Euler(90, 0, 0) : Quaternion.Euler(32, -35, 0), Mathf.Max(4.2f, bounds.extents.magnitude * 1.15f), top, true);
        _previewView.Repaint();
    }

    private void FrameCurrent()
    {
        if (_previewView == null || !_preview.IsReady) return;
        _previewView.LookAt(_preview.Origin + _preview.Facing * Offset + Vector3.up, Quaternion.Euler(25, -35, 0), 4.2f, false, true);
    }

    private void FrameSelectedClip()
    {
        if (!SequenceMode || Entry == null || _previewView == null || !_preview.IsReady) return;
        _followCamera = false;
        Vector3 first = _preview.Origin + _preview.Facing * UltimateSequenceUtility.EvaluateMotion(_definition, EntryStart).position;
        Bounds bounds = new Bounds(first + Vector3.up, new Vector3(2, 2.5f, 2));
        for (int i = 0; i <= 48; i++)
        {
            Vector3 point = _preview.Origin + _preview.Facing * UltimateSequenceUtility.EvaluateMotion(_definition, EntryStart + Entry.Duration * i / 48f).position;
            bounds.Encapsulate(point + new Vector3(-1, 0, -1)); bounds.Encapsulate(point + new Vector3(1, 2.5f, 1));
        }
        _previewView.LookAt(bounds.center, Quaternion.Euler(25, -35, 0), Mathf.Max(4.2f, bounds.extents.magnitude * 1.15f), false, true);
        SelectClip(_selectedClip);
    }

    private void DrawCues()
    {
        var so = new SerializedObject(_definition); so.Update();
        EditorGUILayout.LabelField("연출 이벤트 · VFX", EditorStyles.boldLabel);
        var cues = EditProperty(so, "cues");
        for (int i = 0; i < cues.arraySize; i++)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var cue = cues.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.HorizontalScope())
                {
                    Property(cue, "name", "이벤트 이름");
                    if (GUILayout.Button("삭제", GUILayout.Width(44))) { cues.DeleteArrayElementAtIndex(i); break; }
                }
                Property(cue, "time", "시점 (클립 초)");
                Property(cue, "prefab", "이펙트 프리팹");
                Property(cue, "position", "위치 (전방 기준 m)");
                Property(cue, "euler", "회전 (도)");
                Property(cue, "lifetime", "수명 (게임 초)");
            }
        }
        if (GUILayout.Button("+ 연출 이벤트"))
        {
            int index = cues.arraySize++;
            var item = cues.GetArrayElementAtIndex(index);
            item.FindPropertyRelative("name").stringValue = $"Cue {index + 1}";
            item.FindPropertyRelative("time").floatValue = _time;
            item.FindPropertyRelative("prefab").objectReferenceValue = null;
            item.FindPropertyRelative("position").vector3Value = Vector3.zero;
            item.FindPropertyRelative("euler").vector3Value = Vector3.zero;
            item.FindPropertyRelative("lifetime").floatValue = 3f;
        }
        EditorGUILayout.HelpBox("이벤트 시점과 위치는 Scene에 표시됩니다. 이펙트 실제 재생은 Play Mode에서 확인합니다. 카메라 무빙은 런타임 연출 이벤트에 연결할 확장 지점만 마련되어 있습니다.", MessageType.None);
        if (so.ApplyModifiedProperties()) { Sanitize(); Sample(); }
    }

    private void DrawSetup()
    {
        EditorGUI.BeginChangeCheck();
        _player = (PlayerCore)EditorGUILayout.ObjectField("실제 플레이어", _player, typeof(PlayerCore), true);
        if (EditorGUI.EndChangeCheck()) ClearPreview();
        if (_player == null) EditorGUILayout.HelpBox("씬의 PlayerCore를 연결하면 모델 미리보기와 실제 데이터 연결을 확인할 수 있습니다.", MessageType.Warning);
        else if (_player.UltimateDefinition != _definition) EditorGUILayout.HelpBox("선택한 데이터와 플레이어가 사용하는 데이터가 다릅니다. 플레이어의 Ultimate Definition 연결을 확인하세요.", MessageType.Warning);
        else EditorGUILayout.HelpBox("실제 플레이어와 연결됨 · 수정은 다음 시전부터 반영됩니다. 저장하면 에디터를 다시 열어도 유지됩니다.", MessageType.Info);
        var so = new SerializedObject(_definition); so.Update();
        EditorGUILayout.PropertyField(so.FindProperty("cutscene"), new GUIContent("Q 시작 컷씬"));
        if (!SequenceMode) EditorGUILayout.PropertyField(so.FindProperty("clip"), new GUIContent("단일 애니메이션 클립"));
        EditorGUILayout.PropertyField(so.FindProperty("playbackSpeed"), new GUIContent("실제 스킬 배속"));
        EditorGUILayout.PropertyField(so.FindProperty("cooldown"), new GUIContent("시전부터 쿨다운 (초)"));
        EditorGUILayout.PropertyField(so.FindProperty("invulnerable"), new GUIContent("시전 중 무적"));
        EditorGUILayout.PropertyField(so.FindProperty("targetLayers"), new GUIContent("적중 대상 레이어"));
        if (so.ApplyModifiedProperties()) { Sanitize(); Sample(); }
        if (_definition.cutscene != null)
        {
            if (GUILayout.Button("연결된 컷씬 편집")) VirtualCutsceneStudio.OpenDefinition(_definition.cutscene);
            if (!_definition.cutscene.Ready) EditorGUILayout.HelpBox("연결된 컷씬에 재생 가능한 클립이 없습니다. 컷씬 설정을 확인하세요.", MessageType.Warning);
        }
        else EditorGUILayout.HelpBox("Q 시작 컷씬을 연결하면 즉시 컷씬을 재생하고, 끝나는 순간 공격을 시작합니다. 비워 두면 바로 공격합니다.", MessageType.Info);
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("실제 실행", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(!Application.isPlaying || _player == null || _player.UltimateDefinition != _definition || !_player.CanBeginUltimate))
        {
            if (GUILayout.Button("궁극기 시전 (Play Mode)", GUILayout.Height(28))) _player.TryBeginUltimate();
        }
        if (Application.isPlaying && _player != null && _player.Ultimate != null)
            EditorGUILayout.LabelField($"{(_player.Ultimate.IsActive ? "시전 중" : "대기")}  ·  {_player.Ultimate.Time:0.00}초  ·  쿨다운 {_player.Ultimate.CooldownRemaining:0.0}초");
        EditorGUILayout.HelpBox("E = 기존 스킬 / Q = 궁극기. 상단의 관찰 배속은 미리보기 전용이고, 실제 스킬 배속은 여기서 바꿉니다. 미리보기 모델은 씬 플레이어 옆 6m 위치의 임시 복제본이며 창을 닫거나 Play Mode로 전환하면 정리됩니다.", MessageType.Info);
        if (!SequenceMode)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("연속 클립 모드 시작", EditorStyles.boldLabel);
            DrawClipAddition();
        }
    }

    private void DrawFooter()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label(EditorUtility.IsDirty(_definition) ? "● 저장하지 않은 변경" : "✓ 저장됨", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Undo", EditorStyles.toolbarButton, GUILayout.Width(46))) EditorApplication.delayCall += Undo.PerformUndo;
            if (GUILayout.Button("저장", EditorStyles.toolbarButton, GUILayout.Width(46)))
            {
                EditorApplication.delayCall += () =>
                {
                    if (this == null || _definition == null) return;
                    AssetDatabase.SaveAssetIfDirty(_definition); _savedJson = EditorJsonUtility.ToJson(_definition); Repaint();
                };
            }
            if (GUILayout.Button("열었을 때 / 마지막 저장으로", EditorStyles.toolbarButton, GUILayout.Width(168)) && _savedJson != null)
                EditorApplication.delayCall += () =>
                {
                    if (this == null || _definition == null || _savedJson == null) return;
                    Change("Restore ultimate studio saved values", () =>
                    {
                        EditorJsonUtility.FromJsonOverwrite(_savedJson, _definition);
                        _selectedClip = Mathf.Clamp(_selectedClip, 0, Mathf.Max(0, _definition.sequence.Count - 1));
                        _previewSequenceDirty = true; _sampleSelectedClip = true; _playing = false;
                        _sequenceTime = SourceToSequenceTime(_time);
                    });
                };
        }
    }

    private void Change(string name, Action change)
    {
        Undo.RegisterCompleteObjectUndo(_definition, name); change(); Sanitize(); EditorUtility.SetDirty(_definition); Sample(); Repaint();
    }

    private void Sanitize()
    {
        _definition.playbackSpeed = Mathf.Max(.05f, _definition.playbackSpeed);
        _definition.cooldown = Mathf.Max(0, _definition.cooldown);
        if (SequenceMode)
        {
            foreach (var entry in _definition.sequence) entry?.Normalize();
            _selectedClip = Mathf.Clamp(_selectedClip, 0, _definition.sequence.Count - 1);
            _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, (Hits?.Count ?? 0) - 1));
            return;
        }
        foreach (var h in Hits)
        {
            if (h == null) continue;
            h.time = Mathf.Clamp(h.time, 0, Duration);
            h.size = new Vector3(Mathf.Max(.01f, h.size.x), Mathf.Max(.01f, h.size.y), Mathf.Max(.01f, h.size.z));
            h.radius = Mathf.Max(.01f, h.radius); h.damage = Mathf.Max(0, h.damage); h.hitStopFrames = Mathf.Max(0, h.hitStopFrames);
        }
        var selectedNode = SelectedNode;
        Motion?.Normalize(Duration);
        if (selectedNode != null && Motion.points.Contains(selectedNode)) _selectedNode = Motion.points.IndexOf(selectedNode);
        _selectedNode = Mathf.Clamp(_selectedNode, 0, Mathf.Max(0, (Motion?.points?.Count ?? 0) - 1));
    }

    private Vector3 Offset => SequenceMode ? UltimateSequenceUtility.EvaluateMotion(_definition, SequenceSampleTime).position : _definition?.warp != null ? _definition.warp.EvaluateOffset(_time) : Vector3.zero;

    private void DuringSceneGUI(SceneView scene)
    {
        if (_definition == null || _preview == null || !_preview.IsReady || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (_previewHadView && _previewView == null) return;
        if (_previewView != null && scene != _previewView) return;
        Vector3 origin = _preview.Origin + _preview.Facing * Offset;
        Quaternion facing = _preview.Facing;
        Vector3 castOrigin = _preview.Origin;
        int gridExtent = 5;
        foreach (Vector3 sample in RouteSamples(SequenceMode ? SequenceDuration : Duration, 32))
        {
            gridExtent = Mathf.Max(gridExtent, Mathf.CeilToInt(Mathf.Max(Mathf.Abs(sample.x), Mathf.Abs(sample.z))) + 2);
        }
        gridExtent = Mathf.Min(gridExtent, 50);
        using (new Handles.DrawingScope(new Color(.25f, .4f, .43f, .45f)))
        {
            for (int i = -gridExtent; i <= gridExtent; i++)
            {
                Handles.DrawLine(castOrigin + facing * new Vector3(i, .015f, -gridExtent), castOrigin + facing * new Vector3(i, .015f, gridExtent));
                Handles.DrawLine(castOrigin + facing * new Vector3(-gridExtent, .015f, i), castOrigin + facing * new Vector3(gridExtent, .015f, i));
            }
        }
        Handles.color = Accent;
        Handles.ArrowHandleCap(0, castOrigin + Vector3.up * .03f, facing, 1.4f, EventType.Repaint);
        Handles.DrawWireDisc(castOrigin + Vector3.up * .03f, Vector3.up, .35f);
        Handles.Label(castOrigin - facing * Vector3.forward * .5f, "START · 0초 / 고정 원점\n1칸 = 1m", EditorStyles.whiteBoldLabel);
        Handles.Label(origin + Vector3.up * 2.5f, $"ULTIMATE · {(SequenceMode ? _sequenceTime : _time):0.000}s\n현재 X {Offset.x:0.00} / Y {Offset.y:0.00} / Z {Offset.z:0.00}m", EditorStyles.whiteBoldLabel);
        DrawRouteScene(castOrigin, facing);
        if (_showHitFootprints) DrawHitFootprints(castOrigin, facing);
        DrawPullScene();
        if (SequenceMode)
        {
            DrawSequenceFields(origin, facing * UltimateSequenceUtility.EvaluateMotion(_definition, SequenceSampleTime).rotation);
            return;
        }
        for (int i = 0; i < Hits.Count; i++)
        {
            var hit = Hits[i]; bool selected = i == _selected;
            if (hit == null) continue;
            bool active = IsPreviewFieldActive(hit, _time, FrameRate);
            if (!active) continue;
            Color color = selected || _tab == 1 ? HitColor : Accent;
            color.a = selected || _tab == 1 ? 1f : .65f;
            var pose = UltimateFieldGeometry.Evaluate(hit, origin, facing);
            DrawField(hit, pose, color);
            if (selected || _tab == 1) Handles.Label(pose.center + pose.rotation * new Vector3(hit.shape == UltimateFieldShape.Box ? pose.halfExtents.x + .15f : pose.radius + .15f, 1.2f, 0f), $"{hit.name}  ·  {Mathf.RoundToInt(hit.time * FrameRate)}f\n{hit.time:0.000}s  /  {hit.damage:0.#} damage");
            if (selected && _tab == 0 && !_playing && !EditingPullPosition) EditFieldHandle(hit, pose, origin, facing);
        }
        if (Cues != null) foreach (var cue in Cues)
        {
            if (cue == null) continue;
            Handles.color = new Color(.8f, .55f, 1f, .8f);
            Vector3 location = origin + facing * cue.position;
            Handles.DrawWireDisc(location, Vector3.up, .18f);
            Handles.Label(location, $"◇ {cue.name} · {cue.time:0.00}s");
        }
    }

    private void DrawRouteScene(Vector3 castOrigin, Quaternion facing)
    {
        if (SequenceMode) { DrawSequenceRoute(castOrigin, facing); return; }
        if (Motion == null) return;
        Vector3 Lifted(Vector3 local) => castOrigin + facing * local + Vector3.up * .055f;
        Vector3[] route = RouteSamples(Duration, 96).Select(Lifted).ToArray();
        if (route.Length > 1) using (new Handles.DrawingScope(Motion.enabled ? PathColor : Color.gray)) Handles.DrawAAPolyLine(4f, route);
        Vector3[] travelled = RouteSamples(_time, 48).Select(point => Lifted(point) + Vector3.up * .01f).ToArray();
        if (travelled.Length > 1) using (new Handles.DrawingScope(Accent)) Handles.DrawAAPolyLine(5f, travelled);
        if (Motion.mode != UltimateMotionWarpMode.Path) return;
        for (int i = 0; i < Motion.points.Count; i++)
        {
            var point = Motion.points[i]; if (point == null) continue;
            Vector3 location = Lifted(point.position) + Vector3.up * .1f;
            float size = HandleUtility.GetHandleSize(location) * .09f;
            bool selected = i == _selectedNode && _tab == 1;
            Handles.color = selected ? Color.white : PathColor;
            Handles.DrawWireDisc(Lifted(point.position), Vector3.up, .19f);
            Handles.Label(location + facing * Vector3.right * .22f + Vector3.up * .15f, $"{i + 1:00}  ·  {point.time:0.000}s", EditorStyles.whiteBoldLabel);
            if (_tab == 1)
            {
                int control = GUIUtility.GetControlID(FocusType.Passive);
                int hotBefore = GUIUtility.hotControl;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.Slider2D(control, location, Vector3.up, facing * Vector3.right, facing * Vector3.forward, size, Handles.SphereHandleCap, Vector2.zero);
                bool changed = EditorGUI.EndChangeCheck();
                if (GUIUtility.hotControl == control && hotBefore != control)
                { _selectedNode = i; Seek(point.time); }
                if (changed)
                {
                    Vector3 local = Quaternion.Inverse(facing) * (moved - castOrigin);
                    local.y = 0;
                    Change("Move ultimate path waypoint", () => point.position = local);
                }
            }
            else if (Handles.Button(location, Quaternion.identity, size, size, Handles.SphereHandleCap)) SelectNode(i);
        }
    }

    private void DrawHitFootprints(Vector3 castOrigin, Quaternion facing)
    {
        if (SequenceMode)
        {
            for (int clipIndex = 0; clipIndex < _definition.sequence.Count; clipIndex++)
            {
                var entry = _definition.sequence[clipIndex]; if (entry == null) continue;
                float startTime = UltimateSequenceUtility.GetSegmentStart(_definition, clipIndex);
                for (int i = 0; i < entry.hits.Count; i++)
                {
                    var hit = entry.hits[i]; if (hit == null || !hit.enabled || hit.time < entry.SourceStart || hit.time > entry.SourceEnd) continue;
                    float time = startTime + (hit.time - entry.SourceStart) / Mathf.Max(.05f, entry.speed);
                    Vector3 point = castOrigin + facing * UltimateSequenceUtility.EvaluateMotion(_definition, time).position;
                    point.y = castOrigin.y + .035f;
                    float frameRate = entry.clip != null ? Mathf.Max(1f, entry.clip.frameRate) : 60f;
                    using (new Handles.DrawingScope(new Color(HitColor.r, HitColor.g, HitColor.b, .7f)))
                    {
                        Handles.DrawWireDisc(point, Vector3.up, .22f);
                        Handles.Label(point - facing * Vector3.right * .65f, $"{clipIndex + 1:00}/H{i + 1}  {Mathf.RoundToInt(hit.time * frameRate)}f · 전체 {time:0.00}s", EditorStyles.miniLabel);
                    }
                }
            }
            return;
        }
        if (Motion == null) return;
        for (int i = 0; i < Hits.Count; i++)
        {
            var hit = Hits[i]; if (hit == null || !hit.enabled) continue;
            Vector3 point = castOrigin + facing * Motion.EvaluateOffset(hit.time) + Vector3.up * .025f;
            using (new Handles.DrawingScope(new Color(HitColor.r, HitColor.g, HitColor.b, .75f)))
            {
                Handles.DrawWireDisc(point, Vector3.up, .29f);
                Vector3 right = facing * Vector3.right * .13f;
                Vector3 length = facing * Vector3.forward * .17f;
                Handles.DrawAAPolyLine(3f, point - right - length, point - right + length);
                Handles.DrawAAPolyLine(3f, point + right - length, point + right + length);
                Handles.Label(point - facing * Vector3.right * .8f, $"H{i + 1}  {Mathf.RoundToInt(hit.time * FrameRate)}f · {hit.time:0.000}s", EditorStyles.miniLabel);
            }
        }
    }

    private void DrawSequenceRoute(Vector3 castOrigin, Quaternion facing)
    {
        Vector3 World(Vector3 point) => castOrigin + facing * point + Vector3.up * .05f;
        var route = RouteSamples(SequenceDuration, 160).Select(World).ToArray();
        if (route.Length > 1) using (new Handles.DrawingScope(PathColor)) Handles.DrawAAPolyLine(4f, route);
        var travelled = RouteSamples(_sequenceTime, 96).Select(World).ToArray();
        if (travelled.Length > 1) using (new Handles.DrawingScope(Accent)) Handles.DrawAAPolyLine(5f, travelled);
        for (int i = 0; i < _definition.sequence.Count; i++)
        {
            float time = UltimateSequenceUtility.GetSegmentStart(_definition, i);
            Vector3 location = World(UltimateSequenceUtility.EvaluateMotion(_definition, time).position);
            Handles.color = i == _selectedClip ? Color.white : PathColor;
            float size = HandleUtility.GetHandleSize(location) * .06f;
            if (Handles.Button(location + Vector3.up * .08f, Quaternion.identity, size, size, Handles.SphereHandleCap)) SelectClip(i);
            Handles.Label(location + Vector3.up * .3f, $"{EntryName(_definition.sequence[i], i)}\n연결 시작 {time:0.000}s", EditorStyles.whiteBoldLabel);
        }
    }

    private void DrawSequenceFields(Vector3 origin, Quaternion facing)
    {
        UltimateSequenceUtility.GetSegment(_definition, SequenceSampleTime, out int index, out float elapsed);
        if (index < 0 || index >= _definition.sequence.Count) return;
        var entry = _definition.sequence[index]; if (entry == null) return;
        float sourceTime = entry.SourceTime(elapsed);
        float frameRate = entry.clip != null ? Mathf.Max(1f, entry.clip.frameRate) : 60f;
        bool canEdit = _tab == 0 && index == _selectedClip && !_playing;
        for (int i = 0; i < entry.hits.Count; i++)
        {
            var hit = entry.hits[i]; if (hit == null) continue;
            bool selected = canEdit && i == _selected;
            bool active = hit.time >= entry.SourceStart && hit.time <= entry.SourceEnd && IsPreviewFieldActive(hit, sourceTime, frameRate);
            if (!active) continue;
            var pose = UltimateFieldGeometry.Evaluate(hit, origin, facing);
            var color = selected ? HitColor : Accent;
            DrawField(hit, pose, color);
            if (selected || active)
                Handles.Label(pose.center + pose.rotation * new Vector3(hit.shape == UltimateFieldShape.Box ? pose.halfExtents.x + .15f : pose.radius + .15f, 1.2f, 0), $"{index + 1:00}/{hit.name}  ·  {Mathf.RoundToInt(hit.time * frameRate)}f\n원본 {hit.time:0.000}s");
            if (selected && !EditingPullPosition) EditFieldHandle(hit, pose, origin, facing);
        }
        if (_tab != 2 || Entry == null) return;
        foreach (var cue in Entry.cues)
        {
            if (cue == null || cue.time < Entry.SourceStart || cue.time > Entry.SourceEnd) continue;
            float time = EntryStart + (cue.time - Entry.SourceStart) / Mathf.Max(.05f, Entry.speed);
            var motion = UltimateSequenceUtility.EvaluateMotion(_definition, time);
            Vector3 point = _preview.Origin + _preview.Facing * (motion.position + motion.rotation * cue.position);
            Handles.color = PathColor;
            Handles.DrawWireDisc(point, Vector3.up, .18f); Handles.Label(point, $"◇ {cue.name} · 원본 {cue.time:0.00}s");
        }
    }

    private static void DrawField(UltimateHitField hit, UltimateFieldPose pose, Color color)
    {
        using (new Handles.DrawingScope(color, Matrix4x4.TRS(pose.center, pose.rotation, Vector3.one)))
        {
            if (hit.shape == UltimateFieldShape.Box) Handles.DrawWireCube(Vector3.zero, pose.halfExtents * 2f);
            else
            {
                Handles.DrawWireDisc(Vector3.zero, Vector3.up, pose.radius);
                Handles.DrawWireDisc(Vector3.zero, Vector3.right, pose.radius);
                Handles.DrawWireDisc(Vector3.zero, Vector3.forward, pose.radius);
            }
        }
    }

    private void EditFieldHandle(UltimateHitField hit, UltimateFieldPose pose, Vector3 origin, Quaternion facing)
    {
        EditorGUI.BeginChangeCheck();
        Vector3 position = hit.position, euler = hit.euler, size = hit.size;
        float radius = hit.radius;
        if (_handleMode == 0) position = Quaternion.Inverse(facing) * (Handles.PositionHandle(pose.center, facing) - origin);
        else if (_handleMode == 1) euler = (Quaternion.Inverse(facing) * Handles.RotationHandle(pose.rotation, pose.center)).eulerAngles;
        else if (hit.shape == UltimateFieldShape.Sphere) radius = Handles.RadiusHandle(pose.rotation, pose.center, pose.radius);
        else
        {
            using (new Handles.DrawingScope(Color.white, Matrix4x4.TRS(pose.center, pose.rotation, Vector3.one)))
            {
                _boxHandle.center = Vector3.zero; _boxHandle.size = pose.halfExtents * 2;
                _boxHandle.handleColor = HitColor; _boxHandle.wireframeColor = HitColor;
                _boxHandle.DrawHandle();
                size = _boxHandle.size;
                position = Quaternion.Inverse(facing) * (pose.center + pose.rotation * _boxHandle.center - origin);
            }
        }
        if (EditorGUI.EndChangeCheck()) Change("Edit ultimate damage field", () => { hit.position = position; hit.euler = euler; hit.size = size; hit.radius = radius; });
    }

    private void DrawSpatialDiagrams()
    {
        Rect all = GUILayoutUtility.GetRect(0, 180, GUILayout.ExpandWidth(true));
        Rect top = new Rect(all.x, all.y + 4, (all.width - 8) * .5f, 174);
        Rect side = new Rect(top.xMax + 8, top.y, top.width, top.height);
        DrawDiagram(top, false); DrawDiagram(side, true);
    }

    private bool FieldIsActiveAtPreviewTime(UltimateHitField hit)
    {
        if (!SequenceMode) return IsPreviewFieldActive(hit, _time, FrameRate);
        if (!UltimateSequenceUtility.GetSegment(_definition, SequenceSampleTime, out int index, out float elapsed) ||
            index != _selectedClip || Entry == null) return false;
        return hit != null && hit.time >= Entry.SourceStart && hit.time <= Entry.SourceEnd && IsPreviewFieldActive(hit, Entry.SourceTime(elapsed), FrameRate);
    }

    // A marker has no duration. The shared geometry helper identifies its source frame.
    private static bool IsPreviewFieldActive(UltimateHitField hit, float time, float frameRate = 60f)
        => UltimateFieldGeometry.IsActive(hit, time, frameRate);

    private void DrawDiagram(Rect rect, bool side)
    {
        EditorGUI.DrawRect(rect, new Color(.10f, .13f, .16f));
        GUI.Label(new Rect(rect.x + 8, rect.y + 5, rect.width - 16, 20), side ? "측면 · Z / Y" : "상면 · X / Z", EditorStyles.miniBoldLabel);
        const float scale = 24f;
        Vector2 center = new Vector2(rect.center.x, rect.yMax - 33);
        Handles.BeginGUI();
        var previous = Handles.color;
        Handles.color = new Color(.27f, .33f, .38f);
        for (int i = -6; i <= 6; i++)
        {
            float x = center.x + i * scale;
            if (x > rect.x && x < rect.xMax) Handles.DrawLine(new Vector3(x, rect.y + 25), new Vector3(x, rect.yMax - 15));
            float y = center.y - i * scale;
            if (y > rect.y + 25 && y < rect.yMax - 15) Handles.DrawLine(new Vector3(rect.x + 1, y), new Vector3(rect.xMax - 1, y));
        }
        Vector2 Project(Vector3 p) => side ? center + new Vector2(p.z, -p.y) * scale : center + new Vector2(p.x, -p.z) * scale;
        var hit = Selected;
        bool visible = FieldIsActiveAtPreviewTime(hit);
        if (visible)
        {
            var pose = UltimateFieldGeometry.Evaluate(hit, Vector3.zero, Quaternion.identity);
            Handles.color = HitColor;
            if (hit.shape == UltimateFieldShape.Sphere)
            {
                Vector2 c = Project(pose.center);
                Vector3[] ring = new Vector3[49];
                for (int i = 0; i < ring.Length; i++) { float a = i / 48f * Mathf.PI * 2; ring[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * pose.radius * scale; }
                DrawClippedPolyline(rect, ring);
            }
            else
            {
                Vector3[] corners = new Vector3[8];
                for (int i = 0; i < 8; i++) corners[i] = Project(pose.center + pose.rotation * Vector3.Scale(pose.halfExtents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                for (int i = 0; i < 8; i++) for (int b = 1; b <= 4; b *= 2) if ((i & b) == 0) DrawClippedPolyline(rect, new[] { corners[i], corners[i | b] });
            }
        }
        Handles.color = Accent;
        if (side)
        {
            Handles.DrawLine(Project(Vector3.zero), Project(Vector3.up * 1.5f));
            Handles.DrawWireDisc(Project(Vector3.up * 1.75f), Vector3.forward, 5f);
            Handles.DrawLine(Project(Vector3.up), Project(new Vector3(0, 1.2f, .5f)));
        }
        else
        {
            Handles.DrawWireDisc(center, Vector3.forward, 8f);
            Handles.DrawAAPolyLine(2f, center, center + Vector2.up * -29, center + new Vector2(-5, -23));
        }
        Handles.color = previous; Handles.EndGUI();
        GUI.Label(new Rect(rect.x + 8, rect.yMax - 17, rect.width - 16, 17), visible ? "1칸 = 1m · 타격 마커 프레임" : "현재 비활성 · 마커 프레임에서 필드 표시", EditorStyles.miniLabel);
    }

    private static void DrawClippedPolyline(Rect rect, Vector3[] points)
    {
        // Keep diagrams in their panels; Scene view remains the full, unclipped authoring surface.
        Rect clip = new Rect(rect.x + 1, rect.y + 25, rect.width - 2, rect.height - 42);
        for (int i = 1; i < points.Length; i++)
        {
            Vector2 a = points[i - 1], b = points[i];
            float t0 = 0, t1 = 1; Vector2 d = b - a;
            bool Limit(float p, float q)
            {
                if (Mathf.Abs(p) < .00001f) return q >= 0;
                float r = q / p;
                if (p < 0) { if (r > t1) return false; t0 = Mathf.Max(t0, r); }
                else { if (r < t0) return false; t1 = Mathf.Min(t1, r); }
                return true;
            }
            if (Limit(-d.x, a.x - clip.x) && Limit(d.x, clip.xMax - a.x) && Limit(-d.y, a.y - clip.y) && Limit(d.y, clip.yMax - a.y))
                Handles.DrawAAPolyLine(2f, a + d * t0, a + d * t1);
        }
    }
}

[CustomEditor(typeof(PlayerUltimateDefinition))]
public sealed class PlayerUltimateDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Ultimate Studio 열기", GUILayout.Height(30))) PlayerUltimateStudio.OpenDefinition((PlayerUltimateDefinition)target);
        EditorGUILayout.HelpBox("단일 프레임 타격 마커와 데미지 필드를 전용 툴에서 시각적으로 편집할 수 있습니다.", MessageType.Info);
        DrawDefaultInspector();
    }
}
