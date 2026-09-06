using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace HowToSuck.Networking
{
    public enum PreparedSessionRole { Authority, Guest }
    public interface IPreparedSessionRoleSource { PreparedSessionRole ResolveRole(); }
    [DisallowMultipleComponent]
    public sealed class NgoGameSession : MonoBehaviour,ISessionDriverProvider
    {
        public PreparedSessionRole Role;
        public MonoBehaviour RoleSource;
        public NetworkManager Manager;
        public NetworkConnectionCoordinator Connection;
        public NetworkSessionAdapter SessionPrefab;
        public GameObject OfflineMenuBootstrapPrefab;
        public SessionRoot Session {get;private set;}
        public NgoSessionDriver Driver {get;private set;}
        public NetworkSessionAdapter Control {get;private set;}
        public bool HasAuthority {get;private set;}
        public IReadOnlyCollection<NetworkPlayerAdapter> Players=>players.Values;
        public IReadOnlyCollection<NetworkLootAdapter> Items=>items.Values;
        private readonly Dictionary<ulong,NetworkPlayerAdapter> players=new Dictionary<ulong,NetworkPlayerAdapter>();
        private readonly Dictionary<ulong,NetworkLootAdapter> items=new Dictionary<ulong,NetworkLootAdapter>();
        private readonly Dictionary<ulong,EnemyNetworkAdapter> enemies=new Dictionary<ulong,EnemyNetworkAdapter>();
        private static NgoGameSession current;
        private bool stopping;
        public bool IsStopping=>stopping;
        private LevelContext observedLevel;
        private NativeSceneLoadTracker nativeScenes;
        public static NgoGameSession RequireCurrent()=>current!=null&&current.Driver!=null?current:
            throw new InvalidOperationException("Choose a network session role before gameplay objects awaken.");
        public ISessionDriver CreateDriver(SessionRoot session)
        {
            if(current!=null||Driver!=null||Manager==null||Connection==null||SessionPrefab==null||OfflineMenuBootstrapPrefab==null||session==null||Manager.IsListening)
                throw new InvalidOperationException("Compose one fresh network game before starting NGO.");
            if(session.gameObject!=gameObject||Manager.gameObject!=gameObject||Connection.gameObject!=gameObject)
                throw new InvalidOperationException("The session/manager/connection composition must share one owned persistent root.");
            if(RoleSource!=null)
            {
                if(!(RoleSource is IPreparedSessionRoleSource source))throw new InvalidOperationException("Invalid role source.");
                Role=source.ResolveRole(); // Synchronous before SessionRoot.Initialize/World.Initialize, including in MPPM clients.
            }
            if(!Enum.IsDefined(typeof(PreparedSessionRole),Role))throw new InvalidOperationException("Undefined session role.");
            HasAuthority=Role==PreparedSessionRole.Authority;Session=session;current=this;
            nativeScenes=new NativeSceneLoadTracker(Manager);
            Driver=new NgoSessionDriver(this);
            Manager.OnClientDisconnectCallback+=Disconnected;Connection.SessionLost+=Lost;
            SceneManager.sceneLoaded+=SceneLoaded;
            return Driver;
        }
        // Connection owner calls this only after all initial OnClientConnected callbacks, not merely transport admission.
        public void AttachConnectedGame()
        {
            if(Driver==null||!Manager.IsListening||HasAuthority!=Manager.IsHost||Connection.Phase!=ConnectionPhase.Lobby)
                throw new InvalidOperationException("Connected role does not match pre-initialized world role.");
            nativeScenes.Bind();
            if(HasAuthority)
            {
                if(Control!=null)throw new InvalidOperationException("A session control already exists.");
                var value=Instantiate(SessionPrefab);DontDestroyOnLoad(value.gameObject);
                value.NetworkObject.Spawn(false);
                Session.OpenNetworkLobby();
            }
        }
        internal void BindControl(NetworkSessionAdapter control)
        {
            if(Control!=null&&Control!=control)throw new InvalidOperationException("Duplicate network session control.");
            Control=control;
        }
        internal void ReleaseControl(NetworkSessionAdapter control){if(Control==control)Control=null;}
        internal void Register(NetworkPlayerAdapter player)
        {if(players.ContainsKey(player.OwnerClientId))throw new InvalidOperationException("Duplicate player ownership.");players.Add(player.OwnerClientId,player);}
        internal void Unregister(NetworkPlayerAdapter player)
        {if(players.TryGetValue(player.OwnerClientId,out var old)&&old==player)players.Remove(player.OwnerClientId);}
        internal void Register(NetworkLootAdapter item)
        {if(items.ContainsKey(item.NetworkObjectId))throw new InvalidOperationException("Duplicate loot network ID.");items.Add(item.NetworkObjectId,item);}
        internal void Unregister(NetworkLootAdapter item)
        {if(items.TryGetValue(item.NetworkObjectId,out var old)&&old==item)items.Remove(item.NetworkObjectId);}
        internal void Register(EnemyNetworkAdapter enemy)
        {if(enemies.ContainsKey(enemy.NetworkObjectId))throw new InvalidOperationException("Duplicate enemy network ID.");enemies.Add(enemy.NetworkObjectId,enemy);}
        internal void Unregister(EnemyNetworkAdapter enemy)
        {if(enemies.TryGetValue(enemy.NetworkObjectId,out var old)&&old==enemy)enemies.Remove(enemy.NetworkObjectId);}
        public IntakeReceiver FindReceiver(int intake)
        {
            if(intake==TruckIntake.DefaultIntakeId)return observedLevel!=null&&observedLevel.Truck!=null?observedLevel.Truck.Receiver:null;
            foreach(var value in players.Values)if(value!=null&&value.Motor.PlayerId==intake)return value.GetComponent<IntakeReceiver>();
            return null;
        }
        public void ApplyTruckPresentation(bool active)
        {
            if(HasAuthority)throw new InvalidOperationException("Guest presentation called on host.");
            if(observedLevel!=null&&observedLevel.Truck!=null)observedLevel.Truck.Emitter.Active=active;
            // Only TruckView reads this guest flag; the guest AuthorityWorld never calls SuctionSystem/TruckIntake.Step.
        }
        private void SceneLoaded(Scene scene,LoadSceneMode mode)
        {observedLevel=FindFirstObjectByType<LevelContext>();}
        public bool TryVerifyPrepared(SessionWire state,out ulong ownedObject)
        {
            ownedObject=0;
            if(!HasAuthority&&(Control==null||!Control.HasAcceptedCurrentSnapshot||!state.Equals(Control.Snapshot.Value)))return false;
            if(state.Revision==0||state.ExpectedPlayers<1||state.ExpectedPlayers>4||players.Count!=state.ExpectedPlayers||items.Count!=state.ExpectedItems||
                !players.TryGetValue(Manager.LocalClientId,out var local)||local==null||!local.IsOwner||!local.Input.IsInitialized||local.Input.GameplayAvailable)return false;
            if(observedLevel==null)return false;
            var variant=observedLevel.FindContractVariant(state.Contract.ToString());
            var encounter=observedLevel.FindEnemyEncounter(state.Contract.ToString());
            if(variant==null||!observedLevel.TryValidateContract(variant,out _)||encounter==null||encounter.Spawns==null||enemies.Count!=encounter.Spawns.Length)return false;
            var enemyIds=new HashSet<ulong>();int bosses=0;
            foreach(var enemy in enemies.Values)
            {
                if(enemy==null||!enemy.IsSpawned||!HasAuthority&&!enemy.HasAcceptedCurrentSnapshot)return false;
                var e=enemy.Snapshot.Value;
                if(!enemy.IsSpawned||!e.Run.Equals(state.Run)||e.Revision!=state.Revision||!e.Frozen||e.Phase!=(byte)EnemyPhase.Idle||
                    !enemyIds.Add(e.InstanceId)||enemy.Actor.HasAuthority!=HasAuthority||enemy.Actor.Health!=enemy.Actor.MaximumHealth)return false;
                if(e.BossInstanceId!=0)
                {bosses++;if(e.BossInstanceId!=state.BossInstance||!e.BossRun.Equals(state.BossRun)||!e.BossType.Equals(state.BossId))return false;}
            }
            if(bosses!=1||state.BossStatus!=(byte)BossObjectiveStatus.Active)return false;
            var ids=new HashSet<int>();var lootIds=new HashSet<ulong>();
            foreach(var player in players.Values)
            {
                if(player==null||!player.IsSpawned||!HasAuthority&&!player.HasAcceptedCurrentSnapshot)return false;
                var s=player.Snapshot.Value;
                if(!player.IsSpawned||!s.Run.Equals(state.Run)||s.Revision!=state.Revision||!s.Frozen||!ids.Add(s.PlayerId)||
                    player.Motor.HasMovementAuthority!=HasAuthority||(!HasAuthority&&player.GetComponent<CharacterController>().enabled))return false;
            }
            foreach(var item in items.Values)
            {
                if(item==null||!item.IsSpawned||!HasAuthority&&!item.HasAcceptedCurrentSnapshot)return false;
                var s=item.Snapshot.Value;
                if(!item.IsSpawned||!s.Run.Equals(state.Run)||s.Revision!=state.Revision||!s.Frozen||s.State!=(byte)SuckableState.Available||
                    !lootIds.Add(s.InstanceId)||item.Item.HasPhysicsAuthority!=HasAuthority||!item.Item.Body.isKinematic)return false;
            }
            ownedObject=local.NetworkObjectId;return true;
        }
        public void SetLocalReady(bool ready)
        {if(Control!=null&&Control.IsSpawned)Control.SetLocalReady(ready);}
        private void Disconnected(ulong id)
        {
            if(!HasAuthority||stopping)return;
            Driver.RemoveDisconnectedPlayer(id); // Accepted ingestion already has a last endpoint and completes by existing rules.
        }
        private void Lost()
        {
            if(stopping)return;stopping=true;
            foreach(var player in players.Values)if(player!=null&&player.IsOwner)player.Input.SetGameplayAvailable(false);
            Session.MarkNetworkLost(RoleSource is SoloSessionStartup ? (Connection.LastError ?? "Одиночная сессия завершена. Возврат в главное меню.") : "Хозяин покинул сессию. Возврат в меню.");
            StartCoroutine(ReturnAfterDisconnect());
        }
        public void LeaveGuest()
        {
            if(HasAuthority||stopping)return;stopping=true;
            foreach(var player in players.Values)if(player!=null&&player.IsOwner)player.Input.SetGameplayAvailable(false);
            Session.MarkNetworkLost("Вы покинули сессию.");Connection.StopSession();StartCoroutine(ReturnAfterDisconnect());
        }
        public bool ReturnSoloToEntry()
            => Connection.Mode==ConnectionMode.SoloLoopback&&ReturnProductLobbyToEntry();
        public bool ReturnProductLobbyToEntry()
        {
            if(stopping||Session==null||Session.Phase!=SessionPhase.Lobby||Session.HasPendingSave||
                !(RoleSource is SoloSessionStartup)||!SoloSessionStartup.IsDeferredEntry(OfflineMenuBootstrapPrefab)||
                (Connection.Mode!=ConnectionMode.SoloLoopback&&Connection.Mode!=ConnectionMode.SteamHost&&Connection.Mode!=ConnectionMode.SteamClient))return false;
            return BeginIntentionalProductReturn();
        }
        public bool CancelProductEntryStart()
        {
            if(stopping||Session==null||Driver==null||!(RoleSource is SoloSessionStartup entry)||
                (entry.Phase!=SoloEntryPhase.Starting&&entry.Phase!=SoloEntryPhase.Failed)||
                Session.Phase==SessionPhase.Playing||Session.Phase==SessionPhase.Results||Session.HasPendingSave||
                !SoloSessionStartup.IsDeferredEntry(OfflineMenuBootstrapPrefab))return false;
            return BeginIntentionalProductReturn();
        }
        private bool BeginIntentionalProductReturn()
        {
            stopping=true;
            foreach(var player in players.Values)if(player!=null&&player.IsOwner)player.Input.SetGameplayAvailable(false);
            // Existing state helper freezes/clears the world; an intentional exit emits no SessionLost event.
            Session.MarkNetworkLost("");Connection.StopSession();StartCoroutine(ReturnAfterDisconnect());return true;
        }
        private IEnumerator ReturnAfterDisconnect()
        {
            double deadline=Time.realtimeSinceStartupAsDouble+7;
            while((Manager.IsListening||Manager.ShutdownInProgress)&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            if(Manager.IsListening||Manager.ShutdownInProgress)yield break; // Retain explicit shutdown error; never create a second manager.
            double nativeDeadline=Time.realtimeSinceStartupAsDouble+30;
            while(nativeScenes.HasPendingLocalOperation&&Time.realtimeSinceStartupAsDouble<nativeDeadline)yield return null;
            if(nativeScenes.HasPendingLocalOperation)
            {Debug.LogError("Native scene operation did not drain; offline bootstrap remains blocked.");yield break;}
            yield return null; // Native scene completion callbacks must fully unwind before a new scene owner.
            if(OfflineMenuBootstrapPrefab==null)throw new InvalidOperationException("Bind the existing offline menu bootstrap prefab for disconnect return.");
            var bootstrap=OfflineMenuBootstrapPrefab.GetComponent<GameBootstrap>();
            bool deferred = SoloSessionStartup.IsDeferredEntry(OfflineMenuBootstrapPrefab);
            if(bootstrap==null||(!deferred&&(bootstrap.DeferInitialization||bootstrap.SessionDriverProvider!=null)))
                throw new InvalidOperationException("Disconnect return needs a verified deferred entry or the existing diagnostic offline composition.");
            var relay=new GameObject("Network session return");DontDestroyOnLoad(relay);
            relay.AddComponent<OfflineMenuReturn>().Begin(gameObject,OfflineMenuBootstrapPrefab,Session.LastError);
        }
        private void OnDestroy()
        {
            if(Manager!=null)Manager.OnClientDisconnectCallback-=Disconnected;
            if(Connection!=null)Connection.SessionLost-=Lost;
            SceneManager.sceneLoaded-=SceneLoaded;
            nativeScenes?.Dispose();
            if(current==this)current=null;
        }
    }
}
