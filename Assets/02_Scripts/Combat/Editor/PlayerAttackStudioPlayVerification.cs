using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>Exercises real combo and ordinary dodge input paths, then discards temporary scene changes.</summary>
[InitializeOnLoad]
public static class PlayerAttackStudioPlayVerification
{
    private const string Key = "ProjectR.AttackStudios.PlayVerification";
    private const string ReportPath = "Temp/PlayerAttackStudioPlayVerification.result";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly int[] ExpectedHits = { 1, 1, 2, 3 };
    private static PlayerCore _player;
    private static PlayerSkillHitTestActor _victim;
    private static PlayableDirector _director;
    private static PlayerStateBase[] _basicStates;
    private static int _stage, _startHits, _lastFrame = -1, _checks;
    private static double _deadline, _freezeStarted, _frozenTime, _stageMaxTime;
    private static Vector3 _frozenPosition;
    private static bool _backgroundCaptured, _oldBackground, _manualFreeze, _freezeDone;

    static PlayerAttackStudioPlayVerification() => EditorApplication.update += Tick;

    [MenuItem("Tools/Character/Player/Validate Attack Studios in Play Mode")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Start Attack Studio gameplay validation in Edit Mode.");
        Directory.CreateDirectory("Temp");
        _player = null;
        _victim = null;
        _director = null;
        _lastFrame = -1;
        _checks = 0;
        _stage = 0;
        _backgroundCaptured = _manualFreeze = _freezeDone = false;
        File.WriteAllText(ReportPath, "Running real combo and ordinary dodge attack validation\n");
        SessionState.SetString(Key + ".snapshot", PlayerAttackStudioAuthoringVerification.CaptureAuthoredSnapshot());
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || EditorApplication.isCompiling || !EditorApplication.isPlaying) return;
        try
        {
            // Enable background frames before waiting for startup frames: otherwise an
            // unfocused Game view can remain at frame 1 forever during automated checks.
            if (!_backgroundCaptured)
            {
                _oldBackground = Application.runInBackground;
                _backgroundCaptured = true;
                Application.runInBackground = true;
                _deadline = EditorApplication.timeSinceStartup + 45;
            }
            Check(EditorApplication.timeSinceStartup < _deadline, "Runtime stage completes within its watchdog");
            if (Time.frameCount < 5 || _lastFrame == Time.frameCount) return;
            _lastFrame = Time.frameCount;
            if (_player == null) { Initialize(); return; }

            Check(!_player.IsPerfectDodgeActive && !_player.IsPerfectDodgeWindowOpen && _player.DodgeAttackTarget == null,
                "Ordinary actions never activate perfect-dodge counter state");
            foreach (var id in new[] { DirectorID.DodgeAttackStart, DirectorID.DodgeAttackLoop, DirectorID.DodgeAttackEnd, DirectorID.Skill })
                Check(_player.DirectorContainer.Directors[id].state != PlayState.Playing,
                    "Unrelated Timeline remains stopped: " + id);

            if (_director != null) _stageMaxTime = Math.Max(_stageMaxTime, _director.time);
            if (_manualFreeze)
            {
                Check(_player.IsHitStopped && _player.Animator.speed == 0 && Math.Abs(_director.time - _frozenTime) < .001,
                    "Actual player hitstop freezes Animator and Timeline");
                Check(Vector3.Distance(_player.transform.position, _frozenPosition) < .001,
                    "Actual player hitstop freezes root motion");
                if (EditorApplication.timeSinceStartup - _freezeStarted < .15d) return;
                _player.EndHitStop();
                _manualFreeze = false;
                _freezeDone = true;
                return;
            }

            if (_stage < 4) TickCombo();
            else if (_stage == 4 || _stage == 6) EnterOrdinaryAttack();
            else TickOrdinaryAttack();
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void Initialize()
    {
        _player = UnityEngine.Object.FindAnyObjectByType<PlayerCore>();
        Check(_player != null && _player.StateMachine != null, "Scene player and actual state machine initialize");
        _player.InputCollector.enabled = false;
        SetInput("_moveValue", Vector2.zero);
        foreach (string name in new[] { "_attackValue", "_dodgeValue", "_skillValue", "_ultimateValue" }) SetInput(name, false);
        foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyCore>()) enemy.gameObject.SetActive(false);
        _player.OnPerfectDodgeCheck = () => null;

        var victimObject = new GameObject("Attack Studio verification victim (temporary)") { layer = 6 };
        victimObject.transform.position = _player.transform.position + Vector3.forward * 10;
        var box = victimObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = Vector3.one * 100;
        var sphere = victimObject.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = 50;
        _victim = victimObject.AddComponent<PlayerSkillHitTestActor>();
        _victim.Health = 100000;
        var data = new SerializedObject(_player.AttackInstanceContainer);
        foreach (string name in new[] { "_basicAttack1DamageField", "_basicAttack2DamageField", "_basicAttack3DamageField",
            "_basicAttack4DamageField", "_runAttack1HitDamageField", "_runAttack2HitDamageField" })
        {
            var field = data.FindProperty(name);
            Check(field != null && field.FindPropertyRelative("_hitbox").objectReferenceValue != null, "Actual query hitbox is bound: " + name);
            field.FindPropertyRelative("_targetLayers").intValue = 1 << 6;
            field.FindPropertyRelative("_triggerInteraction").intValue = (int)QueryTriggerInteraction.Collide;
            field.FindPropertyRelative("_hitStopFrame").intValue = 2;
            field.FindPropertyRelative("_hitStopMode").intValue = (int)AttackHitStopMode.AttackerAndVictims;
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        Physics.SyncTransforms();
        _basicStates = new PlayerStateBase[] { _player.StateMachine.BasicAttack1State, _player.StateMachine.BasicAttack2State,
            _player.StateMachine.BasicAttack3State, _player.StateMachine.BasicAttack4State };
        _stage = 0;
        InjectAttack();
        Check(CurrentState == _basicStates[0], "Idle attack input enters Basic Attack 1 through PlayerCore.Update");
        BeginBasicStage();
    }

    private static void TickCombo()
    {
        int hits = _victim.Hits.Count - _startHits;
        Check(hits <= ExpectedHits[_stage], "Combo signals never multiply damage for the victim's two colliders");
        if (_stage < 3)
        {
            Check(CurrentState == _basicStates[_stage], "Combo remains in the expected tier before its input window");
            bool open = (bool)typeof(PlayerBasicAttackState).GetField("_isNextComboEnable", PrivateInstance).GetValue(CurrentState);
            if (!open || _player.IsHitStopped) return;
            Check(hits == ExpectedHits[_stage], "Tier " + (_stage + 1) + " delivers its complete authored hit count before combo input");
            var previous = _director;
            Log($"PASS Basic {_stage + 1}: {hits} hit(s), Timeline reached {_stageMaxTime:F3}s");
            InjectAttack();
            _stage++;
            Check(CurrentState == _basicStates[_stage], "Real combo-window attack input enters tier " + (_stage + 1));
            Check(previous.state != PlayState.Playing, "Previous combo Timeline stops on transition");
            BeginBasicStage();
        }
        else
        {
            if (CurrentState == _basicStates[3]) return;
            Check(CurrentState == _player.StateMachine.IdleState && hits == 3,
                "Basic Attack 4 delivers three hits and returns to Idle through its existing normalized-time finish");
            Check(_freezeDone && _director.state != PlayState.Playing && _stageMaxTime > 0,
                "Combo playback resumes after hitstop and cleans up its final Timeline");
            Log($"PASS Basic 4: {hits} hits; full combo total {_victim.Hits.Count}");
            BeginDodge(false);
        }
    }

    private static void BeginBasicStage()
    {
        _director = _player.DirectorContainer.Directors[(DirectorID)((int)DirectorID.BasicAttack1 + _stage)];
        _startHits = _victim.Hits.Count;
        _stageMaxTime = 0;
        _deadline = EditorApplication.timeSinceStartup + 45;
        if (_stage == 2)
        {
            _player.BeginHitStop();
            _manualFreeze = true;
            _freezeStarted = EditorApplication.timeSinceStartup;
            _frozenTime = _director.time;
            _frozenPosition = _player.transform.position;
        }
    }

    private static void BeginDodge(bool back)
    {
        _stage = back ? 6 : 4;
        _director = null;
        _deadline = EditorApplication.timeSinceStartup + 45;
        var state = back ? (PlayerStateBase)_player.StateMachine.BackDodgeState : _player.StateMachine.FrontDodgeState;
        Check(_player.StateMachine.Transition(state), "A real ordinary " + (back ? "back" : "front") + " dodge consumes an available charge");
        Check(CurrentState == state && !_player.IsPerfectDodgeActive, "Dodge begins without a perfect-dodge success");
    }

    private static void EnterOrdinaryAttack()
    {
        bool back = _stage == 6;
        Check(CurrentState == (back ? (PlayerStateBase)_player.StateMachine.BackDodgeState : _player.StateMachine.FrontDodgeState),
            "Attack input occurs in the ordinary dodge's early input window");
        InjectAttack();
        Check(CurrentState == _player.StateMachine.RunAttackState,
            "Actual attack input routes ordinary " + (back ? "back" : "front") + " dodge to RunAttack");
        _director = _player.DirectorContainer.Directors[DirectorID.RunAttack];
        Check(_director.state == PlayState.Playing, "Ordinary dodge attack starts its connected RunAttack Timeline");
        _startHits = _victim.Hits.Count;
        _stageMaxTime = 0;
        _stage++;
    }

    private static void TickOrdinaryAttack()
    {
        int hits = _victim.Hits.Count - _startHits;
        Check(hits <= 2, "Ordinary dodge hit callbacks preserve one damage application per target per signal");
        if (CurrentState == _player.StateMachine.RunAttackState) return;
        Check(CurrentState == _player.StateMachine.IdleState && hits == 2,
            "Ordinary dodge attack emits both authored hits and reaches Idle via its native end signal");
        Check(_director.state != PlayState.Playing && _stageMaxTime > 1,
            "Ordinary dodge Timeline reaches its end and stops on state exit");
        Log($"PASS {(_stage == 5 ? "front" : "back")} dodge attack: {hits} hits, Timeline reached {_stageMaxTime:F3}s");
        if (_stage == 5) BeginDodge(true);
        else
        {
            Check(_victim.Hits.Count == 11 && _victim.DamageAttempts == 11 && _victim.StopCount > 0,
                "Combo plus both dodge entry paths deliver exactly eleven successful damage events and real victim hitstop");
            Finish(null);
        }
    }

    private static PlayerStateBase CurrentState => (PlayerStateBase)typeof(PlayerStateMachine)
        .GetField("_currentState", PrivateInstance).GetValue(_player.StateMachine);

    private static void SetInput(string field, object value)
        => typeof(PlayerInputCollector).GetField(field, PrivateInstance).SetValue(_player.InputCollector, value);

    private static void InjectAttack()
    {
        SetInput("_attackValue", true);
        try { _player.SendMessage("Update", SendMessageOptions.RequireReceiver); }
        finally { SetInput("_attackValue", false); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message + $" (stage {_stage}, frame {Time.frameCount})");
        _checks++;
    }

    private static void Log(string message) => File.AppendAllText(ReportPath, message + "\n");

    private static void Finish(Exception exception)
    {
        SessionState.SetBool(Key, false);
        if (_player != null) _player.EndHitStop();
        if (_victim != null) UnityEngine.Object.Destroy(_victim.gameObject);
        if (_backgroundCaptured) Application.runInBackground = _oldBackground;
        try
        {
            Check(SessionState.GetString(Key + ".snapshot", string.Empty) == PlayerAttackStudioAuthoringVerification.CaptureAuthoredSnapshot(),
                "Gameplay verification preserves all authored player files, including perfect dodge, E and Q");
        }
        catch (Exception snapshotError) { if (exception == null) exception = snapshotError; }
        string result = exception == null
            ? $"PASS: four-tier combo, front/back ordinary dodge attack, damage deduplication, real hitstop, native cleanup and authored-file invariance ({_checks} observations)."
            : "FAIL: " + exception;
        Log(result);
        if (exception == null) Debug.Log(result); else Debug.LogError(result);
        EditorApplication.ExitPlaymode();
        _player = null;
        _victim = null;
        _backgroundCaptured = _manualFreeze = false;
    }
}
