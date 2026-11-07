using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>A camera viewport, clip timeline and inspector for the same runtime cutscene asset.</summary>
public sealed class VirtualCutsceneStudio : EditorWindow
{
    private static readonly Color Accent = new(.34f, .84f, .88f);
    private static readonly Color[] TrackColors = { new(.35f, .6f, .9f), new(.66f, .47f, .9f), new(.3f, .72f, .6f), new(.91f, .64f, .3f) };
    private static readonly string[] Tabs = { "무대", "캐릭터", "카메라", "이펙트", "사운드", "재생·연결" };
    [SerializeField] private VirtualCutsceneDefinition _definition;
    [SerializeField] private PlayerCore _player;
    [SerializeField] private float _time;
    [SerializeField] private bool _loop = true, _audioEnabled = true;
    [SerializeField] private int _tab;
    [SerializeField] private int[] _selection = new int[4];
    [SerializeField] private bool _inspect;
    [SerializeField] private float _yaw = 180, _pitch = 10, _distance = 3.5f;
    private VirtualCutscenePreview _preview;
    private SerializedObject _serialized;
    private AudioClip _audioMix;
    private bool _playing, _rebuild = true, _scrubbing, _previewAllowed;
    private double _lastTick;
    private Vector2 _inspectorScroll;
    private int _dragTrack = -1, _dragIndex;
    private float _dragMouseX, _dragStart;
    private string _status;
    public VirtualCutsceneDefinition Definition => _definition;
    public float PreviewTime => _time;

    [MenuItem("Tools/Character/Player/Q - Cutscene Studio")]
    public static void Open()
    {
        var definition = AssetDatabase.LoadAssetAtPath<VirtualCutsceneDefinition>(VirtualCutsceneSetup.DefaultPath);
        if (definition == null && !EditorApplication.isPlayingOrWillChangePlaymode) definition = VirtualCutsceneSetup.EnsureDefault();
        OpenDefinition(definition);
    }

    public static void OpenDefinition(VirtualCutsceneDefinition definition)
    {
        var window = GetWindow<VirtualCutsceneStudio>("Cutscene Studio");
        window.SetDefinition(definition);
        window.Show();
    }

    private void OnEnable()
    {
        minSize = new Vector2(1040, 730);
        _preview?.Dispose();
        _preview = new VirtualCutscenePreview();
        _previewAllowed = !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode;
        if (_definition == null) _definition = AssetDatabase.LoadAssetAtPath<VirtualCutsceneDefinition>(VirtualCutsceneSetup.DefaultPath);
        FindPlayer();
        _lastTick = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Undo.undoRedoPerformed += OnUndo;
        AssemblyReloadEvents.beforeAssemblyReload += ClearPreview;
        EditorApplication.playModeStateChanged += OnPlayMode;
        _rebuild = true;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        Undo.undoRedoPerformed -= OnUndo;
        AssemblyReloadEvents.beforeAssemblyReload -= ClearPreview;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        ClearPreview();
    }

    private void OnUndo() { _serialized = null; Invalidate(); Repaint(); }
    private void OnPlayMode(PlayModeStateChange state)
    {
        // isPlayingOrWillChangePlaymode can already be false during teardown.
        // Do not recreate a preview graph between ExitingPlayMode and scene unload.
        _previewAllowed = state == PlayModeStateChange.EnteredEditMode;
        ClearPreview(); FindPlayer(); _rebuild = _previewAllowed; Repaint();
    }
    private void FindPlayer() { if (_player == null) _player = Object.FindAnyObjectByType<PlayerCore>(); }
    private void ClearPreview()
    {
        _playing = false;
        VirtualCutsceneAudioPreview.StopAll();
        _preview?.Dispose();
        if (_audioMix != null) Object.DestroyImmediate(_audioMix);
        _audioMix = null;
    }

    private void SetDefinition(VirtualCutsceneDefinition definition)
    {
        ClearPreview();
        _definition = definition; _serialized = null; _time = 0; _rebuild = true;
        Repaint();
    }

