using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectR.UI.Editor
{
    /// <summary>Explicit, real-frame verification. Does not enter Play Mode or save scenes.</summary>
    public static class PauseSettingsVerification
    {
        [Serializable] private sealed class Report
        {
            public bool passed;
            public int checks;
            public string error;
            public List<string> results = new();
        }

        private sealed class HitstopProbe : IHitStopParticipant
        {
            public bool IsHitStopped { get; private set; }
            public void BeginHitStop() => IsHitStopped = true;
            public void EndHitStop() => IsHitStopped = false;
        }

        private enum Phase { Open, Frozen, Close, Duplicate, CutsceneAdvance, CutsceneOpen, CutsceneFrozen, CutsceneClose }
        private static Report _report;
        private static PauseSettingsController _controller;
        private static PlayerCore _player;
        private static Keyboard _keyboard, _previousKeyboard;
        private static GameObject _audioObject, _duplicate;
        private static AudioSource _source;
        private static AudioClip _clip;
        private static VirtualCutsceneDefinition _definition;
        private static VirtualCutscenePlayback _cutscene;
        private static HitstopProbe _attacker, _victim;
        private static readonly List<Behaviour> Cameras = new();
        private static Phase _phase;
        private static double _started, _phaseStarted, _clock;
        private static int _phaseFrame, _releaseKeyFrame;
        private static float _gameTime, _cutsceneTime;
        private static Quaternion _ringRotation;
        private static Vector3 _playerPosition;
        private static float _initialScale, _initialListenerVolume, _initialVolume, _savedPrefVolume;
        private static bool _initialAudioPause, _initialMuted, _initialCursorVisible, _initialBackground;
        private static bool _hadVolumePreference, _hadMutePreference;
        private static int _savedPrefMute;
        private static CursorLockMode _initialCursorLock;
        public static bool Running { get; private set; }
        public static string ReportPath => Path.GetFullPath("output/PauseSettings/runtime-verification.json");

        [MenuItem("Tools/Project R/Pause Settings/Verify Runtime (Play Mode)")]
        public static void StartRuntimeVerification()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Enter a gameplay scene in Play Mode first.");
            if (Running) throw new InvalidOperationException("Pause settings verification is already running.");
            if (PauseSettingsController.IsPaused || VirtualCutscenePlayback.IsPlaying)
                throw new InvalidOperationException("Resume gameplay and finish the current cutscene before verification.");
            _controller = PauseSettingsController.Instance;
            _player = Object.FindAnyObjectByType<PlayerCore>();
            if (_controller == null || _player == null)
                throw new InvalidOperationException("A bootstrapped settings prefab and scene PlayerCore are required.");

            _report = new Report();
            _initialScale = Time.timeScale;
            _initialAudioPause = AudioListener.pause;
            _initialListenerVolume = AudioListener.volume;
            _initialVolume = _controller.MasterVolume;
            _initialMuted = _controller.Muted;
            _initialCursorLock = Cursor.lockState;
            _initialCursorVisible = Cursor.visible;
            _initialBackground = Application.runInBackground;
            _hadVolumePreference = PlayerPrefs.HasKey(PauseSettingsController.VolumePreference);
            _savedPrefVolume = PlayerPrefs.GetFloat(PauseSettingsController.VolumePreference);
            _hadMutePreference = PlayerPrefs.HasKey(PauseSettingsController.MutePreference);
            _savedPrefMute = PlayerPrefs.GetInt(PauseSettingsController.MutePreference);
            _previousKeyboard = Keyboard.current;
            Running = true;
            _started = EditorApplication.timeSinceStartup;
            _releaseKeyFrame = -1;
            try
            {
                CheckReferences();
                Application.runInBackground = true;
                _keyboard = InputSystem.AddDevice<Keyboard>("PauseSettingsVerificationKeyboard");
                _keyboard.MakeCurrent();
                Time.timeScale = .35f;
                AudioListener.pause = true;
                _audioObject = new GameObject("Pause Settings Verification Audio") { hideFlags = HideFlags.DontSave };
                _source = _audioObject.AddComponent<AudioSource>();
                _clip = AudioClip.Create("Pause verification silence", 24000, 1, 24000, false);
                _source.clip = _clip;
                _source.loop = true;
                _source.ignoreListenerPause = true;
                _source.Play();
                Cameras.Clear();
                foreach (var brain in Object.FindObjectsByType<CinemachineBrain>())
                    if (brain.enabled) Cameras.Add(brain);
                foreach (var input in Object.FindObjectsByType<CinemachineInputAxisController>())
                    if (input.enabled) Cameras.Add(input);
                _attacker = new HitstopProbe();
                _victim = new HitstopProbe();
                HitstopCoordinator.Request(_attacker, new[] { _victim }, 10);
                PressEscape();
                SetPhase(Phase.Open);
                EditorApplication.update += Tick;
                Debug.Log("[PauseSettings] Runtime verification started; temporary keyboard and preferences are restored on completion.");
            }
            catch (Exception exception) { Complete(exception); }
        }

        private static void CheckReferences()
        {
            Require(_controller.overlayGroup && _controller.panel && _controller.artwork && _controller.rotatingRing,
                "Animated window references are assigned.");
            Require(_controller.masterSlider && _controller.muteToggle && _controller.volumeValue && _controller.statusLabel &&
                    _controller.resumeButton && _controller.resetButton && _controller.testButton && _controller.previewSource,
                "Every interactive audio control is wired.");
            Require(_controller.GetComponent<Canvas>().sortingOrder > 32760,
                "Settings render above the virtual cutscene overlay.");
            Require(_controller.GetComponent<GraphicRaycaster>() != null, "The settings Canvas receives UGUI input.");
            foreach (TMP_Text label in _controller.GetComponentsInChildren<TMP_Text>(true))
                Require(label.font != null, "TMP font assigned: " + label.name);
            Require(_controller.equalizerBars != null && _controller.equalizerBars.Length > 3,
                "Animated equalizer has multiple bars.");
            Require(!_controller.overlayGroup.blocksRaycasts && _controller.overlayGroup.alpha == 0,
                "Closed settings are transparent to gameplay pointer input.");
        }

        private static void Tick()
        {
            try
            {
                if (!Application.isPlaying || _controller == null) throw new InvalidOperationException("Play Mode ended during verification.");
                if (EditorApplication.timeSinceStartup - _started > 25) throw new TimeoutException("Runtime verification exceeded 25 seconds.");
                if (_releaseKeyFrame >= 0 && Time.frameCount >= _releaseKeyFrame)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                    _releaseKeyFrame = -1;
                }
                EditorApplication.QueuePlayerLoopUpdate();
                switch (_phase)
                {
                    case Phase.Open:
                        if (!PauseSettingsController.IsPaused) return;
                        Require(_controller.IsOpen, "A real ESC key-down opens settings.");
                        Near(Time.timeScale, 0, "ESC stops scaled game time immediately.");
                        Require(AudioListener.pause && Cursor.lockState == CursorLockMode.None && Cursor.visible,
                            "Pause silences world audio and releases the cursor.");
                        _gameTime = Time.time;
                        _clock = VirtualCutscenePlayback.CombatUnscaledTime;
                        _ringRotation = _controller.rotatingRing.localRotation;
                        _playerPosition = _player.transform.position;
                        SetPhase(Phase.Frozen);
                        break;
                    case Phase.Frozen:
                        if (!Elapsed(.55)) return;
                        Near(Time.time, _gameTime, "Scaled game time stays frozen over real frames.");
                        Near((float)VirtualCutscenePlayback.CombatUnscaledTime, (float)_clock,
                            "Hitstop deadlines retain their remaining time while settings are open.");
                        Require(_attacker.IsHitStopped && _victim.IsHitStopped, "A short hitstop survives a longer settings pause.");
                        Require(Vector3.Distance(_playerPosition, _player.transform.position) < .002f,
                            "The player remains in place during the pause.");
                        Require(_player.InputCollector.MoveValue == Vector2.zero && !_player.InputCollector.IsInputAttack &&
                                !_player.InputCollector.IsInputDodge && !_player.InputCollector.IsInputSkill &&
                                !_player.InputCollector.IsInputUltimate, "Gameplay actions are cleared while the window is open.");
                        Require(_controller.overlayGroup.alpha > .99f && _controller.overlayGroup.interactable &&
                                _controller.overlayGroup.blocksRaycasts, "The opening animation completes at timeScale zero.");
                        Require(Quaternion.Angle(_ringRotation, _controller.rotatingRing.localRotation) > .2f,
                            "Decorative rotation continues while game time is stopped.");
                        foreach (var camera in Cameras) Require(camera == null || !camera.enabled, "Camera controller is suspended: " + camera?.name);
                        Require(!_source.isPlaying, "Audio that ignores listener pause is explicitly suspended.");
                        VerifyAudioControls();
                        PressEscape();
                        SetPhase(Phase.Close);
                        break;
                    case Phase.Close:
                        if (!Elapsed(.55) || PauseSettingsController.IsPaused) return;
                        Near(Time.timeScale, .35f, "ESC restores the previous fractional time scale.");
                        Require(AudioListener.pause, "ESC preserves a pre-existing audio pause.");
                        Require(!_controller.overlayGroup.blocksRaycasts && _controller.overlayGroup.alpha == 0,
                            "The close animation relinquishes the pointer blocker.");
                        Require(!_attacker.IsHitStopped && !_victim.IsHitStopped, "Hitstop resumes its remaining deadline after closing.");
                        Require(_source.isPlaying, "Previously playing ignore-pause audio resumes.");
                        foreach (var camera in Cameras) Require(camera == null || camera.enabled, "Camera controller restored: " + camera?.name);
                        var initial = _controller;
                        PauseSettingsBootstrap.EnsureForGameplay();
                        PauseSettingsBootstrap.EnsureForGameplay();
                        Require(PauseSettingsController.Instance == initial, "Repeated bootstrap calls reuse the current controller.");
                        _duplicate = Object.Instantiate(Resources.Load<GameObject>(PauseSettingsBootstrap.ResourcePath));
                        SetPhase(Phase.Duplicate);
                        break;
                    case Phase.Duplicate:
                        if (!Elapsed(.1)) return;
                        Require(Object.FindObjectsByType<PauseSettingsController>().Length == 1,
                            "A duplicate prefab destroys itself without replacing the active settings instance.");
                        _controller.Open();
                        _controller.enabled = false;
                        Require(!PauseSettingsController.IsPaused, "Disabling the controller releases pause immediately.");
                        Near(Time.timeScale, .35f, "Disable cleanup restores the previous fractional scale.");
                        Require(AudioListener.pause, "Disable cleanup retains pre-existing audio ownership.");
                        _controller.enabled = true;
                        AudioListener.pause = false;
                        _definition = ScriptableObject.CreateInstance<VirtualCutsceneDefinition>();
                        _definition.duration = 10;
                        _definition.allowSkip = true;
                        Require(VirtualCutscenePlayback.TryPlay(_definition, _player, null), "A temporary cutscene starts for nested pause verification.");
                        _cutscene = VirtualCutscenePlayback.Active;
                        SetPhase(Phase.CutsceneAdvance);
                        break;
                    case Phase.CutsceneAdvance:
                        if (!Elapsed(.25)) return;
                        Require(_cutscene != null && _cutscene.Elapsed > 0, "The temporary cutscene advances before pausing.");
                        PressEscape();
                        SetPhase(Phase.CutsceneOpen);
                        break;
                    case Phase.CutsceneOpen:
                        if (!PauseSettingsController.IsPaused) return;
                        Require(VirtualCutscenePlayback.Active == _cutscene, "ESC opens settings instead of skipping an active cutscene.");
                        _cutsceneTime = _cutscene.Elapsed;
                        _clock = VirtualCutscenePlayback.CombatUnscaledTime;
                        SetPhase(Phase.CutsceneFrozen);
                        break;
                    case Phase.CutsceneFrozen:
                        if (!Elapsed(.45)) return;
                        Near(_cutscene.Elapsed, _cutsceneTime, "The unscaled cutscene clock freezes under settings.");
                        Near((float)VirtualCutscenePlayback.CombatUnscaledTime, (float)_clock,
                            "Nested settings and cutscene pause do not move the combat clock.");
                        PressEscape();
                        SetPhase(Phase.CutsceneClose);
                        break;
                    case Phase.CutsceneClose:
                        if (!Elapsed(.5) || PauseSettingsController.IsPaused) return;
                        Require(VirtualCutscenePlayback.Active == _cutscene && _cutscene.Elapsed > _cutsceneTime,
                            "Closing settings resumes the same cutscene without skipping it.");
                        Near(Time.timeScale, 0, "Closing settings leaves the cutscene's world pause intact.");
                        Require(AudioListener.pause, "Closing settings leaves the cutscene's audio pause intact.");
                        _cutscene.Finish(false);
                        _cutscene = null;
                        Near(Time.timeScale, .35f, "Finishing the cutscene restores the original world speed.");
                        Require(!AudioListener.pause, "Finishing the cutscene restores its prior audio state.");
                        Require(VirtualCutscenePlayback.TryPlay(_definition, _player, null),
                            "A second temporary cutscene starts for cancellation cleanup.");
                        _cutscene = VirtualCutscenePlayback.Active;
                        _controller.Open();
                        _cutscene.Finish(false);
                        _cutscene = null;
                        Near(Time.timeScale, 0, "External cutscene cancellation retains the open settings pause.");
                        Require(PauseSettingsController.IsPaused && AudioListener.pause,
                            "Settings retain ownership when the paused cutscene is cancelled.");
                        _controller.CloseImmediately();
                        Near(Time.timeScale, .35f, "Closing after cutscene cancellation restores the underlying world speed.");
                        Require(!AudioListener.pause, "Closing after cutscene cancellation restores underlying audio state.");
                        Complete(null);
                        break;
                }
            }
            catch (Exception exception) { Complete(exception); }
        }

        private static void VerifyAudioControls()
        {
            _controller.muteToggle.isOn = false;
            _controller.masterSlider.value = .37f;
            Near(AudioListener.volume, .37f, "The actual Slider event applies master volume.");
            Require(_controller.volumeValue.text == "37%", "The volume label shows the applied percentage.");
            _controller.muteToggle.isOn = true;
            Near(AudioListener.volume, 0, "The actual Toggle event mutes audio.");
            Near(_controller.masterSlider.value, .37f, "Muting preserves the user's slider setting.");
            _controller.muteToggle.isOn = false;
            Near(AudioListener.volume, .37f, "Unmuting restores the slider volume.");
            Near(PlayerPrefs.GetFloat(PauseSettingsController.VolumePreference), .37f, "Volume changes persist in PlayerPrefs.");
            Require(PlayerPrefs.GetInt(PauseSettingsController.MutePreference) == 0, "Mute changes persist independently.");
            _controller.SetMasterVolume(-1);
            Near(AudioListener.volume, 0, "Out-of-range negative volume clamps to silence.");
            _controller.SetMasterVolume(2);
            Near(AudioListener.volume, 1, "Out-of-range positive volume clamps to full volume.");
            _controller.masterSlider.value = .37f;
            _controller.muteToggle.isOn = true;
            _controller.resetButton.onClick.Invoke();
            Near(_controller.MasterVolume, 1, "The Reset button restores default master volume.");
            Require(!_controller.Muted && !_controller.muteToggle.isOn, "The Reset button clears mute.");
            Require(_controller.previewSource.ignoreListenerPause && !_controller.previewSource.ignoreListenerVolume,
                "Audition audio works during pause and obeys the master volume.");
            _controller.testButton.onClick.Invoke();
            Require(_controller.previewSource.isPlaying, "The Test Sound button starts audition playback while paused.");
        }

        private static void PressEscape()
        {
            _keyboard.MakeCurrent();
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Escape));
            _releaseKeyFrame = Time.frameCount + 2;
        }

        private static void SetPhase(Phase phase)
        {
            _phase = phase;
            _phaseStarted = EditorApplication.timeSinceStartup;
            _phaseFrame = Time.frameCount;
        }

        private static bool Elapsed(double seconds) => Time.frameCount >= _phaseFrame + 3 && EditorApplication.timeSinceStartup - _phaseStarted >= seconds;
        private static void Near(float actual, float expected, string reason) => Require(Mathf.Abs(actual - expected) < .002f, reason + $" ({actual:0.###})");
        private static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
            _report.checks++;
            _report.results.Add(reason);
        }

        private static void Complete(Exception exception)
        {
            EditorApplication.update -= Tick;
            Running = false;
            try
            {
                if (_controller != null)
                {
                    _controller.CloseImmediately();
                    _controller.enabled = true;
                    _controller.SetMasterVolume(_initialVolume);
                    _controller.SetMuted(_initialMuted);
                    if (_controller.previewSource != null) _controller.previewSource.Stop();
                }
                if (_cutscene != null) _cutscene.Finish(false);
                if (_definition != null) Object.Destroy(_definition);
                if (_duplicate != null) Object.Destroy(_duplicate);
                if (_audioObject != null) Object.Destroy(_audioObject);
                if (_clip != null) Object.Destroy(_clip);
                if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
                if (_previousKeyboard != null && _previousKeyboard.added) _previousKeyboard.MakeCurrent();
                if (_hadVolumePreference) PlayerPrefs.SetFloat(PauseSettingsController.VolumePreference, _savedPrefVolume);
                else PlayerPrefs.DeleteKey(PauseSettingsController.VolumePreference);
                if (_hadMutePreference) PlayerPrefs.SetInt(PauseSettingsController.MutePreference, _savedPrefMute);
                else PlayerPrefs.DeleteKey(PauseSettingsController.MutePreference);
                PlayerPrefs.Save();
                Time.timeScale = _initialScale;
                AudioListener.pause = _initialAudioPause;
                AudioListener.volume = _initialListenerVolume;
                Cursor.lockState = _initialCursorLock;
                Cursor.visible = _initialCursorVisible;
                Application.runInBackground = _initialBackground;
            }
            catch (Exception cleanupError) { exception ??= cleanupError; }
            _keyboard = null; _previousKeyboard = null; _definition = null; _cutscene = null;
            _audioObject = null; _source = null; _clip = null; _duplicate = null;
            _report.passed = exception == null;
            _report.error = exception?.ToString();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(_report, true));
            if (exception == null) Debug.Log($"[PauseSettings] PASS {_report.checks} runtime checks. Report: {ReportPath}");
            else Debug.LogError($"[PauseSettings] Runtime verification failed: {exception}\nReport: {ReportPath}");
        }
    }
}
