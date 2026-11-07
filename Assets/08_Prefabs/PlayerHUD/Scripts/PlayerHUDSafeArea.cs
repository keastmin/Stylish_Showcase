using UnityEngine;

namespace ProjectR.UI
{
    /// <summary>Attach to a full-stretch child of an overlay Canvas.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class PlayerHUDSafeArea : MonoBehaviour
    {
        [SerializeField] private bool _respectSafeArea = true;
        private RectTransform _rect;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;
        private bool _lastRespectSafeArea;
        private bool _applied;

        private void OnEnable() => ApplySafeArea(true);
        private void Update() => ApplySafeArea(false);
        private void OnValidate() => _applied = false;

        public void ApplySafeArea(bool force = false)
        {
            if (_rect == null) _rect = GetComponent<RectTransform>();
            int width = Screen.width;
            int height = Screen.height;
            if (width <= 0 || height <= 0) return;
            Rect safe = _respectSafeArea ? Screen.safeArea : new Rect(0f, 0f, width, height);
            if (safe.width <= 0f || safe.height <= 0f) safe = new Rect(0f, 0f, width, height);
            Vector2Int size = new Vector2Int(width, height);
            if (!force && _applied && safe == _lastSafeArea && size == _lastScreenSize && _lastRespectSafeArea == _respectSafeArea) return;

            _rect.anchorMin = new Vector2(Mathf.Clamp01(safe.xMin / width), Mathf.Clamp01(safe.yMin / height));
            _rect.anchorMax = new Vector2(Mathf.Clamp01(safe.xMax / width), Mathf.Clamp01(safe.yMax / height));
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
            _lastSafeArea = safe;
            _lastScreenSize = size;
            _lastRespectSafeArea = _respectSafeArea;
            _applied = true;
        }
    }
}
