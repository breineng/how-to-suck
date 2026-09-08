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
        float pulse;

        public void Present(bool filled, bool reserved, bool next, Sprite icon,
            Color paper, Color accent, Color muted)
        {
            if (presented && wasFilled == filled && wasReserved == reserved &&
                wasNext == next && previousIcon == icon) return;
            if(filled&&(!presented||!wasFilled||previousIcon!=icon))pulse=1;
            presented = true; wasFilled = filled; wasReserved = reserved; wasNext = next; previousIcon = icon;
            if (Outline != null) Outline.color = next ? accent : paper;
            bool showIcon = filled && ItemIcon != null && icon != null;
            if (ItemIcon != null) { ItemIcon.sprite = showIcon ? icon : null; ItemIcon.enabled = showIcon; }
            if (FilledMarker != null) { FilledMarker.enabled = filled && !showIcon; FilledMarker.color = paper; }
            if (ReservedMarker != null) { ReservedMarker.enabled = reserved; ReservedMarker.color = muted; }
            if (NextMarker != null && NextMarker.activeSelf != next) NextMarker.SetActive(next);
        }
        void Update(){pulse=Mathf.MoveTowards(pulse,0,Time.unscaledDeltaTime*3);if(ItemIcon!=null)ItemIcon.transform.localScale=Vector3.one*(1+.2f*pulse);}
    }
}
