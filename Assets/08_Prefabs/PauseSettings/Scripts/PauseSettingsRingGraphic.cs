using UnityEngine;
using UnityEngine.UI;

namespace ProjectR.UI
{
    /// <summary>Resolution-independent broken orbit decoration for the pause artwork.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PauseSettingsRingGraphic : MaskableGraphic
    {
        [Min(.1f)] public float thickness = 1.2f;
        [Range(0f, 1f)] public float arcFraction = .72f;
        [Range(16, 160)] public int segments = 96;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float outer = Mathf.Min(r.width, r.height) * .5f;
            float inner = Mathf.Max(0, outer - thickness);
            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f * arcFraction;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vh.AddVert(r.center + direction * inner, color, Vector2.zero);
                vh.AddVert(r.center + direction * outer, color, Vector2.zero);
                if (i == 0) continue;
                int n = i * 2;
                vh.AddTriangle(n - 2, n - 1, n + 1);
                vh.AddTriangle(n - 2, n + 1, n);
            }
        }
    }
}
