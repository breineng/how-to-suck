using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace HowToSuck
{
    public sealed class ConcreteButtonAccent:MonoBehaviour,ISelectHandler,IDeselectHandler,IPointerEnterHandler,IPointerExitHandler
    {
        public GameObject Accent;bool hovered,selected;
        public void OnSelect(BaseEventData data){selected=true;Refresh();}public void OnDeselect(BaseEventData data){selected=false;Refresh();}
        public void OnPointerEnter(PointerEventData data){hovered=true;Refresh();}public void OnPointerExit(PointerEventData data){hovered=false;Refresh();}
        void OnEnable(){selected=EventSystem.current!=null&&EventSystem.current.currentSelectedGameObject==gameObject;Refresh();}void OnDisable(){hovered=selected=false;if(Accent!=null)Accent.SetActive(false);}
        void Refresh(){if(Accent!=null)Accent.SetActive(hovered||selected);}
    }
}
