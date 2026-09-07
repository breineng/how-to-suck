using TMPro;
using UnityEngine;

namespace HowToSuck
{
    public enum ProductUiButtonStyle { Primary, Secondary, Quiet, Destructive }

    [CreateAssetMenu(menuName = "How to Suck/UI/Product Theme", fileName = "ProductUiTheme")]
    public sealed class ProductUiTheme : ScriptableObject
    {
        [Header("Typography — Menu B")]
        public TMP_FontAsset HeadingFont;
        public TMP_FontAsset BodyFont;
        [Min(1f)] public float HeadingFontSize = 52f;
        [Min(1f)] public float ButtonFontSize = 32f;
        [Min(1f)] public float BodyFontSize = 26f;
        [Min(1f)] public float SmallFontSize = 22f;

        [Header("Palette")]
        public Color Ink = new Color(.025f, .025f, .024f, 1f);
        public Color Paper = new Color(.95f, .945f, .928f, 1f);
        public Color Accent = new Color(.82f, .025f, .03f, 1f);
        public Color HighlightedAccent = new Color(.74f, .021f, .027f, 1f);
        public Color PressedAccent = new Color(.65f, .018f, .025f, 1f);
        public Color ControlSurface = new Color(.895f, .887f, .865f, 1f);
        public Color HighlightedSurface = new Color(.84f, .829f, .801f, 1f);
        public Color PressedSurface = new Color(.77f, .755f, .725f, 1f);
        public Color MutedText = new Color(.38f, .37f, .35f, 1f);
        public Color DisabledBackground = new Color(.84f, .829f, .805f, 1f);
        public Color DisabledText = new Color(.36f, .35f, .33f, 1f);

        [Header("Shared layout values")]
        [Min(0f)] public float PanelPadding = 32f;
        [Min(0f)] public float SectionSpacing = 24f;
        [Min(0f)] public float ControlSpacing = 12f;
        [Min(1f)] public float FocusMarkWidth = 4f;

        public void GetButtonColors(ProductUiButtonStyle style, bool interactable,
            bool highlighted, bool pressed, out Color background, out Color label)
        {
            if (!interactable)
            {
                background = DisabledBackground;
                label = DisabledText;
                return;
            }

            switch (style)
            {
                case ProductUiButtonStyle.Primary:
                case ProductUiButtonStyle.Destructive:
                    background = pressed ? PressedAccent : highlighted ? HighlightedAccent : Accent;
                    label = Paper;
                    break;
                case ProductUiButtonStyle.Quiet:
                    background = pressed ? HighlightedSurface : ControlSurface;
                    if (!highlighted && !pressed) background.a = 0f;
                    label = Ink;
                    break;
                default:
                    background = pressed ? PressedSurface : highlighted ? HighlightedSurface : ControlSurface;
                    label = Ink;
                    break;
            }
        }
    }
}