    private void Invalidate()
    {
        _rebuild = true;
        if (_audioMix != null) Object.DestroyImmediate(_audioMix);
        _audioMix = null;
        VirtualCutsceneAudioPreview.StopAll();
    }

    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float delta = Mathf.Min(.1f, Mathf.Max(0, (float)(now - _lastTick)));
        _lastTick = now;
        if (_definition == null || !_previewAllowed || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (_playing)
        {
            _time += delta;
            if (_time >= _definition.Duration)
            {
                _time = _loop ? Mathf.Repeat(_time, _definition.Duration) : _definition.Duration;
                if (!_loop) _playing = false;
                RestartAudio();
            }
            Repaint();
        }
    }

    private void OnGUI()
    {
        DrawHeader();
        DrawToolbar();
        if (_definition == null)
        {
            EditorGUI.HelpBox(new Rect(20, 120, position.width - 40, 65), "새 컷씬을 만들거나 기존 컷씬 에셋을 선택하세요.", MessageType.Info);
            return;
        }
        if (_serialized == null || _serialized.targetObject != _definition) _serialized = new SerializedObject(_definition);
        float leftWidth = position.width - 386;
        var viewport = new Rect(12, 103, leftWidth, position.height - 338);
        var timeline = new Rect(12, position.height - 223, leftWidth, 188);
        var inspector = new Rect(position.width - 360, 103, 348, position.height - 138);
        DrawViewport(viewport);
        DrawTimeline(timeline);
        DrawInspector(inspector);
        EditorGUI.DrawRect(new Rect(0, position.height - 25, position.width, 25), new Color(.12f, .13f, .16f));
        string status = _status ?? (EditorApplication.isPlaying ? "Game 화면에서 Q로 시전 · 컷씬 종료 후 기존 궁극기 공격으로 연결" : "실시간 3D 미리보기 · 전투 오브젝트를 이동하거나 변경하지 않습니다");
        GUI.Label(new Rect(12, position.height - 22, position.width - 24, 20), status, EditorStyles.miniLabel);
        HandleKeyboard();
    }

