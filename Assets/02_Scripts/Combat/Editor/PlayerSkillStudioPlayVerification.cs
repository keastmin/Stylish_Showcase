using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>Exercises the scene's real E state, then exits Play mode to discard test settings.</summary>
[InitializeOnLoad]
public static class PlayerSkillStudioPlayVerification
{
    private const string Pending = "ProjectR.EStudio.PlayVerification";
    private const string Report = "Temp/PlayerSkillStudioPlayVerification.result";
    private static PlayerCore _player;
    private static PlayableDirector _director;
    private static PlayerSkillSequenceSettings _settings;
    private static PlayerSkillCameraDirector _camera;
    private static int _phase;
    private static int _lastFrame = -1;
    private static double _deadline, _freezeStart, _freezeTime;
    private static double _maxTime;
    private static float _elapsed, _expected, _initialFov, _recoveryElapsed;
    private static Vector3 _initialPosition;
    private static Quaternion _initialRotation;
    private static bool _froze, _released, _runInBackground, _sawCameraMotion, _recoveryStarted, _frozeRecovery;

    static PlayerSkillStudioPlayVerification() => EditorApplication.update += Tick;

    [MenuItem("Tools/Character/Player/Validate E Skill Studio in Play Mode")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Start E validation in Edit mode.");
        File.WriteAllText(Report, "Running E runtime validation\n");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || EditorApplication.isCompiling ||
            !EditorApplication.isPlaying || Time.frameCount < 5 || _lastFrame == Time.frameCount) return;
        _lastFrame = Time.frameCount;
        try
        {
            if (_player == null)
            {
                _player = UnityEngine.Object.FindAnyObjectByType<PlayerCore>();
                Check(_player != null, "Scene player exists");
                _runInBackground = Application.runInBackground;
                Application.runInBackground = true;
                _player.InputCollector.enabled = false;
                foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyCore>())
                    enemy.gameObject.SetActive(false);
                _director = _player.DirectorContainer.Directors[DirectorID.Skill];
                _settings = _director.GetComponent<PlayerSkillSequenceSettings>();
                Check(_settings != null, "Scene sequence settings connected");
                _camera = _director.GetComponent<PlayerSkillCameraDirector>();
                Check(_camera != null && _camera.SkillCamera != null, "Scene camera connected");
                _initialFov = _camera.SkillCamera.Lens.FieldOfView;
                _initialPosition = _camera.SkillCamera.transform.position;
                _initialRotation = _camera.SkillCamera.transform.rotation;
                File.AppendAllText(Report, PlayerSkillStudioAuthoringVerification.RunEnabledHitRuntime() + "\n");
                _phase = 0;
                Begin();
                return;
            }
            Check(EditorApplication.timeSinceStartup < _deadline, "Cast completes without getting stuck");
            _maxTime = Math.Max(_maxTime, _director.time);
            if (!_player.IsHitStopped)
            {
                _elapsed += CombatTimeController.DeltaTime;
                if (_recoveryStarted) _recoveryElapsed += CombatTimeController.DeltaTime;
            }
            if (_director.time > .4 && _director.time < 1.2)
                _sawCameraMotion |= Mathf.Abs(_camera.SkillCamera.Lens.FieldOfView - _initialFov) > .5f;
            if (_phase == 1 && !_froze && _director.time > .35)
            {
                _player.BeginHitStop();
                _froze = true;
                _freezeStart = EditorApplication.timeSinceStartup;
                _freezeTime = _director.time;
                return;
            }
            if (_froze && !_released)
            {
                Check(_player.StateMachine.IsSkillActive, "Hitstop does not exit E");
                Check(Math.Abs(_director.time - _freezeTime) < .001, "Timeline stays frozen during hitstop");
                if (EditorApplication.timeSinceStartup - _freezeStart < .15) return;
                _player.EndHitStop();
                _released = true;
            }
            if (_phase == 1 && _director.state != PlayState.Playing && !_recoveryStarted)
            {
                Check(_player.StateMachine.IsSkillActive, "Recovery starts before returning to idle");
                _recoveryStarted = true;
                _recoveryElapsed = 0;
                _player.BeginHitStop();
                _freezeStart = EditorApplication.timeSinceStartup;
                _frozeRecovery = true;
                return;
            }
            if (_frozeRecovery)
            {
                Check(_player.StateMachine.IsSkillActive, "Recovery stays active during hitstop");
                if (EditorApplication.timeSinceStartup - _freezeStart < .15) return;
                _player.EndHitStop();
                _frozeRecovery = false;
            }
            if (_player.StateMachine.IsSkillActive) return;
            Check(_maxTime >= (_phase == 0 ? _director.duration : 1.45) - .15,
                "State reaches configured sequence endpoint");
            Check(_elapsed + .08f >= _expected, "Recovery is not skipped");
            if (_phase == 1) Check(_recoveryElapsed >= .15f, "Recovery counts combat time after hitstop");
            Check(_sawCameraMotion, "Authored camera FOV changes during E playback");
            Check(_director.state != PlayState.Playing, "Director stops on state exit");
            Check(Mathf.Abs(_camera.SkillCamera.Lens.FieldOfView - _initialFov) < .001f, "Camera FOV restored");
            Check(Vector3.Distance(_camera.SkillCamera.transform.position, _initialPosition) < .001f &&
                Quaternion.Angle(_camera.SkillCamera.transform.rotation, _initialRotation) < .01f, "Camera pose restored");
            File.AppendAllText(Report, $"PASS case {_phase}: maxTimeline={_maxTime:F3}, elapsed={_elapsed:F3}, expected={_expected:F3}\n");
            if (++_phase < 2) Begin();
            else Finish(null);
        }
        catch (Exception exception) { Finish(exception); }
    }

    private static void Begin()
    {
        var settings = new SerializedObject(_settings);
        settings.FindProperty("_useTimelineDuration").boolValue = _phase == 0;
        settings.FindProperty("_endTime").floatValue = 1.45f;
        settings.FindProperty("_recoveryDuration").floatValue = _phase == 0 ? 0 : .2f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        // Nontrivial camera motion must run and then restore its lens and transform.
        var camera = new SerializedObject(_camera);
        camera.FindProperty("_motionEnabled").boolValue = true;
        var keys = camera.FindProperty("_motionKeys");
        keys.arraySize = 2;
        for (int i = 0; i < 2; i++)
        {
            var key = keys.GetArrayElementAtIndex(i);
            key.FindPropertyRelative("time").floatValue = i;
            key.FindPropertyRelative("offset").vector3Value = new Vector3(i * 2, 5, -8 + i);
            key.FindPropertyRelative("euler").vector3Value = new Vector3(16, i * 10, 0);
            key.FindPropertyRelative("fieldOfView").floatValue = 50 + i * 15;
        }
        camera.ApplyModifiedPropertiesWithoutUndo();
        _player.AddSkillGauge(100);
        _expected = (float)_settings.GetEndTime(_director);
        _elapsed = _recoveryElapsed = 0;
        _maxTime = 0;
        _froze = _released = false;
        _sawCameraMotion = _recoveryStarted = _frozeRecovery = false;
        _player.StateMachine.Transition(_player.StateMachine.SkillState);
        _deadline = EditorApplication.timeSinceStartup + 15;
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Finish(Exception exception)
    {
        SessionState.SetBool(Pending, false);
        if (_player != null) _player.EndHitStop();
        Application.runInBackground = _runInBackground;
        string result = exception == null ? "PASS: automatic duration, manual cutoff, recovery, hitstop, camera motion cleanup" : "FAIL: " + exception;
        File.AppendAllText(Report, result + "\n");
        if (exception == null) Debug.Log(result); else Debug.LogError(result);
        EditorApplication.ExitPlaymode();
        _player = null;
    }
}
