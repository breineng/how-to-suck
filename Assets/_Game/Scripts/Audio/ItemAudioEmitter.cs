using UnityEngine;
namespace HowToSuck.Audio
{
    [DefaultExecutionOrder(11010),DisallowMultipleComponent,RequireComponent(typeof(SuckableObject))]
    public sealed class ItemAudioEmitter:MonoBehaviour
    {
        private SuckableObject item;
        private void Awake()=>item=GetComponent<SuckableObject>();
        private void LateUpdate()
        {
            // Same accepted snapshot on solo authority, NGO host and read-only replica.
            // Session-owned Run+Instance ledger survives this emitter's disable/despawn.
            if(item!=null&&item.State==SuckableState.Ingesting&&item.Ingestion!=null)
                GameAudioRoot.Current?.Ingestion(item.Ingestion);
        }
        private void OnCollisionEnter(Collision collision)
        {
            // Kinematic guest contacts are never treated as real collision evidence.
            if(isActiveAndEnabled&&item!=null&&item.HasPhysicsAuthority)GameAudioRoot.Current?.Collision(item,collision);
        }
    }
}
