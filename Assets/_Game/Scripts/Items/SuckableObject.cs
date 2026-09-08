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
        public LootKey Key => new LootKey(RunId, InstanceId);
        public string TypeId { get; private set; }
        public long Value { get; private set; }
        public float RequiredIntakeSize { get; private set; }
        public bool CanBeSwallowedByPlayer { get; private set; }
        public bool CanBeSwallowedByTruck { get; private set; }
        public CargoRole CargoRole { get; private set; }
        public BossKey BossKey { get; private set; }
        public int StoredOwner { get; private set; }
        public int LastStorageOwner { get; private set; }
        public string LastStorageTierId { get; private set; }
        public ulong ActiveShotId { get; private set; }
        public int ShotOwner { get; private set; }
        // Authority-only delivery intent, scoped to the current shot. Loose cargo is never passively accepted.
        public int DirectedIntakeId { get; internal set; }
        private Transform capturedVisual;
        private Vector3 visualPosition, visualScale;
        private Quaternion visualRotation;
        private Renderer[] visualRenderers = Array.Empty<Renderer>();
        private bool[] rendererEnabled = Array.Empty<bool>();
        private bool[] colliderEnabled = Array.Empty<bool>();
        private BossKey boundBossKey;
        public SuckableState State { get; private set; } = SuckableState.Available;
        public bool WorldFrozen { get; private set; }
        public IngestionSnapshot Ingestion { get; internal set; }

        private Rigidbody body;
        private Collider[] gameplayColliders = Array.Empty<Collider>();
        private bool initialized;
        public bool HasPhysicsAuthority { get; private set; } = true;
        private bool roleBound;

        private void Awake() => CachePhysics();

        private void CachePhysics()
        {
            body = GetComponent<Rigidbody>();
            var owned = new List<Collider>();
            foreach (Collider collider in GetComponentsInChildren<Collider>(true))
                if (collider.GetComponentInParent<Rigidbody>(true) == body) owned.Add(collider);
            gameplayColliders = owned.ToArray();
        }

        // Network adapters bind once in Awake, before registration/state publication. Local items default authoritative.
        public void BindPhysicsAuthority(bool authority)
        {
            if ((roleBound || initialized) && HasPhysicsAuthority != authority) throw new InvalidOperationException("An item cannot change authority in place.");
            HasPhysicsAuthority = authority; roleBound = true; CachePhysics(); ApplyBodyMode();
        }
        public void ApplyReplicaState(string runId, ulong id, SuckableState state, bool frozen, LootReplicaProvenance provenance)
        {
            if (HasPhysicsAuthority) throw new InvalidOperationException("The authority cannot consume item replicas.");
            if (!Enum.IsDefined(typeof(SuckableState), state)) throw new ArgumentOutOfRangeException(nameof(state));
            if(id==0)throw new ArgumentException("Replica instance must be nonzero.");
            GameplayReplicaPolicy.RequireLoot(runId,state,initialized?TypeId:Definition?.TypeId,
                initialized?CargoRole:Definition!=null?Definition.CargoRole:(CargoRole)(-1),provenance);
            if (initialized && !BossKey.Equals(provenance.BossKey)) throw new InvalidOperationException("Replica boss identity changed in place.");
            if (!initialized && provenance.CargoRole == CargoRole.BossBody) BindReplicaBossCargo(provenance.BossKey);
            if (!initialized) Initialize(runId, id, true);
            if (runId != RunId || id != InstanceId) throw new InvalidOperationException("A replica cannot change run or instance identity.");
            if (State == SuckableState.Ingesting && state != State) RestoreVisualPose();
            StoredOwner=provenance.StoredOwner;LastStorageOwner=provenance.LastStorageOwner;LastStorageTierId=provenance.LastStorageTierId;
            ActiveShotId=provenance.ActiveShotId;ShotOwner=ActiveShotId!=0?LastStorageOwner:0;
            State = state; WorldFrozen = frozen; ApplyBodyMode();
        }
        public void Initialize(string runId, ulong id, bool frozen = false)
        {
            if (initialized) throw new InvalidOperationException("Loot identity and captured definition cannot be reinitialized in place.");
            if (!new LootKey(runId, id).IsValid) throw new ArgumentException("Loot needs a non-empty run ID.", nameof(runId));
            if (id == 0) throw new ArgumentOutOfRangeException(nameof(id), "Loot instance ID zero is reserved for unregistered objects.");
            if (!TryValidate(out string error)) throw new InvalidOperationException(error);
            if (Definition.CargoRole == CargoRole.BossBody && (!boundBossKey.IsValid || boundBossKey.RunId != runId))
                throw new InvalidOperationException("Boss cargo needs its authority-bound key for this run before initialization.");
            if (Definition.CargoRole == CargoRole.OrdinaryLoot && boundBossKey.IsValid) throw new InvalidOperationException("Ordinary cargo cannot carry a boss identity.");
            RunId = runId;
            InstanceId = id;
            TypeId = Definition.TypeId; Value = Definition.Value; RequiredIntakeSize = Definition.RequiredIntakeSize;
            CanBeSwallowedByPlayer = Definition.CanBeSwallowedByPlayer; CanBeSwallowedByTruck = Definition.CanBeSwallowedByTruck;
            CargoRole = Definition.CargoRole; BossKey = boundBossKey;
            CaptureVisualPose();
            colliderEnabled = new bool[gameplayColliders.Length];
            for (int i = 0; i < colliderEnabled.Length; i++) colliderEnabled[i] = gameplayColliders[i].enabled;
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
            if (!HasPhysicsAuthority || !initialized || State != expected) return false;
            bool loose = expected == SuckableState.Available || expected == SuckableState.InFlight;
            bool allowed = loose && (next == SuckableState.Ingesting || next == SuckableState.Lost) ||
                expected == SuckableState.Ingesting && (next == SuckableState.Delivered || next == SuckableState.Available || next == SuckableState.Lost) ||
                expected == SuckableState.InFlight && (next == SuckableState.Available || next == SuckableState.Spent && CargoRole == CargoRole.OrdinaryLoot);
            if (!allowed || WorldFrozen && (loose && next != SuckableState.Lost || next == SuckableState.Delivered)) return false;
            if (expected == SuckableState.Ingesting) RestoreVisualPose();
            if (expected == SuckableState.InFlight || next == SuckableState.Ingesting) CancelFlightProvenance();
            State = next;
            ApplyBodyMode();
            return true;
        }

        public void BindBossCargo(BossKey key)
        {
            if (!HasPhysicsAuthority || initialized || !key.IsValid || Definition == null || Definition.CargoRole != CargoRole.BossBody)
                throw new InvalidOperationException("Bind a valid boss key on authority before registration.");
            if (boundBossKey.IsValid && !boundBossKey.Equals(key)) throw new InvalidOperationException("Boss identity cannot be rebound.");
            boundBossKey = key;
        }
        // Network adapter's boss-key publication must be wired before its first replica initialization.
        public void BindReplicaBossCargo(BossKey key)
        {
            if (HasPhysicsAuthority || initialized || !key.IsValid || Definition == null || Definition.CargoRole != CargoRole.BossBody)
                throw new InvalidOperationException("Invalid replica boss cargo binding.");
            if (boundBossKey.IsValid && !boundBossKey.Equals(key)) throw new InvalidOperationException("Replica boss identity cannot be rebound.");
            boundBossKey = key;
        }
        public void CancelFlightProvenance() { ActiveShotId = 0; ShotOwner = 0; DirectedIntakeId = 0; }
        internal bool TryStore(int owner, string tier)
        {
            if (!HasPhysicsAuthority || !initialized || WorldFrozen || State != SuckableState.Ingesting || Body == null || capturedVisual == null || owner <= 0 || string.IsNullOrWhiteSpace(tier)) return false;
            RestoreVisualPose(); CancelFlightProvenance(); State = SuckableState.Stored;
            StoredOwner = owner; LastStorageOwner = owner; LastStorageTierId = tier; ApplyBodyMode(); return true;
        }
        internal bool TryReleaseStored(int owner, Vector3 position, Quaternion rotation, ulong shotId)
        {
            if (shotId == 0 || WorldFrozen || !PrepareStoredPose(owner, position, rotation)) return false;
            StoredOwner = 0; State = SuckableState.InFlight; ActiveShotId = shotId; ShotOwner = owner; ApplyBodyMode(); return true;
        }
        internal bool TryReturnStored(int owner, Vector3 position, Quaternion rotation)
        {
            if (!PrepareStoredPose(owner, position, rotation)) return false;
            StoredOwner = 0; State = SuckableState.Available; CancelFlightProvenance(); ApplyBodyMode(); return true;
        }
        private bool PrepareStoredPose(int owner, Vector3 position, Quaternion rotation)
        {
            if (!HasPhysicsAuthority || !initialized || State != SuckableState.Stored || StoredOwner != owner || Body == null || !ValidPose(position, rotation)) return false;
            // Stored is already kinematic. Caller has performed geometry clearance before entering this operation.
            body.position = position; body.rotation = Quaternion.Normalize(rotation); RestoreVisualPose(); return true;
        }
        public bool TryRelocateAvailable(LootKey expected, Vector3 position, Quaternion rotation)
        {
            if (!HasPhysicsAuthority || !initialized || !Key.Equals(expected) || State != SuckableState.Available || Body == null || !ValidPose(position, rotation)) return false;
            // Recovery/drop caller validates the destination; original identity and definition remain untouched.
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.position = position; body.rotation = Quaternion.Normalize(rotation); RestoreVisualPose(); return true;
        }
        private static bool ValidPose(Vector3 p, Quaternion q)
        {
            double norm = (double)q.x*q.x + (double)q.y*q.y + (double)q.z*q.z + (double)q.w*q.w;
            return Finite(p.x) && Finite(p.y) && Finite(p.z) && Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w) && Math.Abs(norm - 1) < .001;
        }
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private void CaptureVisualPose()
        {
            capturedVisual = VisualRoot; visualPosition = VisualRoot.localPosition;
            visualRotation = VisualRoot.localRotation; visualScale = VisualRoot.localScale;
            visualRenderers = VisualRoot.GetComponentsInChildren<Renderer>(true); rendererEnabled = new bool[visualRenderers.Length];
            for (int i = 0; i < visualRenderers.Length; i++) rendererEnabled[i] = visualRenderers[i].enabled;
        }
        public void RestoreVisualPose()
        {
            if (capturedVisual == null) return;
            capturedVisual.localPosition = visualPosition; capturedVisual.localRotation = visualRotation; capturedVisual.localScale = visualScale;
        }

        // Every freeze/state transition enters this one owner of Rigidbody and collider mode.
        private void ApplyBodyMode()
        {
            if (Body == null) return;
            bool kinematic = !HasPhysicsAuthority || WorldFrozen || (State != SuckableState.Available && State != SuckableState.InFlight);
            if (kinematic)
            {
                // Swept CCD is a flight-only mode and cannot accompany a
                // kinematic intake/storage/freeze. The fire service restores its
                // original mode when this flight ends.
                if (body.collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic ||
                    body.collisionDetectionMode == CollisionDetectionMode.Continuous)
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
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

            if (!initialized) return; // Do not overwrite authored enabled flags before their baseline capture.
            bool collisionsEnabled = State == SuckableState.Available || State == SuckableState.InFlight;
            for (int i = 0; i < gameplayColliders.Length; i++)
                if (gameplayColliders[i] != null) gameplayColliders[i].enabled = collisionsEnabled && (i >= colliderEnabled.Length || colliderEnabled[i]);
            bool visible = collisionsEnabled || State == SuckableState.Ingesting;
            for (int i = 0; i < visualRenderers.Length; i++)
                if (visualRenderers[i] != null) visualRenderers[i].enabled = visible && rendererEnabled[i];
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
