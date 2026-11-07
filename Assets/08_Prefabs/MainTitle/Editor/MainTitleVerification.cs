using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectR.UI.MainTitle.Editor
{
    /// <summary>Isolated saved-prefab checks. Does not run Awake, load a game scene, or save QA changes.</summary>
    public static class MainTitleVerification
    {
        private sealed class Checks
        {
            public int count;
            public void Require(bool condition, string message)
            {
                count++;
                if (!condition) throw new InvalidOperationException("[MainTitle QA] " + message);
            }
        }

        [MenuItem("Tools/Project R/Main Title/Verify Saved Prefab")]
        public static void RunFromMenu() => Debug.Log(Verify());

        public static string Verify()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Run Verify outside Play Mode; use VerifyRuntime for live navigation.");

            var checks = new Checks();
            GameObject root = PrefabUtility.LoadPrefabContents(MainTitleBuilder.PrefabPath);
            try
            {
                checks.Require(root != null, "Saved prefab is missing.");
                checks.Require(EditorSceneManager.IsPreviewScene(root.scene), "QA must use an isolated preview scene.");
                MainTitleController title = root.GetComponent<MainTitleController>();
                checks.Require(title != null, "MainTitleController is missing.");

                Canvas canvas = root.GetComponent<Canvas>();
                checks.Require(canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay,
                    "Root must contain a screen-space overlay Canvas.");
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                checks.Require(scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize &&
                    scaler.referenceResolution == new Vector2(1920, 1080), "Expected a scalable 1920 x 1080 layout.");
                checks.Require(root.GetComponent<GraphicRaycaster>() != null, "Interactive Canvas lacks GraphicRaycaster.");
                checks.Require(root.GetComponentInChildren<PlayerHUDSafeArea>(true) != null, "Safe area handling is missing.");
                checks.Require(root.GetComponentsInChildren<EventSystem>(true).Length == 0,
                    "The reusable prefab must defer EventSystem ownership until runtime.");
                checks.Require(root.GetComponentsInChildren<Text>(true).Length == 0,
                    "Legacy UGUI Text was found; all labels must use TMP.");

                var references = new Dictionary<string, Component>
                {
                    { "introGroup", title.introGroup }, { "menuGroup", title.menuGroup },
                    { "artwork", title.artwork }, { "transitionVeil", title.transitionVeil }, { "stagesPanel", title.stagesPanel },
                    { "settingsPanel", title.settingsPanel }, { "quitPanel", title.quitPanel },
                    { "newGameButton", title.newGameButton }, { "stageSelectButton", title.stageSelectButton },
                    { "settingsButton", title.settingsButton }, { "quitButton", title.quitButton },
                    { "stageBackButton", title.stageBackButton }, { "settingsBackButton", title.settingsBackButton },
                    { "quitCancelButton", title.quitCancelButton }, { "quitConfirmButton", title.quitConfirmButton },
                    { "launchStageButton", title.launchStageButton }, { "stageTitle", title.stageTitle },
                    { "stageBoss", title.stageBoss }, { "stageDescription", title.stageDescription },
                    { "stageStatus", title.stageStatus }, { "feedbackLabel", title.feedbackLabel },
                    { "stageArtwork", title.stageArtwork }, { "masterSlider", title.masterSlider },
                    { "muteToggle", title.muteToggle }, { "reducedMotionToggle", title.reducedMotionToggle },
                    { "screenModeDropdown", title.screenModeDropdown }, { "volumeLabel", title.volumeLabel },
                    { "feedbackSource", title.feedbackSource }
                };
                foreach (var entry in references)
                    checks.Require(entry.Value != null && entry.Value.transform.IsChildOf(root.transform),
                        "Missing or external component reference: " + entry.Key);

                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    checks.Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0,
                        "Missing script: " + Path(child, root.transform));

                foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
                {
                    bool intended = graphic.GetComponent<Selectable>() != null || graphic.name == "ModalBlocker" ||
                        (graphic.name == "Template" && graphic.transform.parent == title.screenModeDropdown.transform);
                    checks.Require(!graphic.raycastTarget || intended,
                        "Decorative graphic intercepts input: " + Path(graphic.transform, root.transform));
                }
                foreach (CanvasGroup panel in new[] { title.stagesPanel, title.settingsPanel, title.quitPanel })
                {
                    checks.Require(Mathf.Approximately(panel.alpha, 0) && !panel.interactable && !panel.blocksRaycasts,
                        "Saved modal must begin hidden and noninteractive: " + panel.name);
                    Transform blocker = panel.transform.Find("ModalBlocker");
                    checks.Require(blocker != null && blocker.GetComponent<Graphic>() != null &&
                        blocker.GetComponent<Graphic>().raycastTarget, "Modal input blocker is missing: " + panel.name);
                }

                var mainButtons = new[] { title.newGameButton, title.stageSelectButton, title.settingsButton, title.quitButton };
                var captions = new[] { "새 시작", "스테이지 선택", "설정", "종료" };
                checks.Require(title.introRows != null && title.introRows.Length == mainButtons.Length,
                    "Each main menu item needs a staggered entrance group.");
                checks.Require(!title.transitionVeil.raycastTarget && Mathf.Approximately(title.transitionVeil.color.a, 0),
                    "Scene transition veil must be transparent and nonblocking at rest.");
                var menuBounds = new List<Rect>();
                for (int i = 0; i < mainButtons.Length; i++)
                {
                    checks.Require(title.introRows[i] != null && title.introRows[i].transform == mainButtons[i].transform,
                        "Entrance row does not match menu item: " + captions[i]);
                    checks.Require(mainButtons[i].GetComponentsInChildren<TMP_Text>(true).Any(t => t.text == captions[i]),
                        "Required menu caption is missing: " + captions[i]);
                    Navigation navigation = mainButtons[i].navigation;
                    checks.Require(navigation.mode == Navigation.Mode.Explicit &&
                        navigation.selectOnUp == mainButtons[(i + 3) % 4] && navigation.selectOnDown == mainButtons[(i + 1) % 4],
                        "Main menu keyboard navigation is broken: " + captions[i]);
                    checks.Require(mainButtons[i].targetGraphic != null && mainButtons[i].targetGraphic.raycastTarget,
                        "Button has no active pointer hit surface: " + captions[i]);
                    var rect = (RectTransform)mainButtons[i].transform;
                    checks.Require(rect.rect.width >= 44 && rect.rect.height >= 44,
                        "Menu pointer hit area is smaller than 44 design pixels: " + captions[i]);
                    Rect bounds = new Rect(rect.anchoredPosition - Vector2.Scale(rect.pivot, rect.rect.size), rect.rect.size);
                    checks.Require(menuBounds.All(previous => !previous.Overlaps(bounds)), "Menu buttons overlap: " + captions[i]);
                    menuBounds.Add(bounds);
                }

                foreach (MainTitleFeedback feedback in root.GetComponentsInChildren<MainTitleFeedback>(true))
                    checks.Require(feedback.owner == title && feedback.visual != null && feedback.label != null &&
                        feedback.highlight != null && feedback.accent != null,
                        "Incomplete feedback binding: " + Path(feedback.transform, root.transform));

                checks.Require(title.feedbackSource.playOnAwake == false && title.feedbackSource.spatialBlend == 0, "Menu feedback must be 2D audio without autoplay (ignoreListenerPause is applied in Awake).");
                checks.Require(title.hoverClip != null && title.confirmClip != null && title.backClip != null,
                    "Hover, confirm, or back sound is missing.");
                checks.Require(title.masterSlider.minValue == 0 && title.masterSlider.maxValue == 1 &&
                    !title.masterSlider.wholeNumbers, "Master volume slider must cover continuous 0..1 values.");
                checks.Require(title.screenModeDropdown.template != null &&
                    !title.screenModeDropdown.template.gameObject.activeSelf &&
                    title.screenModeDropdown.itemText != null && title.screenModeDropdown.captionText != null,
                    "TMP dropdown template or text binding is invalid.");
                checks.Require(title.screenModeDropdown.options.Count == 3, "Three screen modes are expected.");

                var fontsChecked = new Dictionary<TMP_FontAsset, HashSet<char>>();
                foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    checks.Require(label is TextMeshProUGUI && label.font != null,
                        "TMP UGUI font missing: " + Path(label.transform, root.transform));
                    checks.Require(!label.raycastTarget, "TMP label must not intercept pointer input: " + label.name);
                    CheckGlyphs(label.font, label.text, label.name, fontsChecked, checks);
                }
                foreach (TMP_Dropdown.OptionData option in title.screenModeDropdown.options)
                    CheckGlyphs(title.screenModeDropdown.captionText.font, option.text, "ScreenMode option", fontsChecked, checks);

                checks.Require(title.stages != null && title.stages.Length > 0, "No stage definitions are configured.");
                checks.Require(title.stageButtons != null && title.stageButtons.Length == title.stages.Length,
                    "Stage button count differs from stage definitions.");
                var ids = new HashSet<string>();
                for (int i = 0; i < title.stages.Length; i++)
                {
                    TitleStageDefinition stage = title.stages[i];
                    checks.Require(stage != null && !string.IsNullOrWhiteSpace(stage.id) && ids.Add(stage.id),
                        "Stage definition or unique ID missing at index " + i);
                    checks.Require(title.stageButtons[i] != null && title.stageButtons[i].transform.IsChildOf(title.stagesPanel.transform),
                        "Stage button is missing or outside its modal: " + i);
                    checks.Require(!string.IsNullOrWhiteSpace(stage.displayName) && !string.IsNullOrWhiteSpace(stage.bossName) &&
                        stage.artwork != null, "Stage needs a name, boss/context name, and artwork: " + stage.id);
                    CheckGlyphs(title.stageTitle.font, stage.displayName, stage.id, fontsChecked, checks);
                    CheckGlyphs(title.stageBoss.font, stage.bossName, stage.id, fontsChecked, checks);
                    CheckGlyphs(title.stageDescription.font, stage.description, stage.id, fontsChecked, checks);
                }

                TitleStageDefinition first = title.stages[0];
                checks.Require(first.available && !string.IsNullOrWhiteSpace(first.scenePath),
                    "New game must have an available first stage with a scene path.");
                checks.Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(first.scenePath) != null,
                    "First stage scene asset does not exist: " + first.scenePath);
                bool enabledInBuild = EditorBuildSettings.scenes.Any(s => s.enabled && s.path == first.scenePath);
                checks.Require(enabledInBuild, "First stage must be enabled in EditorBuildSettings: " + first.scenePath);
                checks.Require(!title.CanLaunchStage(-1) && !title.CanLaunchStage(title.stages.Length),
                    "Public stage eligibility does not respect valid and out-of-range stage indices.");

                // These APIs are deliberately exercised without Awake. Unavailable launch must return before
                // coroutines, events, scene changes, PlayerPrefs writes, or platform settings are touched.
                int requestCount = 0;
                title.onStageRequested.AddListener(_ => requestCount++);
                int sceneCount = SceneManager.sceneCount;
                for (int i = 0; i < title.stages.Length; i++)
                {
                    if (title.stages[i].available) continue;
                    checks.Require(!title.CanLaunchStage(i), "Unavailable stage is reported as launchable: " + i);
                    title.SelectStage(i);
                    checks.Require(title.SelectedStageIndex == i && !title.launchStageButton.interactable,
                        "Unavailable stage still permits button activation: " + i);
                    title.LaunchSelectedStage();
                    checks.Require(!title.IsTransitioning && requestCount == 0 && SceneManager.sceneCount == sceneCount,
                        "Unavailable stage triggered a transition, request event, or scene change: " + i);
                    CheckGlyphs(title.feedbackLabel.font, title.feedbackLabel.text, "Unavailable feedback", fontsChecked, checks);
                    CheckGlyphs(title.stageStatus.font, title.stageStatus.text, "Stage status", fontsChecked, checks);
                }
                TitleStageDefinition[] originalStages = title.stages;
                title.stages = Array.Empty<TitleStageDefinition>();
                title.SelectStage(0);
                title.LaunchSelectedStage();
                checks.Require(!title.launchStageButton.interactable && !title.IsTransitioning && requestCount == 0,
                    "An empty stage configuration must not launch.");
                CheckGlyphs(title.stageTitle.font, title.stageTitle.text, "Empty-stage title", fontsChecked, checks);
                CheckGlyphs(title.stageDescription.font, title.stageDescription.text, "Empty-stage description", fontsChecked, checks);
                title.stages = originalStages;
                title.SelectStage(0);

                // Rendered runtime strings, not source-code punctuation or identifiers.
                const string runtimeCopy = "시작 가능 스테이지로 이동 중… 스테이지 시작을 요청했습니다. " +
                    "현재 이 스테이지에 입장할 수 없습니다. " +
                    "스테이지를 불러오지 못했습니다. 잠시 후 다시 시도해 주세요. " +
                    "종료 버튼 확인 완료 · 빌드에서는 게임이 종료됩니다. " +
                    "전체 화면 테두리 없는 창 창 모드";
                CheckGlyphs(title.feedbackLabel.font, runtimeCopy, "Runtime feedback", fontsChecked, checks);
                return "[MainTitle QA] Saved prefab passed " + checks.count +
                    " checks (references, TMP/glyphs, menu navigation, modal defaults, stage guards, build scene). " +
                    "No gameplay scene loaded; visual and live-input verification remain separate.";
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("Tools/Project R/Main Title/Verify Live Navigation")]
        public static void RunRuntimeFromMenu() => Debug.Log(VerifyRuntime());

        /// <summary>Call after the title has initialized in Play Mode. Does not launch stages or change settings.</summary>
        public static string VerifyRuntime()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("VerifyRuntime requires Play Mode.");
            MainTitleController title = Object.FindFirstObjectByType<MainTitleController>();
            if (title == null) throw new InvalidOperationException("No active MainTitleController exists.");
            var checks = new Checks();
            checks.Require(!title.IsTransitioning, "Wait for any current scene transition to complete.");
            checks.Require(title.CanLaunchStage(0), "First stage must be loadable in Play Mode.");
            checks.Require(title.feedbackSource.ignoreListenerPause, "Runtime UI audio must ignore listener pause.");
            EventSystem events = EventSystem.current;
            checks.Require(events != null && events.isActiveAndEnabled && events.currentInputModule != null,
                "No active UI event/input system.");
            int originalStage = title.SelectedStageIndex;
            GameObject originalSelection = events.currentSelectedGameObject;
            try
            {
                title.ClosePanel();
                events.SetSelectedGameObject(title.stageSelectButton.gameObject);
                title.ShowStages();
                CheckPanel(title, title.stagesPanel, checks);
                checks.Require(events.currentSelectedGameObject == title.stageButtons[title.SelectedStageIndex].gameObject,
                    "Stage modal failed to focus the selected stage.");
                title.ClosePanel();
                checks.Require(events.currentSelectedGameObject == title.stageSelectButton.gameObject,
                    "Closing stage modal failed to restore main-menu focus.");

                events.SetSelectedGameObject(title.settingsButton.gameObject);
                title.ShowSettings();
                CheckPanel(title, title.settingsPanel, checks);
                checks.Require(events.currentSelectedGameObject == title.masterSlider.gameObject,
                    "Settings modal failed to focus its first control.");
                title.ClosePanel();
                checks.Require(events.currentSelectedGameObject == title.settingsButton.gameObject,
                    "Closing settings failed to restore focus.");

                events.SetSelectedGameObject(title.quitButton.gameObject);
                title.ShowQuit();
                CheckPanel(title, title.quitPanel, checks);
                checks.Require(events.currentSelectedGameObject == title.quitCancelButton.gameObject,
                    "Quit modal must initially focus cancellation.");
                title.ClosePanel();
                checks.Require(events.currentSelectedGameObject == title.quitButton.gameObject,
                    "Closing quit modal failed to restore focus.");

                int requests = 0;
                UnityEngine.Events.UnityAction<int> listener = _ => requests++;
                title.onStageRequested.AddListener(listener);
                try
                {
                    for (int i = 0; i < title.stages.Length; i++)
                    {
                        if (title.stages[i] == null || title.stages[i].available) continue;
                        title.SelectStage(i);
                        title.LaunchSelectedStage();
                        checks.Require(!title.IsTransitioning && requests == 0 && !title.launchStageButton.interactable,
                            "Unavailable live stage launch was not blocked: " + i);
                    }
                }
                finally { title.onStageRequested.RemoveListener(listener); }
                return "[MainTitle QA] Live navigation passed " + checks.count +
                    " checks (modal isolation, initial focus, focus restoration, unavailable-stage guard). " +
                    "Pointer, keyboard, dropdown cancellation, animation, and actual loading need interactive checks.";
            }
            finally
            {
                title.ClosePanel();
                title.SelectStage(originalStage);
                events.SetSelectedGameObject(originalSelection != null && originalSelection.activeInHierarchy
                    ? originalSelection : title.newGameButton.gameObject);
            }
        }

        private static void CheckPanel(MainTitleController title, CanvasGroup active, Checks checks)
        {
            checks.Require(active.interactable && active.blocksRaycasts, "Opened modal does not accept input: " + active.name);
            checks.Require(!title.menuGroup.interactable && !title.menuGroup.blocksRaycasts,
                "Main menu accepts input behind a modal.");
            foreach (CanvasGroup panel in new[] { title.stagesPanel, title.settingsPanel, title.quitPanel })
                if (panel != active) checks.Require(!panel.interactable, "Multiple modals accept input: " + panel.name);
        }

        private static void CheckGlyphs(TMP_FontAsset font, string text, string context,
            Dictionary<TMP_FontAsset, HashSet<char>> tested, Checks checks)
        {
            checks.Require(font != null, "No font for " + context);
            if (!tested.TryGetValue(font, out HashSet<char> characters))
            {
                characters = new HashSet<char>();
                tested.Add(font, characters);
            }
            foreach (char character in text ?? string.Empty)
            {
                if (char.IsWhiteSpace(character) || char.IsControl(character) || !characters.Add(character)) continue;
                checks.Require(font.HasCharacter(character, true, true),
                    "Missing rendered glyph '" + character + "' U+" + ((int)character).ToString("X4") +
                    " in " + font.name + " (" + context + ").");
            }
        }

        private static string Path(Transform target, Transform root)
        {
            var parts = new List<string>();
            for (Transform cursor = target; cursor != null && cursor != root; cursor = cursor.parent) parts.Add(cursor.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}



