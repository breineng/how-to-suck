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
        private IntakeReceiver collectingReceiver;
        private float collectionDistance;
        private Vector3 collectionOffset,renderedStart;
        private void Awake(){if(Item==null)Item=GetComponent<SuckableObject>();}
        private void PresentCollection()
        {
            IntakeReceiver receiver=null;
            if(Item.State==SuckableState.Available&&!Item.WorldFrozen&&Item.InstanceId!=0)
                foreach(var candidate in IntakeReceiver.ActiveReceivers)
                    if(candidate!=null&&!candidate.IsTruck&&candidate.PresentationTarget!=null&&candidate.Emitter!=null&&
                        candidate.Emitter.Active&&candidate.Emitter.CollectingItemId==Item.InstanceId)
                    {receiver=candidate;break;}
            if(receiver==null)
            {
                if(collectingReceiver!=null||collectionOffset!=Vector3.zero)Item.RestoreVisualPose();
                collectingReceiver=null;collectionOffset=Vector3.zero;return;
            }
            if(collectingReceiver!=receiver)
            {
                // Only the separate graphics may move. Legacy props with colliders
                // under VisualRoot retain their physical pose until admission.
                foreach(var collider in Item.GameplayColliders)
                    if(collider!=null&&collider.transform.IsChildOf(Item.VisualRoot))return;
                collectingReceiver=receiver;
                collectionDistance=Mathf.Max(.001f,Vector3.Distance(Item.Body.worldCenterOfMass,receiver.Position));
            }
            float remaining=Vector3.Distance(Item.Body.worldCenterOfMass,receiver.Position);
            float progress=Mathf.Clamp01(1-remaining/collectionDistance);
            collectionOffset=(receiver.PresentationTarget.position-receiver.Position)*progress;
            Item.VisualRoot.position=Item.UnshiftedVisualPosition+collectionOffset;
        }
        private void LateUpdate()
        {
            if(Item==null||Item.VisualRoot==null)return;
            var snapshot=Item.Ingestion;
            if(snapshot==null){displayed=null;PresentCollection();return;}
            if(displayed!=snapshot)
            {
                displayed=snapshot;renderedEnd=snapshot.EndPosition;renderedRotation=snapshot.TargetRotation;
                renderedStart=snapshot.StartPosition+(collectingReceiver==snapshot.Receiver?collectionOffset:Vector3.zero);
                collectingReceiver=null;collectionOffset=Vector3.zero;
            }
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
            float t=snapshot.Progress(snapshot.PresentationNow);
            // Continue moving immediately after either nozzle admits the item;
            // mouth expansion runs alongside entry, without a frozen lead-in.
            float progress=Mathf.Clamp01(t/.65f);
            float entry=progress*(2-progress);
            Item.VisualRoot.SetPositionAndRotation(Vector3.Lerp(renderedStart,renderedEnd,entry),Quaternion.Slerp(snapshot.StartRotation,renderedRotation,entry));
            Item.VisualRoot.localScale=snapshot.StartScale*Mathf.Lerp(1,.001f,entry);
        }
        private void OnDisable()
        {
            if(Item!=null)Item.RestoreVisualPose();
            displayed=null;collectingReceiver=null;collectionOffset=Vector3.zero;
        }
    }
}
