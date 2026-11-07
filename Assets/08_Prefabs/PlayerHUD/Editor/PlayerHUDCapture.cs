using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectR.UI.Editor
{
    /// <summary>Offscreen layout review. Restores every changed camera/canvas property.</summary>
    public static class PlayerHUDCapture
    {
        public static string Capture(int width, int height, string path)
        {
            var hud = UnityEngine.Object.FindAnyObjectByType<PlayerHUDController>();
            Camera camera = Camera.main;
            if (!hud || !camera) throw new InvalidOperationException("A scene HUD and MainCamera are required.");
            Canvas canvas = hud.GetComponent<Canvas>();
            CanvasScaler scaler = hud.GetComponent<CanvasScaler>();
            PlayerHUDSafeArea safe = hud.GetComponentInChildren<PlayerHUDSafeArea>();
            var safeRect = safe.GetComponent<RectTransform>();
            var oldMin = safeRect.anchorMin; var oldMax = safeRect.anchorMax;
            bool oldSafe = safe.enabled; bool oldScaler = scaler.enabled;
            RenderMode oldMode = canvas.renderMode; Camera oldCamera = canvas.worldCamera;
            float oldDistance = canvas.planeDistance; float oldScale = canvas.scaleFactor;
            RenderTexture oldTarget = camera.targetTexture; RenderTexture oldActive = RenderTexture.active;
            float oldAspect = camera.aspect;
            var baseData = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (baseData == null) throw new InvalidOperationException("A URP base camera is required for HUD capture.");
            bool oldPostProcessing = baseData.renderPostProcessing;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D texture = null;
            try
            {
                safe.enabled = false; scaler.enabled = false;
                safeRect.anchorMin = Vector2.zero; safeRect.anchorMax = Vector2.one;
                camera.targetTexture = target; camera.aspect = (float)width / height;
                // This is a layout capture with scene post-processing temporarily excluded.
                // The production overlay HUD is likewise never post-processed.
                baseData.renderPostProcessing = false;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera;
                canvas.planeDistance = camera.nearClipPlane + .5f;
                canvas.scaleFactor = Mathf.Sqrt(width / 1920f * height / 1080f);
                Canvas.ForceUpdateCanvases();
                foreach (TMP_Text text in hud.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
                camera.Render();
                RenderTexture.active = target;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, texture.EncodeToPNG());
                return path + " (" + width + "x" + height + ")";
            }
            finally
            {
                RenderTexture.active = oldActive; camera.targetTexture = oldTarget; camera.aspect = oldAspect;
                baseData.renderPostProcessing = oldPostProcessing;
                canvas.renderMode = oldMode; canvas.worldCamera = oldCamera;
                canvas.planeDistance = oldDistance; canvas.scaleFactor = oldScale;
                safeRect.anchorMin = oldMin; safeRect.anchorMax = oldMax;
                safe.enabled = oldSafe; scaler.enabled = oldScaler;
                Canvas.ForceUpdateCanvases();
                if (texture) UnityEngine.Object.DestroyImmediate(texture);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
