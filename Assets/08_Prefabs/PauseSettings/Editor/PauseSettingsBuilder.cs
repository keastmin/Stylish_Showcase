using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectR.UI.Editor
{
    /// <summary>Authoring source for a completely serialized, editable UGUI / TMP pause menu.</summary>
    public static class PauseSettingsBuilder
    {
        public const string Folder = "Assets/08_Prefabs/PauseSettings";
        public const string PrefabPath = Folder + "/Resources/PauseSettings.prefab";
        public const string ArtPath = Folder + "/Art/PauseSanctuary.png";
        private const string Fonts = "Assets/Download Assets/Heat - Complete Modern UI/Fonts/";
        private static readonly Color Ice = Hex("BAF3FF"), Gold = Hex("FFD38A"), White = Hex("EDF5F6"), Muted = Hex("8DA5AD");
        private static TMP_FontAsset regular, bold;

        [MenuItem("Tools/Project R/Pause Settings/Rebuild Prefab")]
        public static void Build()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Build the settings prefab outside Play Mode.");
            if (!AssetDatabase.IsValidFolder(Folder + "/Resources")) AssetDatabase.CreateFolder(Folder, "Resources");
            regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "RobotoCondensed-Regular SDF.asset");
            bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "RobotoCondensed-Bold SDF.asset");
            if (!regular || !bold) throw new InvalidOperationException("Project RobotoCondensed TMP font assets are required.");
            Sprite art = ImportArtwork();
            GameObject root = new GameObject("PauseSettings", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(PauseSettingsController));
            try
            {
                root.layer = 5;
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32765;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                var controller = root.GetComponent<PauseSettingsController>();
                controller.overlayGroup = root.GetComponent<CanvasGroup>();
                controller.overlayGroup.alpha = 0;
                controller.overlayGroup.interactable = false;
                controller.overlayGroup.blocksRaycasts = false;

                Image dim = Image("WorldDimmer", root.transform, 0, 0, 0, 0, new Color(.012f, .025f, .038f, .88f));
                Stretch(dim.rectTransform);
                dim.raycastTarget = true;
                var window = Rect("SettingsWindow", root.transform, 0, 0, 1180, 720);
                window.anchorMin = window.anchorMax = window.pivot = new Vector2(.5f, .5f);
                controller.panel = window;
                Facet("WindowShadow", window, -12, -18, 1204, 744, new Color(0, 0, 0, .32f), Color.clear, 20);
                Facet("WindowFrame", window, 0, 0, 1180, 720, Hex("0C1821"), Hex("375663"), 18);
                Image("TopLight", window, 426, 718, 674, 2, Ice);
                Image("BottomLight", window, 18, 0, 92, 2, Gold);
                BuildArtwork(window, controller, art);
                BuildControls(window, controller);
                controller.previewSource = root.AddComponent<AudioSource>();
                controller.previewSource.playOnAwake = false;
                controller.previewSource.ignoreListenerPause = true;
                controller.previewSource.spatialBlend = 0;
                controller.previewSource.volume = 1f;
                controller.previewSource.dopplerLevel = 0;
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 5;
                Canvas.ForceUpdateCanvases();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                Debug.Log("[PauseSettings] Saved editable UGUI / TMP prefab: " + PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void BuildArtwork(RectTransform window, PauseSettingsController controller, Sprite art)
        {
            var viewport = Rect("ArtworkViewport", window, 18, 18, 390, 684);
            viewport.gameObject.AddComponent<RectMask2D>();
            Image("ArtworkBase", viewport, 0, 0, 390, 684, Hex("091724"));
            Image picture = Image("GeneratedSanctuary", viewport, -55.667f, -34, 501.334f, 752, Color.white, art);
            picture.type = UnityEngine.UI.Image.Type.Simple;
            picture.preserveAspect = false;
            controller.artwork = picture.rectTransform;
            Image("ArtTint", viewport, 0, 0, 390, 684, new Color(.025f, .08f, .12f, .19f));
            var lowerFade = Rect("LowerArtworkFade", viewport, 0, 0, 390, 224).gameObject.AddComponent<PauseSettingsGradientGraphic>();
            lowerFade.color = new Color(.018f, .047f, .066f, .87f);
            lowerFade.raycastTarget = false;
            var upperFade = Rect("UpperArtworkFade", viewport, 0, 540, 390, 144).gameObject.AddComponent<PauseSettingsGradientGraphic>();
            upperFade.bottomOpacity = 0;
            upperFade.topOpacity = 1;
            upperFade.color = new Color(.018f, .047f, .066f, .696f);
            upperFade.raycastTarget = false;
            var orbit = Rect("AnimatedOrbit", viewport, 195, 370, 280, 280);
            orbit.pivot = new Vector2(.5f, .5f);
            var orbitGraphic = orbit.gameObject.AddComponent<PauseSettingsRingGraphic>();
            orbitGraphic.color = new Color(Ice.r, Ice.g, Ice.b, .29f);
            orbitGraphic.raycastTarget = false;
            controller.rotatingRing = orbit;
            var inner = Rect("InnerOrbit", orbit, 0, 0, 256, 256);
            inner.anchorMin = inner.anchorMax = inner.pivot = new Vector2(.5f, .5f);
            inner.localEulerAngles = new Vector3(0, 0, 190);
            var innerGraphic = inner.gameObject.AddComponent<PauseSettingsRingGraphic>();
            innerGraphic.arcFraction = .28f;
            innerGraphic.thickness = 2;
            innerGraphic.color = new Color(Gold.r, Gold.g, Gold.b, .45f);
            innerGraphic.raycastTarget = false;
            Label("ArtBrand", viewport, "PROJECT  R", 28, 622, 240, 30, 24, White, true, 5);
            Label("ArtTerminal", viewport, "FIELD TERMINAL  /  01", 29, 599, 320, 21, 13, Ice, false, 2);
            Image("ArtHeaderRule", viewport, 29, 585, 44, 2, Gold);
            Label("ArtQuote", viewport, "TAKE A\nBREATH.", 28, 112, 336, 110, 48, White, true, 1.5f);
            Label("ArtCaption", viewport, "Your next move can wait.", 30, 79, 330, 28, 19, Ice);
            Image("PausedDot", viewport, 30, 39, 6, 6, Gold);
            Label("SimulationStatus", viewport, "SIMULATION SUSPENDED", 48, 28, 310, 28, 13, Gold, true, 2);
            Image("ArtRightBorder", window, 407, 18, 1, 684, new Color(Ice.r, Ice.g, Ice.b, .16f));
        }

        private static void BuildControls(RectTransform window, PauseSettingsController controller)
        {
            var content = Rect("AudioSettings", window, 458, 40, 660, 640);
            Label("SettingsEyebrow", content, "SETTINGS  /  01", 0, 613, 390, 25, 15, Ice, true, 2.8f);
            Label("Title", content, "Audio", 0, 545, 450, 66, 58, White, true, .2f);
            Label("TitleDescription", content, "Find your balance. Stay in the moment.", 1, 512, 620, 27, 21, Muted);
            var pauseBadge = Facet("PauseBadge", content, 539, 558, 121, 32, new Color(.11f, .20f, .24f, .6f), new Color(.40f, .65f, .68f, .45f), 6);
            Image("PauseGlyphLeft", pauseBadge.transform, 15, 10, 3, 12, Ice);
            Image("PauseGlyphRight", pauseBadge.transform, 21, 10, 3, 12, Ice);
            Label("PauseBadgeLabel", pauseBadge.transform, "PAUSED", 33, 2, 80, 28, 14, Ice, true, 1.2f);
            Image("HeaderDivider", content, 0, 492, 660, 1, Hex("29414B"));

            var master = Facet("MasterVolumeCard", content, 0, 267, 660, 202, Hex("10232E"), Hex("284955"), 10);
            Label("MasterLabel", master.transform, "MASTER VOLUME", 26, 153, 360, 28, 18, White, true, 2);
            Label("MasterDescription", master.transform, "All game sounds", 26, 122, 360, 25, 18, Muted);
            controller.volumeValue = Label("VolumePercent", master.transform, "100%", 430, 112, 203, 65, 54, Ice, true);
            controller.volumeValue.alignment = TextAlignmentOptions.MidlineRight;
            controller.masterSlider = MakeSlider(master.transform, 28, 62, 604, 40);
            Label("QuietLabel", master.transform, "QUIET", 27, 22, 160, 22, 12, Muted, false, 1.8f);
            var loud = Label("FullLabel", master.transform, "FULL", 472, 22, 160, 22, 12, Muted, false, 1.8f);
            loud.alignment = TextAlignmentOptions.MidlineRight;
            controller.equalizerBars = new Image[15];
            for (int i = 0; i < controller.equalizerBars.Length; i++)
            {
                var bar = Image("SignalBar_" + i.ToString("00"), master.transform, 270 + i * 8, 27, 3, 12 + Mathf.Sin(i * .8f) * 5f, new Color(Ice.r, Ice.g, Ice.b, .55f));
                bar.rectTransform.pivot = new Vector2(.5f, 0);
                controller.equalizerBars[i] = bar;
            }

            var mute = Facet("MuteRow", content, 0, 178, 660, 72, Hex("10212A"), new Color(.15f,.26f,.30f,.5f), 7);
            Label("MuteTitle", mute.transform, "Mute all audio", 25, 32, 420, 28, 21, White, true);
            controller.statusLabel = Label("MuteStatus", mute.transform, "SOUND ENABLED", 26, 12, 430, 18, 12, Muted, false, 1.6f);
            controller.muteToggle = MakeToggle(mute.transform, 552, 20, 78, 32);

            controller.testButton = MakeButton("TestSound", content, "PLAY TEST SOUND", 0, 104, 320, 52, false);
            controller.resetButton = MakeButton("ResetAudio", content, "RESTORE DEFAULT", 340, 104, 320, 52, false);
            var saved = Label("SaveHint", content, "Changes are saved automatically", 0, 69, 660, 22, 15, Muted);
            saved.alignment = TextAlignmentOptions.Center;
            controller.resumeButton = MakeButton("Resume", content, "RESUME GAME", 0, -1, 660, 58, true);
            Label("ResumeArrow", controller.resumeButton.transform, ">", 605, 4, 34, 48, 28, Hex("102631"), true).alignment = TextAlignmentOptions.Center;
            var esc = Label("EscapeHint", window, "ESC  /  BACK TO GAME", 500, 8, 580, 22, 12, Muted, false, 2);
            esc.alignment = TextAlignmentOptions.Center;

            Navigation sliderNavigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnDown = controller.muteToggle, selectOnUp = controller.resumeButton };
            controller.masterSlider.navigation = sliderNavigation;
            controller.muteToggle.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = controller.masterSlider, selectOnDown = controller.testButton };
            controller.testButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = controller.muteToggle, selectOnRight = controller.resetButton, selectOnDown = controller.resumeButton };
            controller.resetButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = controller.muteToggle, selectOnLeft = controller.testButton, selectOnDown = controller.resumeButton };
            controller.resumeButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = controller.testButton, selectOnDown = controller.masterSlider };
        }

        private static Slider MakeSlider(Transform parent, float x, float y, float width, float height)
        {
            RectTransform root = Rect("MasterVolumeSlider", parent, x, y, width, height);
            Image hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            Image("Track", root, 0, 17, width, 6, Hex("28424C"));
            for (int i = 0; i <= 10; i++) Image("Tick_" + i.ToString("00"), root, 2 + (width - 4) * i / 10f, 8, 1, 4, Hex("405D66"));
            RectTransform fillArea = Rect("FillArea", root, 0, 17, width, 6);
            Image fill = Image("Fill", fillArea, 0, 0, width, 6, Ice);
            Stretch(fill.rectTransform);
            RectTransform handleArea = Rect("HandleArea", root, 0, 0, width, height);
            Image handle = Image("Handle", handleArea, 0, 20, 18, 30, White);
            handle.rectTransform.pivot = new Vector2(.5f, .5f);
            handle.raycastTarget = true;
            Image("HandleDetail", handle.transform, 7, 7, 3, 16, Hex("356370"));
            Slider slider = root.gameObject.AddComponent<Slider>();
            slider.minValue = 0; slider.maxValue = 1; slider.value = 1;
            slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            handle.rectTransform.sizeDelta = new Vector2(18, -10);
            handle.rectTransform.anchoredPosition = Vector2.zero;
            slider.targetGraphic = handle;
            slider.colors = SelectColors(White, Ice, Gold, Ice);
            return slider;
        }

        private static Toggle MakeToggle(Transform parent, float x, float y, float width, float height)
        {
            var root = Facet("MuteToggle", parent, x, y, width, height, Hex("28414C"), Hex("547782"), 7);
            root.raycastTarget = true;
            Image knob = Image("MuteSwitchKnob", root.transform, 9, 7, 18, 18, Muted);
            Toggle toggle = root.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = root;
            toggle.isOn = false;
            toggle.colors = SelectColors(Color.white, new Color(1.2f, 1.2f, 1.2f), new Color(.7f, .8f, .85f), new Color(1.25f, 1.25f, 1.25f));
            root.gameObject.AddComponent<PauseSettingsHover>();
            var motion = root.gameObject.AddComponent<PauseSettingsToggleVisual>();
            motion.toggle = toggle;
            motion.knob = knob;
            motion.offX = 9;
            motion.onX = width - 27;
            motion.offColor = Muted;
            motion.onColor = Gold;
            return toggle;
        }

        private static Button MakeButton(string name, Transform parent, string label, float x, float y, float w, float h, bool primary)
        {
            Color normal = primary ? Ice : Hex("162D38");
            var panel = Facet(name, parent, x, y, w, h, normal, primary ? Ice : Hex("385A67"), 8);
            panel.raycastTarget = true;
            Button button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = panel;
            button.colors = SelectColors(Color.white, primary ? new Color(1.1f,1.1f,1.1f) : new Color(1.4f,1.4f,1.4f), new Color(.7f,.85f,.9f), primary ? new Color(1.12f,1.12f,1.12f) : new Color(1.4f,1.4f,1.4f));
            var text = Label("Label", panel.transform, label, 10, 0, w - 20, h, primary ? 21 : 16,
                primary ? Hex("102631") : White, true, primary ? 2f : 1.3f);
            text.alignment = TextAlignmentOptions.Center;
            var hover = panel.gameObject.AddComponent<PauseSettingsHover>();
            hover.focusScale = primary ? 1.012f : 1.018f;
            return button;
        }

        private static ColorBlock SelectColors(Color normal, Color highlighted, Color pressed, Color selected)
        {
            return new ColorBlock { normalColor = normal, highlightedColor = highlighted, pressedColor = pressed,
                selectedColor = selected, disabledColor = new Color(.4f,.4f,.4f,.5f), colorMultiplier = 1, fadeDuration = .13f };
        }

        private static Sprite ImportArtwork()
        {
            if (!File.Exists(ArtPath)) throw new FileNotFoundException("Generate the portrait artwork before building settings.", ArtPath);
            AssetDatabase.ImportAsset(ArtPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(ArtPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Could not import pause artwork.");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath);
        }

        private static RectTransform Rect(string name, Transform parent, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }
        private static Image Image(string name, Transform parent, float x, float y, float w, float h, Color color, Sprite sprite = null)
        {
            var image = Rect(name, parent, x, y, w, h).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }
        private static HUDFacetGraphic Facet(string name, Transform parent, float x, float y, float w, float h, Color fill, Color stroke, float cut)
        {
            var graphic = Rect(name, parent, x, y, w, h).gameObject.AddComponent<HUDFacetGraphic>();
            graphic.color = fill; graphic.borderColor = stroke;
            graphic.cornerCut = cut; graphic.bottomAlpha = 1;
            graphic.raycastTarget = false;
            return graphic;
        }
        private static TextMeshProUGUI Label(string name, Transform parent, string value, float x, float y, float w, float h, float size, Color color, bool strong = false, float spacing = 0)
        {
            var label = Rect(name, parent, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = strong ? bold : regular;
            label.text = value; label.fontSize = size; label.color = color;
            label.characterSpacing = spacing;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            return label;
        }
        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        private static Color Hex(string hex) { ColorUtility.TryParseHtmlString("#" + hex, out Color c); return c; }

        [MenuItem("Tools/Project R/Pause Settings/Capture Layout Previews")]
        public static void CapturePreviews()
        {
            Capture(1920, 1080, "output/PauseSettings/PauseSettings_1920x1080.png");
            Capture(1280, 720, "output/PauseSettings/PauseSettings_1280x720.png");
            Debug.Log("[PauseSettings] Layout previews saved in output/PauseSettings.");
        }

        /// <summary>Renders a preview scene; never changes or saves an open gameplay scene.</summary>
        public static string Capture(int width, int height, string path)
        {
            if (Application.isPlaying) throw new InvalidOperationException("Capture the prefab layout outside Play Mode.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) throw new FileNotFoundException("Build the settings prefab first.", PrefabPath);
            Scene scene = EditorSceneManager.NewPreviewScene();
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                GameObject root = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(root, scene);
                var cameraObject = new GameObject("Settings Preview Camera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.scene = scene;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Hex("243C48");
                camera.cullingMask = 1 << 5;
                camera.orthographic = true;
                camera.orthographicSize = 540;
                camera.nearClipPlane = .1f; camera.farClipPlane = 50;
                camera.targetTexture = texture;
                camera.aspect = width / (float)height;
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                var canvas = root.GetComponent<Canvas>();
                root.GetComponent<CanvasScaler>().enabled = false;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                canvas.scaleFactor = Mathf.Min(width / 1920f, height / 1080f);
                var controller = root.GetComponent<PauseSettingsController>();
                controller.enabled = false;
                controller.overlayGroup.alpha = 1;
                controller.overlayGroup.interactable = true;
                controller.overlayGroup.blocksRaycasts = true;
                controller.panel.localScale = Vector3.one;
                controller.panel.anchoredPosition = Vector2.zero;
                Canvas.ForceUpdateCanvases();
                foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true)) label.ForceMeshUpdate();
                camera.Render();
                RenderTexture.active = texture;
                pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                string absolute = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(absolute));
                File.WriteAllBytes(absolute, pixels.EncodeToPNG());
                return absolute;
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (pixels) Object.DestroyImmediate(pixels);
                texture.Release(); Object.DestroyImmediate(texture);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public static string Validate()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) throw new InvalidOperationException("PauseSettings prefab is missing.");
            var c = prefab.GetComponent<PauseSettingsController>();
            if (!c || !c.overlayGroup || !c.panel || !c.artwork || !c.masterSlider || !c.muteToggle || !c.volumeValue ||
                !c.statusLabel || !c.resumeButton || !c.resetButton || !c.testButton || !c.previewSource)
                throw new InvalidOperationException("PauseSettings has a missing serialized runtime reference.");
            if (prefab.GetComponentsInChildren<TMP_Text>(true).Length < 18) throw new InvalidOperationException("TMP labels missing.");
            if (!prefab.GetComponent<GraphicRaycaster>()) throw new InvalidOperationException("GraphicRaycaster missing.");
            if (c.masterSlider.minValue != 0 || c.masterSlider.maxValue != 1) throw new InvalidOperationException("Incorrect audio slider range.");
            if (c.overlayGroup.alpha != 0 || c.overlayGroup.blocksRaycasts) throw new InvalidOperationException("Menu must start hidden.");
            return "PASS: serialized UGUI/TMP settings prefab; all runtime controls, portrait artwork, animation targets, and hidden initial state verified.";
        }
    }
}
