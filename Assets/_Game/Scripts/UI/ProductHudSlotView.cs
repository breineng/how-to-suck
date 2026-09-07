using UnityEngine;
using UnityEngine.UI;

namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class ProductHudSlotView : MonoBehaviour
    {
        public Image Outline, FilledMarker, ReservedMarker, ItemIcon;
        public GameObject NextMarker;
        bool presented, wasFilled, wasReserved, wasNext;
        Sprite previousIcon;

        public void Present(bool filled, bool reserved, bool next, Sprite icon,
            Color paper, Color accent, Color muted)
        {
            if (presented && wasFilled == filled && wasReserved == reserved &&
                wasNext == next && previousIcon == icon) return;
            presented = true; wasFilled = filled; wasReserved = reserved; wasNext = next; previousIcon = icon;
            if (Outline != null) Outline.color = next ? accent : paper;
            bool showIcon = filled && ItemIcon != null && icon != null;
            if (ItemIcon != null) { ItemIcon.sprite = showIcon ? icon : null; ItemIcon.enabled = showIcon; }
            if (FilledMarker != null) { FilledMarker.enabled = filled && !showIcon; FilledMarker.color = paper; }
            if (ReservedMarker != null) { ReservedMarker.enabled = reserved; ReservedMarker.color = muted; }
            if (NextMarker != null && NextMarker.activeSelf != next) NextMarker.SetActive(next);
        }
    }
}
