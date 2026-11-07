using UnityEngine;
using UnityEngine.UI;

namespace ProjectR.UI.MainTitle
{
    /// <summary>A continuous 24-segment veil; all UI text remains separate and editable.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MainTitleGradientGraphic : MaskableGraphic
    {
        public bool horizontal = true;
        [Range(0f, 1f)] public float startOpacity = 1f;
        [Range(0f, 1f)] public float endOpacity;
        [Min(.01f)] public float power = 1f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect area = GetPixelAdjustedRect();
            const int segments = 24;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Color tint = color;
                tint.a *= Mathf.Pow(Mathf.Clamp01(Mathf.Lerp(startOpacity, endOpacity, t)), Mathf.Max(.01f, power));
                if (horizontal)
                {
                    float x = Mathf.Lerp(area.xMin, area.xMax, t);
                    vh.AddVert(new Vector2(x, area.yMin), tint, Vector2.zero);
                    vh.AddVert(new Vector2(x, area.yMax), tint, Vector2.zero);
                }
                else
                {
                    float y = Mathf.Lerp(area.yMin, area.yMax, t);
                    vh.AddVert(new Vector2(area.xMin, y), tint, Vector2.zero);
                    vh.AddVert(new Vector2(area.xMax, y), tint, Vector2.zero);
                }
                if (i == 0) continue;
                int n = i * 2;
                vh.AddTriangle(n - 2, n - 1, n + 1);
                vh.AddTriangle(n - 2, n + 1, n);
            }
        }
    }
}
