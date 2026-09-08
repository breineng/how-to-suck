using UnityEngine;
using UnityEngine.EventSystems;
namespace HowToSuck
{
    [DisallowMultipleComponent,RequireComponent(typeof(UnityEngine.UI.Selectable))]
    public sealed class UiActionSound:MonoBehaviour,IPointerEnterHandler
    {
        UnityEngine.UI.Selectable control;UnityEngine.UI.Button button;UnityEngine.UI.Toggle toggle;
        void Awake(){control=GetComponent<UnityEngine.UI.Selectable>();button=control as UnityEngine.UI.Button;toggle=control as UnityEngine.UI.Toggle;}
        void OnEnable(){if(button!=null)button.onClick.AddListener(Click);if(toggle!=null)toggle.onValueChanged.AddListener(Toggled);}
        void OnDisable(){if(button!=null)button.onClick.RemoveListener(Click);if(toggle!=null)toggle.onValueChanged.RemoveListener(Toggled);}
        // Button already checked eligibility; its action may now have opened a modal.
        void Click()=>Audio.GameAudioRoot.Current?.Action(Audio.SfxId.UiClick,Vector3.zero);
        void Toggled(bool value)=>Click();
        public void OnPointerEnter(PointerEventData e)
        {if(control!=null&&control.IsInteractable())Audio.GameAudioRoot.Current?.Action(Audio.SfxId.UiHover,Vector3.zero);}
    }
}
