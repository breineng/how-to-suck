using UnityEngine;
using UnityEngine.EventSystems;

namespace HowToSuck
{
    // The input module sends Cancel only to the selected GameObject, not its parents.
    public sealed class SessionMenuCancel : MonoBehaviour, ICancelHandler
    {
        public SessionMenuView Menu;
        public void OnCancel(BaseEventData data) => Menu?.OnCancel(data);
    }
}
