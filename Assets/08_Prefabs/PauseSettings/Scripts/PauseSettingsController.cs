using System;
using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Playables;
using UnityEngine.UI;
using UnityEngine.VFX;

namespace ProjectR.UI
{
    /// <summary>Owns an ESC pause session and its reversible world/input suspension.</summary>
    [DefaultExecutionOrder(-12000)]
    [DisallowMultipleComponent]
    public sealed class PauseSettingsController : MonoBehaviour
    {
        public const string VolumePreference = "ProjectR.Audio.MasterVolume";
        public const string MutePreference = "ProjectR.Audio.Muted";

        [Header("Window")]
        public CanvasGroup overlayGroup;
        public RectTransform panel;
        public RectTransform artwork;
        public RectTransform rotatingRing;
        public Image[] equalizerBars;

        [Header("Audio controls")]
        public Slider masterSlider;
        public Toggle muteToggle;
        public TMP_Text volumeValue;
        public TMP_Text statusLabel;
        public Button resumeButton;
        public Button resetButton;
        public Button testButton;
        public AudioSource previewSource;
        public AudioClip previewClip;

        [Header("Motion")]
        [Min(.01f)] public float openDuration = .32f;
        [Min(.01f)] public float closeDuration = .18f;

        public static PauseSettingsController Instance { get; private set; }
        public static bool IsPaused => Instance != null && Instance._ownsPause;
        public static bool InputBlocked => IsPaused || Time.frameCount <= _resumeAfterFrame;
        // Compose other real-time combat clocks from this clock to preserve deadlines.
        public static double UnpausedTime => Time.unscaledTimeAsDouble - _pausedSeconds -
            (IsPaused ? Math.Max(0, Time.unscaledTimeAsDouble - Instance._pauseStarted) : 0);
        public bool IsOpen => _ownsPause && !_closing;
        public float MasterVolume => _volume;
        public bool Muted => _muted;

        private static double _pausedSeconds;
        private static int _resumeAfterFrame = -1;
        private readonly List<Behaviour> _cameraControllers = new();
        private readonly List<AudioSource> _pausedAudio = new();
        private readonly List<ParticleSystem> _pausedParticles = new();
        private readonly List<VisualEffect> _pausedVfx = new();
        private readonly List<(Animator animator, float speed)> _unscaledAnimators = new();
        private readonly List<PlayableDirector> _unscaledDirectors = new();
        private readonly List<EventSystem> _suspendedEventSystems = new();
        private bool _ownsPause, _closing, _canPause = true, _muted, _bound;
        private float _volume, _previousTimeScale, _transition, _ambientTime;
        private bool _previousAudioPause, _previousCursorVisible;
        private CursorLockMode _previousCursorLock;
        private double _pauseStarted;
        private Vector2 _panelPosition, _artworkPosition;
        private Vector3 _panelScale = Vector3.one;
        private float[] _barHeights;
        private GameObject _previousSelection;
        private EventSystem _previousEventSystem;
        private GameObject _ownedEventSystem;
        private InputActionAsset _ownedUIActions;
        private AudioClip _generatedPreview;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
            _pausedSeconds = 0;
            _resumeAfterFrame = -1;
        }

        public static void ApplySavedAudio()
        {
            float value = PlayerPrefs.GetFloat(VolumePreference, 1f);
            if (float.IsNaN(value) || float.IsInfinity(value)) value = 1f;
            AudioListener.volume = PlayerPrefs.GetInt(MutePreference, 0) != 0 ? 0 : Mathf.Clamp01(value);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (panel != null) { _panelPosition = panel.anchoredPosition; _panelScale = panel.localScale; }
            if (artwork != null) _artworkPosition = artwork.anchoredPosition;
            _barHeights = new float[equalizerBars?.Length ?? 0];
            for (int i = 0; i < _barHeights.Length; i++)
                if (equalizerBars[i] != null) _barHeights[i] = equalizerBars[i].rectTransform.sizeDelta.y;
            _volume = PlayerPrefs.GetFloat(VolumePreference, 1f);
            if (float.IsNaN(_volume) || float.IsInfinity(_volume)) _volume = 1f;
            _volume = Mathf.Clamp01(_volume);
            _muted = PlayerPrefs.GetInt(MutePreference, 0) != 0;
            if (masterSlider != null)
            {
                masterSlider.minValue = 0;
                masterSlider.maxValue = 1;
                masterSlider.wholeNumbers = false;
                masterSlider.onValueChanged.AddListener(SetMasterVolume);
            }
            if (muteToggle != null) muteToggle.onValueChanged.AddListener(SetMuted);
            if (resumeButton != null) resumeButton.onClick.AddListener(Close);
            if (resetButton != null) resetButton.onClick.AddListener(ResetAudio);
            if (testButton != null) testButton.onClick.AddListener(PlayTestSound);
            _bound = true;
            EnsurePreviewAudio();
            RefreshAudio();
            SetVisualProgress(0);
        }

