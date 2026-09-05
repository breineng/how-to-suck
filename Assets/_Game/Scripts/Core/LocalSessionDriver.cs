using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HowToSuck
{
    public sealed class LocalSessionDriver : ISessionDriver, IPlayerIntentSink, IWorldSpawner
    {
        private readonly AuthorityWorld world;
        public LocalSessionDriver(AuthorityWorld authorityWorld) { world = authorityWorld; }
        public double Now => Time.realtimeSinceStartupAsDouble;
        public IEnumerator Load(string sceneName)
        {
            var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null) throw new System.InvalidOperationException("Unity could not load " + sceneName);
            while (!operation.isDone) yield return null;
        }
        public void SubmitIntent(int playerId, PlayerIntent intent) => world.SubmitIntent(playerId, intent);
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation) =>
            Object.Instantiate(prefab, position, rotation);
        public void Despawn(GameObject instance) { if (instance != null) Object.Destroy(instance); }
        public void Stop() => world.Clear();
    }
}
