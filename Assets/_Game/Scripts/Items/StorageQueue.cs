using System;
using System.Collections.Generic;
namespace HowToSuck
{
    // Pure bookkeeping. Tokens are private identities, never caller-chosen sequence numbers.
    internal sealed class StorageQueue<T> where T : class
    {
        private readonly Dictionary<LootKey, T> reserved = new Dictionary<LootKey, T>();
        private readonly List<KeyValuePair<LootKey, T>> stored = new List<KeyValuePair<LootKey, T>>();
        internal readonly string RunId;
        internal readonly int Capacity;
        internal bool Closed { get; private set; }
        internal int Count => stored.Count;
        internal int Reserved => reserved.Count;
        internal T At(int index) => index >= 0 && index < stored.Count ? stored[index].Value : null;
        internal bool HasSpace => !Closed && Count + Reserved < Capacity;
        internal StorageQueue(string run, int capacity)
        {
            if (!new LootKey(run, 1).IsValid || capacity < 1) throw new ArgumentException("Invalid storage run/capacity.");
            RunId = run; Capacity = capacity;
        }
        internal bool Reserve(LootKey key, T token)
        {
            if (!HasSpace || !key.IsValid || key.RunId != RunId || token == null || reserved.ContainsKey(key)) return false;
            foreach (var entry in stored) if (entry.Key.Equals(key)) return false;
            reserved.Add(key, token); return true;
        }
        internal bool OwnsReservation(LootKey key, T token) => token != null && reserved.TryGetValue(key, out var actual) && ReferenceEquals(actual, token);
        internal bool Commit(LootKey key, T token)
        {
            if (Closed || !OwnsReservation(key, token)) return false;
            reserved.Remove(key); stored.Add(new KeyValuePair<LootKey, T>(key, token)); return true;
        }
        internal bool Cancel(LootKey key, T token)
        { if (!OwnsReservation(key, token)) return false; reserved.Remove(key); return true; }
        internal bool Peek(out LootKey key, out T token)
        {
            if (Count == 0) { key = default; token = null; return false; }
            key = stored[0].Key; token = stored[0].Value; return true;
        }
        internal bool RemoveFirst(LootKey key, T token)
        {
            if (!Peek(out var actual, out var value) || !actual.Equals(key) || !ReferenceEquals(value, token)) return false;
            stored.RemoveAt(0); return true;
        }
        internal void Close() { Closed = true; }
        internal void Clear() { Closed = true; reserved.Clear(); stored.Clear(); }
    }
}
