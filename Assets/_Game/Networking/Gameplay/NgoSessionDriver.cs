using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
namespace HowToSuck.Networking
{
    public sealed class NgoSessionDriver : INetworkSessionDriver,IWorldSpawner,IWorldSpawnCommitter
    {
        private readonly NgoGameSession game;
        private readonly NetworkWorldSpawner spawner;
        private readonly NgoSceneBarrier scenes;
        private readonly Dictionary<ulong,GameObject> playerObjects=new Dictionary<ulong,GameObject>();
        private readonly Dictionary<ulong,int> playerIds=new Dictionary<ulong,int>();
        private SessionRoot session;
        private RunPreparationBarrier barrier;
        private List<ulong> loadingRoster;
        public uint Revision {get;private set;}
        public int ExpectedItems {get;private set;}
        public int ExpectedPlayers {get;private set;}
        public bool HasAuthority=>game.HasAuthority;
        public bool CanBeginContract=>HasAuthority&&game.Control!=null&&game.Control.IsSpawned&&game.Connection.CanBeginContract;
        private double lastAuthorityTime;
        public double Now
        {
            get
            {
                if(game.Manager.IsListening)
                {
                    double now=game.Manager.ServerTime.Time;
                    if(!double.IsNaN(now)&&!double.IsInfinity(now))lastAuthorityTime=Math.Max(lastAuthorityTime,now);
                }
                // Never switch a live contract to a different clock epoch when its network stops.
                return lastAuthorityTime;
            }
        }
        public NgoSessionDriver(NgoGameSession owner)
        {game=owner;spawner=new NetworkWorldSpawner(game.Manager);scenes=new NgoSceneBarrier(game.Manager);}
        public void Bind(SessionRoot value){if(session!=null)throw new InvalidOperationException("Driver already bound.");session=value;}
        public bool EnterPreparing(string contractId)=>CanBeginContract&&game.Connection.EnterPreparing(contractId);
        public IEnumerator Load(string sceneName)
        {
            if(!HasAuthority)throw new InvalidOperationException("Guests follow NGO scene events; they cannot load a shared scene.");
            loadingRoster=new List<ulong>(game.Manager.ConnectedClientsIds);loadingRoster.Sort();
            var operation=scenes.LoadOnHost(sceneName);
            Exception failure=null;
            try
            {
                while(true)
                {
                    bool next=false;object yielded=null;
                    try{next=operation.MoveNext();if(next)yielded=operation.Current;}
                    catch(Exception error){failure=error;}
                    if(!next||failure!=null)break;
                    yield return yielded;
                }
            }
            finally{(operation as IDisposable)?.Dispose();}
            if(failure!=null)
            {
                game.Connection.StopUnexpected("Network scene load failed: "+failure.Message);
                throw failure; // SessionRoot's ShuttingDown guard prevents any stale NGO/MainMenu continuation.
            }
        }
        public IEnumerator PrepareGameplay(LevelContext level,VacuumDefinition vacuum)
        {
            if(!HasAuthority||game.Control==null||!game.Control.IsSpawned||game.Connection.Phase!=ConnectionPhase.Preparing||loadingRoster==null)
                throw new InvalidOperationException("Complete network scene synchronization before preparing the world.");
            if(loadingRoster.Count<1||loadingRoster.Count>4||level.PlayerSpawns.Length<loadingRoster.Count)throw new InvalidOperationException("Insufficient authored player spawns.");
            Revision=checked(Revision+1);ExpectedItems=level.LootSpawns.Length;ExpectedPlayers=loadingRoster.Count;
            barrier=new RunPreparationBarrier(session.RunId,loadingRoster,Revision,ExpectedItems,Now);
            foreach(var id in loadingRoster)barrier.RecordSceneLoaded(id); // NgoSceneBarrier already verified these exact scene acknowledgments.
            session.World.PrepareWorld(level,this,vacuum,session.Controller,()=>Now);
            for(int index=0;index<loadingRoster.Count;index++)
            {
                var id=loadingRoster[index];var point=level.PlayerSpawns[index];
                var instance=spawner.Spawn(level.PlayerPrefab,point.position,point.rotation);playerObjects.Add(id,instance);
                var player=instance.GetComponent<NetworkPlayerAdapter>();
                if(player==null)throw new InvalidOperationException("Player prefab is missing its network adapter.");
                player.ConfigurePrepared(session.RunId,Revision,index+1,id,vacuum.TierId);
                session.World.RegisterPlayer(player.Motor);playerIds.Add(id,index+1);
                spawner.CommitSpawn(instance);
                if(!barrier.AssignPlayer(id,player.NetworkObjectId))throw new InvalidOperationException("Could not bind the prepared player to its connection.");
            }
            game.Control.Publish(); // Full initial snapshot, never a timing-dependent start RPC.
            while(true)
            {
                if(!game.Manager.IsListening||game.Manager.ShutdownInProgress)throw new InvalidOperationException("Connection ended during preparation.");
                var status=barrier.Poll(Now,game.Manager.ConnectedClientsIds);
                if(status==PreparationStatus.Ready)
                {if(!barrier.TryCommit(Now,game.Manager.ConnectedClientsIds))throw new InvalidOperationException("Preparation commit expired.");break;}
                if(status==PreparationStatus.Cancelled||status==PreparationStatus.TimedOut)throw new InvalidOperationException(barrier.Failure);
                yield return null;
            }
            // SessionRoot now starts its same ContractController and unfreezes the same AuthorityWorld exactly once.
        }
        public bool Acknowledge(ulong sender,string run,uint revision,ulong ownedObject,int items,int players)
        {
            if(!HasAuthority||barrier==null||!game.Manager.ConnectedClients.TryGetValue(sender,out var client)||
                client.PlayerObject==null||client.PlayerObject.NetworkObjectId!=ownedObject)return false;
            return barrier.Acknowledge(sender,run,revision,ownedObject,items,players);
        }
        public GameObject Spawn(GameObject prefab,Vector3 position,Quaternion rotation)=>spawner.Spawn(prefab,position,rotation);
        public void CommitSpawn(GameObject instance)=>spawner.CommitSpawn(instance);
        public void Despawn(GameObject instance)=>spawner.Despawn(instance);
        public void ClearPlayers()
        {
            barrier?.Cancel("World cleared.");barrier=null;ExpectedItems=ExpectedPlayers=0;
            foreach(var pair in playerObjects)if(pair.Value!=null)spawner.Despawn(pair.Value);
            playerObjects.Clear();playerIds.Clear();
        }
        public void RemoveDisconnectedPlayer(ulong id)
        {
            if(!HasAuthority||!playerObjects.TryGetValue(id,out var instance))return;
            // NGO may already have destroyed the owned PlayerObject before invoking disconnect callbacks.
            if(playerIds.TryGetValue(id,out var playerId))session.World.RemovePlayer(playerId);
            if(instance!=null)spawner.Despawn(instance);
            playerObjects.Remove(id);playerIds.Remove(id);
        }
        public void PhaseChanged(SessionPhase phase)
        {
            if(!HasAuthority)return;
            var connection=game.Connection;
            if(phase==SessionPhase.Playing&&connection.Phase==ConnectionPhase.Preparing)
                connection.SetAuthoritativePhase(ConnectionPhase.Running,session.CurrentContract.ContractId);
            else if(phase==SessionPhase.Results&&connection.Phase==ConnectionPhase.Running)
                connection.SetAuthoritativePhase(ConnectionPhase.Results,session.CurrentContract.ContractId);
            else if(phase==SessionPhase.Lobby&&(connection.Phase==ConnectionPhase.Preparing||connection.Phase==ConnectionPhase.Results))
                connection.SetAuthoritativePhase(ConnectionPhase.Lobby,null);
            game.Control?.Publish();
        }
        public void LeaveGuest()=>game.LeaveGuest();
        public void Stop()
        {
            if(HasAuthority){session.World.Clear();ClearPlayers();}
            game.Connection.StopSession(); // Despawn owned world while the transport is still live, then shut down.
        }
    }
}
