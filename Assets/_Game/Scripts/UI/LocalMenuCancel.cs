using UnityEngine;
using UnityEngine.EventSystems;
namespace HowToSuck
{
 public sealed class LocalMenuCancel:MonoBehaviour,ICancelHandler
 {
  public LocalMenuView Menu;
  public void OnCancel(BaseEventData data){if(Menu!=null&&Menu.IsOpen)Menu.OnCancel(data);else GetComponentInParent<SessionMenuView>()?.OnCancel(data);}
 }
}
