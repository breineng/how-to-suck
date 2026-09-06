using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
namespace HowToSuck.Networking
{
    // Implemented later by item/player snapshot adapters. No game rules or spawn calls belong in this callback.
    public interface IPreparedNetworkSpawn
    {
        bool IsPlayerObject { get; }
        ulong OwnerConnectionId { get; }
        void PrepareNetworkState();
    }
    public sealed class NetworkWorldSpawner : IWorldSpawner, IWorldSpawnCommitter
    {
        private readonly NetworkManager manager;
        private readonly HashSet<GameObject> owned = new HashSet<GameObject>(), pending = new HashSet<GameObject>();
        public NetworkWorldSpawner(NetworkManager value) { manager = value != null ? value : throw new ArgumentNullException(nameof(value)); }
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            RequireServer();
            owned.RemoveWhere(value => value == null); pending.RemoveWhere(value => value == null);
            if (prefab == null || prefab.GetComponent<NetworkObject>() == null) throw new InvalidOperationException("Use a registered network prefab.");
            var instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
            owned.Add(instance); pending.Add(instance); return instance; // No network publication before CommitSpawn.
        }
        public void CommitSpawn(GameObject instance)
        {
            RequireServer();
            if (instance == null || !pending.Contains(instance)) throw new InvalidOperationException("Only an owned pending spawn can be committed once.");
            var network = instance.GetComponent<NetworkObject>();
            if (network == null || network.IsSpawned) throw new InvalidOperationException("Network spawn is missing or was already published.");
            var item = instance.GetComponent<SuckableObject>();
            if (item != null && (string.IsNullOrWhiteSpace(item.RunId) || item.InstanceId == 0 || !item.WorldFrozen))
                throw new InvalidOperationException("Register and freeze the domain item before network publication.");
            IPreparedNetworkSpawn adapter = null;
            foreach (var behaviour in instance.GetComponents<MonoBehaviour>())
                if (behaviour is IPreparedNetworkSpawn candidate)
                { if (adapter != null) throw new InvalidOperationException("Only one snapshot/spawn adapter may own this root."); adapter = candidate; }
            if (adapter == null) throw new InvalidOperationException("An initialized network snapshot adapter is required.");
            adapter.PrepareNetworkState();
            if (adapter.IsPlayerObject)
            {
                if (!manager.ConnectedClients.TryGetValue(adapter.OwnerConnectionId, out var owner) || owner.PlayerObject != null)
                    throw new InvalidOperationException("Assign one player to one admitted connection.");
                network.SpawnAsPlayerObject(adapter.OwnerConnectionId, true);
            }
            else network.Spawn(true);
            pending.Remove(instance);
        }
        public void Despawn(GameObject instance)
        {
            if (instance == null) return;
            if (!owned.Contains(instance)) throw new InvalidOperationException("This spawner does not own the instance.");
            var network = instance.GetComponent<NetworkObject>();
            if (network != null && network.IsSpawned)
            {
                // SDK owns its spawned-object destruction once shutdown begins.
                if (manager.ShutdownInProgress) { pending.Remove(instance); owned.Remove(instance); return; }
                if (manager.IsServer && manager.IsListening) network.Despawn(true);
                else UnityEngine.Object.Destroy(instance); // Completed shutdown: dispose a surviving local object.
            }
            else UnityEngine.Object.Destroy(instance);
            pending.Remove(instance); owned.Remove(instance);
        }
        private void RequireServer()
        { if (!manager.IsServer || !manager.IsListening || manager.ShutdownInProgress) throw new InvalidOperationException("Only the live authority can mutate network spawns."); }
    }
}
