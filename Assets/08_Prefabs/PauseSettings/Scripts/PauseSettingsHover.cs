using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectR.UI
{
    /// <summary>Subtle pointer and keyboard focus response, independent of game time.</summary>
    [DisallowMultipleComponent]
    public sealed class PauseSettingsHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        [Range(1f, 1.1f)] public float focusScale = 1.025f;
        [Min(1f)] public float response = 16f;
        private bool _hovered;
        private bool _selected;
        private Vector3 _baseScale = Vector3.one;

        private void Awake() => _baseScale = transform.localScale;
        private void OnDisable()
        {
            _hovered = _selected = false;
            transform.localScale = _baseScale;
        }
        private void Update()
        {
            float target = _hovered || _selected ? focusScale : 1f;
            transform.localScale = Vector3.Lerp(transform.localScale, _baseScale * target,
                1f - Mathf.Exp(-response * Time.unscaledDeltaTime));
        }
        public void OnPointerEnter(PointerEventData eventData) => _hovered = true;
        public void OnPointerExit(PointerEventData eventData) => _hovered = false;
        public void OnSelect(BaseEventData eventData) => _selected = true;
        public void OnDeselect(BaseEventData eventData) => _selected = false;
    }
}
