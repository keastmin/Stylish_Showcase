using UnityEngine;
using UnityEngine.UI;

namespace ProjectR.UI
{
    /// <summary>A single continuous mesh fade keeps artwork legible without overlapping image strips.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PauseSettingsGradientGraphic : MaskableGraphic
    {
        [Range(0, 1)] public float bottomOpacity = 1;
        [Range(0, 1)] public float topOpacity;
        [Min(.1f)] public float fadePower = 1.7f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect rect = GetPixelAdjustedRect();
            const int rows = 16;
            for (int i = 0; i <= rows; i++)
            {
                float t = i / (float)rows;
                Color tint = color;
                tint.a *= Mathf.Pow(Mathf.Lerp(bottomOpacity, topOpacity, t), fadePower);
                float y = Mathf.Lerp(rect.yMin, rect.yMax, t);
                vh.AddVert(new Vector2(rect.xMin, y), tint, Vector2.zero);
                vh.AddVert(new Vector2(rect.xMax, y), tint, Vector2.zero);
                if (i == 0) continue;
                int n = i * 2;
                vh.AddTriangle(n - 2, n - 1, n + 1);
                vh.AddTriangle(n - 2, n + 1, n);
            }
        }
    }
}
