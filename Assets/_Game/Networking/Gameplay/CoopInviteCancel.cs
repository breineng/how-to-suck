using UnityEngine;
using UnityEngine.EventSystems;

namespace HowToSuck.Networking
{
    // UGUI sends Cancel to the selected control only.
    public sealed class CoopInviteCancel : MonoBehaviour, ICancelHandler
    {
        public CoopInviteView View;
        public void OnCancel(BaseEventData data) => View?.OnCancel(data);
    }
}
