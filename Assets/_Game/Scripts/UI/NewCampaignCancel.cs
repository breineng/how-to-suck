using UnityEngine;
using UnityEngine.EventSystems;

namespace HowToSuck
{
    public sealed class NewCampaignCancel : MonoBehaviour, ICancelHandler
    {
        public NewCampaignView View;
        public void OnCancel(BaseEventData data) => View?.OnCancel(data);
    }
}
