using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Animation first, with a single visible clock and task-focused controls.</summary>
public sealed class PlayerDodgeStudio : EditorWindow
{
    private static readonly Color Cyan = new Color(.25f, .83f, .88f);
    private static readonly Color Gold = new Color(1f, .69f, .27f);
    private static readonly Color Violet = new Color(.7f, .5f, .95f);
    private static readonly string[] PhaseNames = { "회피", "반격 준비", "연속 공격", "마무리" };
    private static readonly string[] EffectNames = { "비네트", "흑백 · 채도", "노출", "색수차", "렌즈 왜곡", "카메라 줌", "화면 흔들림" };
    [SerializeField] private PlayerDodgeDefinition _definition;
    [SerializeField] private PlayerCore _player;
    [SerializeField] private bool _backwards;
    [SerializeField] private int _tab, _phase, _cueIndex, _previewMode, _uiVersion;
    [SerializeField] private bool _counterCue, _loop = true, _inPlace = true;
    [SerializeField] private bool _showSetup, _showAdvanced, _showRecording, _showTarget, _testThreat;
    [SerializeField] private float _testDistance = 3.5f, _testRange = 4f, _threatStart, _threatEnd = .6f;
    [SerializeField] private float _counterPosition = .3f, _counterAt = .45f;
    [SerializeField] private bool _manualCounter;
    [SerializeField] private bool _forceAfterimagePreview;
    [SerializeField] private float _time, _poseTime, _previewSpeed = 1;
    [SerializeField] private string _baseline;
    [SerializeField] private PlayerDodgeDefinition _baselineAsset;
    private bool _playing, _focusClip = true, _dirty = true, _posePicked, _truncated;
    private float _duration = 1, _windowStart = -1, _windowEnd = -1;
    private double _lastUpdate;
    private Vector2 _inspectorScroll, _recordScroll;
    private string _message, _previewError, _capturePath;
    private DodgeStudioPreview _preview;
    private DodgeSequence _sample;
    private Vector3 _samplePosition;
    private readonly List<SimFrame> _frames = new List<SimFrame>();
    private readonly List<DodgeSequenceEvent> _events = new List<DodgeSequenceEvent>();
    private Rect _timeline;
    private int _dragControl, _dragUndo;
    private bool _dragAsset;
    private Action<float> _dragSetter;
    private bool _resetGuiState;

    private struct SimFrame
    {
        public float time, source, scale, effect;
        public DodgePhase phase;
        public bool window, invulnerable;
        public Vector3 position;
    }

    private DodgeMotion EditMotion => _definition.Motion((DodgePhase)Mathf.Clamp(_phase, 0, 3), _backwards);
    private List<DodgeScreenCue> CueList => _counterCue ? _definition.counterEffects : _definition.dodgeEffects;
    private DodgeScreenCue Cue => _definition != null && _cueIndex >= 0 && _cueIndex < CueList.Count ? CueList[_cueIndex] : null;
    public bool PreviewPlaying => _playing;
    public float PreviewCursor => _focusClip ? _poseTime : _time;
    public float PreviewSourceTime => _preview != null ? _preview.SampledTime : 0;
    public Animator PreviewAnimator => _preview?.ModelAnimator;
    public int PreviewParticleCount => _preview?.Afterimage?.Particles ?? 0;
    public int PreviewAfterimageCount => _preview?.Afterimage?.Ghosts ?? 0;
    public bool PreviewVfxCatchingUp => _preview?.Afterimage?.CatchingUp == true;

    [MenuItem("Tools/Character/Player/Dodge Studio")]
    public static void Open()
    {
        var window = GetWindow<PlayerDodgeStudio>("회피 조정");
        window.FindTargets();
        window.PlayPreview(0);
        window.Show();
    }

    public static void OpenDefinition(PlayerDodgeDefinition definition)
    {
        var window = GetWindow<PlayerDodgeStudio>("회피 조정");
        window._definition = definition;
        window.CaptureBaseline();
        window.PlayPreview(0);
        window.Show();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("회피 조정");
        minSize = new Vector2(1020, 720);
        _preview = new DodgeStudioPreview();
        _dirty = true;
        _capturePath = null;
        _previewError = null;
        _dragControl = 0;
        _resetGuiState = true;
        if (_uiVersion < 2)
        {
            _previewMode = 0;
            _tab = _phase = 0;
            _showTarget = _showSetup = _showAdvanced = _showRecording = false;
            _inPlace = true;
            _previewSpeed = 1;
            _uiVersion = 2;
        }
        _focusClip = _previewMode == 0;
        _playing = true;
        _lastUpdate = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Undo.undoRedoPerformed += Changed;
        EditorApplication.playModeStateChanged += ModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += ClearPreview;
        FindTargets();
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        Undo.undoRedoPerformed -= Changed;
        EditorApplication.playModeStateChanged -= ModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= ClearPreview;
        ClearPreview();
    }

    private void Changed() { _dirty = true; Repaint(); }
    private void ModeChanged(PlayModeStateChange state)
    {
        ClearPreview();
        _player = null;
        _previewError = null;
        _dirty = true;
        _lastUpdate = EditorApplication.timeSinceStartup;
        Repaint();
    }
    private void ClearPreview() { _preview?.Dispose(); }

