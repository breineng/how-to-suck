using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HowToSuck.Networking
{
    // NGO scene synchronization only; run-world/player-ready acknowledgments are a separate required barrier.
    public sealed class NgoSceneBarrier
    {
        private readonly NetworkManager manager;
        private bool loading;
        public NgoSceneBarrier(NetworkManager networkManager) { manager = networkManager ?? throw new ArgumentNullException(nameof(networkManager)); }
        public double AuthorityNow => manager.IsListening ? manager.ServerTime.Time : Time.realtimeSinceStartupAsDouble;
        public IEnumerator LoadOnHost(string sceneName)
        {
            if (!manager.IsHost || !manager.IsListening || manager.SceneManager == null)
                throw new InvalidOperationException("Only the active host may request a network scene load.");
            if (loading) throw new InvalidOperationException("A network scene barrier is already pending.");
            loading = true;
            var sceneManager = manager.SceneManager;
            var expected = new HashSet<ulong>(manager.ConnectedClientsIds);
            bool completed = false; string failure = null;
            string expectedName = System.IO.Path.GetFileNameWithoutExtension(sceneName);
            void OnLoaded(string loaded, LoadSceneMode mode, List<ulong> done, List<ulong> timedOut)
            {
                if (System.IO.Path.GetFileNameWithoutExtension(loaded) != expectedName || mode != LoadSceneMode.Single) return;
                if (timedOut.Count != 0) failure = "A player timed out while loading the scene.";
                else
                    foreach (ulong client in expected) if (!done.Contains(client)) { failure = "An admitted player did not finish scene loading."; break; }
                completed = true;
            }
            sceneManager.OnLoadEventCompleted += OnLoaded;
            try
            {
                var status = sceneManager.LoadScene(sceneName, LoadSceneMode.Single);
                if (status != SceneEventProgressStatus.Started) throw new InvalidOperationException("NGO rejected scene load: " + status);
                double until = Time.realtimeSinceStartupAsDouble + 30;
                while (!completed)
                {
                    if (!manager.IsListening || manager.ShutdownInProgress) throw new InvalidOperationException("The network session ended while loading.");
                    if (Time.realtimeSinceStartupAsDouble >= until) throw new TimeoutException("Network scene loading exceeded thirty seconds.");
                    foreach (ulong client in expected)
                        if (!manager.ConnectedClients.ContainsKey(client)) throw new InvalidOperationException("The loading roster changed.");
                    yield return null;
                }
                if (failure != null) throw new InvalidOperationException(failure);
                if (!manager.IsListening || manager.ShutdownInProgress) throw new InvalidOperationException("The network session ended during scene completion.");
                foreach (ulong client in expected)
                    if (!manager.ConnectedClients.ContainsKey(client)) throw new InvalidOperationException("The completed loading roster changed.");
            }
            finally { sceneManager.OnLoadEventCompleted -= OnLoaded; loading = false; }
        }
    }
}