        private void Update()
        {
            if (Instance != this) return;
            bool escape = Keyboard.current?.escapeKey.wasPressedThisFrame == true;
            bool gamepadMenu = Gamepad.current?.startButton.wasPressedThisFrame == true;
            if ((escape || gamepadMenu) && _canPause)
            {
                if (!_ownsPause && Time.frameCount > _resumeAfterFrame) Open();
                else if (!_closing) Close();
            }
            if (!_ownsPause) return;

            float step = Time.unscaledDeltaTime / (_closing ? closeDuration : openDuration);
            _transition = Mathf.MoveTowards(_transition, _closing ? 0 : 1, step);
            SetVisualProgress(_transition);
            AnimateDecor(Time.unscaledDeltaTime);
            if (_closing && _transition <= 0) ReleasePause();
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else if (!_ownsPause) Open();
        }

        public void Open()
        {
            if (!Application.isPlaying || Instance != this || !_canPause || _ownsPause) return;
            _previousEventSystem = EventSystem.current;
            _previousSelection = _previousEventSystem != null ? _previousEventSystem.currentSelectedGameObject : null;
            EnsureEventSystem();
            _previousTimeScale = Time.timeScale;
            _previousAudioPause = AudioListener.pause;
            _previousCursorLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            _pauseStarted = Time.unscaledTimeAsDouble;
            _ownsPause = true;
            _closing = false;
            _transition = 0;
            Time.timeScale = 0;
            AudioListener.pause = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SuspendUnscaledWorld();
            if (overlayGroup != null) { overlayGroup.blocksRaycasts = true; overlayGroup.interactable = true; }
            SetVisualProgress(0);
            if (EventSystem.current != null && masterSlider != null)
                EventSystem.current.SetSelectedGameObject(masterSlider.gameObject);
        }

        public void Close()
        {
            if (!_ownsPause || _closing) return;
            _closing = true;
            if (overlayGroup != null) overlayGroup.interactable = false;
            // Retain pause and the full-screen raycast blocker through the exit animation.
            PlayerPrefs.Save();
        }

        public void SetSceneAvailability(bool available)
        {
            _canPause = available;
            if (!available) ReleasePause();
        }

        public void CloseImmediately() => ReleasePause();

        /// <summary>A cutscene may finish or be cancelled beneath the open settings window.</summary>
        public static void RestoreWorldTime(float timeScale, bool listenerPaused)
        {
            if (IsPaused)
            {
                Instance._previousTimeScale = timeScale;
                Instance._previousAudioPause = listenerPaused;
                // Keep ownership until settings closes; do not strand its original cutscene snapshot.
                Time.timeScale = 0;
                AudioListener.pause = true;
            }
            else
            {
                Time.timeScale = timeScale;
                AudioListener.pause = listenerPaused;
            }
        }

        public static void ReapplyCameraPause()
        {
            if (!IsPaused) return;
            foreach (var controller in FindObjectsByType<CinemachineBrain>()) Instance.SuspendCamera(controller);
            foreach (var controller in FindObjectsByType<CinemachineInputAxisController>()) Instance.SuspendCamera(controller);
        }

        public void SetMasterVolume(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            _volume = Mathf.Clamp01(value);
            RefreshAudio();
        }

        public void SetMuted(bool value)
        {
            _muted = value;
            RefreshAudio();
        }

        public void ResetAudio()
        {
            _volume = 1;
            _muted = false;
            RefreshAudio();
            PlayTestSound();
        }

        private void RefreshAudio()
        {
            AudioListener.volume = _muted ? 0 : _volume;
            PlayerPrefs.SetFloat(VolumePreference, _volume);
            PlayerPrefs.SetInt(MutePreference, _muted ? 1 : 0);
            if (masterSlider != null) masterSlider.SetValueWithoutNotify(_volume);
            if (muteToggle != null) muteToggle.SetIsOnWithoutNotify(_muted);
            if (volumeValue != null) volumeValue.text = Mathf.RoundToInt(_volume * 100) + "%";
            if (statusLabel != null) statusLabel.text = _muted || _volume <= 0 ? "MUTED" : "SOUND ENABLED";
        }

