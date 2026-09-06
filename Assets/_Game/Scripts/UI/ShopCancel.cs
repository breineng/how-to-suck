using UnityEngine;
using UnityEngine.EventSystems;
namespace HowToSuck
{
    // Explicit Cancel forwarding for the selected UGUI button; no polling of gameplay input.
    public sealed class ShopCancel : MonoBehaviour, ICancelHandler
    {
        public ShopView View;
        public void OnCancel(BaseEventData data){if(View!=null)View.OnCancel(data);}
    }
}
