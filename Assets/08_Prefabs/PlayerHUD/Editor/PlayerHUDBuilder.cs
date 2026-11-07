using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ProjectR.UI.Editor
{
    /// <summary>Rebuildable, fully serialized UGUI authoring source. Never runs in a player build.</summary>
    public static class PlayerHUDBuilder
    {
        public const string Folder = "Assets/08_Prefabs/PlayerHUD";
        public const string PrefabPath = Folder + "/PlayerHUD.prefab";
        private const string Fonts = "Assets/Download Assets/Heat - Complete Modern UI/Fonts/";
        private static TMP_FontAsset regular, bold;
        private static Sprite solidSprite;
        private static readonly Color Ice = Hex("BAF3FF");
        private static readonly Color Gold = Hex("FFD38A");
        private static readonly Color White = Hex("EEF7FA");
        private static readonly Color Muted = Hex("91A9B5");
        private static readonly Color Ink = new Color(.025f, .045f, .06f, .91f);

        [MenuItem("Tools/Project R/Player HUD/Rebuild Prefab")]
        public static void Build()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Build the HUD outside Play Mode.");
            Directory.CreateDirectory(Folder);
            regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "RobotoCondensed-Regular SDF.asset");
            bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Fonts + "RobotoCondensed-Bold SDF.asset");
            if (!regular || !bold) throw new InvalidOperationException("Project RobotoCondensed TMP fonts are required.");
            Sprite crest = ImportCrest();
            solidSprite = ImportSolidSprite();
            GameObject root = new GameObject("PlayerHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            try
            {
                root.layer = 5;
                Canvas canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 40;
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = .5f;
                CanvasGroup cg = root.GetComponent<CanvasGroup>();
                cg.interactable = false;
                cg.blocksRaycasts = false;
                var controller = root.AddComponent<PlayerHUDController>();

                RectTransform safe = Rect("SafeArea", root.transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                safe.anchorMax = Vector2.one;
                safe.offsetMin = safe.offsetMax = Vector2.zero;
                safe.gameObject.AddComponent<PlayerHUDSafeArea>();

                BuildPlayerStatus(safe, controller, crest);
                BuildActionDock(safe, controller, crest);

                var feedback = Rect("CombatFeedback", safe, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 250), new Vector2(540, 82));
                controller.feedbackGroup = feedback.gameObject.AddComponent<CanvasGroup>();
                controller.feedbackGroup.alpha = 0;
                Panel("FeedbackPlate", feedback, Vector2.zero, new Vector2(540, 82)).color = new Color(.025f,.045f,.06f,.8f);
                Line("AccentLeft", feedback, 0, 24, 3, 34, Ice);
                Line("AccentRight", feedback, 537, 24, 3, 34, Ice);
                controller.feedbackLabel = Label("FeedbackTitle", feedback, "PERFECT DODGE", 16, 34, 508, 36, 28, Ice, true, 3);
                controller.feedbackLabel.alignment = TextAlignmentOptions.Center;
                controller.feedbackHint = Label("CounterHint", feedback, "LMB  COUNTER", 16, 11, 508, 23, 18, White, false, 1.5f);
                controller.feedbackHint.alignment = TextAlignmentOptions.Center;
                controller.healthNormalColor = White;
                controller.healthCriticalColor = Hex("FF7D86");
                controller.skillChargingColor = Hex("718A9A");
                controller.skillReadyColor = Ice;
                controller.ultimateReadyColor = Gold;
                controller.SetPreview(100, 100, 0, 100, 50);
                controller.SetDodgePreview(2, 0f);
                controller.SetUltimatePreview(true, false, true, 0f, 8f);
                foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
                Canvas.ForceUpdateCanvases();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[PlayerHUD] Saved " + PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void BuildPlayerStatus(Transform safe, PlayerHUDController controller, Sprite crest)
        {
            // Two independent rails leave the playfield visible; no full-panel background or labels.
            var status = Rect("PlayerStatus", safe, Vector2.up, Vector2.up, new Vector2(56, -48), new Vector2(432, 68));
            var health = Rect("Health", status, Vector2.zero, Vector2.zero, new Vector2(0, 36), new Vector2(432, 30));
            var healthRail = Panel("RailFrame", health, new Vector2(32, 0), new Vector2(400, 30));
            healthRail.cornerCut = 7;
            healthRail.color = new Color(.025f, .045f, .06f, .78f);
            healthRail.borderColor = new Color(.44f, .60f, .66f, .38f);
            var healthBadge = Panel("HealthIcon", health, new Vector2(0, 1), new Vector2(28, 28));
            healthBadge.cornerCut = 6;
            Line("CrossHorizontal", healthBadge.transform, 7, 12, 14, 4, White);
            Line("CrossVertical", healthBadge.transform, 12, 7, 4, 14, White);
            var healthTrack = Rect("HealthTrack", health, Vector2.zero, Vector2.zero, new Vector2(40, 9), new Vector2(384, 12));
            Picture("Track", healthTrack, Vector2.zero, new Vector2(384, 12), null, new Color(.13f,.21f,.25f,1));
            controller.healthTrail = Filled("DamageTrail", healthTrack, new Vector2(384, 12), Hex("718A9A"));
            controller.healthFill = Filled("HealthFill", healthTrack, new Vector2(384, 12), White);
            for (int i = 1; i < 4; i++) Line("Division_" + i.ToString("00"), healthTrack, i * 96, 0, 1, 12, new Color(.03f,.08f,.10f,.6f));
            var low = Rect("LowHealthAlert", health, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(432, 30));
            controller.lowHealthGroup = low.gameObject.AddComponent<CanvasGroup>();
            controller.lowHealthGroup.alpha = 0;
            Line("CriticalAccent", low, 40, 4, 384, 2, Hex("FF7D86"));
            Line("CriticalCrossHorizontal", low, 7, 13, 14, 4, Hex("FF7D86"));
            Line("CriticalCrossVertical", low, 12, 8, 4, 14, Hex("FF7D86"));

            var skill = Rect("SkillGauge", status, Vector2.zero, Vector2.zero, new Vector2(0, 2), new Vector2(364, 28));
            var skillRail = Panel("RailFrame", skill, new Vector2(32, 2), new Vector2(332, 24));
            skillRail.cornerCut = 6;
            skillRail.color = new Color(.025f, .045f, .06f, .68f);
            skillRail.borderColor = new Color(.44f, .60f, .66f, .28f);
            var skillBadge = Panel("SkillIcon", skill, Vector2.zero, new Vector2(28, 28));
            skillBadge.cornerCut = 6;
            controller.skillGaugeIcon = Picture("ScytheCrest", skillBadge.transform, new Vector2(2, 2), new Vector2(24, 24), crest, Ice);
            controller.skillGaugeIcon.preserveAspect = true;
            var skillTrack = Rect("SkillTrack", skill, Vector2.zero, Vector2.zero, new Vector2(40, 11), new Vector2(316, 6));
            Picture("Track", skillTrack, Vector2.zero, new Vector2(316, 6), null, new Color(.13f,.21f,.25f,1));
            controller.skillFill = Filled("SkillFill", skillTrack, new Vector2(316, 6), Hex("718A9A"));
            controller.skillThresholdMarker = Line("CostThreshold", skillTrack, 158, -3, 2, 12, White).rectTransform;
        }

        private static void BuildActionDock(Transform safe, PlayerHUDController controller, Sprite crest)
        {
            var dock = Rect("ActionDock", safe, Vector2.right, Vector2.right, new Vector2(-56, 48), new Vector2(418, 288));
            Panel("Plate", dock, Vector2.zero, new Vector2(418, 288));
            Line("TopAccent", dock, 318, 287, 84, 2, Ice);
            Line("RowDivider", dock, 16, 184, 386, 1, new Color(.45f,.58f,.64f,.35f));
            Line("UltimateDivider", dock, 16, 96, 386, 1, new Color(.45f,.58f,.64f,.35f));
            var dodge = Rect("DodgeAction", dock, Vector2.zero, Vector2.zero, new Vector2(0, 184), new Vector2(418, 104));
            var dodgeKey = Panel("DodgeKeycap", dodge, new Vector2(16, 25), new Vector2(86, 48));
            dodgeKey.color = new Color(.10f,.17f,.21f,.92f);
            dodgeKey.cornerCut = 6;
            controller.dodgeKeyLabel = Label("DodgeKey", dodgeKey.transform, "SHIFT", 0, 0, 86, 48, 19, White, true, .5f);
            controller.dodgeKeyLabel.alignment = TextAlignmentOptions.Center;
            Label("DodgeLabel", dodge, "DODGE", 120, 72, 102, 22, 18, Muted, true, 1.5f);
            controller.dodgeStatus = Label("DodgeStatus", dodge, "AVAILABLE", 238, 72, 156, 22, 16, Ice, true, .7f);
            controller.dodgeStatus.alignment = TextAlignmentOptions.MidlineRight;
            controller.dodgeChargeOne = Line("Charge_01", dodge, 120, 42, 34, 12, Ice);
            controller.dodgeChargeTwo = Line("Charge_02", dodge, 160, 42, 34, 12, Ice);
            controller.dodgeValue = Label("DodgeValue", dodge, "2 / 2", 214, 34, 82, 28, 24, White, true);
            var recharge = Rect("RechargeTrack", dodge, Vector2.zero, Vector2.zero, new Vector2(120, 18), new Vector2(178, 6));
            Picture("Track", recharge, Vector2.zero, new Vector2(178, 6), null, new Color(.13f,.21f,.25f,1));
            controller.dodgeRechargeFill = Filled("RechargeFill", recharge, new Vector2(178, 6), Ice);
            controller.dodgeCooldown = Label("CooldownTime", dodge, "READY", 312, 9, 82, 26, 20, Ice, true);
            controller.dodgeCooldown.alignment = TextAlignmentOptions.MidlineRight;

            var skill = Rect("SkillAction", dock, Vector2.zero, Vector2.zero, new Vector2(0, 96), new Vector2(418, 88));
            var skillKey = Panel("SkillKeycap", skill, new Vector2(16, 18), new Vector2(86, 48));
            skillKey.color = Ice;
            skillKey.borderColor = Ice;
            skillKey.cornerCut = 6;
            controller.skillKeyLabel = Label("SkillKey", skillKey.transform, "E", 0, 0, 86, 48, 25, Ink, true);
            controller.skillKeyLabel.alignment = TextAlignmentOptions.Center;
            controller.skillIcon = Picture("ScytheCrest", skill, new Vector2(116, 22), new Vector2(44, 44), crest, Ice);
            controller.skillIcon.preserveAspect = true;
            Label("SkillLabel", skill, "SKILL", 174, 52, 220, 22, 18, Muted, true, 1.5f);
            controller.skillStatus = Label("SkillStatus", skill, "CHARGING", 174, 22, 220, 29, 25, White, true, 1.1f);
            var readiness = Rect("ReadyAccent", skill, Vector2.zero, Vector2.zero, new Vector2(174, 12), new Vector2(220, 8));
            controller.readinessGroup = readiness.gameObject.AddComponent<CanvasGroup>();
            controller.readinessGroup.alpha = 0;
            Line("ReadyLine", readiness, 0, 3, 196, 2, Ice);
            var diamond = Line("ReadyDiamond", readiness, 212, 0, 6, 6, Ice);
            diamond.rectTransform.localEulerAngles = new Vector3(0, 0, 45);

            var ultimate = Rect("UltimateAction", dock, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(418, 96));
            controller.ultimateKeycap = Panel("UltimateKeycap", ultimate, new Vector2(16, 26), new Vector2(86, 48));
            controller.ultimateKeycap.color = Gold;
            controller.ultimateKeycap.borderColor = Gold;
            controller.ultimateKeycap.cornerCut = 6;
            controller.ultimateKeyLabel = Label("UltimateKey", controller.ultimateKeycap.transform, "Q", 0, 0, 86, 48, 25, Ink, true);
            controller.ultimateKeyLabel.alignment = TextAlignmentOptions.Center;
            controller.ultimateIcon = Picture("UltimateCrest", ultimate, new Vector2(116, 30), new Vector2(44, 44), crest, Gold);
            controller.ultimateIcon.preserveAspect = true;
            Label("UltimateLabel", ultimate, "ULTIMATE", 174, 60, 220, 22, 18, Gold, true, 1.5f);
            controller.ultimateStatus = Label("UltimateStatus", ultimate, "READY", 174, 30, 142, 29, 23, Gold, true, .7f);
            controller.ultimateCooldown = Label("UltimateCooldown", ultimate, "READY", 318, 31, 76, 26, 20, Gold, true);
            controller.ultimateCooldown.alignment = TextAlignmentOptions.MidlineRight;
            var ultimateTrack = Rect("UltimateRechargeTrack", ultimate, Vector2.zero, Vector2.zero, new Vector2(174, 18), new Vector2(220, 5));
            Picture("Track", ultimateTrack, Vector2.zero, new Vector2(220, 5), null, new Color(.13f,.21f,.25f,1));
            controller.ultimateFill = Filled("UltimateRechargeFill", ultimateTrack, new Vector2(220, 5), Gold);
            var ultimateReady = Rect("UltimateReadyAccent", ultimate, Vector2.zero, Vector2.zero, new Vector2(116, 12), new Vector2(286, 70));
            controller.ultimateReadinessGroup = ultimateReady.gameObject.AddComponent<CanvasGroup>();
            var ultimateDiamond = Line("ReadyDiamond", ultimateReady, 16, 0, 6, 6, Gold);
            ultimateDiamond.rectTransform.localEulerAngles = new Vector3(0, 0, 45);
            Line("ReadyEdge", ultimateReady, 284, 12, 2, 46, Gold);
        }

        [MenuItem("Tools/Project R/Player HUD/Install In Active Scene")]
        public static void InstallInActiveScene()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Install outside Play Mode.");
            Scene scene = SceneManager.GetActiveScene();
            var existing = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PlayerHUDController>(true)).FirstOrDefault();
            if (existing) { Selection.activeGameObject = existing.gameObject; return; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (!prefab) throw new InvalidOperationException("Build the PlayerHUD prefab first.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "Install Player HUD");
            var player = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PlayerCore>(true)).FirstOrDefault();
            instance.GetComponent<PlayerHUDController>().BindPlayer(player);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponent<PlayerHUDController>());
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                foreach (PlayerHealthUI old in sceneRoot.GetComponentsInChildren<PlayerHealthUI>(true))
                { Undo.RecordObject(old.gameObject, "Replace legacy HP HUD"); old.gameObject.SetActive(false); }
                foreach (PlayerSkillGaugeUI old in sceneRoot.GetComponentsInChildren<PlayerSkillGaugeUI>(true))
                { Undo.RecordObject(old.gameObject, "Replace legacy skill HUD"); old.gameObject.SetActive(false); }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = instance;
            Debug.Log("[PlayerHUD] Installed in " + scene.path + ". Legacy player bars disabled; enemy HUD retained.");
        }

        private static Sprite ImportCrest()
        {
            string path = Folder + "/Art/ScytheCrest.png";
            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Generate Art/ScytheCrest.png before building the HUD.");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Sprite ImportSolidSprite()
        {
            string path = Folder + "/Art/WhitePixel.png";
            if (!File.Exists(path)) File.WriteAllBytes(path, Texture2D.whiteTexture.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }
        private static HUDFacetGraphic Panel(string name, Transform parent, Vector2 pos, Vector2 size)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.zero, pos, size);
            var graphic = rt.gameObject.AddComponent<HUDFacetGraphic>();
            graphic.color = Ink;
            graphic.bottomAlpha = .94f;
            graphic.raycastTarget = false;
            return graphic;
        }
        private static Image Picture(string name, Transform parent, Vector2 pos, Vector2 size, Sprite sprite, Color color)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.zero, pos, size);
            var graphic = rt.gameObject.AddComponent<Image>();
            graphic.sprite = sprite;
            graphic.color = color;
            graphic.raycastTarget = false;
            return graphic;
        }
        private static Image Line(string name, Transform parent, float x, float y, float w, float h, Color color)
        { return Picture(name, parent, new Vector2(x, y), new Vector2(w, h), null, color); }
        private static Image Filled(string name, Transform parent, Vector2 size, Color color)
        {
            var image = Picture(name, parent, Vector2.zero, size, solidSprite, color);
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = 0;
            image.fillAmount = 1;
            return image;
        }
        private static TMP_Text Label(string name, Transform parent, string text, float x, float y, float w, float h, float size, Color color, bool heavy = false, float spacing = 0)
        {
            var rt = Rect(name, parent, Vector2.zero, Vector2.zero, new Vector2(x, y), new Vector2(w, h));
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = heavy ? bold : regular;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.characterSpacing = spacing;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = size * .75f;
            tmp.fontSizeMax = size;
            tmp.raycastTarget = false;
            return tmp;
        }
        private static Color Hex(string hex) { Color color; ColorUtility.TryParseHtmlString("#" + hex, out color); return color; }
    }
}
