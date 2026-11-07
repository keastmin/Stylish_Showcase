using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Edits the attack assets and animation events actually consumed by EnemyCore.</summary>
public sealed class EnemyAttackStudio : EditorWindow
{
    private static readonly Color Red = new Color(1f, .32f, .27f);
    private static readonly Color Gold = new Color(1f, .73f, .25f);
    private static readonly Color Cyan = new Color(.3f, .84f, .92f);
    [SerializeField] private EnemyCore _enemy;
    [SerializeField] private EnemyAttackSO _attack;
    [SerializeField] private AnimationClip _clip;
    [SerializeField] private int _hitbox, _tab;
    [SerializeField] private float _time, _speed = 1, _targetDistance = 2.5f;
    [SerializeField] private bool _loop = true, _showNotice = true, _showAll = true, _sceneHandles;
    [SerializeField] private EnemyAttackPreviewMotion _motion = EnemyAttackPreviewMotion.AttackWarp;
    private EnemyCore[] _enemies = Array.Empty<EnemyCore>();
    private EnemyAttackSO[] _attacks = Array.Empty<EnemyAttackSO>();
    private AnimationClip[] _clips = Array.Empty<AnimationClip>();
    private EnemyAttackStudioPreview _preview;
    [SerializeField] private List<UnityEngine.Object> _edited = new List<UnityEngine.Object>();
    private readonly BoxBoundsHandle _boxHandle = new BoxBoundsHandle();
    private Vector2 _scroll;
    private bool _playing, _needsBuild = true;
    private double _lastTick;
    private int _timelineControl, _planControl, _dragUndo;
    private bool _dragHit, _dragSize;
    private float _planScale;
    private Vector2 _planOrigin, _planCenter;
    private string _error, _capturePath;
    private int _handleMode;
    private float _exitTime = -1;

    public EnemyCore SelectedEnemy => _enemy;
    public EnemyAttackSO SelectedAttack => _attack;
    public AnimationClip SelectedClip => _clip;
    public Animator PreviewAnimator => _preview?.ModelAnimator;
    public float PreviewTime => _time;

    [MenuItem("Tools/Character/Enemy/Attack Studio")]
    public static void Open()
    {
        var window = GetWindow<EnemyAttackStudio>("적 공격 조정");
        window.FindTargets();
        window.Show();
    }

    [MenuItem("CONTEXT/EnemyCore/공격 비주얼 에디터 열기")]
    private static void OpenContext(MenuCommand command)
    {
        var window = GetWindow<EnemyAttackStudio>("적 공격 조정");
        window.SelectEnemy(command.context as EnemyCore);
        window.Show();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("적 공격 조정");
        minSize = new Vector2(1060, 730);
        _preview = new EnemyAttackStudioPreview();
        _needsBuild = true;
        _lastTick = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Undo.undoRedoPerformed += Changed;
        EditorApplication.playModeStateChanged += ModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += ClearPreview;
        SceneView.duringSceneGui += DrawSceneHandles;
        FindTargets();
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        Undo.undoRedoPerformed -= Changed;
        EditorApplication.playModeStateChanged -= ModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= ClearPreview;
        SceneView.duringSceneGui -= DrawSceneHandles;
        EndDrag();
        ClearPreview();
    }

    private void ClearPreview() { _preview?.Dispose(); _needsBuild = true; }
    private void Changed() { Repaint(); SceneView.RepaintAll(); }
    private void ModeChanged(PlayModeStateChange state) { EndDrag(); _playing = false; ClearPreview(); Repaint(); }

    public void FindTargets()
    {
        ClearPreview();
        _enemies = UnityEngine.Object.FindObjectsByType<EnemyCore>(FindObjectsInactive.Include)
            .Where(e => e.gameObject.scene.IsValid() && (e.hideFlags & HideFlags.HideAndDontSave) == 0)
            .OrderBy(e => e.name).ToArray();
        var selected = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<EnemyCore>() : null;
        if (_enemy == null) _enemy = selected != null ? selected : _enemies.FirstOrDefault();
        RefreshAttacks();
    }

    public void SelectEnemy(EnemyCore enemy)
    {
        EndDrag();
        _enemy = enemy;
        _attack = null;
        _time = 0;
        _playing = false;
        ClearPreview();
        RefreshAttacks();
        Repaint();
    }

    public void SelectAttack(EnemyAttackSO attack)
    {
        EndDrag();
        _attack = attack;
        _hitbox = 0;
        _time = 0;
        _playing = false;
        RefreshClips();
        _preview?.RequestFrame();
        Repaint();
    }

    private void RefreshAttacks()
    {
        _attacks = EnemyAttackStudioData.FindAttacks(_enemy);
        if (!_attacks.Contains(_attack)) _attack = _attacks.FirstOrDefault();
        RefreshClips();
    }

    private void RefreshClips()
    {
        _clips = EnemyAttackStudioData.FindClips(_enemy, _attack);
        if (!_clips.Contains(_clip)) _clip = _clips.FirstOrDefault();
        _exitTime = EnemyAttackStudioData.GetEarliestExitTime(_enemy, _clip);
        _time = Mathf.Clamp(_time, 0, Duration);
    }

