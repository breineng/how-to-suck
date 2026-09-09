using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HowToSuck
{
    // Uses the final TMP glyph positions, so inline frames follow alignment, wrapping and font scaling.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class InlineKeycapGraphic : MaskableGraphic
    {
        protected InlineKeycapGraphic() { useLegacyMeshGeneration = false; }
        public TMP_Text Label;
        public int FrameCount { get; private set; }
        public void RefreshGeometry()
        {
            if (!isActiveAndEnabled) return;
            // TMP can notify from inside the Canvas rebuild; do not register another rebuild there.
            if (CanvasUpdateRegistry.IsRebuildingGraphics()) UpdateGeometry();
            else SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear(); FrameCount = 0;
            if (Label == null || !Label.isActiveAndEnabled) return;
            var info = Label.textInfo;
            for (int l = 0; l < info.linkCount; l++)
            {
                var link = info.linkInfo[l];
                if (link.GetLinkID() != InlineKeycaps.LinkId) continue;
                float left = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.PositiveInfinity, top = float.NegativeInfinity, size = 0;
                Color32 tint = Label.color;
                int end = Mathf.Min(info.characterCount, link.linkTextfirstCharacterIndex + link.linkTextLength);
                for (int c = link.linkTextfirstCharacterIndex; c < end; c++)
                {
                    var ch = info.characterInfo[c];
                    if (!ch.isVisible) continue;
                    // Glyph quad corners include SDF outline padding; advances describe the actual text span.
                    left = Mathf.Min(left, ch.origin); right = Mathf.Max(right, ch.xAdvance);
                    // Stable height even for punctuation keys whose visible ink is tiny.
                    bottom = Mathf.Min(bottom, ch.baseLine - ch.pointSize * .12f);
                    top = Mathf.Max(top, ch.baseLine + ch.pointSize * .85f);
                    size = Mathf.Max(size, ch.pointSize); tint = ch.vertex_BL.color;
                }
                if (size <= 0 || float.IsInfinity(left)) continue;
                float pad = size * .14f, width = Mathf.Max(size, right - left + 2 * pad);
                float mid = (left + right) * .5f; left = mid - width * .5f; right = mid + width * .5f;
                bottom -= size * .04f; top += size * .04f;
                float stroke = Mathf.Max(1.2f, size * .055f);
                Quad(vertices, left, bottom, right, bottom + stroke, tint);
                Quad(vertices, left, top - stroke, right, top, tint);
                Quad(vertices, left, bottom + stroke, left + stroke, top - stroke, tint);
                Quad(vertices, right - stroke, bottom + stroke, right, top - stroke, tint);
                FrameCount++;
            }
        }
        private static void Quad(VertexHelper vh, float l, float b, float r, float t, Color32 tint)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(l, b), tint, Vector2.zero); vh.AddVert(new Vector3(l, t), tint, Vector2.zero);
            vh.AddVert(new Vector3(r, t), tint, Vector2.zero); vh.AddVert(new Vector3(r, b), tint, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
