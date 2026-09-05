using UnityEngine;
namespace HowToSuck
{
    [DefaultExecutionOrder(2000)]
    [DisallowMultipleComponent]
    public sealed class IngestionView : MonoBehaviour
    {
        public SuckableObject Item;
        private IngestionSnapshot displayed;
        private Vector3 renderedEnd;
        private Quaternion renderedRotation;
        private void Awake(){if(Item==null)Item=GetComponent<SuckableObject>();}
        private void LateUpdate()
        {
            var snapshot=Item!=null?Item.Ingestion:null;if(snapshot==null || Item.VisualRoot==null)return;
            if(displayed!=snapshot){displayed=snapshot;renderedEnd=snapshot.EndPosition;renderedRotation=snapshot.TargetRotation;}
            var receiver=snapshot.Receiver;
            if(receiver!=null)
            {
                var presentation=receiver.PresentationTarget;
                if(presentation!=null)
                {
                    Vector3 offset=Quaternion.Inverse(snapshot.TargetRotation)*(snapshot.EndPosition-snapshot.TargetPosition);
                    renderedEnd=presentation.position+presentation.rotation*offset;
                    renderedRotation=presentation.rotation;
                }
                else{renderedEnd=snapshot.EndPosition;renderedRotation=snapshot.TargetRotation;}
            }
            // A destroyed receiver keeps its last visible endpoint. Only presentation reads this pose;
            // admission, completion time, collection and the frozen physics root stay authoritative.
            float t=snapshot.Progress(Time.realtimeSinceStartupAsDouble);
            float entry=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.2f,.65f,t));
            Item.VisualRoot.SetPositionAndRotation(Vector3.Lerp(snapshot.StartPosition,renderedEnd,entry),Quaternion.Slerp(snapshot.StartRotation,renderedRotation,entry));
            Item.VisualRoot.localScale=snapshot.StartScale*Mathf.Lerp(1,.001f,entry);
        }
    }
}