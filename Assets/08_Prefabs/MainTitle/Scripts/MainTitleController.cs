using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ProjectR.UI.MainTitle
{
    /// <summary>Editable title menu navigation, settings and validated scene entry. Never resets progression.</summary>
    [DisallowMultipleComponent]
    public sealed class MainTitleController : MonoBehaviour
    {
        public const string ReducedMotionPreference = "ProjectR.UI.ReducedMotion";
        public const string ScreenModePreference = "ProjectR.Display.FullScreenMode";

        [Header("Presentation")]
        public CanvasGroup introGroup, menuGroup;
        public CanvasGroup[] introRows;
        public RectTransform artwork;
        public Image transitionVeil;
        public CanvasGroup stagesPanel, settingsPanel, quitPanel;
        [Min(.01f)] public float introDuration = .6f;
        [Min(.01f)] public float panelDuration = .22f;
        [Min(0f)] public float parallaxDistance = 8f;

        [Header("Navigation")]
        public Button newGameButton, stageSelectButton, settingsButton, quitButton;
        public Button stageBackButton, settingsBackButton, quitCancelButton, quitConfirmButton, launchStageButton;
        public Button[] stageButtons;

        [Header("Stage details")]
        public TMP_Text stageTitle, stageBoss, stageDescription, stageStatus, feedbackLabel;
        public Image stageArtwork;
        public TitleStageDefinition[] stages;
        [Tooltip("Disable when an external game-flow system handles onStageRequested instead.")]
        public bool loadScenesAutomatically = true;
        public UnityEvent<int> onStageRequested = new UnityEvent<int>();

        [Header("Settings")]
        public Slider masterSlider;
        public Toggle muteToggle, reducedMotionToggle;
        public TMP_Dropdown screenModeDropdown;
        public TMP_Text volumeLabel;

        [Header("Feedback")]
        public AudioSource feedbackSource;
        public AudioClip hoverClip, confirmClip, backClip;

        public int SelectedStageIndex { get; private set; }
        public bool IsTransitioning { get; private set; }
        public bool ReducedMotion { get; private set; }
        public bool IsPanelOpen => _panel != Panel.None;

        private enum Panel { None, Stages, Settings, Quit }
        private Panel _panel;
        private float _introTime, _stageAmount, _settingsAmount, _quitAmount, _launchVisibility = 1f;
        private float _volume = 1f, _lastSoundAt = -100f, _feedbackUntil;
        private bool _muted, _cursorOwned, _previousCursorVisible, _bound;
        private CursorLockMode _previousCursorLock;
        private Vector2 _artworkOrigin;
        private Vector2[] _introRowOrigins;
        private bool _dropdownWasExpanded;
        private GameObject _returnFocus, _originalSelection;
        private GameObject _ownedEventSystem;
        private EventSystem _eventSystem;
        private InputSystemUIInputModule _addedModule;
        private InputActionAsset _ownedActions;
        private readonly List<UnityAction> _stageCallbacks = new List<UnityAction>();
        private Coroutine _launchRoutine;
        private static readonly FullScreenMode[] ScreenModes =
            { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };

        private void Awake()
        {
            if (artwork != null) _artworkOrigin = artwork.anchoredPosition;
            _introRowOrigins = new Vector2[introRows?.Length ?? 0];
            for (int i = 0; i < _introRowOrigins.Length; i++)
                if (introRows[i] != null && introRows[i].transform is RectTransform row)
                    _introRowOrigins[i] = row.anchoredPosition;
            ReducedMotion = PlayerPrefs.GetInt(ReducedMotionPreference, 0) != 0;
            ReadAudioSettings();
            BindControls();
            ConfigureScreenModes();
            SelectStage(0);
            if (feedbackSource != null)
            {
                feedbackSource.playOnAwake = false;
                feedbackSource.spatialBlend = 0;
                feedbackSource.ignoreListenerPause = true;
            }
            SetFeedback(string.Empty);
            ApplyVisuals();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            _introTime = 0;
            _launchVisibility = 1f;
            IsTransitioning = false;
            _panel = Panel.None;
            _stageAmount = _settingsAmount = _quitAmount = 0;
            _dropdownWasExpanded = false;
            EnsureEventSystem();
            if (_eventSystem != null) _originalSelection = _eventSystem.currentSelectedGameObject;
            OwnCursor();
            ReadAudioSettings();
            Focus(newGameButton);
            ApplyVisuals();
        }

        private void Update()
        {
            float delta = Time.unscaledDeltaTime;
            _introTime += delta;
            float step = ReducedMotion ? 1f : delta / Mathf.Max(.01f, panelDuration);
            _stageAmount = Mathf.MoveTowards(_stageAmount, _panel == Panel.Stages ? 1 : 0, step);
            _settingsAmount = Mathf.MoveTowards(_settingsAmount, _panel == Panel.Settings ? 1 : 0, step);
            _quitAmount = Mathf.MoveTowards(_quitAmount, _panel == Panel.Quit ? 1 : 0, step);
            ApplyVisuals();
            AnimateArtwork(delta);
            if (!IsTransitioning && (Keyboard.current?.escapeKey.wasPressedThisFrame == true ||
                                     Gamepad.current?.buttonEast.wasPressedThisFrame == true))
            {
                // The UI module may already have consumed Cancel and hidden the dropdown this frame.
                if (_dropdownWasExpanded || (screenModeDropdown != null && screenModeDropdown.IsExpanded))
                {
                    if (screenModeDropdown != null) screenModeDropdown.Hide();
                }
                else if (_panel == Panel.None) ShowQuit();
                else ClosePanel();
            }
            if (_feedbackUntil > 0 && Time.unscaledTime >= _feedbackUntil) SetFeedback(string.Empty);
        }

        private void LateUpdate() => _dropdownWasExpanded = screenModeDropdown != null && screenModeDropdown.IsExpanded;

        private void BindControls()
        {
            newGameButton?.onClick.AddListener(StartNewGame);
            stageSelectButton?.onClick.AddListener(ShowStages);
            settingsButton?.onClick.AddListener(ShowSettings);
            quitButton?.onClick.AddListener(ShowQuit);
            stageBackButton?.onClick.AddListener(ClosePanel);
            settingsBackButton?.onClick.AddListener(ClosePanel);
            quitCancelButton?.onClick.AddListener(ClosePanel);
            quitConfirmButton?.onClick.AddListener(QuitGame);
            launchStageButton?.onClick.AddListener(LaunchSelectedStage);
            for (int i = 0; i < (stageButtons?.Length ?? 0); i++)
            {
                int captured = i;
                UnityAction callback = () => SelectStage(captured);
                _stageCallbacks.Add(callback);
                stageButtons[i]?.onClick.AddListener(callback);
            }
            if (masterSlider != null)
            {
                masterSlider.minValue = 0;
                masterSlider.maxValue = 1;
                masterSlider.wholeNumbers = false;
                masterSlider.onValueChanged.AddListener(SetMasterVolume);
            }
            muteToggle?.onValueChanged.AddListener(SetMuted);
            reducedMotionToggle?.onValueChanged.AddListener(SetReducedMotion);
            screenModeDropdown?.onValueChanged.AddListener(SetScreenMode);
            _bound = true;
        }

        public void ShowStages()
        {
            if (IsTransitioning || stagesPanel == null) return;
            OpenPanel(Panel.Stages, stageSelectButton);
            SelectStage(SelectedStageIndex);
            Focus(stageButtons != null && SelectedStageIndex < stageButtons.Length ? stageButtons[SelectedStageIndex] : stageBackButton);
        }

        public void ShowSettings()
        {
            if (IsTransitioning || settingsPanel == null) return;
            ReadAudioSettings();
            UpdateScreenModeLabel();
            OpenPanel(Panel.Settings, settingsButton);
            Focus(masterSlider != null ? (Selectable)masterSlider : settingsBackButton);
        }

        public void ShowQuit()
        {
            if (IsTransitioning || quitPanel == null) return;
            OpenPanel(Panel.Quit, quitButton);
            Focus(quitCancelButton);
        }

        private void OpenPanel(Panel panel, Button fallback)
        {
            if (_panel == Panel.None)
                _returnFocus = _eventSystem != null && _eventSystem.currentSelectedGameObject != null
                    ? _eventSystem.currentSelectedGameObject : fallback != null ? fallback.gameObject : null;
            _panel = panel;
            ApplyVisuals();
        }

        public void ClosePanel()
        {
            if (IsTransitioning || _panel == Panel.None) return;
            if (screenModeDropdown != null && screenModeDropdown.IsExpanded) screenModeDropdown.Hide();
            _panel = Panel.None;
            PlayerPrefs.Save();
            PlaySound(backClip, .72f);
            ApplyVisuals();
            if (_eventSystem != null)
                _eventSystem.SetSelectedGameObject(_returnFocus != null ? _returnFocus : newGameButton != null ? newGameButton.gameObject : null);
        }

        public void SelectStage(int index)
        {
            if (IsTransitioning) return;
            if (stages == null || stages.Length == 0)
            {
                SelectedStageIndex = 0;
                if (stageTitle != null) stageTitle.text = "스테이지 준비 중";
                if (stageBoss != null) stageBoss.text = string.Empty;
                if (stageDescription != null) stageDescription.text = "플레이할 스테이지가 아직 지정되지 않았습니다.";
                if (stageStatus != null) stageStatus.text = "준비 중";
                if (stageArtwork != null) stageArtwork.enabled = false;
                if (launchStageButton != null) launchStageButton.interactable = false;
                UpdateStageNavigation(false);
                return;
            }
            SelectedStageIndex = Mathf.Clamp(index, 0, stages.Length - 1);
            TitleStageDefinition selected = stages[SelectedStageIndex];
            if (stageTitle != null) stageTitle.text = selected?.displayName ?? "스테이지 준비 중";
            if (stageBoss != null) stageBoss.text = selected?.bossName ?? string.Empty;
            if (stageDescription != null) stageDescription.text = selected?.description ?? string.Empty;
            if (stageStatus != null) stageStatus.text = selected != null && selected.available ? "시작 가능" : "콘셉트 미리보기 · 준비 중";
            if (stageArtwork != null)
            {
                stageArtwork.sprite = selected?.artwork;
                stageArtwork.enabled = selected?.artwork != null;
            }
            if (launchStageButton != null) launchStageButton.interactable = selected != null && selected.available;
            UpdateStageNavigation(selected != null && selected.available);
        }

        private void UpdateStageNavigation(bool canLaunch)
        {
            Button last = stageButtons != null && stageButtons.Length > 0 ? stageButtons[stageButtons.Length - 1] : null;
            if (last != null)
            {
                Navigation navigation = last.navigation;
                navigation.selectOnDown = canLaunch && launchStageButton != null ? launchStageButton : stageBackButton;
                last.navigation = navigation;
            }
            if (stageBackButton != null)
            {
                Navigation navigation = stageBackButton.navigation;
                navigation.selectOnUp = canLaunch && launchStageButton != null ? launchStageButton : last;
                stageBackButton.navigation = navigation;
            }
        }

        public bool CanLaunchStage(int index)
        {
            if (stages == null || index < 0 || index >= stages.Length || stages[index] == null || !stages[index].available) return false;
            return !loadScenesAutomatically || (!string.IsNullOrWhiteSpace(stages[index].scenePath) &&
                                                Application.CanStreamedLevelBeLoaded(stages[index].scenePath));
        }

        public void StartNewGame()
        {
            if (IsTransitioning) return;
            // The design defines a start point. Save data and clear records are deliberately preserved.
            SelectStage(0);
            LaunchSelectedStage();
        }

        public void LaunchSelectedStage()
        {
            if (IsTransitioning) return;
            if (stages == null || SelectedStageIndex < 0 || SelectedStageIndex >= stages.Length ||
                stages[SelectedStageIndex] == null || !stages[SelectedStageIndex].available)
            {
                SetFeedback("아직 준비 중인 스테이지입니다.");
                return;
            }
            TitleStageDefinition stage = stages[SelectedStageIndex];
            if (loadScenesAutomatically && (string.IsNullOrWhiteSpace(stage.scenePath) ||
                                             !Application.CanStreamedLevelBeLoaded(stage.scenePath)))
            {
                SetFeedback("현재 이 스테이지에 입장할 수 없습니다.");
                Debug.LogWarning("[MainTitle] Scene is missing from the active Build Profile: " + stage.scenePath, this);
                return;
            }
            _launchRoutine = StartCoroutine(LaunchStageRoutine(SelectedStageIndex, stage.scenePath));
        }

        private IEnumerator LaunchStageRoutine(int index, string scenePath)
        {
            IsTransitioning = true;
            PlayerPrefs.Save();
            SetFeedback("스테이지로 이동 중…", 0);
            float duration = ReducedMotion ? .08f : .32f;
            for (float time = 0; time < duration; time += Time.unscaledDeltaTime)
            {
                _launchVisibility = 1f - Mathf.SmoothStep(0, 1, time / duration);
                ApplyVisuals();
                yield return null;
            }
            _launchVisibility = 0;
            ApplyVisuals();
            Exception failure = null;
            AsyncOperation loading = null;
            try
            {
                onStageRequested?.Invoke(index);
                if (loadScenesAutomatically)
                {
                    // Restore our cursor before the destination scene's scripts take ownership.
                    ReleaseCursor();
                    loading = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
                    if (loading == null) throw new InvalidOperationException("SceneManager returned no load operation.");
                }
            }
            catch (Exception error) { failure = error; }
            if (failure != null)
            {
                Debug.LogException(failure, this);
                OwnCursor();
                SetFeedback("스테이지를 불러오지 못했습니다. 잠시 후 다시 시도해 주세요.");
            }
            else if (loading != null)
            {
                while (!loading.isDone) yield return null;
            }
            else SetFeedback("스테이지 시작을 요청했습니다.");
            _launchVisibility = 1;
            IsTransitioning = false;
            _launchRoutine = null;
            ApplyVisuals();
        }

        public void QuitGame()
        {
            if (IsTransitioning) return;
            PlayerPrefs.Save();
#if UNITY_EDITOR
            SetFeedback("종료 버튼 확인 완료 · 빌드에서는 게임이 종료됩니다.");
            ClosePanel();
#else
            IsTransitioning = true;
            Application.Quit();
#endif
        }

        private void ReadAudioSettings()
        {
            _volume = PlayerPrefs.GetFloat(PauseSettingsController.VolumePreference, 1f);
            if (float.IsNaN(_volume) || float.IsInfinity(_volume)) _volume = 1f;
            _volume = Mathf.Clamp01(_volume);
            _muted = PlayerPrefs.GetInt(PauseSettingsController.MutePreference, 0) != 0;
            if (masterSlider != null) masterSlider.SetValueWithoutNotify(_volume);
            if (muteToggle != null) muteToggle.SetIsOnWithoutNotify(_muted);
            if (reducedMotionToggle != null) reducedMotionToggle.SetIsOnWithoutNotify(ReducedMotion);
            ApplyAudio();
        }

        public void SetMasterVolume(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            _volume = Mathf.Clamp01(value);
            if (masterSlider != null) masterSlider.SetValueWithoutNotify(_volume);
            PlayerPrefs.SetFloat(PauseSettingsController.VolumePreference, _volume);
            ApplyAudio();
        }

        public void SetMuted(bool value)
        {
            _muted = value;
            if (muteToggle != null) muteToggle.SetIsOnWithoutNotify(value);
            PlayerPrefs.SetInt(PauseSettingsController.MutePreference, value ? 1 : 0);
            ApplyAudio();
        }

        private void ApplyAudio()
        {
            AudioListener.volume = _muted ? 0 : _volume;
            if (volumeLabel != null) volumeLabel.text = Mathf.RoundToInt(_volume * 100) + "%";
        }

        public void SetReducedMotion(bool value)
        {
            ReducedMotion = value;
            PlayerPrefs.SetInt(ReducedMotionPreference, value ? 1 : 0);
            if (reducedMotionToggle != null) reducedMotionToggle.SetIsOnWithoutNotify(value);
            if (value && artwork != null) artwork.anchoredPosition = _artworkOrigin;
        }

        private void ConfigureScreenModes()
        {
            if (screenModeDropdown == null) return;
            screenModeDropdown.ClearOptions();
            screenModeDropdown.AddOptions(new List<string> { "전체 화면", "테두리 없는 창", "창 모드" });
            int saved = PlayerPrefs.GetInt(ScreenModePreference, (int)Screen.fullScreenMode);
            for (int i = 0; i < ScreenModes.Length; i++)
                if ((int)ScreenModes[i] == saved) { Screen.fullScreenMode = ScreenModes[i]; break; }
            UpdateScreenModeLabel();
        }

        private void UpdateScreenModeLabel()
        {
            if (screenModeDropdown == null) return;
            int index = Array.IndexOf(ScreenModes, Screen.fullScreenMode);
            screenModeDropdown.SetValueWithoutNotify(index < 0 ? 1 : index);
            screenModeDropdown.RefreshShownValue();
        }

        public void SetScreenMode(int index)
        {
            if (index < 0 || index >= ScreenModes.Length) return;
            Screen.fullScreenMode = ScreenModes[index];
            PlayerPrefs.SetInt(ScreenModePreference, (int)ScreenModes[index]);
        }

        private void ApplyVisuals()
        {
            float entrance = ReducedMotion ? 1f : Mathf.SmoothStep(0, 1, Mathf.Clamp01(_introTime / Mathf.Max(.01f, introDuration)));
            bool entering = entrance < .95f;
            if (introGroup != null && introGroup != menuGroup) introGroup.alpha = entrance * _launchVisibility;
            if (menuGroup != null)
            {
                bool inheritsEntrance = introGroup != null && introGroup != menuGroup && menuGroup.transform.IsChildOf(introGroup.transform);
                menuGroup.alpha = (inheritsEntrance ? 1f : entrance * _launchVisibility) * (_panel == Panel.None ? 1f : .18f);
                menuGroup.interactable = !entering && _panel == Panel.None && !IsTransitioning;
                menuGroup.blocksRaycasts = _panel == Panel.None;
            }
            ApplyPanel(stagesPanel, _stageAmount, _panel == Panel.Stages);
            ApplyPanel(settingsPanel, _settingsAmount, _panel == Panel.Settings);
            ApplyPanel(quitPanel, _quitAmount, _panel == Panel.Quit);
            if (transitionVeil != null)
            {
                Color tint = transitionVeil.color;
                tint.a = 1f - _launchVisibility;
                transitionVeil.color = tint;
                transitionVeil.raycastTarget = IsTransitioning;
            }
            for (int i = 0; i < (introRows?.Length ?? 0); i++)
            {
                if (introRows[i] == null) continue;
                float reveal = ReducedMotion ? 1f : Mathf.SmoothStep(0, 1, Mathf.Clamp01((_introTime - .04f - i * .055f) / .35f));
                introRows[i].alpha = reveal;
                if (_introRowOrigins != null && i < _introRowOrigins.Length && introRows[i].transform is RectTransform row)
                    row.anchoredPosition = _introRowOrigins[i] - new Vector2(0, 16f * (1f - reveal));
            }
        }

        private void ApplyPanel(CanvasGroup group, float amount, bool current)
        {
            if (group == null) return;
            group.alpha = Mathf.SmoothStep(0, 1, amount) * _launchVisibility;
            group.interactable = current && !IsTransitioning;
            group.blocksRaycasts = current || amount > .01f;
        }

        private void AnimateArtwork(float delta)
        {
            if (artwork == null) return;
            if (ReducedMotion) { artwork.anchoredPosition = _artworkOrigin; return; }
            Vector2 pointer = Vector2.zero;
            if (Mouse.current != null && Screen.width > 0 && Screen.height > 0)
            {
                Vector2 position = Mouse.current.position.ReadValue();
                pointer = new Vector2(Mathf.Clamp(position.x / Screen.width * 2 - 1, -1, 1),
                                      Mathf.Clamp(position.y / Screen.height * 2 - 1, -1, 1));
            }
            float time = Time.unscaledTime;
            Vector2 drift = new Vector2(Mathf.Sin(time * .14f) * 3f, Mathf.Cos(time * .18f) * 2f);
            Vector2 target = _artworkOrigin + drift - pointer * parallaxDistance;
            artwork.anchoredPosition = Vector2.Lerp(artwork.anchoredPosition, target, 1f - Mathf.Exp(-3f * delta));
        }

        private void EnsureEventSystem()
        {
            _eventSystem = EventSystem.current;
            if (_eventSystem == null || !_eventSystem.isActiveAndEnabled)
            {
                _eventSystem = null;
                foreach (EventSystem candidate in FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                    if (candidate.isActiveAndEnabled) { _eventSystem = candidate; break; }
            }
            if (_eventSystem == null)
            {
                if (_ownedEventSystem == null)
                {
                    _ownedEventSystem = new GameObject("Main Title EventSystem", typeof(EventSystem));
                    _ownedEventSystem.transform.SetParent(transform, false);
                }
                _ownedEventSystem.SetActive(true);
                _eventSystem = _ownedEventSystem.GetComponent<EventSystem>();
            }
            if (_addedModule != null) _addedModule.enabled = true;
            bool hasModule = false;
            foreach (BaseInputModule module in _eventSystem.GetComponents<BaseInputModule>())
                if (module.isActiveAndEnabled) { hasModule = true; break; }
            if (!hasModule)
            {
                _addedModule = _eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
                if (_addedModule.actionsAsset == null) _addedModule.AssignDefaultActions();
                _ownedActions = Instantiate(_addedModule.actionsAsset);
                _ownedActions.name = "Main Title UI Actions";
                _addedModule.actionsAsset = _ownedActions;
            }
            if (_ownedActions != null) _ownedActions.Enable();
        }

        private void Focus(Selectable target)
        {
            if (_eventSystem != null && target != null) _eventSystem.SetSelectedGameObject(target.gameObject);
        }

        private void OwnCursor()
        {
            if (_cursorOwned) return;
            _previousCursorVisible = Cursor.visible;
            _previousCursorLock = Cursor.lockState;
            _cursorOwned = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ReleaseCursor()
        {
            if (!_cursorOwned) return;
            Cursor.lockState = _previousCursorLock;
            Cursor.visible = _previousCursorVisible;
            _cursorOwned = false;
        }

        public void PlayHover() => PlaySound(hoverClip, .42f, true);
        public void PlayConfirm() => PlaySound(confirmClip, .72f);

        private void PlaySound(AudioClip clip, float volume, bool throttle = false)
        {
            if (clip == null || feedbackSource == null || (throttle && Time.unscaledTime - _lastSoundAt < .04f)) return;
            _lastSoundAt = Time.unscaledTime;
            feedbackSource.PlayOneShot(clip, volume);
        }

        private void SetFeedback(string message, float duration = 5f)
        {
            if (feedbackLabel != null) feedbackLabel.text = message;
            _feedbackUntil = string.IsNullOrEmpty(message) || duration <= 0 ? 0 : Time.unscaledTime + duration;
        }

        private void OnDisable()
        {
            if (!Application.isPlaying) return;
            if (_launchRoutine != null) { StopCoroutine(_launchRoutine); _launchRoutine = null; }
            IsTransitioning = false;
            ReleaseCursor();
            if (_eventSystem != null && _eventSystem.currentSelectedGameObject != null &&
                _eventSystem.currentSelectedGameObject.transform.IsChildOf(transform))
                _eventSystem.SetSelectedGameObject(_originalSelection);
            if (_addedModule != null) _addedModule.enabled = false;
            if (_ownedActions != null) _ownedActions.Disable();
            if (_ownedEventSystem != null) _ownedEventSystem.SetActive(false);
            PlayerPrefs.Save();
            if (artwork != null) artwork.anchoredPosition = _artworkOrigin;
        }

        private void OnDestroy()
        {
            if (_bound)
            {
                newGameButton?.onClick.RemoveListener(StartNewGame);
                stageSelectButton?.onClick.RemoveListener(ShowStages);
                settingsButton?.onClick.RemoveListener(ShowSettings);
                quitButton?.onClick.RemoveListener(ShowQuit);
                stageBackButton?.onClick.RemoveListener(ClosePanel);
                settingsBackButton?.onClick.RemoveListener(ClosePanel);
                quitCancelButton?.onClick.RemoveListener(ClosePanel);
                quitConfirmButton?.onClick.RemoveListener(QuitGame);
                launchStageButton?.onClick.RemoveListener(LaunchSelectedStage);
                for (int i = 0; i < _stageCallbacks.Count && i < (stageButtons?.Length ?? 0); i++)
                    stageButtons[i]?.onClick.RemoveListener(_stageCallbacks[i]);
                masterSlider?.onValueChanged.RemoveListener(SetMasterVolume);
                muteToggle?.onValueChanged.RemoveListener(SetMuted);
                reducedMotionToggle?.onValueChanged.RemoveListener(SetReducedMotion);
                screenModeDropdown?.onValueChanged.RemoveListener(SetScreenMode);
            }
            if (_addedModule != null && (_ownedEventSystem == null || _addedModule.gameObject != _ownedEventSystem)) Destroy(_addedModule);
            if (_ownedActions != null) Destroy(_ownedActions);
        }

        private void OnApplicationPause(bool paused) { if (paused) PlayerPrefs.Save(); }
        private void OnApplicationQuit() => PlayerPrefs.Save();
    }
}
