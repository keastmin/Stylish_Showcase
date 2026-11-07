using UnityEngine;
using UnityEngine.UI;

namespace ProjectR.UI
{
    /// <summary>Resolution-independent chamfered panel built from UGUI vertices.</summary>
    [AddComponentMenu("Project R/UI/HUD Facet Graphic")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HUDFacetGraphic : MaskableGraphic
    {
        [Min(0)] public float cornerCut = 12f;
        [Min(0)] public float borderWidth = 1f;
        public Color borderColor = new Color(0.44f, 0.60f, 0.66f, 0.5f);
        [Range(0, 1)] public float bottomAlpha = 0.8f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            float cut = Mathf.Min(cornerCut, Mathf.Min(r.width, r.height) * .5f);
            Vector2[] outer = Corners(r, cut);
            float inset = Mathf.Min(borderWidth, Mathf.Min(r.width, r.height) * .45f);
            Rect inside = new Rect(r.x + inset, r.y + inset, r.width - inset * 2, r.height - inset * 2);
            Vector2[] inner = Corners(inside, Mathf.Min(Mathf.Max(0, cut - inset * .6f), Mathf.Min(inside.width, inside.height) * .5f));
            for (int i = 0; i < 8; i++) vh.AddVert(outer[i], borderColor, Vector2.zero);
            for (int i = 0; i < 8; i++) vh.AddVert(inner[i], borderColor, Vector2.zero);
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;
                vh.AddTriangle(i, next, 8 + next);
                vh.AddTriangle(i, 8 + next, 8 + i);
            }
            Color center = color;
            center.a *= (1 + bottomAlpha) * .5f;
            vh.AddVert(inside.center, center, Vector2.zero);
            for (int i = 0; i < 8; i++)
            {
                Color tint = color;
                tint.a *= Mathf.Lerp(bottomAlpha, 1, Mathf.InverseLerp(r.yMin, r.yMax, inner[i].y));
                vh.AddVert(inner[i], tint, Vector2.zero);
            }
            for (int i = 0; i < 8; i++) vh.AddTriangle(16, 17 + i, 17 + (i + 1) % 8);
        }

        private static Vector2[] Corners(Rect r, float c)
        {
            return new[] { new Vector2(r.xMin+c,r.yMin), new Vector2(r.xMax-c,r.yMin),
                new Vector2(r.xMax,r.yMin+c), new Vector2(r.xMax,r.yMax-c),
                new Vector2(r.xMax-c,r.yMax), new Vector2(r.xMin+c,r.yMax),
                new Vector2(r.xMin,r.yMax-c), new Vector2(r.xMin,r.yMin+c) };
        }
    }
}