        public void PlayTestSound()
        {
            EnsurePreviewAudio();
            if (previewSource == null) return;
            previewSource.Stop();
            previewSource.PlayOneShot(previewClip != null ? previewClip : _generatedPreview, .35f);
        }

        private void EnsurePreviewAudio()
        {
            if (previewSource == null)
            {
                var sourceObject = new GameObject("UI Audio Preview");
                sourceObject.transform.SetParent(transform, false);
                previewSource = sourceObject.AddComponent<AudioSource>();
            }
            previewSource.playOnAwake = false;
            previewSource.spatialBlend = 0;
            previewSource.ignoreListenerPause = true;
            previewSource.ignoreListenerVolume = false;
            previewSource.loop = false;
            if (previewClip != null || _generatedPreview != null) return;
            const int sampleRate = 24000;
            const float duration = .65f;
            var samples = new float[Mathf.RoundToInt(sampleRate * duration)];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Clamp01(t / .018f) * Mathf.Pow(1 - t / duration, 2.5f);
                samples[i] = envelope * (Mathf.Sin(2 * Mathf.PI * 523.25f * t) * .5f +
                    Mathf.Sin(2 * Mathf.PI * 659.25f * t) * .25f + Mathf.Sin(2 * Mathf.PI * 783.99f * t) * .15f);
            }
            _generatedPreview = AudioClip.Create("Settings volume audition", samples.Length, 1, sampleRate, false);
            _generatedPreview.SetData(samples, 0);
        }

        private void EnsureEventSystem()
        {
            // A dedicated action asset remains usable if gameplay disabled its own UI action map.
            foreach (var system in FindObjectsByType<EventSystem>())
                if (system.enabled && (_ownedEventSystem == null || system.gameObject != _ownedEventSystem))
                { _suspendedEventSystems.Add(system); system.enabled = false; }
            if (_ownedEventSystem == null)
            {
                _ownedEventSystem = new GameObject("Pause Settings EventSystem", typeof(EventSystem));
                _ownedEventSystem.transform.SetParent(transform, false);
                var module = _ownedEventSystem.AddComponent<InputSystemUIInputModule>();
                // InputSystemUIInputModule's defaults are static/shared; clone the asset before use.
                if (module.actionsAsset == null) module.AssignDefaultActions();
                _ownedUIActions = Instantiate(module.actionsAsset);
                _ownedUIActions.name = "Pause Settings UI Actions";
                module.actionsAsset = _ownedUIActions;
                _ownedUIActions.Enable();
            }
            else _ownedEventSystem.SetActive(true);
        }

        private void SuspendUnscaledWorld()
        {
            foreach (var controller in FindObjectsByType<CinemachineBrain>()) SuspendCamera(controller);
            foreach (var controller in FindObjectsByType<CinemachineInputAxisController>()) SuspendCamera(controller);
            // Cutscene audio lives on transient DontSave objects, which FindObjectsByType omits.
            // Limit the broader lookup to active, loaded scene voices so prefab assets are untouched.
            foreach (var source in Resources.FindObjectsOfTypeAll<AudioSource>())
                if (source != previewSource && source.isActiveAndEnabled && source.gameObject.scene.IsValid() &&
                    source.gameObject.scene.isLoaded && source.ignoreListenerPause && source.isPlaying)
                { source.Pause(); _pausedAudio.Add(source); }
            foreach (var particle in FindObjectsByType<ParticleSystem>())
                if (particle.main.useUnscaledTime && particle.isPlaying)
                { particle.Pause(false); _pausedParticles.Add(particle); }
            foreach (var effect in FindObjectsByType<VisualEffect>())
                if (!effect.pause) { effect.pause = true; _pausedVfx.Add(effect); }
            foreach (var animator in FindObjectsByType<Animator>())
                if (animator.updateMode == AnimatorUpdateMode.UnscaledTime && animator.isActiveAndEnabled &&
                    !animator.transform.IsChildOf(transform))
                { _unscaledAnimators.Add((animator, animator.speed)); animator.speed = 0; }
            foreach (var director in FindObjectsByType<PlayableDirector>())
                if (director.timeUpdateMode == DirectorUpdateMode.UnscaledGameTime && director.state == PlayState.Playing)
                { director.Pause(); _unscaledDirectors.Add(director); }
        }

        private void SuspendCamera(Behaviour controller)
        {
            if (!controller.enabled) return;
            _cameraControllers.Add(controller);
            controller.enabled = false;
        }

        private void ReleasePause()
        {
            if (!_ownsPause) { SetVisualProgress(0); return; }
            _pausedSeconds += Math.Max(0, Time.unscaledTimeAsDouble - _pauseStarted);
            _ownsPause = false;
            _closing = false;
            _transition = 0;
            _resumeAfterFrame = Time.frameCount + 1;
            Time.timeScale = _previousTimeScale;
            AudioListener.pause = _previousAudioPause;
            Cursor.lockState = _previousCursorLock;
            Cursor.visible = _previousCursorVisible;
            foreach (var source in _pausedAudio) if (source != null) source.UnPause();
            foreach (var particle in _pausedParticles) if (particle != null && particle.isPaused) particle.Play(false);
            foreach (var effect in _pausedVfx) if (effect != null) effect.pause = false;
            foreach (var entry in _unscaledAnimators) if (entry.animator != null) entry.animator.speed = entry.speed;
            foreach (var director in _unscaledDirectors) if (director != null && director.isActiveAndEnabled) director.Resume();
            foreach (var controller in _cameraControllers) if (controller != null) controller.enabled = true;
            _pausedAudio.Clear(); _pausedParticles.Clear(); _pausedVfx.Clear();
            _unscaledAnimators.Clear(); _unscaledDirectors.Clear(); _cameraControllers.Clear();
            if (previewSource != null) previewSource.Stop();
            SetVisualProgress(0);
            if (_ownedEventSystem != null) _ownedEventSystem.SetActive(false);
            foreach (var system in _suspendedEventSystems) if (system != null) system.enabled = true;
            _suspendedEventSystems.Clear();
            if (_previousEventSystem != null && _previousEventSystem.isActiveAndEnabled)
                _previousEventSystem.SetSelectedGameObject(_previousSelection != null && _previousSelection.activeInHierarchy ? _previousSelection : null);
            _previousSelection = null;
            _previousEventSystem = null;
            PlayerPrefs.Save();
        }

        private void SetVisualProgress(float progress)
        {
            float eased = 1 - Mathf.Pow(1 - Mathf.Clamp01(progress), 3);
            if (overlayGroup != null)
            {
                overlayGroup.alpha = eased;
                overlayGroup.blocksRaycasts = _ownsPause;
                overlayGroup.interactable = _ownsPause && !_closing;
            }
            if (panel != null)
            {
                panel.anchoredPosition = _panelPosition + new Vector2(0, Mathf.Lerp(-24, 0, eased));
                panel.localScale = _panelScale * Mathf.Lerp(.96f, 1, eased);
            }
        }

        private void AnimateDecor(float dt)
        {
            _ambientTime += dt;
            if (artwork != null)
                artwork.anchoredPosition = _artworkPosition + new Vector2(Mathf.Sin(_ambientTime * .27f) * 5, Mathf.Cos(_ambientTime * .31f) * 7);
            if (rotatingRing != null) rotatingRing.Rotate(0, 0, -dt * 4);
            for (int i = 0; i < _barHeights.Length; i++)
            {
                if (equalizerBars[i] == null) continue;
                float pulse = _muted || _volume <= 0 ? .12f : .3f + .7f * Mathf.Abs(Mathf.Sin(_ambientTime * (1.8f + i * .12f) + i * .77f));
                RectTransform rect = equalizerBars[i].rectTransform;
                var size = rect.sizeDelta;
                size.y = Mathf.Max(3, _barHeights[i] * pulse);
                rect.sizeDelta = size;
            }
        }

        private void OnApplicationPause(bool paused) { if (paused) PlayerPrefs.Save(); }
        private void OnApplicationQuit() => PlayerPrefs.Save();
        private void OnDisable() { if (Instance == this) ReleasePause(); }
        private void OnDestroy()
        {
            if (Instance != this) return;
            ReleasePause();
            if (_bound)
            {
                if (masterSlider != null) masterSlider.onValueChanged.RemoveListener(SetMasterVolume);
                if (muteToggle != null) muteToggle.onValueChanged.RemoveListener(SetMuted);
                if (resumeButton != null) resumeButton.onClick.RemoveListener(Close);
                if (resetButton != null) resetButton.onClick.RemoveListener(ResetAudio);
                if (testButton != null) testButton.onClick.RemoveListener(PlayTestSound);
            }
            if (_generatedPreview != null) Destroy(_generatedPreview);
            if (_ownedUIActions != null) Destroy(_ownedUIActions);
            Instance = null;
        }
    }
}
