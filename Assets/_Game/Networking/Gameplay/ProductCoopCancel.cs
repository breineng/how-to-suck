using UnityEngine;
using UnityEngine.EventSystems;
namespace HowToSuck.Networking
{
    public sealed class ProductCoopCancel:MonoBehaviour,ICancelHandler
    {
        public ProductCoopPanelView Panel;
        public void OnCancel(BaseEventData data)=>Panel?.OnCancel(data);
    }
}
