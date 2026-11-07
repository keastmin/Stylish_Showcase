using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectR.UI.MainTitle
{
    /// <summary>Pointer and keyboard focus share one interruptible, unscaled feedback response.</summary>
    [DisallowMultipleComponent]
    public sealed class MainTitleFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
    {
        public Image highlight;
        public Image accent;
        public TMP_Text label;
        public RectTransform visual;
        public MainTitleController owner;
        [Min(.01f)] public float hoverDuration = .12f;

        private Selectable _selectable;
        private bool _hovered, _selected, _pressed, _initialized;
        private float _focus, _press, _submitUntil;
        private Vector3 _baseScale;
        private Vector2 _basePosition;
        private Color _highlightColor, _accentColor, _labelColor;

        private bool CanInteract => _selectable == null || _selectable.IsInteractable();

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
            if (visual == null) visual = transform as RectTransform;
            if (owner == null) owner = GetComponentInParent<MainTitleController>();
            _baseScale = visual != null ? visual.localScale : Vector3.one;
            _basePosition = visual != null ? visual.anchoredPosition : Vector2.zero;
            _highlightColor = highlight != null ? highlight.color : Color.clear;
            _accentColor = accent != null ? accent.color : Color.clear;
            _labelColor = label != null ? label.color : Color.white;
            _initialized = true;
            Apply();
        }

        private void Update()
        {
            bool motionReduced = owner != null && owner.ReducedMotion;
            float target = CanInteract && (_hovered || _selected) ? 1f : 0f;
            float pressed = CanInteract && (_pressed || Time.unscaledTime < _submitUntil) ? 1f : 0f;
            float blend = motionReduced ? 1f : 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime / Mathf.Max(.01f, hoverDuration));
            _focus = Mathf.Lerp(_focus, target, blend);
            _press = Mathf.Lerp(_press, pressed, blend);
            Apply();
        }

        private void Apply()
        {
            if (highlight != null)
            {
                Color tint = _highlightColor;
                tint.a = Mathf.Lerp(_highlightColor.a, Mathf.Max(.16f, _highlightColor.a), _focus);
                highlight.color = tint;
            }
            if (accent != null)
            {
                Color tint = _accentColor;
                tint.a = Mathf.Lerp(_accentColor.a * .26f, 1f, _focus);
                accent.color = tint;
            }
            if (label != null) label.color = Color.Lerp(_labelColor, new Color(.729f, .953f, 1f, _labelColor.a), _focus);
            if (visual != null)
            {
                bool reduced = owner != null && owner.ReducedMotion;
                visual.localScale = _baseScale * (reduced ? 1f : 1f + .012f * _focus - .027f * _press);
                visual.anchoredPosition = _basePosition + (reduced ? Vector2.zero : new Vector2(4f * _focus, 0));
            }
        }

        private void OnDisable()
        {
            _hovered = _selected = _pressed = false;
            _focus = _press = _submitUntil = 0;
            if (_initialized) Apply();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!CanInteract) return;
            _hovered = true;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject);
            owner?.PlayHover();
        }
        public void OnPointerExit(PointerEventData eventData) { _hovered = false; _pressed = false; }
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!CanInteract || eventData.button != PointerEventData.InputButton.Left) return;
            _pressed = true;
            owner?.PlayConfirm();
        }
        public void OnPointerUp(PointerEventData eventData) => _pressed = false;
        public void OnSelect(BaseEventData eventData) { _selected = true; if (CanInteract) owner?.PlayHover(); }
        public void OnDeselect(BaseEventData eventData) { _selected = false; _pressed = false; }
        public void OnSubmit(BaseEventData eventData)
        {
            if (!CanInteract) return;
            _submitUntil = Time.unscaledTime + .09f;
            owner?.PlayConfirm();
        }
    }
}
