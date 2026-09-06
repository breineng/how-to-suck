using UnityEngine;
namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class ItemFireContactRelay : MonoBehaviour
    {
        private ItemFireService owner;
        private SuckableObject item;
        internal void Bind(ItemFireService service, SuckableObject source)
        {
            if (owner != null && !ReferenceEquals(owner, service)) throw new System.InvalidOperationException("Contact relay already has an owner.");
            owner = service; item = source;
        }
        internal void Unbind(ItemFireService service) { if (ReferenceEquals(owner, service)) { owner = null; item = null; } }
        private void OnCollisionEnter(Collision collision)
        {
            if (owner == null || item == null || !item.HasPhysicsAuthority || item.State != SuckableState.InFlight || item.ActiveShotId == 0) return;
            // Collision is engine-owned and reused. Copy only this contact's values; never mutate gameplay here.
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : item.Body.position;
            owner.EnqueueContact(item, item.ActiveShotId, item.ShotOwner, collision.collider, collision.relativeVelocity.magnitude, point);
        }
    }
}
