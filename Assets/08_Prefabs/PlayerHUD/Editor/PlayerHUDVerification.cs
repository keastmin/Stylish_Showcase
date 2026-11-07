using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectR.UI.Editor
{
    /// <summary>Checks the saved asset in an isolated prefab scene; never saves QA state.</summary>
    public static class PlayerHUDVerification
    {
        [MenuItem("Tools/Project R/Player HUD/Verify Saved Prefab")]
        public static void RunFromMenu() => Debug.Log(Run());

        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Run prefab verification outside Play Mode.");
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerHUDBuilder.PrefabPath);
            int checks = 0;
            try
            {
                var hud = root.GetComponent<PlayerHUDController>();
                Require(hud != null, "Controller is missing.", ref checks);
                var canvas = root.GetComponent<Canvas>();
                Require(canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay, "Expected an overlay Canvas.", ref checks);
                var scaler = root.GetComponent<CanvasScaler>();
                Require(scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize
                    && scaler.referenceResolution == new Vector2(1920f, 1080f), "Canvas scaling differs from the authored 1920x1080 layout.", ref checks);
                var group = root.GetComponent<CanvasGroup>();
                Require(group != null && !group.blocksRaycasts && !group.interactable, "HUD must not intercept gameplay input.", ref checks);
                Require(hud.Player == null, "The reusable prefab must not persist a scene player reference.", ref checks);
                Require(root.GetComponentInChildren<PlayerHUDSafeArea>(true) != null, "Safe area is missing.", ref checks);

                var references = new Dictionary<string, Component>
                {
                    { "healthFill", hud.healthFill }, { "healthTrail", hud.healthTrail },
                    { "skillFill", hud.skillFill }, { "skillIcon", hud.skillIcon }, { "skillGaugeIcon", hud.skillGaugeIcon },
                    { "skillThresholdMarker", hud.skillThresholdMarker },
                    { "feedbackGroup", hud.feedbackGroup }, { "lowHealthGroup", hud.lowHealthGroup },
                    { "readinessGroup", hud.readinessGroup }, { "skillStatus", hud.skillStatus },
                    { "feedbackLabel", hud.feedbackLabel }, { "feedbackHint", hud.feedbackHint },
                    { "skillKeyLabel", hud.skillKeyLabel }, { "dodgeKeyLabel", hud.dodgeKeyLabel }, { "dodgeChargeOne", hud.dodgeChargeOne },
                    { "dodgeChargeTwo", hud.dodgeChargeTwo }, { "dodgeRechargeFill", hud.dodgeRechargeFill },
                    { "dodgeValue", hud.dodgeValue }, { "dodgeStatus", hud.dodgeStatus }, { "dodgeCooldown", hud.dodgeCooldown },
                    { "ultimateFill", hud.ultimateFill }, { "ultimateIcon", hud.ultimateIcon },
                    { "ultimateKeycap", hud.ultimateKeycap }, { "ultimateReadinessGroup", hud.ultimateReadinessGroup },
                    { "ultimateKeyLabel", hud.ultimateKeyLabel }, { "ultimateStatus", hud.ultimateStatus },
                    { "ultimateCooldown", hud.ultimateCooldown }
                };
                foreach (var reference in references)
                {
                    Require(reference.Value != null && reference.Value.transform.IsChildOf(root.transform),
                        "Missing or external reference: " + reference.Key, ref checks);
                }
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0,
                        "Missing script on " + transform.name, ref checks);
                foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
                    Require(!graphic.raycastTarget, "Raycast target enabled: " + graphic.name, ref checks);
                foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
                    Require(label.font != null, "Missing TMP font on " + label.name, ref checks);

                // These states exercise thresholds independently of fill animation.
                Require(root.transform.Find("SafeArea/Controls") == null, "Removed movement/attack key guide must not return.", ref checks);
                var dock = root.transform.Find("SafeArea/ActionDock") as RectTransform;
                Require(dock != null && dock.anchorMin == Vector2.right && dock.anchorMax == Vector2.right
                    && dock.Find("UltimateAction") != null, "Ultimate must be in the bottom-right action dock.", ref checks);
                hud.RefreshInputLabels();
                Require(hud.ultimateKeyLabel.text == "Q", "Unbound authoring preview must show the default ultimate key.", ref checks);
                hud.SetUltimatePreview(true, false, true, 0f, 8f);
                Require(hud.IsUltimateReady && hud.ultimateStatus.text == "READY" && hud.ultimateFill.fillAmount == 1f
                    && hud.ultimateIcon.color == hud.ultimateReadyColor && hud.ultimateReadinessGroup.alpha == 1f,
                    "A usable ultimate must show a full gold ready indicator.", ref checks);
                hud.SetUltimatePreview(true, true, false, 6f, 8f);
                Require(!hud.IsUltimateReady && hud.ultimateStatus.text == "ACTIVE" && hud.ultimateCooldown.text == "6.0s"
                    && Mathf.Approximately(hud.ultimateFill.fillAmount, .25f), "Active cast must take priority over cooldown.", ref checks);
                hud.SetUltimatePreview(true, false, false, 4f, 8f);
                Require(hud.ultimateStatus.text == "COOLDOWN" && hud.ultimateCooldown.text == "4.0s"
                    && Mathf.Approximately(hud.ultimateFill.fillAmount, .5f) && hud.ultimateReadinessGroup.alpha == 0f,
                    "Ultimate recharge must use gameplay remaining time and its cast duration.", ref checks);
                hud.SetUltimatePreview(true, false, false, .001f, 8f);
                Require(hud.ultimateCooldown.text == "0.1s", "A nonzero ultimate cooldown must not display zero seconds.", ref checks);
                hud.SetUltimatePreview(true, false, false, 0f, 0f);
                Require(!hud.IsUltimateReady && hud.ultimateStatus.text == "WAIT" && hud.ultimateFill.fillAmount == 1f,
                    "A zero-cooldown ultimate blocked by gameplay must not report ready.", ref checks);
                hud.SetUltimatePreview(true, false, false, 12f, 8f);
                Require(hud.ultimateFill.fillAmount == 0f, "Ultimate progress must clamp to valid fill limits.", ref checks);
                hud.SetUltimatePreview(false, false, false, 0f, 0f);
                Require(!hud.IsUltimateReady && hud.ultimateStatus.text == "UNAVAILABLE" && hud.ultimateFill.fillAmount == 0f,
                    "A missing ultimate definition must not invent readiness.", ref checks);
                hud.SetDodgePreview(1, .4f);
                Require(hud.dodgeValue.text == "1 / 2" && hud.dodgeStatus.text == "AVAILABLE"
                    && hud.dodgeCooldown.text == "0.4s", "One dodge must remain available during shared recharge.", ref checks);
                Require(Mathf.Approximately(hud.dodgeRechargeFill.fillAmount, .6f), "Dodge recharge progress must match remaining time.", ref checks);
                hud.SetDodgePreview(0, 1f);
                Require(hud.dodgeValue.text == "0 / 2" && hud.dodgeStatus.text == "RECHARGING"
                    && hud.dodgeCooldown.text == "1.0s" && hud.dodgeRechargeFill.fillAmount == 0f,
                    "Second dodge must show exhausted charges and a restarted timer.", ref checks);
                hud.SetDodgePreview(2, 0f);
                Require(hud.dodgeValue.text == "2 / 2" && hud.dodgeCooldown.text == "READY"
                    && hud.dodgeRechargeFill.fillAmount == 1f, "Both charges must show ready together.", ref checks);
                hud.SetPreview(100f, 100f, 49f, 100f, 50f);
                Require(!hud.IsSkillReady && Mathf.Approximately(hud.skillFill.fillAmount, .49f)
                    && hud.skillGaugeIcon.color == hud.skillChargingColor, "49/100 must show a charging gauge at cost 50.", ref checks);
                hud.SetPreview(100f, 100f, 50f, 100f, 50f);
                Require(hud.IsSkillReady && hud.skillGaugeIcon.color == hud.skillReadyColor && hud.readinessGroup.alpha == 1f,
                    "Readiness must begin at the real cost, not at a full bar.", ref checks);
                Require(Mathf.Approximately(hud.skillFill.fillAmount, .5f), "Ready at cost 50 must still show a half-filled gauge.", ref checks);
                hud.SetPreview(20f, 100f, 69f, 100f, 70f);
                Require(!hud.IsSkillReady && Mathf.Approximately(hud.healthFill.fillAmount, .2f)
                    && hud.healthFill.color == hud.healthCriticalColor && hud.lowHealthGroup.alpha > 0f,
                    "Low health and a different skill cost must be represented independently.", ref checks);
                Require(Mathf.Approximately(hud.skillThresholdMarker.anchorMin.x, .7f)
                    && Mathf.Approximately(hud.skillThresholdMarker.anchorMax.x, .7f), "Cost marker must follow 70/100.", ref checks);
                hud.SetPreview(-10f, 100f, 150f, 100f, 50f);
                Require(hud.healthFill.fillAmount == 0f && hud.healthTrail.fillAmount == 0f && hud.skillFill.fillAmount == 1f,
                    "Visual values must clamp to valid fill limits.", ref checks);
                hud.BindPlayer(null);
                Require(hud.healthFill.fillAmount == 0f && hud.skillFill.fillAmount == 0f && !hud.IsSkillReady
                    && hud.skillGaugeIcon.color == hud.skillChargingColor && hud.lowHealthGroup.alpha == 0f
                    && hud.readinessGroup.alpha == 0f && !hud.skillThresholdMarker.gameObject.activeSelf,
                    "An unbound HUD must not invent live health, gauge, or skill cost.", ref checks);
                Require(hud.feedbackGroup.alpha == 0f && hud.feedbackHint.text == string.Empty,
                    "An unbound HUD must not show a counter prompt.", ref checks);
                Require(hud.dodgeValue.text == "-- / --" && hud.dodgeCooldown.text == "--"
                    && hud.dodgeStatus.text == "NO SIGNAL", "Unbound dodge HUD must not invent readiness.", ref checks);
                Require(!hud.IsUltimateReady && hud.ultimateStatus.text == "NO SIGNAL" && hud.ultimateCooldown.text == "--"
                    && hud.ultimateFill.fillAmount == 0f && hud.ultimateReadinessGroup.alpha == 0f,
                    "Unbound ultimate HUD must clear its state and ready accent.", ref checks);
                return "[PlayerHUD] PASS: " + checks + " checks; prefab references, input transparency, TMP fonts, skill thresholds, dodge/ultimate states, critical/clamped values and unbound state. Preview scene discarded without saving.";
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void Require(bool condition, string message, ref int checks)
        {
            if (!condition) throw new InvalidOperationException("[PlayerHUD] " + message);
            checks++;
        }
    }
}