    private float Duration => _clip != null ? Mathf.Max(.001f, _clip.length) : 1;
    private float FrameRate => _clip != null ? Mathf.Max(1, _clip.frameRate) : 60;
    private float HitTime => EnemyAttackStudioData.GetHitTime(_clip, _attack);
    private void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = Mathf.Min(.1f, (float)(now - _lastTick));
        _lastTick = now;
        if (!_playing || _clip == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        _time += dt * _speed;
        if (_time >= Duration) { _time = _loop ? _time % Duration : Duration; if (!_loop) _playing = false; }
        Repaint();
    }

    public void SetPreviewTime(float time) { _playing = false; _time = Mathf.Clamp(time, 0, Duration); Repaint(); }
    public void CapturePreview(string path) { _capturePath = path; Repaint(); }

    private void OnGUI()
    {
        DrawHeader();
        if (_enemy == null)
        {
            EditorGUILayout.HelpBox("씬의 EnemyCore를 선택하거나 적 프리팹을 대상 칸에 연결하세요.", MessageType.Info);
            return;
        }
        bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
        if (playing) EditorGUILayout.HelpBox("Play Mode에서는 읽기만 가능합니다. 편집과 독립 미리보기는 재생을 종료하면 사용할 수 있습니다.", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                DrawAttackPicker();
                using (new EditorGUI.DisabledScope(playing)) DrawPreviewControls();
                Rect viewport = GUILayoutUtility.GetRect(300, 300, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                DrawPreview(viewport, playing);
                using (new EditorGUI.DisabledScope(playing || _clip == null)) { DrawPlayback(); DrawTimeline(); }
                GUILayout.Label("빨강: 현재 판정 · 노랑: 감지 범위 · 청록: 예상 돌진 · 보라: 최대 거리", EditorStyles.miniLabel);
                GUILayout.Space(4);
            }
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(390)))
            {
                _tab = GUILayout.Toolbar(_tab, new[] { "공격 · 판정", "적 · 전투 AI" }, GUILayout.Height(28));
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                using (new EditorGUI.DisabledScope(playing))
                {
                    if (_tab == 0) DrawAttackSettings(); else DrawEnemySettings();
                }
                EditorGUILayout.EndScrollView();
            }
        }
    }

    private void DrawHeader()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label("ENEMY  /  ATTACK STUDIO", EditorStyles.boldLabel, GUILayout.Width(215));
            var enemy = EditorGUILayout.ObjectField(_enemy, typeof(EnemyCore), true, GUILayout.MinWidth(130)) as EnemyCore;
            if (enemy != _enemy) SelectEnemy(enemy);
            if (_enemies.Length > 0)
            {
                int current = Array.IndexOf(_enemies, _enemy);
                int next = EditorGUILayout.Popup(current, _enemies.Select(e => e != null ? e.name : "삭제됨").ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(135));
                if (next != current && next >= 0) SelectEnemy(_enemies[next]);
            }
            if (GUILayout.Button("새로 찾기", EditorStyles.toolbarButton, GUILayout.Width(70))) FindTargets();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("변경 저장", EditorStyles.toolbarButton, GUILayout.Width(85))) SaveEdits();
        }
    }

    private void DrawAttackPicker()
    {
        GUILayout.Space(5);
        using (new EditorGUILayout.HorizontalScope())
        {
            foreach (var attack in _attacks)
            {
                bool chosen = attack == _attack;
                if (GUILayout.Toggle(chosen, attack.name, EditorStyles.miniButton, GUILayout.Height(30)) && !chosen) SelectAttack(attack);
            }
        }
        if (_clips.Length > 1)
        {
            int index = Array.IndexOf(_clips, _clip);
            int next = EditorGUILayout.Popup("연결 애니메이션", index, _clips.Select(c => c.name).ToArray());
            if (next != index) { _clip = _clips[next]; _exitTime = EnemyAttackStudioData.GetEarliestExitTime(_enemy, _clip); SetPreviewTime(0); }
        }
    }

    private void DrawPreview(Rect rect, bool playMode)
    {
        EditorGUI.DrawRect(rect, new Color(.075f, .09f, .12f));
        if (playMode) { GUI.Label(rect, "Play Mode 종료 후 미리보기를 사용할 수 있습니다.", CenterLabel()); return; }
        if (_clip == null) { GUI.Label(rect, "OnAttack 이벤트가 연결된 애니메이션이 없습니다.", CenterLabel()); return; }
        try
        {
            if (_preview == null) _preview = new EnemyAttackStudioPreview();
            if (_needsBuild) { _preview.Build(_enemy); _needsBuild = false; _error = null; }
            _preview.Draw(rect, _clip, _time, _attack, _hitbox, _showNotice, _showAll,
                HitTime >= 0 && Mathf.Abs(_time - HitTime) <= 1 / FrameRate, _targetDistance, _motion);
            if (_capturePath != null && Event.current.type == EventType.Repaint)
            { _preview.SaveLastFrame(_capturePath); _capturePath = null; }
        }
        catch (Exception ex) { _error = ex.Message; _playing = false; }
        if (!string.IsNullOrEmpty(_error)) GUI.Label(new Rect(rect.x + 12, rect.y + 45, rect.width - 24, 70), _error, EditorStyles.wordWrappedLabel);
        string mode = _motion == EnemyAttackPreviewMotion.AttackWarp ? "설정한 돌진 적용" : _motion == EnemyAttackPreviewMotion.InPlace ? "제자리" : "원본 루트 이동";
        GUI.Label(new Rect(rect.x + 12, rect.y + 10, rect.width - 24, 24), "독립 미리보기  /  " + mode, EditorStyles.whiteBoldLabel);
        if (_motion == EnemyAttackPreviewMotion.AttackWarp && _preview.Ready && _attack != null)
        {
            GUI.Label(new Rect(rect.x + 12, rect.y + 37, rect.width - 24, 20),
                $"현재 이동 {_preview.RootPosition.z:0.00}m  /  예상 도착 {_preview.PlannedWarpDistance:0.00}m  /  최대 {_attack.AttackWarpDistance:0.00}m", EditorStyles.whiteLabel);
            if (_preview.AuthoredPlanarTravel < .001f && _time > .1f)
                GUI.Label(new Rect(rect.x + 12, rect.y + 60, rect.width - 24, 20), "현재 구간에 원본 루트 이동이 없어 돌진도 정지합니다.", EditorStyles.whiteMiniLabel);
        }
        GUI.Label(new Rect(rect.x + 12, rect.yMax - 30, rect.width - 24, 22), "정면 고정 표적 기준 · 충돌/추적/블렌드 제외 · 드래그: 회전 / 휠: 확대", EditorStyles.whiteMiniLabel);
    }

    private void DrawPreviewControls()
    {
        _motion = (EnemyAttackPreviewMotion)GUILayout.Toolbar((int)_motion, new[] { "돌진 적용", "제자리", "원본 루트 이동" }, GUILayout.Height(24));
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("표적 거리 (m)", GUILayout.Width(83));
            float maximum = Mathf.Max(15, _attack != null ? _attack.GetMaximumAttackReach() + 2 : 15);
            _targetDistance = EditorGUILayout.Slider(_targetDistance, 0, maximum);
            if (GUILayout.Button("전체 경로 맞춤", GUILayout.Width(100))) _preview?.RequestFrame();
        }
    }

    private void DrawPlayback()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(_playing ? "Ⅱ 일시정지" : "▶ 재생", GUILayout.Width(95), GUILayout.Height(27)))
            { if (_time >= Duration) _time = 0; _playing = !_playing; _lastTick = EditorApplication.timeSinceStartup; }
            if (GUILayout.Button("|◀", GUILayout.Width(32))) SetPreviewTime(0);
            if (GUILayout.Button("−1F", GUILayout.Width(40))) SetPreviewTime(_time - 1 / FrameRate);
            if (GUILayout.Button("+1F", GUILayout.Width(40))) SetPreviewTime(_time + 1 / FrameRate);
            if (GUILayout.Button("타격 순간", GUILayout.Width(76)) && HitTime >= 0) SetPreviewTime(HitTime);
            _loop = GUILayout.Toggle(_loop, "반복", GUILayout.Width(50));
            _speed = EditorGUILayout.Popup(Array.IndexOf(new[] { .25f, .5f, 1f, 1.5f }, _speed), new[] { "0.25×", "0.5×", "1×", "1.5×" }, GUILayout.Width(62)) switch
            { 0 => .25f, 1 => .5f, 3 => 1.5f, _ => 1f };
        }
        float time = EditorGUILayout.Slider("애니메이션 위치", _time, 0, Duration);
        if (!Mathf.Approximately(time, _time)) SetPreviewTime(time);
    }

    private void DrawTimeline()
    {
        Rect rect = GUILayoutUtility.GetRect(100, 67, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(.11f, .13f, .17f));
        Rect track = new Rect(rect.x + 12, rect.y + 24, rect.width - 24, 25);
        float hit = HitTime;
        if (hit >= 0) EditorGUI.DrawRect(new Rect(track.x, track.y, track.width * hit / Duration, track.height), new Color(.34f, .25f, .12f));
        if (_exitTime >= 0)
        {
            float endX = track.x + track.width * _exitTime / Duration;
            EditorGUI.DrawRect(new Rect(endX, track.y, track.xMax - endX, track.height), new Color(.24f, .14f, .18f));
            GUI.Label(new Rect(endX + 4, track.y + 3, track.xMax - endX - 4, 18), "Animator 전환 이후", EditorStyles.miniLabel);
        }
        for (int i = 0; i <= 8; i++)
        {
            float x = track.x + track.width * i / 8;
            EditorGUI.DrawRect(new Rect(x, track.y, 1, track.height), new Color(.35f, .38f, .42f));
            GUI.Label(new Rect(x - 8, rect.y + 3, 50, 18), (Duration * i / 8).ToString("0.00"), EditorStyles.miniLabel);
        }
        if (_clip != null)
            foreach (var item in AnimationUtility.GetAnimationEvents(_clip))
            {
                float x = track.x + track.width * item.time / Duration;
                bool damage = item.functionName == "OnAttack" && item.objectReferenceParameter == _attack;
                EditorGUI.DrawRect(new Rect(x - (damage ? 4 : 1), track.y, damage ? 8 : 2, track.height), damage ? Red : Cyan * .75f);
            }
        float cursor = track.x + track.width * _time / Duration;
        EditorGUI.DrawRect(new Rect(cursor - 1, track.y - 4, 2, track.height + 8), Color.white);
        GUI.Label(new Rect(rect.x + 12, rect.yMax - 17, rect.width - 24, 18), $"{_time:0.000}s  ·  {Mathf.RoundToInt(_time * FrameRate)}F  /  빨간 마커 드래그: 타격 시점 변경", EditorStyles.miniLabel);
        int id = GUIUtility.GetControlID("EnemyAttackTimeline".GetHashCode(), FocusType.Passive, rect);
        var ev = Event.current;
        if (GUI.enabled && !EditorApplication.isPlayingOrWillChangePlaymode && ev.type == EventType.MouseDown && ev.button == 0 && track.Contains(ev.mousePosition))
        {
            _dragHit = hit >= 0 && Mathf.Abs(ev.mousePosition.x - (track.x + track.width * hit / Duration)) < 10 && EnemyAttackStudioData.CanEditEvents(_clip);
            _timelineControl = id; GUIUtility.hotControl = id;
            Undo.IncrementCurrentGroup(); _dragUndo = Undo.GetCurrentGroup();
            _playing = false; SeekTimeline(track, ev.mousePosition.x); ev.Use();
        }
        if (GUI.enabled && !EditorApplication.isPlayingOrWillChangePlaymode && _timelineControl == id && GUIUtility.hotControl == id && ev.type == EventType.MouseDrag)
        { SeekTimeline(track, ev.mousePosition.x); ev.Use(); }
        if (_timelineControl == id && ev.type == EventType.MouseUp) { EndDrag(); ev.Use(); }
    }

    private void SeekTimeline(Rect track, float x)
    {
        float time = Mathf.Clamp01((x - track.x) / track.width) * Duration;
        time = Mathf.Clamp(Mathf.Round(time * FrameRate) / FrameRate, 0, Duration);
        if (_dragHit) SetHitTime(time);
        _time = time; Repaint();
    }

    private void EndDrag()
    {
        if (_timelineControl == 0 && _planControl == 0) return;
        if (GUIUtility.hotControl == _timelineControl || GUIUtility.hotControl == _planControl) GUIUtility.hotControl = 0;
        Undo.CollapseUndoOperations(_dragUndo); _timelineControl = _planControl = 0;
    }

    private void SetHitTime(float value)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EnemyAttackStudioData.SetAttackEventTime(_clip, _attack, value))
        {
            if (!_edited.Contains(_clip)) _edited.Add(_clip);
            _playing = false; _time = Mathf.Clamp(value, 0, Duration); Changed();
        }
    }

    private void DrawAttackSettings()
    {
        if (_attack == null) { EditorGUILayout.HelpBox("대상 적에 연결된 공격 데이터가 없습니다.", MessageType.Info); return; }
        Section("01  타격 타이밍");
        EditorGUILayout.ObjectField("공격 데이터", _attack, typeof(EnemyAttackSO), false);
        EditorGUILayout.ObjectField("애니메이션", _clip, typeof(AnimationClip), false);
        EditorGUILayout.HelpBox("공격 데이터와 애니메이션은 이 에셋을 사용하는 모든 적에게 함께 적용됩니다. Ctrl+Z로 되돌릴 수 있습니다.", MessageType.None);
        float hit = HitTime;
        using (new EditorGUI.DisabledScope(!EnemyAttackStudioData.CanEditEvents(_clip) || hit < 0))
        {
            EditorGUI.BeginChangeCheck();
            float newHit = EditorGUILayout.Slider(new GUIContent("타격 시점 (초)", "실제 OnAttack 애니메이션 이벤트를 이동합니다."), Mathf.Max(0, hit), 0, Duration);
            int frame = EditorGUILayout.IntField("타격 프레임", Mathf.RoundToInt(Mathf.Max(0, hit) * FrameRate));
            if (EditorGUI.EndChangeCheck()) { SetHitTime(frame != Mathf.RoundToInt(Mathf.Max(0, hit) * FrameRate) ? frame / FrameRate : newHit); }
            if (GUILayout.Button("현재 포즈를 타격 시점으로 지정")) SetHitTime(_time);
        }
        if (hit < 0) EditorGUILayout.HelpBox("연결된 OnAttack 이벤트를 찾지 못했습니다. Animator에 등록된 클립과 이벤트 연결을 확인하세요.", MessageType.Warning);
        else if (!EnemyAttackStudioData.CanEditEvents(_clip)) EditorGUILayout.HelpBox("가져온 모델의 클립은 읽기 전용입니다. 원본 Import Settings에서 이벤트를 편집하세요.", MessageType.Info);
        if (_clip != null && AnimationUtility.GetAnimationEvents(_clip).Count(e => e.functionName == "OnAttack" && e.objectReferenceParameter == _attack) > 1)
            EditorGUILayout.HelpBox("같은 공격 이벤트가 여러 개 있습니다. 여기서는 첫 타격 시점만 편집합니다.", MessageType.Warning);
        EditorGUILayout.HelpBox("개별 클립의 전체 포즈를 보여줍니다. 실제 연속 공격은 Animator의 전환·블렌드 설정에 따라 연결됩니다.", MessageType.None);
        if (_exitTime >= 0)
            EditorGUILayout.HelpBox($"Animator가 {_exitTime:0.000}초부터 다음 동작으로 전환합니다." +
                (hit >= _exitTime ? " 현재 타격은 전환 이후라 실제 전투에서 발생하지 않을 수 있습니다. 타격을 앞당기거나 Animator 전환을 조정하세요." : " 타격 시점을 이 구간 안에 배치하세요."),
                hit >= _exitTime ? MessageType.Warning : MessageType.None);

        var so = new SerializedObject(_attack); so.Update();
        IntField(so, "HitStopFrame", "히트스탑 (60Hz 프레임)", 0);
        Property(so, "DamagedLayer", "피격 대상 레이어");
        Section("02  공격 판정");
        var hitboxes = so.FindProperty("HitboxInfo");
        using (new EditorGUILayout.HorizontalScope())
        {
            if (hitboxes.arraySize > 0)
            {
                _hitbox = Mathf.Clamp(_hitbox, 0, hitboxes.arraySize - 1);
                _hitbox = EditorGUILayout.Popup(_hitbox, Enumerable.Range(0, hitboxes.arraySize).Select(i => "판정 " + (i + 1)).ToArray());
            }
            if (GUILayout.Button("+ 추가", GUILayout.Width(62)))
            {
                int index = hitboxes.arraySize; hitboxes.InsertArrayElementAtIndex(index); _hitbox = index;
                if (index == 0)
                {
                    var item = hitboxes.GetArrayElementAtIndex(index);
                    item.FindPropertyRelative("Size").vector3Value = Vector3.one;
                    item.FindPropertyRelative("Offset").vector3Value = new Vector3(0, 1, 1);
                    item.FindPropertyRelative("DamageAmount").floatValue = 10;
                }
            }
            using (new EditorGUI.DisabledScope(hitboxes.arraySize == 0))
                if (GUILayout.Button("삭제", GUILayout.Width(45))) { hitboxes.DeleteArrayElementAtIndex(_hitbox); _hitbox = Mathf.Max(0, _hitbox - 1); }
        }
        if (hitboxes.arraySize > 0)
        {
            var hb = hitboxes.GetArrayElementAtIndex(Mathf.Clamp(_hitbox, 0, hitboxes.arraySize - 1));
            EditorGUILayout.PropertyField(hb.FindPropertyRelative("HitboxType"), new GUIContent("판정 모양"));
            var damage = hb.FindPropertyRelative("DamageAmount");
            EditorGUI.BeginChangeCheck();
            float damageValue = EditorGUILayout.FloatField("피해량", damage.floatValue);
            if (EditorGUI.EndChangeCheck() && GUI.enabled) damage.floatValue = Mathf.Max(0, damageValue);
            VectorField(hb, "Size", "크기 (X / Y / Z)", true);
            VectorField(hb, "Offset", "중심 위치 (X / Y / Z)", false);
            VectorField(hb, "AdditiveNoticeRange", "회피 감지 추가 크기", true);
            if (hb.FindPropertyRelative("HitboxType").enumValueIndex == (int)EnemyAttackHitboxType.Sphere)
            {
                EditorGUILayout.HelpBox("구 판정은 크기 중 가장 큰 축을 지름으로 사용합니다. 크기가 0이면 레거시 반지름을 사용합니다.", MessageType.None);
                EditorGUILayout.PropertyField(hb.FindPropertyRelative("Radius"), new GUIContent("레거시 반지름 (X)"));
            }
        }
        if (hitboxes.arraySize > 1) EditorGUILayout.HelpBox("판정이 겹치면 각 판정의 피해가 중복 적용될 수 있습니다.", MessageType.Info);
        Apply(so);
        DrawPlan();
        _showNotice = EditorGUILayout.Toggle("회피 감지 범위 표시", _showNotice);
        _showAll = EditorGUILayout.Toggle("모든 판정 함께 표시", _showAll);
        _sceneHandles = EditorGUILayout.Toggle("Scene 뷰에서 직접 편집", _sceneHandles);
        if (_sceneHandles) _handleMode = GUILayout.Toolbar(_handleMode, new[] { "위치 이동", "크기 조정" });

        Section("03  예고 · 추적 · 돌진");
        so.Update();
        FloatField(so, "AttackAnimationTransitionTime", _attack.AttackID == EnemyAttackID.CloseAttack1Hit ? "공격 예고 시간 (초)" : "추적 각도 계산 시간 (초)", 0);
        FloatField(so, "AttackWarpDistance", "최대 돌진 거리 (m)", 0);
        FloatField(so, "AttackRotationAnglePerSecond", "추적 회전 속도 (°/초)", 0);
        Apply(so);
        if (_attack.AttackID == EnemyAttackID.CloseAttack2Hit) EditorGUILayout.HelpBox("2타는 Animator 전환으로 이어집니다. 위 시간은 2타 앞에 대기 시간을 추가하지 않습니다.", MessageType.None);
        GUILayout.Label($"최대 전방 도달 범위  {_attack.GetMaximumAttackReach():0.00} m", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("미리보기 상단의 '돌진 적용'에서 경로와 도착 판정을 확인하세요. 보라는 최대 거리, 청록은 표적 거리에서 예상한 도착점입니다. 실제 표적 충돌에 따라 더 일찍 멈출 수 있습니다.", MessageType.None);
        Section("미리보기 옵션");
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("정면")) { _preview.yaw = 180; _preview.pitch = 10; }
            if (GUILayout.Button("측면")) { _preview.yaw = 90; _preview.pitch = 10; }
            if (GUILayout.Button("위에서")) { _preview.yaw = 180; _preview.pitch = 80; }
            if (GUILayout.Button("시점 초기화")) { _preview.yaw = 110; _preview.pitch = 25; _preview.RequestFrame(); }
        }
    }

    private void DrawPlan()
    {
        if (_attack.HitboxInfo == null || _attack.HitboxInfo.Length == 0) return;
        _hitbox = Mathf.Clamp(_hitbox, 0, _attack.HitboxInfo.Length - 1);
        var hb = _attack.HitboxInfo[_hitbox];
        Rect rect = GUILayoutUtility.GetRect(100, 185, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(.09f, .11f, .14f));
        float extent = Mathf.Max(3, Mathf.Abs(hb.Offset.x) + Mathf.Abs(hb.Size.x), Mathf.Abs(hb.Offset.z) + Mathf.Abs(hb.Size.z));
        float scale = Mathf.Min(rect.width - 30, rect.height - 30) / (extent * 2);
        Vector2 origin = rect.center;
        Vector2 center = origin + new Vector2(hb.Offset.x, -hb.Offset.z) * scale;
        Vector2 size = new Vector2(Mathf.Abs(hb.Size.x), Mathf.Abs(hb.Size.z)) * scale;
        if (hb.HitboxType == EnemyAttackHitboxType.Sphere)
        { float d = Mathf.Max(Mathf.Abs(hb.Size.x), Mathf.Abs(hb.Size.y), Mathf.Abs(hb.Size.z)); if (d <= .001f) d = Mathf.Max(0, hb.Radius.x) * 2; size = Vector2.one * d * scale; }
        for (int i = -Mathf.CeilToInt(extent); i <= extent; i++)
        {
            float x = origin.x + i * scale, y = origin.y + i * scale;
            if (x >= rect.x && x <= rect.xMax) EditorGUI.DrawRect(new Rect(x, rect.y, 1, rect.height), new Color(.16f, .19f, .23f));
            if (y >= rect.y && y <= rect.yMax) EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, 1), new Color(.16f, .19f, .23f));
        }
        GUI.BeginClip(rect);
        Handles.BeginGUI();
        Vector2 localCenter = center - rect.position;
        Handles.color = Red;
        if (hb.HitboxType == EnemyAttackHitboxType.Sphere) Handles.DrawWireDisc(localCenter, Vector3.forward, size.x * .5f);
        else Handles.DrawWireCube(localCenter, new Vector3(size.x, size.y, 0));
        if (_showNotice)
        {
            Handles.color = Gold;
            Vector2 addition = new Vector2(Mathf.Abs(hb.AdditiveNoticeRange.x), Mathf.Abs(hb.AdditiveNoticeRange.z)) * scale;
            if (hb.HitboxType == EnemyAttackHitboxType.Sphere)
                Handles.DrawWireDisc(localCenter, Vector3.forward, (size.x + Mathf.Max(Mathf.Abs(hb.AdditiveNoticeRange.x), Mathf.Abs(hb.AdditiveNoticeRange.y), Mathf.Abs(hb.AdditiveNoticeRange.z)) * scale) * .5f);
            else Handles.DrawWireCube(localCenter, new Vector3(size.x + addition.x, size.y + addition.y, 0));
        }
        Handles.color = Cyan; Handles.DrawLine(origin - rect.position, origin - rect.position + Vector2.up * 30);
        Handles.EndGUI(); GUI.EndClip();
        EditorGUI.DrawRect(new Rect(origin.x - 4, origin.y - 4, 8, 8), Cyan);
        EditorGUI.DrawRect(new Rect(center.x - 5, center.y - 5, 10, 10), Red);
        Vector2 corner = center + size * .5f;
        EditorGUI.DrawRect(new Rect(corner.x - 4, corner.y - 4, 8, 8), Color.white);
        GUI.Label(new Rect(rect.x + 7, rect.y + 4, rect.width - 14, 20), "평면도 · 위쪽 = 전방 +Z · 격자 1m", EditorStyles.miniLabel);
        GUI.Label(new Rect(rect.x + 7, rect.yMax - 22, rect.width - 14, 20), "빨간 점: 위치 이동  /  흰 점: 크기 조정", EditorStyles.miniLabel);
        int id = GUIUtility.GetControlID("EnemyAttackPlan".GetHashCode(), FocusType.Passive, rect);
        var ev = Event.current;
        if (GUI.enabled && ev.type == EventType.MouseDown && ev.button == 0 && rect.Contains(ev.mousePosition) &&
            ((ev.mousePosition - center).magnitude < 14 || (ev.mousePosition - corner).magnitude < 14))
        {
            _dragSize = (ev.mousePosition - corner).magnitude < (ev.mousePosition - center).magnitude;
            _planScale = scale; _planOrigin = origin; _planCenter = center;
            _planControl = id; GUIUtility.hotControl = id; Undo.IncrementCurrentGroup(); _dragUndo = Undo.GetCurrentGroup(); ev.Use();
        }
        if (GUI.enabled && _planControl == id && GUIUtility.hotControl == id && ev.type == EventType.MouseDrag)
        {
            Undo.RecordObject(_attack, "적 공격 판정 조정");
            if (_dragSize)
            {
                var delta = (ev.mousePosition - _planCenter) * 2 / _planScale;
                if (hb.HitboxType == EnemyAttackHitboxType.Sphere) hb.Size = Vector3.one * Mathf.Max(.01f, Mathf.Abs(delta.x), Mathf.Abs(delta.y));
                else hb.Size = new Vector3(Mathf.Max(.01f, Mathf.Abs(delta.x)), hb.Size.y, Mathf.Max(.01f, Mathf.Abs(delta.y)));
            }
            else { var pos = (ev.mousePosition - _planOrigin) / _planScale; hb.Offset = new Vector3(pos.x, hb.Offset.y, -pos.y); }
            _attack.HitboxInfo[_hitbox] = hb; Mark(_attack); ev.Use();
        }
        if (_planControl == id && ev.type == EventType.MouseUp) { EndDrag(); ev.Use(); }
    }

    private void DrawEnemySettings()
    {
        Section("대상 적의 기본 설정");
        EditorGUILayout.HelpBox("아래 값은 선택한 적 인스턴스에 적용됩니다. 공유 공격 데이터와 적용 범위가 다릅니다.", MessageType.None);
        EditComponent(_enemy, so =>
        {
            FloatField(so, "_maxHP", "최대 체력", 1);
            Property(so, "_minimumStaggerLevel", "기본 경직 기준");
            Property(so, "_resistantMinimumStaggerLevel", "저항 중 경직 기준");
            IntField(so, "_minConsecutiveHitReactions", "연속 피격 최소 횟수", 1);
            IntField(so, "_maxConsecutiveHitReactions", "연속 피격 최대 횟수", so.FindProperty("_minConsecutiveHitReactions").intValue);
            FloatField(so, "_hitReactionChainResetDelay", "연속 피격 초기화 (초)", 0);
            RangeField(so, "_hitReactionResistanceDurationRange", "경직 저항 유지 (초)");
        });
        Section("플레이어 감지");
        EditComponent(_enemy.GetComponent<EnemyTargetDetector>(), so =>
        {
            FloatField(so, "_detectRange", "감지 거리 (m)", 0);
            FloatField(so, "_lostRange", "추적 종료 거리 (m)", so.FindProperty("_detectRange").floatValue);
            Property(so, "_targetLayer", "감지 레이어");
        });
        Section("씬 전체의 공격 리듬");
        var rhythm = UnityEngine.Object.FindAnyObjectByType<EnemyAttackTimingController>();
        if (rhythm == null) EditorGUILayout.HelpBox("씬에 EnemyAttackTimingController가 없습니다.", MessageType.None);
        else
        {
            EditorGUILayout.ObjectField("공유 컨트롤러", rhythm, typeof(EnemyAttackTimingController), true);
            EditComponent(rhythm, so =>
            {
                RangeField(so, "_initialAttackDelayRange", "첫 공격 대기 (초)");
                RangeField(so, "_repeatAttackDelayRange", "재공격 대기 (초)");
                RangeField(so, "_betweenAttackGapRange", "적 사이 공격 간격 (초)");
                RangeField(so, "_urgentAttackDelayRange", "긴급 공격 대기 (초)");
            });
        }
        Section("씬 전체의 거리 · 포위");
        var positioning = UnityEngine.Object.FindAnyObjectByType<EnemyPositioningController>();
        if (positioning != null) EditComponent(positioning, so =>
        {
            FloatField(so, "_minimumCombatRadius", "최소 교전 거리", 0);
            FloatField(so, "_maximumCombatRadius", "최대 교전 거리", so.FindProperty("_minimumCombatRadius").floatValue);
            FloatField(so, "_minimumAttackDistance", "공격 가능 최소 거리", 0);
            FloatField(so, "_maximumAttackDistance", "공격 가능 최대 거리", so.FindProperty("_minimumAttackDistance").floatValue);
            FloatField(so, "_personalSpaceRadius", "적 사이 간격", .1f);
            FloatField(so, "_orbitInfluence", "측면 이동 강도", 0);
        });
    }

    private void DrawSceneHandles(SceneView scene)
    {
        if (!_sceneHandles || _enemy == null || _attack == null || EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.IsPersistent(_enemy)) return;
        if (_attack.HitboxInfo == null || _hitbox < 0 || _hitbox >= _attack.HitboxInfo.Length) return;
        var hb = _attack.HitboxInfo[_hitbox];
        var pool = _enemy.GetComponent<EnemyHitboxPool>();
        var origin = pool != null ? new SerializedObject(pool).FindProperty("_hitboxContainer")?.objectReferenceValue as Transform : null;
        using (new Handles.DrawingScope(Red, (origin != null ? origin : _enemy.transform).localToWorldMatrix))
        {
            EditorGUI.BeginChangeCheck();
            if (_handleMode == 0) hb.Offset = Handles.PositionHandle(hb.Offset, Quaternion.identity);
            else if (hb.HitboxType == EnemyAttackHitboxType.Box)
            {
                _boxHandle.center = hb.Offset; _boxHandle.size = Abs(hb.Size); _boxHandle.DrawHandle();
                hb.Size = _boxHandle.size; hb.Offset = _boxHandle.center;
            }
            else
            {
                float radius = Mathf.Max(hb.Size.x, hb.Size.y, hb.Size.z) * .5f;
                if (radius <= 0) radius = Mathf.Max(0, hb.Radius.x);
                radius = Handles.RadiusHandle(Quaternion.identity, hb.Offset, radius);
                hb.Size = Vector3.one * Mathf.Max(.01f, radius * 2);
            }
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(_attack, "적 공격 Scene 판정 편집"); _attack.HitboxInfo[_hitbox] = hb; Mark(_attack); }
            if (hb.HitboxType == EnemyAttackHitboxType.Box) Handles.DrawWireCube(hb.Offset, Abs(hb.Size));
            else Handles.DrawWireDisc(hb.Offset, Vector3.up, Mathf.Max(hb.Size.x, hb.Size.y, hb.Size.z) * .5f);
            Handles.Label(hb.Offset + Vector3.up, "선택한 공격 판정 · 적 로컬 기준 (워핑 전)");
        }
    }

    private void EditComponent(UnityEngine.Object component, Action<SerializedObject> draw)
    {
        if (component == null) return;
        var so = new SerializedObject(component); so.Update(); draw(so); Apply(so);
    }
    private void Apply(SerializedObject so) { if (so.ApplyModifiedProperties()) Mark(so.targetObject); }
    private void Mark(UnityEngine.Object obj)
    {
        EditorUtility.SetDirty(obj); if (!_edited.Contains(obj)) _edited.Add(obj);
        if (obj is Component component && !EditorUtility.IsPersistent(component))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
        Changed();
    }
    private void SaveEdits()
    {
        var scenes = new HashSet<UnityEngine.SceneManagement.Scene>();
        foreach (var obj in _edited.ToArray())
        {
            if (obj == null) continue;
            if (EditorUtility.IsPersistent(obj)) AssetDatabase.SaveAssetIfDirty(obj);
            else if (obj is Component component) scenes.Add(component.gameObject.scene);
        }
        if (_attack != null) AssetDatabase.SaveAssetIfDirty(_attack);
        if (_clip != null) AssetDatabase.SaveAssetIfDirty(_clip);
        bool saved = true;
        foreach (var scene in scenes) if (scene.IsValid() && scene.isDirty) saved &= EditorSceneManager.SaveScene(scene);
        if (saved) _edited.Clear();
        ShowNotification(new GUIContent(saved ? "공격 설정과 편집한 대상 저장 완료" : "씬 저장을 완료하지 못했습니다."));
    }
    private static void Section(string title) { GUILayout.Space(12); GUILayout.Label(title, EditorStyles.boldLabel); GUILayout.Space(3); }
    private static void Property(SerializedObject so, string name, string label) { var p = so.FindProperty(name); if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label), true); }
    private static void FloatField(SerializedObject so, string name, string label, float min) { var p = so.FindProperty(name); if (p == null) return; EditorGUI.BeginChangeCheck(); float v = EditorGUILayout.FloatField(label, p.floatValue); if (EditorGUI.EndChangeCheck() && GUI.enabled) p.floatValue = Mathf.Max(min, v); }
    private static void IntField(SerializedObject so, string name, string label, int min) { var p = so.FindProperty(name); if (p == null) return; EditorGUI.BeginChangeCheck(); int v = EditorGUILayout.IntField(label, p.intValue); if (EditorGUI.EndChangeCheck() && GUI.enabled) p.intValue = Mathf.Max(min, v); }
    private static void RangeField(SerializedObject so, string name, string label) { var p = so.FindProperty(name); if (p == null) return; EditorGUI.BeginChangeCheck(); var v = EditorGUILayout.Vector2Field(label + " (최소 / 최대)", p.vector2Value); if (EditorGUI.EndChangeCheck() && GUI.enabled) { v.x = Mathf.Max(0, v.x); v.y = Mathf.Max(v.x, v.y); p.vector2Value = v; } }
    private static void VectorField(SerializedProperty parent, string name, string label, bool positive) { var p = parent.FindPropertyRelative(name); EditorGUI.BeginChangeCheck(); var v = EditorGUILayout.Vector3Field(label, p.vector3Value); if (EditorGUI.EndChangeCheck() && GUI.enabled) p.vector3Value = positive ? Abs(v) : v; }
    private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    private static GUIStyle CenterLabel() => new GUIStyle(EditorStyles.whiteLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
}
