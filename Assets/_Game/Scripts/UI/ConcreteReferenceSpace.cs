using UnityEngine;
namespace HowToSuck
{
 // A private coordinate space for the approved menu. The existing root CanvasScaler is never changed.
 [ExecuteAlways,DisallowMultipleComponent,RequireComponent(typeof(RectTransform))]
 public sealed class ConcreteReferenceSpace:MonoBehaviour
 {
  bool fitting;
  public void FitNow()
  {
   if(fitting||!(transform.parent is RectTransform parent))return;var r=(RectTransform)transform;float height=parent.rect.height;
   if(height<=0||parent.rect.width<=0)return;fitting=true;
   try{float scale=height/1080f;r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=Vector2.zero;r.localRotation=Quaternion.identity;r.localScale=new Vector3(scale,scale,1);r.sizeDelta=parent.rect.size/scale;}
   finally{fitting=false;}
  }
  void OnEnable()=>FitNow();void OnRectTransformDimensionsChange()=>FitNow();void LateUpdate()=>FitNow();
 }
}
