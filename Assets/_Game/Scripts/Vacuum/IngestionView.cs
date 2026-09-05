using UnityEngine;
namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class IngestionView : MonoBehaviour
    {
        public SuckableObject Item;
        private void Awake(){if(Item==null)Item=GetComponent<SuckableObject>();}
        private void LateUpdate()
        {
            var snapshot=Item!=null ? Item.Ingestion : null;if(snapshot==null || Item.VisualRoot==null)return;
            float t=snapshot.Progress(Time.realtimeSinceStartupAsDouble);
            float entry=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.2f,.65f,t));
            var endpoint=snapshot.TargetPosition-snapshot.TargetRotation*Vector3.forward*.14f;
            Item.VisualRoot.SetPositionAndRotation(Vector3.Lerp(snapshot.StartPosition,endpoint,entry),Quaternion.Slerp(snapshot.StartRotation,snapshot.TargetRotation,entry));
            Item.VisualRoot.localScale=snapshot.StartScale*Mathf.Lerp(1,.001f,entry);
        }
    }
}