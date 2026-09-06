using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace HowToSuck.Networking
{
    // Retains Unity native operations independently of the NGO manager's shutdown/lifetime.
    public sealed class NativeSceneLoadTracker : IDisposable
    {
        private readonly NetworkManager manager;
        private NetworkSceneManager bound;
        private readonly HashSet<AsyncOperation> pending=new HashSet<AsyncOperation>();
        public NativeSceneLoadTracker(NetworkManager value)
        {manager=value??throw new ArgumentNullException(nameof(value));manager.OnClientStarted+=Bind;manager.OnServerStarted+=Bind;Bind();}
        public void Bind()
        {
            if(manager.SceneManager==null||ReferenceEquals(bound,manager.SceneManager))return;
            Unbind();bound=manager.SceneManager;bound.OnLoad+=OnLoad;bound.OnUnload+=OnUnload;
        }
        private void OnLoad(ulong client,string scene,LoadSceneMode mode,AsyncOperation operation){if(operation!=null)pending.Add(operation);}
        private void OnUnload(ulong client,string scene,AsyncOperation operation){if(operation!=null)pending.Add(operation);}
        public bool HasPendingLocalOperation
        {get{pending.RemoveWhere(operation=>operation==null||operation.isDone);return pending.Count!=0;}}
        private void Unbind(){if(bound!=null){bound.OnLoad-=OnLoad;bound.OnUnload-=OnUnload;bound=null;}}
        public void Dispose(){manager.OnClientStarted-=Bind;manager.OnServerStarted-=Bind;Unbind();}
    }
}
