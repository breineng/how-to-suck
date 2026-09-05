using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HowToSuck
{
    public sealed class LocalSessionDriver : ISessionDriver
    {
        public double Now => Time.realtimeSinceStartupAsDouble;
        public IEnumerator Load(string sceneName)
        {
            var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null) throw new System.InvalidOperationException("Unity could not load " + sceneName);
            while (!operation.isDone) yield return null;
        }
        public void Stop() { }
    }
}
