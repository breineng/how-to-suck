using TMPro;
using UnityEngine;

namespace HowToSuck
{
    [DisallowMultipleComponent, RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class InlineKeycaps : MonoBehaviour
    {
        public const string LinkId = "keycap";
        private TMP_Text label;
        private InlineKeycapGraphic frames;
        public int VisibleFrameCount => frames != null ? frames.FrameCount : 0;

        public static string Key(string label) => string.IsNullOrEmpty(label) || label == InputBindingLabels.Unbound ? InputBindingLabels.Unbound :
            "<nobr><space=0.32em><link=\"" + LinkId + "\">" + label + "</link><space=0.32em></nobr>";
        public static void Set(TMP_Text label, string value)
        {
            if (label == null) return;
            if (value != null && value.Contains("<link=\"" + LinkId + "\">"))
            {
                label.richText = true;
                if (label.GetComponent<InlineKeycaps>() == null) label.gameObject.AddComponent<InlineKeycaps>();
            }
            if (label.text != value) label.text = value;
        }
        private void OnEnable()
        {
            label = GetComponent<TMP_Text>();
            if (frames == null)
            {
                var go = new GameObject("Inline key frames", typeof(RectTransform), typeof(CanvasRenderer), typeof(InlineKeycapGraphic));
                var rect = (RectTransform)go.transform; rect.SetParent(transform, false);
                rect.pivot = label.rectTransform.pivot;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
                frames = go.GetComponent<InlineKeycapGraphic>(); frames.Label = label; frames.raycastTarget = false;
            }
            frames.enabled = true;
            label.OnPreRenderText += Render;
            label.SetVerticesDirty();
        }
        private void Render(TMP_TextInfo info) => frames.RefreshGeometry();
        private void OnDisable()
        {
            if (label != null) label.OnPreRenderText -= Render;
            if (frames != null) frames.enabled = false;
        }
    }
}
