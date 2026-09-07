#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace HowToSuck.Diagnostics
{
    // Owned diagnostic object survives destruction of a network root, so an enum cannot stand in for a loaded offline scene.
    public sealed class GameplayProcessLifecycleObserver:MonoBehaviour
    {
        private string directory;private double next;
        public void Initialize(string reportDirectory){directory=reportDirectory;}
        [Serializable] private sealed class State
        {public string utc,activeScene;public string[] loadedScenes,sessionPhases;public int pid,frame,networkManagers,listeningManagers;public double realtime;}
        private void Update()
        {
            if(directory==null||Time.realtimeSinceStartupAsDouble<next)return;next=Time.realtimeSinceStartupAsDouble+.25;
            var managers=FindObjectsByType<NetworkManager>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            var sessions=FindObjectsByType<SessionRoot>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            var value=new State{utc=DateTime.UtcNow.ToString("O"),pid=System.Diagnostics.Process.GetCurrentProcess().Id,frame=Time.frameCount,
                realtime=Time.realtimeSinceStartupAsDouble,activeScene=SceneManager.GetActiveScene().name,networkManagers=managers.Length,
                listeningManagers=managers.Count(m=>m.IsListening||m.ShutdownInProgress),sessionPhases=sessions.Select(s=>s.Phase.ToString()).ToArray(),
                loadedScenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i).name).ToArray()};
            // Immutable append-only evidence avoids a concurrent reader replacing a lifecycle record.
            File.AppendAllText(Path.Combine(directory,"lifecycle.jsonl"),JsonUtility.ToJson(value)+Environment.NewLine);
        }
    }
}
#endif
