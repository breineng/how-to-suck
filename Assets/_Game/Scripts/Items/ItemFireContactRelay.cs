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
        private void OnCollisionEnter(Collision collision) => RelayPhysicalContact(collision);
        // Speculative CCD can report Enter while two shapes are still far apart.
        // A real impact may arrive later as Stay on that same collision pair.
        private void OnCollisionStay(Collision collision) => RelayPhysicalContact(collision);
        private void RelayPhysicalContact(Collision collision)
        {
            if (owner == null || item == null || !item.HasPhysicsAuthority || item.State != SuckableState.InFlight || item.ActiveShotId == 0) return;
            // Broad-phase predictions alone are not hits. Fast cargo passing a
            // doorway can report both jambs over half a metre away, with no
            // collision response. Keep delivery/damage provenance in that case.
            bool physical = collision.impulse.sqrMagnitude > .00000001f;
            float nearest = float.PositiveInfinity;
            Vector3 point = item.Body.position;
            for (int i = 0; i < collision.contactCount; i++)
            {
                var contact = collision.GetContact(i);
                if (contact.separation >= nearest) continue;
                nearest = contact.separation;
                point = contact.point;
            }
            if (!physical && nearest > .005f) return;
            // Collision is engine-owned and reused. Copy only this contact's values; never mutate gameplay here.
            owner.EnqueueContact(item, item.ActiveShotId, item.ShotOwner, collision.collider, collision.relativeVelocity.magnitude, point);
        }
    }
}
