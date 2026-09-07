using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace HowToSuck
{
 // Keeps keyboard-selected bindings visible inside the same real ScrollRect used by mouse wheel.
 public sealed class ConcreteScrollSelection:MonoBehaviour
 {
  public ScrollRect Scroll;
  private readonly Vector3[] corners=new Vector3[4];
  void LateUpdate()
  {
   if(Scroll==null||Scroll.content==null||Scroll.viewport==null||EventSystem.current==null)return;
   var selected=EventSystem.current.currentSelectedGameObject;
   if(selected==null||!selected.activeInHierarchy||!selected.transform.IsChildOf(Scroll.content))return;
   var control=selected.GetComponent<Selectable>();if(control==null||!control.IsInteractable())return;
   var rect=selected.transform as RectTransform;if(rect==null)return;
   rect.GetWorldCorners(corners);float bottom=float.PositiveInfinity,top=float.NegativeInfinity;
   foreach(var c in corners){float y=Scroll.viewport.InverseTransformPoint(c).y;bottom=Mathf.Min(bottom,y);top=Mathf.Max(top,y);}
   var view=Scroll.viewport.rect;float delta=top>view.yMax?view.yMax-top:bottom<view.yMin?view.yMin-bottom:0;
   if(Mathf.Abs(delta)<.01f)return;Scroll.StopMovement();var p=Scroll.content.anchoredPosition;p.y=Mathf.Clamp(p.y+delta,0,Mathf.Max(0,Scroll.content.rect.height-view.height));Scroll.content.anchoredPosition=p;
  }
 }
}
