using System;
using UnityEngine;
namespace HowToSuck
{
    // Authority-owned inventory. Kept outside player GameObject lifetime until safe drop/world teardown.
    public sealed class PlayerStorage
    {
        private readonly StorageQueue<IngestionSnapshot> queue;
        public string RunId => queue.RunId;
        public int OwnerId { get; }
        public int Capacity => queue.Capacity;
        public int Count => queue.Count;
        public int Reserved => queue.Reserved;
        public bool IsDetached => queue.Closed;
        public bool HasSpace => queue.HasSpace;
        public PlayerStorage(string runId, int ownerId, int capacity)
        {
            if (ownerId <= 0) throw new ArgumentOutOfRangeException(nameof(ownerId));
            OwnerId = ownerId; queue = new StorageQueue<IngestionSnapshot>(runId, capacity);
        }
        internal bool Reserve(IngestionSnapshot snapshot) => snapshot != null && snapshot.Storage == this &&
            !snapshot.IsTruck && snapshot.PlayerId == OwnerId && queue.Reserve(new LootKey(snapshot.RunId, snapshot.InstanceId), snapshot);
        internal bool Commit(IngestionSnapshot snapshot)
        {
            var key = new LootKey(snapshot.RunId, snapshot.InstanceId);
            var item = snapshot.Item;
            if (IsDetached || !queue.OwnsReservation(key, snapshot) || item == null || item.Key.Equals(key) == false ||
                !item.TryStore(OwnerId, snapshot.TierId)) return false;
            // No callbacks occur between the preceding validation/state write and this exact reservation commit.
            if (!queue.Commit(key, snapshot)) throw new InvalidOperationException("Storage reservation changed inside an atomic commit.");
            return true;
        }
        internal void Cancel(IngestionSnapshot snapshot)
        { if (snapshot != null) queue.Cancel(new LootKey(snapshot.RunId, snapshot.InstanceId), snapshot); }
        public void Detach() => queue.Close();
        public bool TryPeek(out LootKey key, out SuckableObject item)
        {
            item = null;
            if (!queue.Peek(out key, out var snapshot)) return false;
            item = snapshot.Item;
            return item != null && item.Key.Equals(key) && item.State == SuckableState.Stored && item.StoredOwner == OwnerId;
        }
        // Caller validates collision-free launch geometry; this validates ownership, identity and finite pose.
        // Velocity/shot cooldown/input consumption are owned by the separate authoritative shot consumer.
        public bool TryReleaseFirst(LootKey expected, Vector3 position, Quaternion rotation, ulong shotId, out SuckableObject item)
        {
            item = null;
            if (IsDetached || shotId == 0 || !TryPeek(out var key, out var first) || !key.Equals(expected) || first.WorldFrozen) return false;
            if (!first.TryReleaseStored(OwnerId, position, rotation, shotId)) return false;
            queue.Peek(out _, out var token);
            if (!queue.RemoveFirst(expected, token)) throw new InvalidOperationException("FIFO changed during release.");
            item = first; return true;
        }
        // Also works for a detached owner's queue. Never removes an entry when no safe pose was supplied.
        public bool TryReturnFirst(LootKey expected, Vector3 position, Quaternion rotation, out SuckableObject item)
        {
            item = null;
            if (!TryPeek(out var key, out var first) || !key.Equals(expected) || !first.TryReturnStored(OwnerId, position, rotation)) return false;
            queue.Peek(out _, out var token);
            if (!queue.RemoveFirst(expected, token)) throw new InvalidOperationException("FIFO changed during return.");
            item = first; return true;
        }
        // Only called once the entire run is being destroyed; not an owner-disconnect operation.
        internal void ClearForWorldEnd() => queue.Clear();
    }
}
