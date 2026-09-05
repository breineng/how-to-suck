using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HowToSuck
{
    public sealed class LootRegistry
    {
        public string RunId { get; private set; }
        public IReadOnlyDictionary<ulong, SuckableObject> Items => view;

        private readonly Dictionary<ulong, SuckableObject> items = new Dictionary<ulong, SuckableObject>();
        private readonly ReadOnlyDictionary<ulong, SuckableObject> view;
        private ulong lastId;

        public LootRegistry() => view = new ReadOnlyDictionary<ulong, SuckableObject>(items);

        public void Begin(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("Loot registry needs a non-empty run ID.", nameof(runId));
            Clear();
            RunId = runId;
        }

        public ulong Register(SuckableObject item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrEmpty(RunId)) throw new InvalidOperationException("Call LootRegistry.Begin before registering loot.");
            if (item.RunId == RunId && item.InstanceId != 0)
            {
                if (items.TryGetValue(item.InstanceId, out SuckableObject existing) && existing == item)
                    return item.InstanceId;
                throw new InvalidOperationException("This object already received an instance ID in the current run; it cannot be registered a second time.");
            }
            if (!item.TryValidate(out string error)) throw new InvalidOperationException(error);
            if (lastId == ulong.MaxValue) throw new InvalidOperationException("Loot instance ID range is exhausted.");
            ulong id = lastId + 1;
            item.Initialize(RunId, id);
            items.Add(id, item);
            lastId = id;
            return id;
        }

        public bool Unregister(SuckableObject item)
        {
            if (item == null || item.RunId != RunId) return false;
            return items.TryGetValue(item.InstanceId, out SuckableObject existing) && existing == item && items.Remove(item.InstanceId);
        }

        public bool Unregister(ulong instanceId) => items.Remove(instanceId);

        // The world/spawner owns object destruction; clearing this registry only releases references.
        public void Clear()
        {
            items.Clear();
            lastId = 0;
            RunId = null;
        }
    }
}
