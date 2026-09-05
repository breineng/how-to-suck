using System;
using System.Collections.Generic;
using UnityEngine;

namespace HowToSuck
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SuckableObject : MonoBehaviour
    {
        public SuckableDefinition Definition;
        public Transform VisualRoot;

        public Rigidbody Body
        {
            get
            {
                if (body == null) body = GetComponent<Rigidbody>();
                return body;
            }
        }
        public Collider[] GameplayColliders => gameplayColliders;
        public string RunId { get; private set; }
        public ulong InstanceId { get; private set; }
        public SuckableState State { get; private set; } = SuckableState.Available;
        public bool WorldFrozen { get; private set; }
        public IngestionSnapshot Ingestion { get; internal set; }

        private Rigidbody body;
        private Collider[] gameplayColliders = Array.Empty<Collider>();
        private bool initialized;

        private void Awake() => CachePhysics();

        private void CachePhysics()
        {
            body = GetComponent<Rigidbody>();
            var owned = new List<Collider>();
            foreach (Collider collider in GetComponentsInChildren<Collider>(true))
                if (collider.GetComponentInParent<Rigidbody>(true) == body) owned.Add(collider);
            gameplayColliders = owned.ToArray();
        }

        public void Initialize(string runId, ulong id, bool frozen = false)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("Loot needs a non-empty run ID.", nameof(runId));
            if (id == 0) throw new ArgumentOutOfRangeException(nameof(id), "Loot instance ID zero is reserved for unregistered objects.");
            if (!TryValidate(out string error)) throw new InvalidOperationException(error);
            RunId = runId;
            InstanceId = id;
            State = SuckableState.Available;
            WorldFrozen = frozen;
            initialized = true;
            ApplyBodyMode();
        }

        public void SetWorldFrozen(bool frozen)
        {
            WorldFrozen = frozen;
            ApplyBodyMode();
        }

        public bool TryTransition(SuckableState expected, SuckableState next)
        {
            if (!initialized || State != expected) return false;
            bool allowed = expected == SuckableState.Available &&
                    (next == SuckableState.Ingesting || next == SuckableState.Lost) ||
                expected == SuckableState.Ingesting &&
                    (next == SuckableState.Collected || next == SuckableState.Lost);
            if (!allowed || WorldFrozen && next == SuckableState.Ingesting) return false;
            State = next;
            ApplyBodyMode();
            return true;
        }

        // Every freeze/state transition enters this one owner of Rigidbody and collider mode.
        private void ApplyBodyMode()
        {
            if (Body == null) return;
            bool kinematic = WorldFrozen || State != SuckableState.Available;
            if (kinematic)
            {
                // Unity rejects velocity writes on a body that is already kinematic.
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.isKinematic = true;
            }
            else if (body.isKinematic)
            {
                body.isKinematic = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }

            bool collisionsEnabled = State == SuckableState.Available;
            foreach (Collider collider in gameplayColliders)
                if (collider != null) collider.enabled = collisionsEnabled;
        }

        public bool TryValidate(out string error)
        {
            CachePhysics();
            if (Definition == null)
                return Invalid("is missing its item definition", out error);
            if (!Definition.TryValidate(out error)) return false;
            if (body == null || GetComponentsInChildren<Rigidbody>(true).Length != 1)
                return Invalid("must have exactly one Rigidbody on its root", out error);
            if (float.IsNaN(body.mass) || float.IsInfinity(body.mass) || body.mass <= 0f)
                return Invalid("must have a finite positive Rigidbody mass", out error);
            if (!UnitScale(transform.localScale) || !UnitScale(transform.lossyScale))
                return Invalid("must have unit root/world scale; resize its visuals and authored colliders instead", out error);
            if (VisualRoot == null || VisualRoot == transform || !VisualRoot.IsChildOf(transform))
                return Invalid("needs a distinct child VisualRoot for presentation", out error);
            if (gameplayColliders.Length == 0)
                return Invalid("needs at least one collider attached to its root Rigidbody", out error);

            bool hasSolidShape = false;
            foreach (Collider collider in gameplayColliders)
            {
                bool supported = collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider;
                if (collider is MeshCollider mesh) supported = mesh.convex && mesh.sharedMesh != null;
                if (!supported)
                    return Invalid($"collider '{collider.name}' must be a primitive or a convex MeshCollider with a mesh", out error);
                if (!collider.isTrigger) hasSolidShape = true;
            }
            if (!hasSolidShape) return Invalid("needs a solid gameplay collider, not only triggers", out error);
            error = null;
            return true;
        }

        private static bool UnitScale(Vector3 scale) =>
            Mathf.Approximately(scale.x, 1f) && Mathf.Approximately(scale.y, 1f) && Mathf.Approximately(scale.z, 1f);

        private bool Invalid(string reason, out string error)
        {
            error = $"Item '{name}' {reason}.";
            return false;
        }
    }
}