    private void FindTargets()
    {
        if (_player == null || EditorUtility.IsPersistent(_player))
        {
            var actor = UnityEngine.Object.FindAnyObjectByType<PlayerCore>();
            if (actor != null && actor != _player)
            { _player = actor; ClearPreview(); _dirty = true; }
        }
        if (_player == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerDodgeSetup.PlayerPath);
            if (prefab != null) _player = prefab.GetComponent<PlayerCore>();
        }
        if (_definition == null && _player != null) _definition = _player.DodgeDefinition;
        if (_definition == null) _definition = AssetDatabase.LoadAssetAtPath<PlayerDodgeDefinition>(PlayerDodgeSetup.DefinitionPath);
        CaptureBaseline();
    }

    private void CaptureBaseline()
    {
        if (_baselineAsset == _definition && !string.IsNullOrEmpty(_baseline)) return;
        _baselineAsset = _definition;
        _baseline = _definition != null ? EditorJsonUtility.ToJson(_definition) : null;
    }

    public void PlayPreview(int mode)
    {
        if (_previewMode != Mathf.Clamp(mode, 0, 2)) _resetGuiState = true;
        _previewMode = Mathf.Clamp(mode, 0, 2);
        _focusClip = _previewMode == 0;
        _showRecording = false;
        _time = _poseTime = 0;
        _preview?.Afterimage?.Reset();
        _playing = true;
        _dirty = true;
        _lastUpdate = EditorApplication.timeSinceStartup;
        Repaint();
    }

    public void PausePreview() { _playing = false; Repaint(); }

    public void InspectPose(int phase, float sourceTime)
    {
        _phase = Mathf.Clamp(phase, 0, 3);
        _previewMode = 0;
        _focusClip = true;
        _poseTime = Mathf.Clamp(sourceTime, 0, EditMotion.End);
        _playing = false;
        _posePicked = true;
        _dirty = true;
        Repaint();
    }

    public void PreparePreview()
    {
        FindTargets();
        if (_definition == null || _player == null || _player.Animator == null) return;
        if (!_preview.Ready) _preview.Build(_player);
        if (_dirty) Rebuild();
        ApplyPose();
    }

    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        double elapsed = Math.Max(0, now - _lastUpdate);
        _lastUpdate = now;
        if (_preview?.Afterimage?.CatchingUp == true)
        { EditorApplication.QueuePlayerLoopUpdate(); Repaint(); }
        if (!_playing || _definition == null || _showRecording) return;
        // Do not cap every editor update to 0.1s: slow/occluded editor windows
        // then played far more slowly than their displayed observation speed.
        if (_focusClip)
        {
            float end = Mathf.Max(.001f, EditMotion.End);
            _poseTime += (float)elapsed * _previewSpeed * Mathf.Max(.01f, EditMotion.speed);
            if (_poseTime > end)
            {
                if (_loop) _poseTime %= end;
                else { _poseTime = end; _playing = false; }
            }
        }
        else
        {
            _time += (float)elapsed * _previewSpeed;
            if (_time > _duration)
            {
                if (_loop) { _time %= Mathf.Max(.001f, _duration); _preview?.Afterimage?.Reset(); }
                else { _time = _duration; _playing = false; }
            }
            if (!_dirty) Sample();
        }
        // The cloned skeleton is updated on the playback clock, independently of
        // how often Unity chooses to repaint the editor window.
        if (!_dirty && _preview != null && _preview.Ready) ApplyPose();
        Repaint();
    }

    private void ApplyPose()
    {
        if (_preview == null || !_preview.Ready || _definition == null) return;
        var motion = _focusClip ? EditMotion : _sample?.Motion;
        if (motion == null || motion.clip == null) return;
        Vector3 position = _focusClip ? (_inPlace ? Vector3.zero : _preview.Root(motion, _poseTime, _player)) : _samplePosition;
        _preview.SamplePose(motion.clip, _focusClip ? _poseTime : _sample.SourceTime, position);
    }

    private void OnGUI()
    {
        ResetGuiState();
        if (_preview == null) _preview = new DodgeStudioPreview();
        if (_player == null || EditorUtility.IsPersistent(_player) || _definition == null) FindTargets();
        DrawHeader();
        if (_showSetup) DrawSetup();
        if (_definition == null)
        {
            EditorGUILayout.HelpBox("회피 설정을 찾지 못했습니다. Player가 있는 씬에서 아래 버튼으로 연결하세요.", MessageType.Info);
            if (GUILayout.Button("기존 회피 설정 가져오기", GUILayout.Height(32))) EditorApplication.delayCall += PlayerDodgeSetup.SetUp;
            return;
        }
        if (_showRecording) { DrawRecording(); return; }

        int view = GUILayout.Toolbar(_previewMode, new[] { "① 동작만 보기", "② 슬로우 · 화면 연출", "③ 회피 → 반격" }, GUILayout.Height(32));
        if (view != _previewMode) PlayPreview(view);
        ResetGuiState();
        if (_dirty)
        {
            try
            {
                if (!_preview.Ready && _player != null && _player.Animator != null) _preview.Build(_player);
                Rebuild();
            }
            catch (Exception exception) { _previewError = exception.Message; _dirty = false; }
        }
        float height = Mathf.Clamp(position.height - (_focusClip ? 350 : 390) - (_showSetup ? 100 : 0), 230, 580);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                DrawViewOptions();
                Rect viewport = GUILayoutUtility.GetRect(10, height, GUILayout.ExpandWidth(true));
                DrawPreview(viewport);
                DrawTransport();
            }
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Clamp(position.width * .33f, 350, 400))))
            {
                int tab = GUILayout.Toolbar(_tab, new[] { "슬로우", "화면 효과", "판정·입력", "반격 연결", "종료 시점", "잔상 VFX" },
                    new GUIStyle(EditorStyles.toolbarButton) { fontSize = 11, padding = new RectOffset(3, 3, 2, 2) }, GUILayout.Height(27));
                if (tab != _tab)
                {
                    _tab = tab;
                    _inspectorScroll = Vector2.zero;
                    _showAdvanced = false;
                    if (tab == 2 || tab == 4 || tab == 5) _phase = 0;
                    PlayPreview(tab == 2 || tab == 4 ? 0 : tab == 3 ? 2 : 1);
                    _resetGuiState = true;
                }
                ResetGuiState();
                _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll, GUILayout.Height(height + 72));
                Undo.RecordObject(_definition, "Edit Dodge Settings");
                _posePicked = false;
                EditorGUI.BeginChangeCheck();
                if (_tab == 0) DrawSlow();
                if (_tab == 1) DrawEffects();
                if (_tab == 2) DrawJudgement();
                if (_tab == 3) DrawCounter();
                if (_tab == 4) DrawPerfectEnd();
                if (_tab == 5) DrawAfterimage();
                bool edited = EditorGUI.EndChangeCheck();
                if (edited)
                {
                    MarkChanged();
                    if (_tab == 5 && _definition.afterimage?.WantsChromatic == true && _focusClip)
                        PlayPreview(1);
                    if (_focusClip && _tab < 2 && !_posePicked)
                        PlayPreview(_tab == 1 && _counterCue ? 2 : 1);
                }
                EditorGUILayout.EndScrollView();
            }
        }
        if (_focusClip) DrawSourceTimeline();
        else DrawTimeline();
        DrawFooter();
        ProcessDrag();
    }

    private void ResetGuiState()
    {
        if (!_resetGuiState) return;
        EditorGUIUtility.editingTextField = false;
        GUIUtility.hotControl = GUIUtility.keyboardControl = 0;
        GUI.FocusControl(null);
        _dragControl = 0;
        _resetGuiState = false;
    }

    private void DrawHeader()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("회피 조정", EditorStyles.boldLabel, GUILayout.Width(90));
            GUILayout.Label(_definition != null && EditorUtility.IsDirty(_definition) ? "변경사항 미저장" : "저장된 설정", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("설정 저장", EditorStyles.toolbarButton, GUILayout.Width(80)) && _definition != null)
            {
                AssetDatabase.SaveAssetIfDirty(_definition);
                _baseline = EditorJsonUtility.ToJson(_definition);
                _message = "저장했습니다. 게임에는 다음 회피부터 적용됩니다.";
            }
            if (GUILayout.Button(new GUIContent("기준값 복원", "창을 열었을 때 또는 마지막으로 저장한 값으로 돌아갑니다. Ctrl+Z로 취소할 수 있습니다."), EditorStyles.toolbarButton, GUILayout.Width(92)) && !string.IsNullOrEmpty(_baseline))
            {
                Undo.RecordObject(_definition, "Restore Dodge Baseline");
                EditorJsonUtility.FromJsonOverwrite(_baseline, _definition);
                MarkChanged();
            }
            _showSetup = GUILayout.Toggle(_showSetup, "모델 · 연결", EditorStyles.toolbarButton, GUILayout.Width(88));
            if (GUILayout.Button(_showRecording ? "미리보기로" : "실전 기록", EditorStyles.toolbarButton, GUILayout.Width(80))) _showRecording = !_showRecording;
        }
        GUILayout.Label("보기 선택 → 재생하며 값 조정 → 설정 저장     ·     숫자와 마커 모두 Ctrl+Z로 되돌릴 수 있습니다.", EditorStyles.miniLabel);
    }

    private void DrawSetup()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUI.BeginChangeCheck();
            _player = (PlayerCore)EditorGUILayout.ObjectField("미리볼 모델", _player, typeof(PlayerCore), true);
            _definition = (PlayerDodgeDefinition)EditorGUILayout.ObjectField("편집할 설정", _definition, typeof(PlayerDodgeDefinition), false);
            if (EditorGUI.EndChangeCheck()) { ClearPreview(); _previewError = null; CaptureBaseline(); _dirty = true; }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("모델 다시 찾기")) { _player = null; FindTargets(); ClearPreview(); _previewError = null; _dirty = true; }
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button("기본 설정 연결")) EditorApplication.delayCall += PlayerDodgeSetup.SetUp;
            }
            if (_player != null && _definition != null && _player.DodgeDefinition != _definition)
                EditorGUILayout.HelpBox("현재 플레이어에 연결된 설정과 다릅니다. 게임에 적용하려면 PlayerCore의 Dodge Definition에 이 설정을 연결하세요.", MessageType.Warning);
        }
    }

    private void DrawViewOptions()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            _backwards = GUILayout.Toolbar(_backwards ? 1 : 0, new[] { "전방 회피", "후방 회피" }, GUILayout.Width(180)) == 1;
            if (EditorGUI.EndChangeCheck()) PlayPreview(_previewMode);
            if (_focusClip)
            {
                int phase = EditorGUILayout.Popup(_phase, PhaseNames);
                if (phase != _phase) { _phase = phase; PlayPreview(0); }
                _inPlace = GUILayout.Toggle(_inPlace, "제자리", EditorStyles.miniButton, GUILayout.Width(60));
            }
            else if (_previewMode == 2)
            {
                GUILayout.Label("반격 입력", GUILayout.Width(58));
                EditorGUI.BeginChangeCheck();
                _counterPosition = GUILayout.HorizontalSlider(_counterPosition, .05f, .95f, GUILayout.MinWidth(65));
                if (EditorGUI.EndChangeCheck()) { _manualCounter = false; _dirty = true; }
                GUILayout.Label(_counterPosition < .34f ? "빠르게" : _counterPosition < .67f ? "중간" : "늦게", GUILayout.Width(45));
            }
            if (GUILayout.Button(new GUIContent("시점 초기화", "우클릭 드래그: 회전 / 휠: 확대"), EditorStyles.miniButton, GUILayout.Width(88)))
            { _preview.yaw = 150; _preview.pitch = 12; _preview.distance = 4.8f; }
        }
        GUILayout.Label(_focusClip ? "슬로우 없이 애니메이션을 반복합니다. 원하는 자세에서 일시정지하거나 아래 프레임을 옮기세요." :
            _previewMode == 1 ? "완벽 회피 성공을 가정해 슬로우와 화면 효과를 함께 재생합니다." :
            "반격 입력 시점을 바꿔 슬로우 해제와 화면 효과가 어떻게 이어지는지 확인하세요.", EditorStyles.miniLabel);
    }

    private void DrawPreview(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(.075f, .09f, .12f));
        if (_player == null || _player.Animator == null)
        {
            GUI.Label(rect, "모델을 준비하지 못했습니다.\n위의 ‘모델 · 연결 → 모델 다시 찾기’를 눌러주세요.", CenterStyle());
            return;
        }
        try
        {
            if (!_preview.Ready) _preview.Build(_player);
            if (_sample == null) return;
            _preview.SetThreat(_showTarget, !_focusClip && _time >= _threatStart && _time <= _threatEnd, _testDistance, _testRange);
            var motion = _focusClip ? EditMotion : _sample.Motion;
            Vector3 origin = _focusClip ? (_inPlace ? Vector3.zero : _preview.Root(motion, _poseTime, _player)) : _samplePosition;
            if (Event.current.type == EventType.Repaint)
            {
                bool showVfx = !_focusClip && (_definition.afterimage?.WantsChromatic == true || (_tab == 5 && _forceAfterimagePreview));
                _preview.Afterimage?.Update(_definition.afterimage, showVfx, _time, AfterimageEventTime(), SampleAfterimageFrame, _playing);
            }
            _preview.Draw(rect, _sample, origin, _focusClip ? motion : null, _poseTime);
            _previewError = null;
            string description = _focusClip ? PhaseNames[_phase] + " · " + Mathf.RoundToInt(_poseTime * motion.FrameRate) + " / " + Mathf.RoundToInt(motion.End * motion.FrameRate) + " 프레임" :
                (_sample.CounterActive ? "회피 반격" : _sample.PerfectActive ? "완벽 회피" : "연출 마무리") +
                "    전투 속도 " + (_sample.SlowScale * 100).ToString("0") + "%    반격 " + (_sample.WindowOpen ? "입력 가능" : "입력 닫힘");
            GUI.Label(new Rect(rect.x + 12, rect.y + 10, rect.width - 24, 22), (_playing ? "▶ 재생 중   " : "Ⅱ 일시정지   ") + description, EditorStyles.whiteLabel);
            if (_preview.Afterimage?.Enabled == true)
                GUI.Label(new Rect(rect.x + 12, rect.y + 34, rect.width - 24, 22),
                    (_preview.Afterimage.CatchingUp ? "입자 시뮬레이션 준비 중 · " : "") +
                    "포즈 잔상 " + PreviewAfterimageCount + "  ·  GPU 입자 " + PreviewParticleCount, EditorStyles.whiteLabel);
            if (!string.IsNullOrEmpty(_capturePath) && Event.current.type == EventType.Repaint)
            { _preview.SaveLastFrame(_capturePath); _capturePath = null; }
        }
        catch (Exception exception)
        {
            _previewError = exception.Message;
            GUI.Label(rect, "미리보기 준비 실패\n" + _previewError + "\n모델 · 연결에서 다시 찾기를 눌러주세요.", CenterStyle());
        }
        var evt = Event.current;
        if (rect.Contains(evt.mousePosition) && evt.type == EventType.ScrollWheel)
        { _preview.distance = Mathf.Clamp(_preview.distance + evt.delta.y * .15f, 2.5f, 20); evt.Use(); Repaint(); }
        if (rect.Contains(evt.mousePosition) && evt.type == EventType.MouseDrag && evt.button == 1)
        { _preview.yaw += evt.delta.x * .5f; _preview.pitch = Mathf.Clamp(_preview.pitch + evt.delta.y * .3f, -10, 75); evt.Use(); Repaint(); }
    }

    public void CapturePreview(string path) { _capturePath = path; Repaint(); }

    private void DrawTransport()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(_playing ? "Ⅱ 일시정지" : "▶ 재생", GUILayout.Height(28), GUILayout.Width(106)))
            {
                if (!_playing && PreviewCursor >= (_focusClip ? EditMotion.End : _duration) - .0001f) { _time = _poseTime = 0; }
                _playing = !_playing;
                _lastUpdate = EditorApplication.timeSinceStartup;
            }
            if (GUILayout.Button("↻ 처음부터", GUILayout.Height(28), GUILayout.Width(95))) PlayPreview(_previewMode);
            if (GUILayout.Button("−1 프레임", GUILayout.Height(28), GUILayout.Width(82))) StepFrame(-1);
            if (GUILayout.Button("+1 프레임", GUILayout.Height(28), GUILayout.Width(82))) StepFrame(1);
            GUILayout.FlexibleSpace();
            _loop = GUILayout.Toggle(_loop, "반복", GUILayout.Width(48));
            GUILayout.Label("관찰 속도", GUILayout.Width(62));
            float[] speeds = { .25f, .5f, 1, 2 };
            int current = Array.FindIndex(speeds, s => Mathf.Abs(s - _previewSpeed) < .01f);
            int selected = EditorGUILayout.Popup(current < 0 ? 2 : current, new[] { "0.25배", "0.5배", "1배", "2배" }, GUILayout.Width(66));
            if (selected != current && selected >= 0) _previewSpeed = speeds[selected];
        }
        EditorGUI.BeginChangeCheck();
        float cursor = EditorGUILayout.Slider(_focusClip ? "애니메이션 위치" : "연출 재생 위치", PreviewCursor, 0, _focusClip ? EditMotion.End : _duration);
        if (EditorGUI.EndChangeCheck()) Scrub(cursor);
    }

    private void DrawSlow()
    {
        Heading("얼마나 느리게, 언제까지?");
        var slow = _definition.slow;
        slow.StartDelay = Seconds("시작까지 기다림", slow.StartDelay, 1, "완벽 회피가 성공한 뒤 슬로우를 시작하기까지의 실제 시간");
        slow.SlowScale = EditorGUILayout.Slider(new GUIContent("전투 속도 (%)", "100%는 정상 속도입니다. 작을수록 더 느려집니다."), slow.SlowScale * 100, 1, 100) / 100;
        GUILayout.Label((slow.SlowScale * 100).ToString("0") + "% 속도 · " + (1 / Mathf.Max(.01f, slow.SlowScale)).ToString("0.0") + "배 느리게", EditorStyles.miniLabel);
        slow.MaxDuration = Seconds("슬로우 최대 길이", slow.MaxDuration, 5, "느려지는 시간을 포함한 최대 길이. 회피 종료나 반격이 먼저 오면 일찍 끝날 수 있습니다.", .01f);
        slow.FadeOutDuration = Seconds("정상 속도로 돌아오기", slow.FadeOutDuration, 1, "반격하지 않고 끝났을 때의 복귀 시간");
        DrawPerfectEndWarning();
        if (GUILayout.Button("완벽 회피 종료 시점 조정")) OpenPerfectEnd();
        PreviewButton("이 설정으로 회피 재생", 1);
        EditorGUILayout.HelpBox("반격했을 때의 해제 방식은 ‘반격 연결’에서 따로 조절합니다.", MessageType.None);
        _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "고급 · 진입 곡선과 복귀 조건", true);
        if (!_showAdvanced) return;
        slow.FadeInDuration = Seconds("감속에 걸리는 시간", slow.FadeInDuration, 1,
            "100%에서 설정한 전투 속도까지 내려가는 데 걸리는 실제 시간입니다. 0초면 즉시 감속합니다.");
        GUILayout.Label("100% → " + (slow.SlowScale * 100).ToString("0") + "%까지 " + slow.FadeInDuration.ToString("0.00") + "초에 걸쳐 감속", EditorStyles.wordWrappedMiniLabel);
        slow.FadeInCurve = EditorGUILayout.CurveField("느려지는 곡선", slow.FadeInCurve);
        slow.FadeOutCurve = EditorGUILayout.CurveField("돌아오는 곡선", slow.FadeOutCurve);
        _definition.releaseSlowWithWindow = EditorGUILayout.Toggle("반격 입력 종료와 함께 복귀", _definition.releaseSlowWithWindow);
    }

    private void OpenPerfectEnd()
    {
        _tab = 4;
        _inspectorScroll = Vector2.zero;
        var motion = _backwards ? _definition.back : _definition.front;
        InspectPose(0, Mathf.Max(0, motion.perfectEnd));
    }

    private void DrawPerfectEnd()
    {
        Heading("완벽 회피 종료");
        var motion = _backwards ? _definition.back : _definition.front;
        GUILayout.Label((_backwards ? "후방" : "전방") + " 회피의 종료 시점을 편집합니다.", EditorStyles.wordWrappedMiniLabel);
        GUILayout.Label("위에서 전방 / 후방을 선택하면 각각의 종료 프레임을 지정할 수 있습니다.", EditorStyles.wordWrappedMiniLabel);
        bool wasEnabled = _definition.closeAtAnimationMarker;
        _definition.closeAtAnimationMarker = EditorGUILayout.Toggle("지정한 자세에서 종료", wasEnabled);
        if (!wasEnabled && _definition.closeAtAnimationMarker && motion.perfectEnd < 0)
            motion.perfectEnd = motion.End;
        using (new EditorGUI.DisabledScope(!_definition.closeAtAnimationMarker))
        {
            EditorGUI.BeginChangeCheck();
            float end = FrameField("종료 프레임", Mathf.Max(0, motion.perfectEnd), motion, 0);
            if (EditorGUI.EndChangeCheck()) motion.perfectEnd = end;
            float source = _focusClip ? (_phase == 0 ? _poseTime : -1) :
                _sample != null && _sample.Phase == DodgePhase.Dodge ? _sample.SourceTime : -1;
            using (new EditorGUI.DisabledScope(source < 0))
                if (GUILayout.Button("현재 보이는 자세를 종료 시점으로", GUILayout.Height(28)))
                    SetPerfectEnd(source);
        }
        EditorGUILayout.HelpBox("원본 애니메이션 프레임을 기준으로 지정합니다. 아래 보라색 ‘완벽 회피 종료’ 마커도 드래그할 수 있습니다.\n이 자세에 도달하면 완벽 회피와 반격 입력이 종료됩니다.", MessageType.None);
        GUILayout.Label("종료 자세 사용 여부는 전방·후방에 함께 적용됩니다. 반격 입력 시간 만료나 행동 중단이 먼저 오면 일찍 끝납니다.", EditorStyles.wordWrappedMiniLabel);
        DrawPerfectEndWarning();
        PreviewButton("종료 시점까지 슬로우 확인", 1);
    }

    private void SetPerfectEnd(float source)
    {
        var motion = _backwards ? _definition.back : _definition.front;
        motion.perfectEnd = Mathf.Clamp(Mathf.Round(source * motion.FrameRate) / motion.FrameRate, 0, motion.End);
        _definition.closeAtAnimationMarker = true;
        InspectPose(0, motion.perfectEnd);
        MarkChanged();
    }

    private void DrawPerfectEndWarning()
    {
        var motion = _backwards ? _definition.back : _definition.front;
        if (!_definition.closeAtAnimationMarker || motion.perfectEnd < 0) return;
        float available = (Mathf.Min(motion.perfectEnd, motion.End) - motion.judgeStart) / Mathf.Max(.01f, motion.speed);
        if (available <= 0)
            EditorGUILayout.HelpBox("종료 시점이 성공 판정과 같거나 더 빠릅니다. 종료 프레임을 판정 뒤로 옮기세요.", MessageType.Warning);
        else if (_definition.slow.StartDelay >= available)
            EditorGUILayout.HelpBox("슬로우가 시작되기 전에 완벽 회피가 끝납니다. 종료 프레임을 뒤로 옮기거나 ‘시작까지 기다림’을 줄이세요.", MessageType.Warning);
    }

    private void DrawAfterimage()
    {
        Heading("플레이어 잔상을 어떻게 남길까요?");
        var settings = _definition.afterimage ?? (_definition.afterimage = new DodgeAfterimageSettings());
        var previous = settings.mode;
        settings.mode = (DodgeAfterimageMode)EditorGUILayout.Popup("게임에 적용할 잔상", (int)settings.mode,
            new[] { "기존 잔상", "컬러 디더 VFX", "기존 + 컬러 디더 VFX", "잔상 끄기" });
        if (previous != settings.mode && settings.WantsChromatic && settings.prefab == null)
            settings.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectR.VFX.Editor.ChromaticAfterimageBuilder.PrefabPath);
        if (previous != settings.mode) _forceAfterimagePreview = false;
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("제작한 VFX 연결 · 적용"))
            {
                settings.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectR.VFX.Editor.ChromaticAfterimageBuilder.PrefabPath);
                settings.mode = DodgeAfterimageMode.Chromatic;
                MarkChanged();
            }
            if (GUILayout.Button("이 창에서 입자 VFX 재생")) PreviewAfterimage();
        }
        if (settings.mode == DodgeAfterimageMode.Legacy)
            EditorGUILayout.HelpBox("현재 게임의 기존 잔상을 유지합니다. ‘컬러 디더 VFX’를 선택하면 제작한 새 잔상과 푸른 입자를 사용합니다.", MessageType.None);
        if (settings.mode == DodgeAfterimageMode.Off)
            EditorGUILayout.HelpBox("회피의 기존 잔상과 새 잔상을 모두 끕니다. E 스킬에는 영향을 주지 않습니다.", MessageType.None);
        using (new EditorGUI.DisabledScope(!settings.WantsChromatic))
        {
            settings.prefab = (GameObject)EditorGUILayout.ObjectField("VFX 프리팹", settings.prefab, typeof(GameObject), false);
            settings.start = (DodgeAfterimageStart)EditorGUILayout.Popup("새 VFX 시작", (int)settings.start,
                new[] { "회피 시작 · 일반 회피 포함", "완벽 회피 성공", "슬로우 시작" });
            settings.emissionDuration = Mathf.Max(.01f, Seconds("잔상을 생성하는 시간", settings.emissionDuration, 2));
            settings.snapshotInterval = Mathf.Max(.02f, Seconds("포즈 생성 간격", settings.snapshotInterval, .3f));
            settings.snapshotLifetime = Mathf.Max(.1f, Seconds("각 잔상의 수명", settings.snapshotLifetime, 2));
            settings.maxSnapshots = EditorGUILayout.IntSlider("최대 포즈 수", settings.maxSnapshots, 2, 24);
            settings.opacity = EditorGUILayout.Slider("잔상 불투명도", settings.opacity, 0, 1);
            settings.noiseScale = EditorGUILayout.Slider("노이즈 세밀함", settings.noiseScale, 1, 60);
            settings.includeRigidMeshes = EditorGUILayout.Toggle("무기도 잔상에 포함", settings.includeRigidMeshes);
            GUILayout.Space(6);
            GUILayout.Label("주변으로 흩날리는 푸른 입자", EditorStyles.boldLabel);
            settings.fragmentsPerSecond = EditorGUILayout.Slider("작은 입자 수 / 초", settings.fragmentsPerSecond, 0, 1500);
            settings.glintsPerSecond = EditorGUILayout.Slider("빛점 수 / 초", settings.glintsPerSecond, 0, 220);
            settings.edgeColor = EditorGUILayout.ColorField(new GUIContent("소멸 경계 색"), settings.edgeColor, true, false, true);
            settings.edgeStrength = EditorGUILayout.Slider("경계 빛 강도", settings.edgeStrength, 0, 2);
            settings.useRealTime = EditorGUILayout.Popup("연출 시간 기준", settings.useRealTime ? 1 : 0,
                new[] { "전투 속도 · 히트스톱 따름", "실제 시간 · 히트스톱 따름" }) == 1;
            settings.stopOnDodgeExit = EditorGUILayout.Toggle("회피 동작 종료 시 생성 중단", settings.stopOnDodgeExit);
            if (settings.WantsChromatic && !settings.HasValidPrefab)
                EditorGUILayout.HelpBox("잔상 머티리얼과 VFX Graph가 연결된 프리팹이 필요합니다. 연결 전에는 게임에서 기존 잔상을 사용합니다.", MessageType.Warning);
            else if (settings.mode == DodgeAfterimageMode.Both)
                EditorGUILayout.HelpBox("기존 잔상은 원래대로 슬로우 시작 시 재생하고, 새 VFX는 위에서 선택한 시점에 추가합니다.", MessageType.None);
        }
        GUILayout.Space(8);
        EditorGUILayout.HelpBox("이 창의 재생·일시정지·시간축으로 잔상과 실제 GPU 입자를 함께 확인합니다. 몸체와 무기의 원래 색을 유지하며, 게임 적용값은 설정 저장으로 보관합니다.", MessageType.None);
        if (_forceAfterimagePreview && !settings.WantsChromatic)
            EditorGUILayout.HelpBox("새 VFX를 미리보기에서만 표시하고 있습니다. 게임 적용 방식은 위의 선택을 유지합니다.", MessageType.None);
    }

    public void PreviewAfterimage()
    {
        _tab = 5;
        _phase = 0;
        _forceAfterimagePreview = true;
        if (_preview != null) { _preview.distance = Mathf.Max(5.5f, _preview.distance); _preview.follow = true; }
        PlayPreview(1);
    }

    private float AfterimageEventTime()
    {
        var settings = _definition.afterimage;
        if (settings == null || settings.start == DodgeAfterimageStart.DodgeStart) return 0;
        string name = settings.start == DodgeAfterimageStart.PerfectSuccess ? "완벽 회피 성공" : "슬로우 시작";
        int index = _events.FindIndex(e => e.name == name);
        return index >= 0 ? _events[index].time : -1;
    }

    private DodgeAfterimageFrame SampleAfterimageFrame(float time)
    {
        if (_frames.Count == 0)
            return new DodgeAfterimageFrame(_definition.Motion(DodgePhase.Dodge, _backwards).clip, 0, 1, Vector3.zero, true);
        float cursor = time * 60;
        int first = Mathf.Clamp(Mathf.FloorToInt(cursor), 0, _frames.Count - 1);
        int next = Mathf.Min(first + 1, _frames.Count - 1);
        var a = _frames[first]; var b = _frames[next];
        float blend = Mathf.Clamp01(cursor - Mathf.Floor(cursor));
        float source = a.phase == b.phase ? Mathf.Lerp(a.source, b.source, blend) : a.source;
        return new DodgeAfterimageFrame(_definition.Motion(a.phase, _backwards).clip, source,
            Mathf.Lerp(a.scale, b.scale, blend), Vector3.Lerp(a.position, b.position, blend), a.phase == DodgePhase.Dodge);
    }

    public void OpenAfterimagePreview()
    {
        if (_definition == null || Application.isPlaying) return;
        var settings = _definition.afterimage ?? new DodgeAfterimageSettings();
        var prefab = settings.prefab != null ? settings.prefab : AssetDatabase.LoadAssetAtPath<GameObject>(ProjectR.VFX.Editor.ChromaticAfterimageBuilder.PrefabPath);
        var source = prefab != null ? prefab.GetComponent<ProjectR.VFX.ChromaticAfterimageTrail>() : null;
        if (source == null || source.afterimageMaterial == null || source.particles == null || source.particles.visualEffectAsset == null)
        { _message = "미리볼 VFX 프리팹을 먼저 연결하세요."; return; }
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(ProjectR.VFX.Editor.ChromaticAfterimageBuilder.ScenePath);
        if (!scene.isLoaded) scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(ProjectR.VFX.Editor.ChromaticAfterimageBuilder.ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
        var preview = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ProjectR.VFX.ChromaticAfterimagePreview>()).FirstOrDefault();
        if (preview == null) return;
        preview.effect.afterimageMaterial = source.afterimageMaterial;
        preview.effect.particles.visualEffectAsset = source.particles.visualEffectAsset;
        settings.ApplyTo(preview.effect);
        preview.emissionDuration = Mathf.Max(.01f, settings.emissionDuration);
        preview.skillMode = false;
        preview.dodgePose = _definition.Motion(DodgePhase.Dodge, _backwards).clip;
        preview.effect.Bind(preview.model.transform);
        preview.Seek(0);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        ProjectR.VFX.Editor.ChromaticAfterimageWindow.Open();
        _message = "VFX 미리보기 창에서 Play preview로 재생하세요. 회피 설정은 저장하지 않고 미리보기 값만 전달했습니다.";
    }

    private void DrawEffects()
    {
        Heading("화면에 어떤 효과를 줄까요?");
        bool counter = GUILayout.Toolbar(_counterCue ? 1 : 0, new[] { "회피 때", "반격 때" }) == 1;
        if (counter != _counterCue) { _counterCue = counter; _cueIndex = 0; _dirty = true; PlayPreview(counter ? 2 : 1); }
        if (!_counterCue && (Cue == null || Cue.kind != DodgeEffectKind.Saturation))
        {
            bool hasDesaturation = _definition.dodgeEffects.Any(c => c != null && c.kind == DodgeEffectKind.Saturation);
            if (GUILayout.Button(hasDesaturation ? "완벽 회피 흑백 · 채도 조정" : "+ 완벽 회피 · 부분 흑백 추가", GUILayout.Height(26)))
                OpenDesaturationPreview();
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            string[] names = CueList.Select(c => (c.enabled ? "● " : "○ ") + c.name).ToArray();
            int selected = names.Length == 0 ? -1 : EditorGUILayout.Popup(_cueIndex, names);
            if (selected != _cueIndex && selected >= 0) { _cueIndex = selected; _dirty = true; }
            if (GUILayout.Button("+ 추가", GUILayout.Width(65))) ShowEffectMenu();
        }
        var cue = Cue;
        if (cue == null)
        {
            EditorGUILayout.HelpBox("‘+ 추가’에서 화면 효과를 선택하세요.", MessageType.Info);
            return;
        }
        cue.enabled = EditorGUILayout.Toggle("이 효과 사용", cue.enabled);
        cue.strength = Strength(cue.kind, cue.strength);
        if (cue.kind == DodgeEffectKind.Saturation)
        {
            GUILayout.Label(cue.strength <= 0
                ? "0% = 원래 색감 · 100% = 완전 흑백\n현재 색감 약 " + Mathf.Clamp(100 + cue.strength, 0, 100).ToString("0") + "% 유지"
                : "현재 채도를 높이는 설정입니다. 고급의 ‘채도 변화’에서 조절할 수 있습니다.", EditorStyles.wordWrappedMiniLabel);
            if (!cue.enabled) EditorGUILayout.HelpBox("이 효과 사용을 켜면 미리보기와 게임에 흑백 연출이 나타납니다.", MessageType.Info);
        }
        var anchors = _counterCue ? new[] { DodgeEffectAnchor.CounterStart, DodgeEffectAnchor.CounterLoop, DodgeEffectAnchor.CounterEnd, DodgeEffectAnchor.AnimationPose } :
            new[] { DodgeEffectAnchor.DodgeStart, DodgeEffectAnchor.PerfectSuccess, DodgeEffectAnchor.SlowStart, DodgeEffectAnchor.AnimationPose };
        var labels = _counterCue ? new[] { "반격이 시작되면", "연속 공격에 들어가면", "마무리 공격에 들어가면", "지정한 애니메이션 자세에서" } :
            new[] { "회피를 시작하면", "완벽 회피가 성공하면", "슬로우가 시작되면", "지정한 애니메이션 자세에서" };
        int anchor = EditorGUILayout.Popup("언제 시작?", Array.IndexOf(anchors, cue.anchor), labels);
        if (anchor >= 0) cue.anchor = anchors[anchor];
        if (cue.anchor == DodgeEffectAnchor.AnimationPose)
        {
            if (_counterCue) cue.posePhase = (DodgePhase)(EditorGUILayout.Popup("기준 동작", Mathf.Clamp((int)cue.posePhase - 1, 0, 2), PhaseNames.Skip(1).ToArray()) + 1);
            else cue.posePhase = DodgePhase.Dodge;
            var motion = _definition.Motion(cue.posePhase, _backwards);
            cue.sourceTime = FrameField("시작 자세", cue.sourceTime, motion, (int)cue.posePhase);
        }
        cue.delay = Seconds("추가로 기다림", cue.delay, 2);
        cue.fadeIn = Seconds("나타나는 시간", cue.fadeIn, 2);
        cue.hold = Seconds("유지하는 시간", cue.hold, 3);
        cue.fadeOut = Seconds("사라지는 시간", cue.fadeOut, 2);
        GUILayout.Label("효과 길이 " + cue.Duration.ToString("0.00") + "초 · 실제 시간 기준 (슬로우와 독립)", EditorStyles.wordWrappedMiniLabel);
        if (!_counterCue)
        {
            cue.onCounter = (DodgeEffectHandoff)EditorGUILayout.Popup("반격하면 이 효과는", (int)cue.onCounter, new[] { "원래 시간대로 계속", "현재 강도에서 종료", "반격의 같은 효과로 연결" });
            if (cue.onCounter == DodgeEffectHandoff.FadeOut) cue.counterFade = Seconds("반격 때 사라지는 시간", cue.counterFade, 1);
        }
        PreviewButton(cue.kind == DodgeEffectKind.Saturation ? "흑백으로 바뀌고 돌아오는 과정 재생" : "화면 효과를 보며 재생", _counterCue ? 2 : 1);
        if (cue.kind == DodgeEffectKind.Saturation && GUILayout.Button("흑백이 가장 강한 순간에서 멈춰 보기"))
            PreviewEffectPeak();
        _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, cue.kind == DodgeEffectKind.Saturation ? "고급 · 채도 변화, 곡선, 이름" : "고급 · 색상, 곡선, 이름", true);
        if (!_showAdvanced) return;
        cue.name = EditorGUILayout.TextField("이름", cue.name);
        if (cue.kind == DodgeEffectKind.Saturation)
            cue.strength = EditorGUILayout.Slider(new GUIContent("채도 변화", "-100은 완전 흑백, 0은 원래 색감, 양수는 더 선명한 색감입니다."), cue.strength, -100, 100);
        if (cue.kind == DodgeEffectKind.Vignette) cue.color = EditorGUILayout.ColorField("가장자리 색상", cue.color);
        cue.inCurve = EditorGUILayout.CurveField("나타나는 곡선", cue.inCurve);
        cue.outCurve = EditorGUILayout.CurveField("사라지는 곡선", cue.outCurve);
        if (GUILayout.Button("선택한 효과 삭제"))
        {
            CueList.Remove(cue);
            _cueIndex = Mathf.Clamp(_cueIndex, 0, CueList.Count - 1);
            MarkChanged();
            GUIUtility.ExitGUI();
        }
    }

    public void OpenDesaturationPreview()
    {
        if (_definition == null) return;
        _tab = 1;
        _counterCue = false;
        _cueIndex = _definition.dodgeEffects.FindIndex(c => c != null && c.kind == DodgeEffectKind.Saturation);
        if (_cueIndex < 0)
        {
            Undo.RecordObject(_definition, "Add Perfect Dodge Desaturation");
            _definition.dodgeEffects.Add(DodgeScreenCue.PerfectDodgeDesaturation());
            _cueIndex = _definition.dodgeEffects.Count - 1;
            MarkChanged();
        }
        _inspectorScroll = Vector2.zero;
        _showAdvanced = false;
        PlayPreview(1);
    }

    private void PreviewEffectPeak()
    {
        PlayPreview(_counterCue ? 2 : 1);
        PreparePreview();
        if (_frames.Count == 0) { PausePreview(); return; }
        var peak = _frames[0];
        foreach (var frame in _frames)
            if (Mathf.Abs(frame.effect) > Mathf.Abs(peak.effect)) peak = frame;
        Scrub(peak.time);
    }

    private void ShowEffectMenu()
    {
        var menu = new GenericMenu();
        for (int i = 0; i < EffectNames.Length; i++)
        {
            int index = i;
            menu.AddItem(new GUIContent(EffectNames[i]), false, () =>
            {
                Undo.RecordObject(_definition, "Add Dodge Screen Effect");
                float[] strengths = { .45f, -65, .7f, .4f, -.2f, -8, 1.5f };
                var cue = (DodgeEffectKind)index == DodgeEffectKind.Saturation ? DodgeScreenCue.PerfectDodgeDesaturation() :
                    new DodgeScreenCue { name = EffectNames[index], kind = (DodgeEffectKind)index, strength = strengths[index] };
                if (_counterCue) { cue.anchor = DodgeEffectAnchor.CounterStart; cue.name = EffectNames[index]; }
                CueList.Add(cue);
                _cueIndex = CueList.Count - 1;
                MarkChanged();
                PlayPreview(_counterCue ? 2 : 1);
            });
        }
        menu.ShowAsContext();
    }

    private static float Strength(DodgeEffectKind kind, float value)
    {
        switch (kind)
        {
            case DodgeEffectKind.Vignette: return EditorGUILayout.Slider("가장자리 진하기 (%)", value * 100, 0, 100) / 100;
            case DodgeEffectKind.ChromaticAberration: return EditorGUILayout.Slider("색 분리 강도 (%)", value * 100, 0, 100) / 100;
            case DodgeEffectKind.Saturation:
                EditorGUI.BeginChangeCheck();
                float monochrome = EditorGUILayout.Slider(new GUIContent("흑백 강도 (%)", "화면 전체의 채도를 낮춥니다. 65%는 색감이 일부 남는 부분 흑백입니다."), Mathf.Clamp(-value, 0, 100), 0, 100);
                // Merely showing this control must preserve existing positive saturation values.
                return EditorGUI.EndChangeCheck() ? -monochrome : value;
            case DodgeEffectKind.Exposure: return EditorGUILayout.Slider("밝기 변화 (EV)", value, -3, 3);
            case DodgeEffectKind.LensDistortion: return EditorGUILayout.Slider("왜곡 강도", value, -1, 1);
            case DodgeEffectKind.FieldOfView: return EditorGUILayout.Slider(new GUIContent("줌 변화 (도)", "음수는 가까이, 양수는 넓게 보입니다."), value, -25, 25);
            default: return EditorGUILayout.Slider("흔들림 각도", value, 0, 8);
        }
    }

    private void DrawJudgement()
    {
        Heading("성공 판정을 어느 자세에 둘까요?");
        GUILayout.Label("① 원하는 자세에서 멈춤 → ② 판정 프레임 지정", EditorStyles.wordWrappedMiniLabel);
        var motion = _backwards ? _definition.back : _definition.front;
        motion.judgeOverWindow = EditorGUILayout.Popup("검사 방식", motion.judgeOverWindow ? 1 : 0, new[] { "한 프레임에서 한 번", "정해진 구간 동안" }) == 1;
        motion.judgeStart = FrameField(motion.judgeOverWindow ? "판정 시작" : "판정 프레임", motion.judgeStart, motion, 0);
        if (motion.judgeOverWindow) motion.judgeEnd = Mathf.Max(motion.judgeStart, FrameField("판정 끝", motion.judgeEnd, motion, 0));
        if (GUILayout.Button("현재 보이는 자세를 판정 시작으로", GUILayout.Height(26)))
        {
            if (_focusClip && _phase == 0) motion.judgeStart = _poseTime;
            else InspectPose(0, motion.judgeStart);
            MarkChanged();
        }
        EditorGUILayout.Space(8);
        Heading("반격 버튼은 언제 받을까요?");
        _definition.counterOpenDelay = Seconds("성공 후 입력 시작", _definition.counterOpenDelay, 2);
        _definition.counterWindowDuration = Seconds("입력 가능한 길이", _definition.counterWindowDuration, 4, null, .01f);
        _definition.invulnerableOnSuccess = EditorGUILayout.Toggle("완벽 회피 성공 시 무적", _definition.invulnerableOnSuccess);
        if (GUILayout.Button("완벽 회피 종료 시점 조정")) OpenPerfectEnd();
        PreviewButton("판정·입력 구간 확인", 1);
        _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "고급 · 무적 시간과 동작 배속", true);
        if (!_showAdvanced) return;
        motion.speed = EditorGUILayout.Slider("회피 동작 배속", motion.speed, .1f, 3);
        _definition.invulnerabilityDelay = Seconds("무적 시작 지연", _definition.invulnerabilityDelay, 2);
        _definition.invulnerabilityDuration = Seconds("무적 최대 시간 (0=자동)", _definition.invulnerabilityDuration, 4);
        EditorGUILayout.HelpBox("게임의 실제 성공 조건은 적의 공격 예고 범위입니다. 미리보기에서는 성공을 가정합니다.", MessageType.None);
    }

    private void DrawCounter()
    {
        Heading("반격하면 슬로우를 어떻게 풀까요?");
        var link = _definition.counter;
        link.slowExit = (DodgeSlowExit)EditorGUILayout.Popup("속도 복귀 방식", (int)link.slowExit,
            new[] { "바로 정상 속도로", "부드럽게 정상 속도로", "지정한 공격 자세까지 유지" });
        if (link.slowExit == DodgeSlowExit.UntilAnimation)
        {
            link.releasePhase = (DodgePhase)(EditorGUILayout.Popup("유지할 동작", Mathf.Clamp((int)link.releasePhase - 1, 0, 2), PhaseNames.Skip(1).ToArray()) + 1);
            var motion = _definition.Motion(link.releasePhase, _backwards);
            link.releaseSourceTime = FrameField("이 자세부터 복귀", link.releaseSourceTime, motion, (int)link.releasePhase);
        }
        if (link.slowExit != DodgeSlowExit.Immediate) link.fadeDuration = Seconds("정상 속도까지 걸리는 시간", link.fadeDuration, 1);
        string[] descriptions = { "반격 버튼을 누르는 순간 회피의 슬로우를 해제합니다.", "현재 속도에서 정한 시간 동안 부드럽게 복귀합니다.", "선택한 공격 자세를 보여준 다음 정상 속도로 복귀합니다." };
        EditorGUILayout.HelpBox(descriptions[(int)link.slowExit], MessageType.None);
        link.invulnerable = EditorGUILayout.Toggle("반격 중 무적 유지", link.invulnerable);
        PreviewButton("반격까지 이어서 재생", 2);
        if (GUILayout.Button("반격까지 이어지는 화면 효과 조정"))
        { _tab = 1; _counterCue = false; PlayPreview(2); }
        _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "고급 · 복귀 곡선과 유지 상한", true);
        if (!_showAdvanced) return;
        link.fadeCurve = EditorGUILayout.CurveField("복귀 곡선", link.fadeCurve);
        if (link.slowExit == DodgeSlowExit.UntilAnimation) link.maxHold = Seconds("유지 상한 (실제 초)", link.maxHold, 5, null, .01f);
        if (GUILayout.Button("기존 반격 타임라인 선택")) SelectCounterTimeline();
    }

    private void Heading(string title)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        EditorGUILayout.Space(3);
    }

    private static float Seconds(string label, float value, float max, string tooltip = null, float min = 0)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            value = EditorGUILayout.Slider(new GUIContent(label, tooltip ?? "실제 시간 기준입니다."), value, min, Mathf.Max(max, value));
            GUILayout.Label("초", GUILayout.Width(17));
        }
        return Mathf.Max(min, value);
    }

    private float FrameField(string label, float value, DodgeMotion motion, int phase)
    {
        EditorGUI.BeginChangeCheck();
        int frame = EditorGUILayout.IntSlider(label + " (f)", Mathf.RoundToInt(value * motion.FrameRate), 0, Mathf.RoundToInt(motion.End * motion.FrameRate));
        if (EditorGUI.EndChangeCheck())
        {
            value = Mathf.Min(motion.End, frame / motion.FrameRate);
            InspectPose(phase, value);
        }
        if (GUILayout.Button("이 프레임 보기 · " + value.ToString("0.000") + "초", EditorStyles.miniButton))
            InspectPose(phase, value);
        return value;
    }

    private void PreviewButton(string label, int mode)
    {
        EditorGUILayout.Space(6);
        if (GUILayout.Button("▶ " + label, GUILayout.Height(29))) PlayPreview(mode);
        EditorGUILayout.Space(4);
    }

    private void DrawSourceTimeline()
    {
        var motion = EditMotion;
        Rect area = GUILayoutUtility.GetRect(10, 106, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(area, new Color(.1f, .115f, .14f));
        _timeline = new Rect(area.x + 106, area.y + 25, area.width - 120, 75);
        GUI.Label(new Rect(area.x + 10, area.y + 8, 95, 40), "애니메이션\n프레임", EditorStyles.miniLabel);
        DrawRuler(area, motion.End, true, motion.FrameRate);
        EditorGUI.DrawRect(new Rect(_timeline.x, area.y + 45, _timeline.width, 18), new Color(.2f, .39f, .49f));
        GUI.Label(new Rect(_timeline.x + 5, area.y + 45, _timeline.width, 19), PhaseNames[_phase] + " · 눈금을 누르거나 빨간 재생선을 옮겨 자세 확인", EditorStyles.whiteMiniLabel);
        if (_phase == 0)
        {
            SourceHandle(motion.judgeStart, area.y + 72, Gold, "성공 판정", value => motion.judgeStart = value, motion);
            if (motion.judgeOverWindow) SourceHandle(motion.judgeEnd, area.y + 72, Gold, "판정 끝", value => motion.judgeEnd = Mathf.Max(value, motion.judgeStart), motion);
            if (_definition.closeAtAnimationMarker && motion.perfectEnd >= 0)
                SourceHandle(motion.perfectEnd, area.y + 72, Violet, "완벽 회피 종료", value => motion.perfectEnd = value, motion);
        }
        float x = X(_poseTime, motion.End);
        EditorGUI.DrawRect(new Rect(x, area.y + 22, 2, 78), new Color(1, .35f, .3f));
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && _timeline.Contains(Event.current.mousePosition))
        {
            float span = motion.End;
            BeginDrag(mouse => Scrub(Mathf.Round(T(mouse, span) * motion.FrameRate) / motion.FrameRate), false);
            _dragSetter(Event.current.mousePosition.x);
            Event.current.Use();
        }
    }

    private void DrawTimeline()
    {
        Rect area = GUILayoutUtility.GetRect(10, 148, GUILayout.ExpandWidth(true));
        _timeline = new Rect(area.x + 106, area.y + 23, area.width - 120, 120);
        EditorGUI.DrawRect(area, new Color(.1f, .115f, .14f));
        DrawRuler(area, _duration, false, 60);
        string[] labels = { "동작", "슬로우", "반격 가능", "선택 화면 효과" };
        for (int i = 0; i < labels.Length; i++) GUI.Label(new Rect(area.x + 10, area.y + 26 + i * 28, 94, 24), labels[i], EditorStyles.miniLabel);
        if (_frames.Count > 1)
        {
            DrawRuns(f => (int)f.phase, (a, b, phase) =>
            { if (phase < 4) Bar(a, b, area.y + 27, phase == 0 ? new Color(.2f, .4f, .5f) : new Color(.4f, .3f, .52f), PhaseNames[phase]); });
            DrawCurve(area.y + 55, f => 1 - f.scale, Cyan);
            DrawRuns(f => f.window ? 1 : 0, (a, b, v) => { if (v == 1) Bar(a, b, area.y + 83, Gold, "이때 공격 입력"); });
            var cue = Cue;
            if (cue != null)
            {
                float start = _events.Where(e => e.name == "화면 · " + cue.name).Select(e => e.time).DefaultIfEmpty(-1).First();
                if (start >= 0)
                {
                    Bar(start, start + cue.Duration, area.y + 111, Violet, cue.name);
                    float anchor = start - cue.delay;
                    RealHandle(start, area.y + 111, Violet, value => cue.delay = Mathf.Max(0, value - anchor));
                    RealHandle(start + cue.Duration, area.y + 111, Violet, value => cue.hold = Mathf.Max(0, value - start - cue.fadeIn - cue.fadeOut));
                }
            }
            float success = _events.Where(e => e.name == "완벽 회피 성공").Select(e => e.time).DefaultIfEmpty(-1).First();
            if (success >= 0)
            {
                RealHandle(success + _definition.slow.StartDelay, area.y + 55, Cyan, value => _definition.slow.StartDelay = Mathf.Max(0, value - success));
                RealHandle(success + _definition.counterOpenDelay, area.y + 83, Gold, value => _definition.counterOpenDelay = Mathf.Max(0, value - success));
            }
            if (_previewMode == 2)
            {
                float x = X(_counterAt);
                EditorGUI.DrawRect(new Rect(x, area.y + 20, 1, 120), Gold);
                GUI.Label(new Rect(x + 5, area.y + 23, 100, 19), "반격 입력", EditorStyles.miniLabel);
            }
        }
        float head = X(_time);
        EditorGUI.DrawRect(new Rect(head, area.y + 21, 2, 121), new Color(1, .35f, .3f));
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && _timeline.Contains(Event.current.mousePosition))
        {
            BeginDrag(mouse => Scrub(T(mouse, _duration)), false);
            _dragSetter(Event.current.mousePosition.x);
            Event.current.Use();
        }
    }

    private void DrawRuler(Rect area, float span, bool frames, float rate)
    {
        float interval = frames ? Mathf.Max(1, Mathf.Ceil(span * rate / 10)) / rate : span > 10 ? 2 : span > 4 ? 1 : .25f;
        for (float t = 0; t <= span + .0001f; t += interval)
        {
            float x = X(t, span);
            GUI.Label(new Rect(x + 2, area.y + 3, 70, 20), frames ? Mathf.RoundToInt(t * rate) + "f" : t.ToString("0.00") + "초", EditorStyles.miniLabel);
            EditorGUI.DrawRect(new Rect(x, area.y + 23, 1, area.height - 25), new Color(.2f, .22f, .26f));
        }
    }

    private void SourceHandle(float value, float y, Color color, string label, Action<float> setter, DodgeMotion motion)
    {
        float x = X(value, motion.End);
        var hit = new Rect(x - 7, y, 14, 25);
        EditorGUI.DrawRect(new Rect(x - 4, y, 8, 21), color);
        float labelWidth = EditorStyles.miniLabel.CalcSize(new GUIContent(label)).x + 4;
        float labelX = x + 7 + labelWidth <= _timeline.xMax ? x + 7 : x - 7 - labelWidth;
        GUI.Label(new Rect(labelX, y + 1, labelWidth, 22), new GUIContent(label, "드래그해서 프레임을 옮깁니다. Ctrl+Z로 취소할 수 있습니다."), EditorStyles.miniLabel);
        EditorGUIUtility.AddCursorRect(hit, MouseCursor.SlideArrow);
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hit.Contains(Event.current.mousePosition))
        {
            BeginDrag(mouse =>
            {
                float source = Mathf.Min(motion.End, Mathf.Round(T(mouse, motion.End) * motion.FrameRate) / motion.FrameRate);
                setter(source);
                _poseTime = source;
            }, true);
            Event.current.Use();
        }
    }

    private void RealHandle(float value, float y, Color color, Action<float> setter)
    {
        float x = X(value);
        Rect hit = new Rect(x - 6, y, 12, 22);
        EditorGUI.DrawRect(new Rect(x - 3, y, 6, 21), color);
        EditorGUIUtility.AddCursorRect(hit, MouseCursor.SlideArrow);
        if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hit.Contains(Event.current.mousePosition))
        { BeginDrag(mouse => setter(T(mouse, _duration)), true); Event.current.Use(); }
    }

    private void BeginDrag(Action<float> setter, bool asset)
    {
        _playing = false;
        _dragControl = GUIUtility.GetControlID(FocusType.Passive);
        GUIUtility.hotControl = _dragControl;
        _dragSetter = setter;
        _dragAsset = asset;
        if (asset) { Undo.IncrementCurrentGroup(); _dragUndo = Undo.GetCurrentGroup(); }
    }

    private void ProcessDrag()
    {
        if (_dragControl == 0 || GUIUtility.hotControl != _dragControl) return;
        if (Event.current.type == EventType.MouseDrag)
        {
            if (_dragAsset) Undo.RecordObject(_definition, "Move Dodge Marker");
            _dragSetter?.Invoke(Event.current.mousePosition.x);
            if (_dragAsset) MarkChanged();
            Event.current.Use();
            Repaint();
        }
        if (Event.current.type == EventType.MouseUp)
        {
            if (_dragAsset) Undo.CollapseUndoOperations(_dragUndo);
            GUIUtility.hotControl = 0; _dragControl = 0; _dragSetter = null;
            Event.current.Use();
        }
    }

    private float X(float value, float span = -1) => _timeline.x + Mathf.Clamp01(value / Mathf.Max(.001f, span < 0 ? _duration : span)) * _timeline.width;
    private float T(float x, float span) => Mathf.Clamp01((x - _timeline.x) / _timeline.width) * span;
    private void Bar(float a, float b, float y, Color color, string label = "")
    {
        Rect rect = new Rect(X(a), y, Mathf.Max(1, X(b) - X(a)), 21);
        EditorGUI.DrawRect(rect, color);
        if (rect.width > 45) GUI.Label(new Rect(rect.x + 5, y + 1, rect.width - 5, 20), label, EditorStyles.whiteMiniLabel);
    }
    private void DrawRuns(Func<SimFrame, int> value, Action<float, float, int> draw)
    {
        int current = value(_frames[0]); float start = 0;
        for (int i = 1; i < _frames.Count; i++)
            if (value(_frames[i]) != current) { draw(start, _frames[i].time, current); start = _frames[i].time; current = value(_frames[i]); }
        draw(start, _duration, current);
    }
    private void DrawCurve(float y, Func<SimFrame, float> value, Color color)
    {
        if (Event.current.type != EventType.Repaint) return;
        Handles.BeginGUI(); Handles.color = color;
        for (int i = 1; i < _frames.Count; i++)
            Handles.DrawLine(new Vector3(X(_frames[i - 1].time), y + 22 - Mathf.Clamp01(value(_frames[i - 1])) * 20),
                new Vector3(X(_frames[i].time), y + 22 - Mathf.Clamp01(value(_frames[i])) * 20));
        Handles.EndGUI();
    }

    private DodgeSequence NewSimulation()
    {
        DodgeSequence sequence = null;
        sequence = new DodgeSequence(_definition, _backwards, () =>
        {
            if (!_testThreat) return true;
            float time = sequence?.Time ?? 0;
            Vector3 root = _preview.Root(_backwards ? _definition.back : _definition.front, sequence?.SourceTime ?? 0, _player);
            return time >= _threatStart && time <= _threatEnd && Vector3.ProjectOnPlane(root - Vector3.forward * _testDistance, Vector3.up).sqrMagnitude <= _testRange * _testRange;
        });
        return sequence;
    }

    private void Advance(DodgeSequence sequence, float target, ref bool attempted, bool counter)
    {
        if (counter && !attempted && _counterAt <= sequence.Time + .00001f) { attempted = true; sequence.TryCounter(); }
        while (sequence.Time + .00001f < target)
        {
            float step = Mathf.Min(1f / 120, target - sequence.Time);
            if (counter && !attempted && _counterAt > sequence.Time && _counterAt < sequence.Time + step) step = _counterAt - sequence.Time;
            sequence.Tick(step);
            if (counter && !attempted && sequence.Time + .00001f >= _counterAt) { attempted = true; sequence.TryCounter(); }
        }
    }

    private void Rebuild()
    {
        _preview?.Afterimage?.Reset();
        _dirty = false;
        _previewError = null;
        _frames.Clear(); _events.Clear();
        if (_focusClip)
        {
            _duration = EditMotion.End;
            _poseTime = Mathf.Clamp(_poseTime, 0, EditMotion.End);
            _sample = new DodgeSequence(_definition, _backwards, () => false);
            return;
        }
        _windowStart = _windowEnd = -1;
        // First find the actual input interval, including an earlier animation marker.
        if (_previewMode == 2 && !_manualCounter)
        {
            var natural = NewSimulation();
            for (int i = 0; i < 30 * 120; i++)
            {
                if (natural.WindowOpen) { if (_windowStart < 0) _windowStart = natural.Time; _windowEnd = natural.Time; }
                natural.Tick(1f / 120);
                if (natural.Phase == DodgePhase.Complete && !natural.HasPendingWork) break;
            }
            if (_windowStart >= 0) _counterAt = Mathf.Lerp(_windowStart, _windowEnd, _counterPosition);
        }
        var sequence = NewSimulation();
        bool attempted = false;
        Vector3 offset = Vector3.zero, previousRoot = Vector3.zero;
        DodgePhase previous = sequence.Phase;
        float completedAt = -1;
        _truncated = true;
        for (int i = 0; i <= 30 * 60; i++)
        {
            float target = i / 60f;
            Advance(sequence, target, ref attempted, _previewMode == 2);
            if (sequence.Phase != previous && sequence.Phase != DodgePhase.Complete)
            { offset += previousRoot; previous = sequence.Phase; }
            Vector3 root = _preview.Root(sequence.Motion, sequence.SourceTime, _player);
            _frames.Add(new SimFrame { time = sequence.Time, source = sequence.SourceTime, phase = sequence.Phase, scale = sequence.SlowScale,
                window = sequence.WindowOpen, invulnerable = sequence.Invulnerable, effect = sequence.Effect(Cue != null ? Cue.kind : DodgeEffectKind.Vignette).value, position = offset + root });
            previousRoot = root;
            if (sequence.Phase == DodgePhase.Complete && !sequence.HasPendingWork)
            {
                if (completedAt < 0) completedAt = sequence.Time;
                // Let the compact GPU particles finish their .95 second maximum lifetime.
                float tail = _definition.afterimage != null && (_definition.afterimage.WantsChromatic || _forceAfterimagePreview)
                    ? Mathf.Max(1.05f, _definition.afterimage.snapshotLifetime) : .18f;
                if (sequence.Time >= completedAt + tail) { _truncated = false; break; }
            }
        }
        _duration = Mathf.Max(.25f, _frames[_frames.Count - 1].time);
        _events.AddRange(sequence.Events);
        _time = Mathf.Clamp(_time, 0, _duration);
        Sample();
    }

    private void Sample()
    {
        if (_focusClip || _definition == null) return;
        _sample = NewSimulation();
        bool attempted = false;
        Advance(_sample, _time, ref attempted, _previewMode == 2);
        if (_frames.Count > 0)
        {
            float index = _time * 60;
            int first = Mathf.Clamp(Mathf.FloorToInt(index), 0, _frames.Count - 1);
            int next = Mathf.Min(first + 1, _frames.Count - 1);
            _samplePosition = Vector3.Lerp(_frames[first].position, _frames[next].position, index - Mathf.Floor(index));
        }
    }

    private void Scrub(float value)
    {
        _playing = false;
        if (_focusClip) _poseTime = Mathf.Clamp(value, 0, EditMotion.End);
        else { _time = Mathf.Clamp(value, 0, _duration); Sample(); }
        ApplyPose();
        Repaint();
    }

    private void StepFrame(int direction)
    {
        if (_focusClip) Scrub(_poseTime + direction / EditMotion.FrameRate);
        else Scrub(_time + direction / 60f);
    }

    private void MarkChanged() { EditorUtility.SetDirty(_definition); _dirty = true; }
    private static GUIStyle CenterStyle() => new GUIStyle(EditorStyles.whiteLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };

    private void DrawFooter()
    {
        if (_focusClip)
            GUILayout.Label("애니메이션 " + _poseTime.ToString("0.000") + "초  ·  " + Mathf.RoundToInt(_poseTime * EditMotion.FrameRate) + "프레임    |    노란 마커: 성공 판정 / 보라 마커: 완벽 회피 종료", EditorStyles.miniLabel);
        else
        {
            bool rejected = _previewMode == 2 && !_events.Any(e => e.name == "반격 시작");
            GUILayout.Label(rejected ? "반격이 성립하지 않습니다. 판정·입력 구간을 확인하거나 모델·연결에서 테스트 조건을 해제하세요." :
                "실제 경과 " + _time.ToString("0.00") + "초 / " + _duration.ToString("0.00") + "초    |    청록: 슬로우 / 주황: 반격 가능 / 보라: 화면 효과" + (_truncated ? "  ·  30초까지만 표시" : ""), EditorStyles.miniLabel);
        }
        if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message, EditorStyles.miniLabel);
        if (_showSetup) DrawTestOptions();
    }

    private void DrawTestOptions()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            _showTarget = GUILayout.Toggle(_showTarget, "테스트 대상 표시", GUILayout.Width(135));
            EditorGUI.BeginChangeCheck();
            _testThreat = GUILayout.Toggle(_testThreat, "거리·예고 조건도 검사", GUILayout.Width(170));
            if (EditorGUI.EndChangeCheck()) _dirty = true;
        }
        if (!_testThreat) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            GUILayout.Label("거리", GUILayout.Width(34));
            _testDistance = Mathf.Max(0, EditorGUILayout.FloatField(_testDistance, GUILayout.Width(60)));
            GUILayout.Label("반경", GUILayout.Width(34));
            _testRange = Mathf.Max(.01f, EditorGUILayout.FloatField(_testRange, GUILayout.Width(60)));
            GUILayout.Label("예고 시작", GUILayout.Width(64));
            _threatStart = Mathf.Max(0, EditorGUILayout.FloatField(_threatStart, GUILayout.Width(60)));
            GUILayout.Label("예고 끝", GUILayout.Width(54));
            _threatEnd = Mathf.Max(_threatStart, EditorGUILayout.FloatField(_threatEnd, GUILayout.Width(60)));
            if (EditorGUI.EndChangeCheck()) _dirty = true;
        }
    }

    private void SelectCounterTimeline()
    {
        if (_player == null) return;
        var container = _player.GetComponent<TimelineDirectorContainer>();
        var infos = new SerializedObject(container).FindProperty("_directorInfos");
        var id = _phase <= 1 ? DirectorID.DodgeAttackStart : _phase == 2 ? DirectorID.DodgeAttackLoop : DirectorID.DodgeAttackEnd;
        for (int i = 0; i < infos.arraySize; i++)
        {
            var info = infos.GetArrayElementAtIndex(i);
            if (info.FindPropertyRelative("ID").intValue == (int)id)
            { Selection.activeObject = info.FindPropertyRelative("Director").objectReferenceValue; break; }
        }
    }

    private void DrawRecording()
    {
        var runtime = _player != null ? _player.Dodge : null;
        if (runtime?.Sequence == null)
        {
            EditorGUILayout.HelpBox("Play Mode에서 회피하면 실제 속도·판정·이벤트가 이곳에 기록됩니다. 미리보기는 위의 ‘미리보기로’에서 계속 사용할 수 있습니다.", MessageType.Info);
            return;
        }
        var sequence = runtime.Sequence;
        GUILayout.Label("실제 " + sequence.Time.ToString("0.00") + "초  ·  " + sequence.EndReason, EditorStyles.boldLabel);
        GUILayout.Label("회피 요청 " + (sequence.SlowScale * 100).ToString("0") + "%  /  실제 적용 " + (CombatTimeController.Scale * 100).ToString("0") + "%  /  히트스톱 " + _player.IsHitStopped);
        if (GUILayout.Button("실전 기록 CSV 저장"))
        {
            string path = EditorUtility.SaveFilePanel("회피 실전 기록", Application.dataPath + "/../Temp", "DodgeRecording", "csv");
            if (!string.IsNullOrEmpty(path))
            {
                var csv = new StringBuilder("real_seconds,phase,source_seconds,requested_scale,applied_scale,counter_window,invulnerable,x,y,z\n");
                foreach (var f in runtime.Recording) csv.AppendLine(FormattableString.Invariant($"{f.time},{f.phase},{f.sourceTime},{f.requestedScale},{f.appliedScale},{f.window},{f.invulnerable},{f.position.x},{f.position.y},{f.position.z}"));
                File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
            }
        }
        _recordScroll = EditorGUILayout.BeginScrollView(_recordScroll);
        foreach (var e in sequence.Events) EditorGUILayout.LabelField(e.time.ToString("0.000") + "초", e.name);
        EditorGUILayout.EndScrollView();
    }
}

[CustomEditor(typeof(PlayerDodgeDefinition))]
public sealed class PlayerDodgeDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("회피 조정 열기 · 애니메이션 자동 재생", GUILayout.Height(32))) PlayerDodgeStudio.OpenDefinition((PlayerDodgeDefinition)target);
        EditorGUILayout.HelpBox("위의 조정 툴에서 모델을 보며 편집할 수 있습니다. 아래는 원본 데이터입니다.", MessageType.None);
        DrawDefaultInspector();
    }
}