    private void DrawHeader()
    {
        EditorGUI.DrawRect(new Rect(0, 0, position.width, 61), new Color(.105f, .12f, .155f));
        EditorGUI.DrawRect(new Rect(0, 60, position.width, 2), Accent);
        GUI.Label(new Rect(16, 9, 500, 27), "CUTSCENE STUDIO", new GUIStyle(EditorStyles.boldLabel) { fontSize = 20, normal = { textColor = Color.white } });
        GUI.Label(new Rect(17, 36, 650, 18), "Q 궁극기  /  가상 무대 · 캐릭터 · 카메라 · VFX · 사운드", EditorStyles.miniLabel);
        GUI.Label(new Rect(position.width - 170, 20, 150, 24), EditorApplication.isPlaying ? "●  GAME CONNECTED" : "●  VIRTUAL STAGE", new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleRight, normal = { textColor = Accent } });
    }

    private void DrawToolbar()
    {
        GUILayout.BeginArea(new Rect(12, 70, position.width - 24, 25));
        using (new EditorGUILayout.HorizontalScope())
        {
            var next = (VirtualCutsceneDefinition)EditorGUILayout.ObjectField(_definition, typeof(VirtualCutsceneDefinition), false, GUILayout.MinWidth(220));
            if (next != _definition) SetDefinition(next);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("새 컷씬", GUILayout.Width(76))) CreateAsset(false);
                if (GUILayout.Button("복제", GUILayout.Width(52))) CreateAsset(true);
                if (GUILayout.Button("저장", GUILayout.Width(52)) && _definition != null)
                { AssetDatabase.SaveAssetIfDirty(_definition); _status = "저장 완료 · " + AssetDatabase.GetAssetPath(_definition); }
            }
            if (GUILayout.Button("미리보기 새로고침", GUILayout.Width(130))) { FindPlayer(); Invalidate(); }
        }
        GUILayout.EndArea();
    }

    private void DrawViewport(Rect area)
    {
        EditorGUI.DrawRect(area, new Color(.075f, .085f, .105f));
        GUI.Label(new Rect(area.x + 10, area.y + 7, 220, 20), _inspect ? "자유 탐색  /  드래그 회전 · 휠 줌" : "연출 카메라  /  최종 화면", EditorStyles.miniBoldLabel);
        _inspect = GUI.Toggle(new Rect(area.xMax - 95, area.y + 5, 85, 22), _inspect, "자유 탐색", "Button");
        var space = new Rect(area.x + 8, area.y + 32, area.width - 16, area.height - 79);
        float width = Mathf.Min(space.width, space.height * 16 / 9);
        var frame = new Rect(space.center.x - width / 2, space.center.y - width * 9 / 32, width, width * 9 / 16);
        if (_previewAllowed && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (_rebuild && Event.current.type == EventType.Repaint)
            {
                _rebuild = false;
                try { _preview.Build(_definition, _player); _status = null; }
                catch (Exception exception) { _status = "미리보기 오류: " + exception.Message; Debug.LogException(exception); }
            }
            HandleViewportInput(frame);
            var texture = _preview.Render(frame, _time, _inspect, Quaternion.Euler(_pitch, _yaw, 0), _distance, _definition.actorPosition + Vector3.up * 1.1f);
            if (texture != null) GUI.DrawTexture(frame, texture, ScaleMode.StretchToFill, false);
            if (!_inspect)
            {
                EditorGUI.DrawRect(new Rect(frame.x, frame.y, frame.width, frame.height * _definition.letterbox), Color.black);
                EditorGUI.DrawRect(new Rect(frame.x, frame.yMax - frame.height * _definition.letterbox, frame.width, frame.height * _definition.letterbox), Color.black);
                EditorGUI.DrawRect(frame, new Color(0, 0, 0, _definition.BlackOpacity(_time)));
            }
        }
        else
        {
            var active = VirtualCutscenePlayback.Active;
            if (active != null && active.Output != null) GUI.DrawTexture(frame, active.Output, ScaleMode.ScaleToFit, false);
            else GUI.Label(frame, "Game 화면에서 Q를 누르세요", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 16 });
            Repaint();
        }
        var controls = new Rect(area.x + 8, area.yMax - 35, area.width - 16, 28);
        GUILayout.BeginArea(controls);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button(_playing ? "Ⅱ 일시정지" : "▶ 재생", GUILayout.Width(89))) TogglePlayback();
                if (GUILayout.Button("■", GUILayout.Width(30))) { _playing = false; Seek(0); }
                if (GUILayout.Button("◀ 1f", GUILayout.Width(47))) Seek(_time - 1 / 60f);
                if (GUILayout.Button("1f ▶", GUILayout.Width(47))) Seek(_time + 1 / 60f);
                _loop = GUILayout.Toggle(_loop, "반복", GUILayout.Width(46));
                bool audio = GUILayout.Toggle(_audioEnabled, "소리", GUILayout.Width(46));
                if (audio != _audioEnabled) { _audioEnabled = audio; RestartAudio(); }
            }
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{_time:0.000} / {_definition.Duration:0.000}s", EditorStyles.miniBoldLabel);
        }
        GUILayout.EndArea();
    }

    private void HandleViewportInput(Rect frame)
    {
        var current = Event.current;
        if (!frame.Contains(current.mousePosition)) return;
        if (_inspect && current.type == EventType.MouseDrag && (current.button == 0 || current.button == 1))
        {
            _yaw += current.delta.x * .45f;
            _pitch = Mathf.Clamp(_pitch + current.delta.y * .35f, -75, 80);
            current.Use(); Repaint();
        }
        if (_inspect && current.type == EventType.ScrollWheel)
        { _distance = Mathf.Clamp(_distance * Mathf.Exp(current.delta.y * .08f), .25f, 40); current.Use(); Repaint(); }
        if (current.type == EventType.DragUpdated || current.type == EventType.DragPerform)
        {
            var objects = DragAndDrop.objectReferences;
            if (objects.Length != 1) return;
            bool valid = objects[0] is AnimationClip || objects[0] is AudioClip || objects[0] is GameObject;
            if (!valid) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (current.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                Edit("Drop Studio Asset", () =>
                {
                    if (objects[0] is AnimationClip animation)
                    { _definition.animations.Add(new VirtualCutsceneAnimation { clip = animation, name = animation.name, start = _time, duration = animation.length }); _tab = 1; _selection[0] = _definition.animations.Count - 1; }
                    else if (objects[0] is AudioClip sound)
                    { _definition.sounds.Add(new VirtualCutsceneSound { clip = sound, name = sound.name, start = _time, duration = sound.length }); _tab = 4; _selection[3] = _definition.sounds.Count - 1; }
                    else if (objects[0] is GameObject prefab && EditorUtility.IsPersistent(prefab)) { _definition.backgroundPrefab = prefab; _tab = 0; }
                });
            }
            current.Use();
        }
    }

    private void DrawTimeline(Rect area)
    {
        EditorGUI.DrawRect(area, new Color(.105f, .115f, .14f));
        float labelWidth = 64;
        var ruler = new Rect(area.x + labelWidth, area.y + 8, area.width - labelWidth - 10, 25);
        GUI.Label(new Rect(area.x + 10, area.y + 9, 50, 20), "60 FPS", EditorStyles.miniBoldLabel);
        float duration = _definition.Duration;
        float step = duration <= 3 ? .25f : duration <= 10 ? 1 : Mathf.Ceil(duration / 10);
        for (float t = 0; t <= duration + .0001f; t += step)
        {
            float x = ruler.x + t / duration * ruler.width;
            EditorGUI.DrawRect(new Rect(x, ruler.y + 19, 1, area.height - 28), new Color(1, 1, 1, .055f));
            GUI.Label(new Rect(x + 3, ruler.y, 52, 18), $"{t:0.##}", EditorStyles.miniLabel);
        }
        string[] names = { "동작", "카메라", "VFX", "SFX" };
        for (int track = 0; track < 4; track++)
        {
            var row = new Rect(ruler.x, area.y + 38 + track * 33, ruler.width, 29);
            GUI.Label(new Rect(area.x + 10, row.y + 5, 54, 20), names[track], EditorStyles.miniBoldLabel);
            EditorGUI.DrawRect(row, new Color(1, 1, 1, .025f));
            for (int i = 0; i < Count(track); i++)
            {
                GetClip(track, i, out var start, out var length, out var name);
                float clippedStart = Mathf.Clamp(start, 0, duration), clippedEnd = Mathf.Clamp(start + length, 0, duration);
                if (start >= duration) continue;
                var clip = new Rect(row.x + clippedStart / duration * row.width, row.y + 2, Mathf.Max(4, (clippedEnd - clippedStart) / duration * row.width), 25);
                bool selected = _tab == track + 1 && _selection[track] == i;
                EditorGUI.DrawRect(clip, TrackColors[track] * (selected ? 1 : .7f));
                if (selected)
                { EditorGUI.DrawRect(new Rect(clip.x, clip.y, clip.width, 2), Color.white); EditorGUI.DrawRect(new Rect(clip.x, clip.yMax - 2, clip.width, 2), Color.white); }
                GUI.Label(new Rect(clip.x + 5, clip.y + 3, Mathf.Max(0, clip.width - 7), 20), name, EditorStyles.miniBoldLabel);
                if (!EditorApplication.isPlaying && Event.current.type == EventType.MouseDown && Event.current.button == 0 && clip.Contains(Event.current.mousePosition))
                {
                    _tab = track + 1; _selection[track] = i;
                    _dragTrack = track; _dragIndex = i; _dragMouseX = Event.current.mousePosition.x; _dragStart = start;
                    Undo.RecordObject(_definition, "Move Cutscene Clip");
                    GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                    Event.current.Use(); Repaint();
                }
            }
        }
        float playhead = ruler.x + Mathf.Clamp01(_time / duration) * ruler.width;
        EditorGUI.DrawRect(new Rect(playhead, ruler.y, 2, area.height - 12), Accent);
        GUI.Label(new Rect(Mathf.Min(playhead + 4, ruler.xMax - 58), area.yMax - 18, 64, 18), $"{Mathf.RoundToInt(_time * 60)} f", EditorStyles.miniLabel);
        var evt = Event.current;
        if (_dragTrack >= 0 && evt.type == EventType.MouseDrag)
        {
            float start = Mathf.Clamp(_dragStart + (evt.mousePosition.x - _dragMouseX) / ruler.width * duration, 0, duration - .01f);
            if (!evt.shift) start = Mathf.Round(start * 60) / 60;
            SetStart(_dragTrack, _dragIndex, start);
            EditorUtility.SetDirty(_definition); Invalidate(); evt.Use(); Repaint();
        }
        if (_dragTrack >= 0 && evt.type == EventType.MouseUp)
        { _dragTrack = -1; GUIUtility.hotControl = 0; evt.Use(); }
        if (evt.type == EventType.MouseDown && evt.button == 0 && ruler.Contains(evt.mousePosition))
        { _scrubbing = true; GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive); Seek((evt.mousePosition.x - ruler.x) / ruler.width * duration); evt.Use(); }
        if (_scrubbing && evt.type == EventType.MouseDrag) { Seek((evt.mousePosition.x - ruler.x) / ruler.width * duration); evt.Use(); }
        if (_scrubbing && evt.type == EventType.MouseUp) { _scrubbing = false; GUIUtility.hotControl = 0; evt.Use(); }
    }

    private void DrawInspector(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(.14f, .15f, .18f));
        GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 9, rect.width - 20, rect.height - 18));
        _tab = GUILayout.SelectionGrid(_tab, Tabs, 3, GUILayout.Height(48));
        GUILayout.Space(10);
        _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);
        _serialized.Update();
        EditorGUI.BeginChangeCheck();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            switch (_tab)
            {
                case 0: DrawStageInspector(); break;
                case 1: DrawActorInspector(); break;
                case 2: DrawShotInspector(); break;
                case 3: DrawEffectInspector(); break;
                case 4: DrawSoundInspector(); break;
                case 5: DrawPlaybackInspector(); break;
            }
        }
        bool changed = EditorGUI.EndChangeCheck();
        if (_serialized.ApplyModifiedProperties() || changed) { Invalidate(); _time = Mathf.Clamp(_time, 0, _definition.Duration); }
        if (_tab == 5)
        {
            GUILayout.Space(8);
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || VirtualCutscenePlayback.IsPlaying))
                if (GUILayout.Button("게임에서 Q 궁극기 시전", GUILayout.Height(30))) { FindPlayer(); if (_player != null && !_player.TryBeginUltimate()) _status = "현재 궁극기를 사용할 수 없습니다. 쿨다운·캐릭터 상태를 확인하세요."; }
            using (new EditorGUI.DisabledScope(!VirtualCutscenePlayback.IsPlaying))
                if (GUILayout.Button("현재 컷씬 중단 / 전투 복구")) VirtualCutscenePlayback.CancelFor(_player);
        }
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawStageInspector()
    {
        Section("배경과 무대");
        Field("backgroundColor", "배경색"); Field("backgroundPrefab", "배경 프리팹");
        Field("backgroundPosition", "배경 위치"); Field("backgroundEuler", "배경 회전"); Field("backgroundScale", "배경 크기");
        Field("skybox", "스카이박스"); Field("showFloor", "바닥 표시"); Field("floorMaterial", "바닥 머티리얼"); Field("floorColor", "기본 바닥색");
        Section("스튜디오 조명");
        Field("keyLightColor", "주광 색상"); Field("keyLightIntensity", "주광 강도"); Field("keyLightEuler", "주광 방향");
        Field("fillLightColor", "보조광 색상"); Field("fillLightIntensity", "보조광 강도");
        EditorGUILayout.HelpBox("배경 프리팹은 연출용으로 복제됩니다. 전투 스크립트·충돌·오디오는 실행하지 않습니다. 파티클은 시간축에 맞춰 샘플링합니다.", MessageType.None);
    }

    private void DrawActorInspector()
    {
        Section("캐릭터");
        Field("actorPrefab", "외형 프리팹");
        EditorGUILayout.HelpBox("외형을 비우면 현재 플레이어를 복제합니다. 애니메이션은 해당 모델의 리그와 호환되어야 합니다.", MessageType.None);
        Field("actorPosition", "위치"); Field("actorEuler", "회전"); Field("actorScale", "크기");
        var entry = DrawClipSelector(0, "animations", "애니메이션");
        if (entry == null) return;
        CommonFields(entry);
        Relative(entry, "clip", "애니메이션 클립"); Relative(entry, "sourceStart", "원본 시작 (초)"); Relative(entry, "speed", "재생 배속"); Relative(entry, "loop", "원본 반복");
        EditorGUILayout.HelpBox("구간이 겹치면 나중에 시작한 동작을 사용합니다. 빈 구간은 이전 마지막 자세를 유지합니다. 루트 이동과 Animation Event는 적용하지 않습니다.", MessageType.None);
    }

    private void DrawShotInspector()
    {
        var entry = DrawClipSelector(1, "shots", "카메라 샷");
        if (entry == null) return;
        CommonFields(entry);
        Section("시작 구도");
        Relative(entry, "position", "카메라 위치"); Relative(entry, "lookAt", "바라볼 위치"); Relative(entry, "fieldOfView", "시야각");
        Section("끝 구도");
        Relative(entry, "endPosition", "카메라 위치"); Relative(entry, "endLookAt", "바라볼 위치"); Relative(entry, "endFieldOfView", "시야각");
        Relative(entry, "smooth", "부드러운 이동"); Relative(entry, "roll", "화면 기울기"); Relative(entry, "shake", "흔들림 크기"); Relative(entry, "shakeFrequency", "흔들림 속도");
        using (new EditorGUI.DisabledScope(!_inspect || _preview.Stage == null))
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("탐색 구도 → 시작")) CaptureShot(false);
            if (GUILayout.Button("탐색 구도 → 끝")) CaptureShot(true);
        }
        EditorGUILayout.HelpBox("자유 탐색에서 마우스로 구도를 맞춘 뒤 시작/끝에 저장하세요. 다음 샷의 시작 시각에 카메라가 컷 전환됩니다.", MessageType.None);
    }

    private void DrawEffectInspector()
    {
        var entry = DrawClipSelector(2, "effects", "이펙트");
        if (entry == null) return;
        CommonFields(entry);
        Relative(entry, "prefab", "이펙트 프리팹"); Relative(entry, "position", "위치"); Relative(entry, "euler", "회전"); Relative(entry, "scale", "크기"); Relative(entry, "relativeToActor", "캐릭터 기준 배치");
        EditorGUILayout.HelpBox("파티클과 메시 이펙트를 지원합니다. 게임 로직을 가진 스크립트와 충돌은 제거합니다. 커스텀 스크립트로만 생성되는 효과는 시각 프리팹으로 준비하세요.", MessageType.None);
    }

    private void DrawSoundInspector()
    {
        var entry = DrawClipSelector(3, "sounds", "사운드");
        if (entry == null) return;
        CommonFields(entry);
        Relative(entry, "clip", "오디오 클립"); Relative(entry, "sourceStart", "원본 시작 (초)"); Relative(entry, "volume", "볼륨"); Relative(entry, "pitch", "배속 / 피치");
        Relative(entry, "fadeIn", "페이드 인 (초)"); Relative(entry, "fadeOut", "페이드 아웃 (초)"); Relative(entry, "loop", "원본 반복");
        if (GUILayout.Button("선택한 원본 소리 듣기"))
        {
            var sound = _definition.sounds[_selection[3]];
            VirtualCutsceneAudioPreview.StopAll(); VirtualCutsceneAudioPreview.PlayClip(sound.clip, sound.sourceStart);
        }
        EditorGUILayout.HelpBox("재생 시 배속·볼륨·페이드를 합성해 미리 듣습니다. 편집기에서 데이터를 읽을 수 없는 압축/스트리밍 클립은 게임 재생으로 확인하세요.", MessageType.None);
    }

    private void DrawPlaybackInspector()
    {
        Section("재생 설정");
        Field("enabled", "Q 컷씬 사용"); Field("duration", "전체 시간 (초)"); Field("letterbox", "상하 검은 띠");
        Field("fadeIn", "시작 페이드 (초)"); Field("fadeOut", "종료 페이드 (초)"); Field("allowSkip", "Esc로 연출 건너뛰기");
        Section("전투 연결");
        var player = (PlayerCore)EditorGUILayout.ObjectField("미리보기 플레이어", _player, typeof(PlayerCore), true);
        if (player != _player) { _player = player; Invalidate(); }
        var ultimate = _player != null ? _player.UltimateDefinition : AssetDatabase.LoadAssetAtPath<PlayerUltimateDefinition>(PlayerUltimateSetup.DefinitionPath);
        EditorGUILayout.ObjectField("연결할 궁극기", ultimate, typeof(PlayerUltimateDefinition), false);
        bool connected = ultimate != null && ultimate.cutscene == _definition;
        EditorGUILayout.HelpBox(connected ? "이 컷씬이 Q 궁극기에 연결되어 있습니다." : "이 컷씬은 현재 Q 궁극기에 연결되어 있지 않습니다.", connected ? MessageType.Info : MessageType.Warning);
        if (GUILayout.Button("현재 컷씬을 Q 궁극기에 연결", GUILayout.Height(28)) && ultimate != null)
        {
            Undo.RecordObject(ultimate, "Connect Q Cutscene"); ultimate.cutscene = _definition; EditorUtility.SetDirty(ultimate); AssetDatabase.SaveAssetIfDirty(ultimate); _status = "Q 궁극기 연결 완료";
        }
        if (GUILayout.Button("현재 미리보기 프레임 저장"))
        {
            string path = EditorUtility.SaveFilePanel("컷씬 프레임 저장", "", "CutsceneFrame", "png");
            if (!string.IsNullOrEmpty(path)) { _preview.SaveFrame(path); _status = "프레임 저장 완료: " + path; }
        }
        EditorGUILayout.HelpBox("Q → 전투 정지 → 별도 무대 렌더링 → 원래 전투 카메라 복구 → 기존 궁극기 공격. 컷씬의 시간은 전투 배속에 영향을 받지 않습니다.", MessageType.None);
    }

    private SerializedProperty DrawClipSelector(int track, string property, string label)
    {
        Section(label + " 트랙");
        int count = Count(track);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("+ 추가")) { _serialized.ApplyModifiedProperties(); AddClip(track); _serialized.Update(); }
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUILayout.Button("복제")) { _serialized.ApplyModifiedProperties(); DuplicateClip(track); _serialized.Update(); }
                if (GUILayout.Button("삭제")) { _serialized.ApplyModifiedProperties(); RemoveClip(track); _serialized.Update(); }
            }
        }
        count = Count(track);
        if (count == 0) { EditorGUILayout.HelpBox(label + "을 추가하세요. 시간축의 구간을 드래그해 시작 시각을 바꿀 수 있습니다.", MessageType.None); return null; }
        _selection[track] = Mathf.Clamp(_selection[track], 0, count - 1);
        var labels = new string[count];
        for (int i = 0; i < count; i++) { GetClip(track, i, out _, out _, out var name); labels[i] = (i + 1) + ". " + name; }
        _selection[track] = EditorGUILayout.Popup("선택", _selection[track], labels);
        return _serialized.FindProperty(property).GetArrayElementAtIndex(_selection[track]);
    }

    private static void Section(string label) { GUILayout.Space(8); GUILayout.Label(label, EditorStyles.boldLabel); GUILayout.Space(3); }
    private void Field(string property, string label) => EditorGUILayout.PropertyField(_serialized.FindProperty(property), new GUIContent(label), true);
    private static void Relative(SerializedProperty entry, string property, string label) => EditorGUILayout.PropertyField(entry.FindPropertyRelative(property), new GUIContent(label), true);
    private static void CommonFields(SerializedProperty entry)
    { Relative(entry, "name", "이름"); Relative(entry, "start", "시작 (초)"); Relative(entry, "duration", "길이 (초)"); }

    private void Edit(string label, Action edit)
    { Undo.RecordObject(_definition, label); edit(); EditorUtility.SetDirty(_definition); Invalidate(); Repaint(); }

    private int Count(int track) => track switch { 0 => _definition.animations.Count, 1 => _definition.shots.Count, 2 => _definition.effects.Count, _ => _definition.sounds.Count };
    private void GetClip(int track, int index, out float start, out float duration, out string name)
    {
        switch (track)
        {
            case 0: var a = _definition.animations[index]; start = a.start; duration = a.duration; name = a.name; break;
            case 1: var c = _definition.shots[index]; start = c.start; duration = c.duration; name = c.name; break;
            case 2: var e = _definition.effects[index]; start = e.start; duration = e.duration; name = e.name; break;
            default: var s = _definition.sounds[index]; start = s.start; duration = s.duration; name = s.name; break;
        }
    }
    private void SetStart(int track, int index, float value)
    {
        switch (track) { case 0: _definition.animations[index].start = value; break; case 1: _definition.shots[index].start = value; break; case 2: _definition.effects[index].start = value; break; default: _definition.sounds[index].start = value; break; }
    }
    private void AddClip(int track) => Edit("Add Cutscene Clip", () =>
    {
        switch (track)
        {
            case 0: _definition.animations.Add(new VirtualCutsceneAnimation { start = _time, duration = Mathf.Max(.1f, _definition.Duration - _time) }); break;
            case 1: _definition.shots.Add(new VirtualCutsceneShot { start = _time, duration = Mathf.Max(.1f, _definition.Duration - _time), name = "카메라 " + (_definition.shots.Count + 1) }); break;
            case 2: _definition.effects.Add(new VirtualCutsceneEffect { start = _time }); break;
            case 3: _definition.sounds.Add(new VirtualCutsceneSound { start = _time }); break;
        }
        _selection[track] = Count(track) - 1;
    });
    private static T Copy<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
    private void DuplicateClip(int track) => Edit("Duplicate Cutscene Clip", () =>
    {
        int index = Mathf.Clamp(_selection[track], 0, Count(track) - 1);
        switch (track)
        {
            case 0: _definition.animations.Add(Copy(_definition.animations[index])); break;
            case 1: _definition.shots.Add(Copy(_definition.shots[index])); break;
            case 2: _definition.effects.Add(Copy(_definition.effects[index])); break;
            case 3: _definition.sounds.Add(Copy(_definition.sounds[index])); break;
        }
        _selection[track] = Count(track) - 1;
    });
    private void RemoveClip(int track) => Edit("Remove Cutscene Clip", () =>
    {
        int index = Mathf.Clamp(_selection[track], 0, Count(track) - 1);
        switch (track) { case 0: _definition.animations.RemoveAt(index); break; case 1: _definition.shots.RemoveAt(index); break; case 2: _definition.effects.RemoveAt(index); break; case 3: _definition.sounds.RemoveAt(index); break; }
        _selection[track] = Mathf.Max(0, Count(track) - 1);
    });

    private void CaptureShot(bool end)
    {
        if (_preview.Stage == null || _definition.shots.Count == 0) return;
        _serialized.ApplyModifiedProperties();
        Edit("Capture Cutscene Camera", () =>
        {
            var shot = _definition.shots[Mathf.Clamp(_selection[1], 0, _definition.shots.Count - 1)];
            var camera = _preview.Stage.Camera;
            Vector3 focus = _definition.actorPosition + Vector3.up * 1.1f;
            if (end) { shot.endPosition = camera.transform.position; shot.endLookAt = focus; shot.endFieldOfView = camera.fieldOfView; }
            else { shot.position = camera.transform.position; shot.lookAt = focus; shot.fieldOfView = camera.fieldOfView; }
        });
        _serialized.Update();
    }

    private void CreateAsset(bool duplicate)
    {
        string path = EditorUtility.SaveFilePanelInProject(duplicate ? "컷씬 복제" : "새 컷씬", "VirtualCutscene", "asset", "컷씬 에셋 저장 위치");
        if (string.IsNullOrEmpty(path)) return;
        var definition = duplicate && _definition != null ? Instantiate(_definition) : CreateInstance<VirtualCutsceneDefinition>();
        definition.renderLayer = VirtualCutsceneSetup.EnsureLayer();
        AssetDatabase.CreateAsset(definition, path);
        SetDefinition(definition);
    }

    public void Seek(float time)
    { _time = _definition != null ? Mathf.Clamp(time, 0, _definition.Duration) : 0; RestartAudio(); Repaint(); }
    private void TogglePlayback()
    {
        if (!_playing && _time >= _definition.Duration) _time = 0;
        _playing = !_playing; _lastTick = EditorApplication.timeSinceStartup; RestartAudio(); Repaint();
    }
    private void RestartAudio()
    {
        VirtualCutsceneAudioPreview.StopAll();
        if (!_playing || !_audioEnabled || _definition == null || _definition.sounds.Count == 0) return;
        if (_audioMix == null) _audioMix = VirtualCutsceneAudioPreview.BuildMix(_definition);
        if (_audioMix != null) VirtualCutsceneAudioPreview.PlayClip(_audioMix, _time);
    }
    private void HandleKeyboard()
    {
        var evt = Event.current;
        if (evt.type != EventType.KeyDown || EditorGUIUtility.editingTextField || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (evt.keyCode == KeyCode.Space) { TogglePlayback(); evt.Use(); }
        else if (evt.keyCode == KeyCode.LeftArrow) { Seek(_time - 1 / 60f); evt.Use(); }
        else if (evt.keyCode == KeyCode.RightArrow) { Seek(_time + 1 / 60f); evt.Use(); }
    }
    public void SavePreviewFrame(string path) => _preview.SaveFrame(path);
}

[CustomEditor(typeof(VirtualCutsceneDefinition))]
internal sealed class VirtualCutsceneDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (GUILayout.Button("Cutscene Studio에서 편집", GUILayout.Height(30))) VirtualCutsceneStudio.OpenDefinition((VirtualCutsceneDefinition)target);
        DrawDefaultInspector();
    }
}
