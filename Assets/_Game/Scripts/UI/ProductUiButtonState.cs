using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HowToSuck
{
    /// <summary>Visual state only. Bind a Button with its Transition set to None.</summary>
    [DisallowMultipleComponent]
    public sealed class ProductUiButtonState : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler, ISubmitHandler
    {
        public ProductUiTheme Theme;
        public Button Button;
        public Image Background;
        public TMP_Text Label;
        [Tooltip("A separate red stroke. Place outside the button fill; use a non-raycast Graphic.")]
        public Graphic FocusMark;
        public ProductUiButtonStyle Style = ProductUiButtonStyle.Secondary;
        public TMP_FontAsset FontOverride;
        [Tooltip("Zero uses the theme's button size. Existing autosizing and alignment are preserved.")]
        [Min(0f)] public float FontSizeOverride;

        bool hovered;
        bool selected;
        bool pressed;
        float submitFlashUntil;
        int previousState = -1;

        void OnEnable()
        {
            if (Button == null) Button = GetComponent<Button>();
            selected = EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject == gameObject;
            Refresh();
        }

        void OnDisable()
        {
            hovered = selected = pressed = false;
            submitFlashUntil = 0f;
            previousState = -1;
            SetFocusMark(false);
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) return;
            hovered = pressed = false;
            submitFlashUntil = 0f;
            Refresh();
        }

        void Update()
        {
            // IsInteractable also observes parent CanvasGroups and runtime availability changes.
            bool interactable = Button != null && Button.IsActive() && Button.IsInteractable();
            if (!interactable) { pressed = false; submitFlashUntil = 0f; }
            int state = (interactable ? 1 : 0) | (hovered ? 2 : 0) |
                (selected ? 4 : 0) | (pressed ? 8 : 0) | (SubmitFlashing ? 16 : 0);
            if (state != previousState) Refresh();
        }

        public void Refresh()
        {
            if (Theme == null) return;
            bool interactable = Button != null && Button.IsActive() && Button.IsInteractable();
            bool highlighted = hovered || selected;
            Theme.GetButtonColors(Style, interactable, highlighted, (pressed && hovered) || SubmitFlashing,
                out Color background, out Color label);
            if (Background != null) Background.color = background;
            if (Label != null)
            {
                Label.color = label;
                TMP_FontAsset font = FontOverride != null ? FontOverride : Theme.HeadingFont;
                if (font != null && Label.font != font) Label.font = font;
                Label.fontSize = FontSizeOverride > 0f ? FontSizeOverride : Theme.ButtonFontSize;
            }
            SetFocusMark(interactable && highlighted);
            previousState = (interactable ? 1 : 0) | (hovered ? 2 : 0) |
                (selected ? 4 : 0) | (pressed ? 8 : 0) | (SubmitFlashing ? 16 : 0);
        }

        bool SubmitFlashing => Time.unscaledTime < submitFlashUntil;

        void SetFocusMark(bool visible)
        {
            if (FocusMark == null) return;
            Color color = Theme != null ? Theme.Accent : new Color(.82f, .025f, .03f, 1f);
            if (!visible) color.a = 0f;
            FocusMark.color = color;
        }

        public void OnPointerEnter(PointerEventData data) { hovered = true; Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovered = false; Refresh(); }
        public void OnPointerDown(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || Button == null ||
                !Button.IsActive() || !Button.IsInteractable()) return;
            pressed = true;
            Refresh();
        }
        public void OnPointerUp(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left) return;
            pressed = false;
            Refresh();
        }
        public void OnSelect(BaseEventData data) { selected = true; Refresh(); }
        public void OnDeselect(BaseEventData data) { selected = false; Refresh(); }
        public void OnSubmit(BaseEventData data)
        {
            if (!isActiveAndEnabled || Button == null || !Button.IsActive() || !Button.IsInteractable()) return;
            // Button still owns Submit and invokes its existing listeners exactly once.
            submitFlashUntil = Time.unscaledTime + .12f;
            Refresh();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            previousState = -1;
            if (isActiveAndEnabled) Refresh();
        }
#endif
    }
}
