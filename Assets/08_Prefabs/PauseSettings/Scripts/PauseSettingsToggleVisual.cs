using UnityEngine;
using UnityEngine.UI;

namespace ProjectR.UI
{
    /// <summary>Moves the mute switch thumb even while the combat world is paused.</summary>
    [DisallowMultipleComponent]
    public sealed class PauseSettingsToggleVisual : MonoBehaviour
    {
        public Toggle toggle;
        public Image knob;
        public float offX = 9;
        public float onX = 51;
        public Color offColor = Color.gray;
        public Color onColor = Color.white;

        private void Update()
        {
            if (toggle == null || knob == null) return;
            float blend = 1 - Mathf.Exp(-20 * Time.unscaledDeltaTime);
            Vector2 position = knob.rectTransform.anchoredPosition;
            position.x = Mathf.Lerp(position.x, toggle.isOn ? onX : offX, blend);
            knob.rectTransform.anchoredPosition = position;
            knob.color = Color.Lerp(knob.color, toggle.isOn ? onColor : offColor, blend);
        }
    }
}
