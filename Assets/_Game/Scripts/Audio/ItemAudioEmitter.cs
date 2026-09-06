using UnityEngine;
namespace HowToSuck.Audio
{
    [DefaultExecutionOrder(11010),DisallowMultipleComponent,RequireComponent(typeof(SuckableObject))]
    public sealed class ItemAudioEmitter:MonoBehaviour
    {
        private SuckableObject item;
        private void Awake()=>item=GetComponent<SuckableObject>();
        // Ingestion accents now originate once from a real authority admission, not from repeated snapshots.
        private void OnCollisionEnter(Collision collision)
        {
            // Kinematic guest contacts are never treated as real collision evidence.
            if(isActiveAndEnabled&&item!=null&&item.HasPhysicsAuthority)GameAudioRoot.Current?.Collision(item,collision);
        }
    }
}
