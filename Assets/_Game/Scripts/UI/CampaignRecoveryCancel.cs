using UnityEngine;
using UnityEngine.EventSystems;

namespace HowToSuck
{
    // UI cancel is sent to the selected button; forward it to its owning recovery dialog.
    public sealed class CampaignRecoveryCancel : MonoBehaviour, ICancelHandler
    {
        public CampaignRecoveryView Owner;
        public void OnCancel(BaseEventData data){if(Owner!=null)Owner.OnCancel(data);}
    }
}
