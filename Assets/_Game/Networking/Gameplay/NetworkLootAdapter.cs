using System;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
namespace HowToSuck.Networking
{
    [DisallowMultipleComponent,RequireComponent(typeof(NetworkObject),typeof(SuckableObject))]
    public sealed class NetworkLootAdapter : NetworkBehaviour,IPreparedNetworkSpawn
    {
        public readonly NetworkVariable<LootWire> Snapshot=new NetworkVariable<LootWire>(default,
            NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        public bool IsPlayerObject=>false;
        public ulong OwnerConnectionId=>NetworkManager.ServerClientId;
        public SuckableObject Item {get;private set;}
        private NgoGameSession game;
        private ReplicaIngestionBinding ingestion;
        private uint revision;
        private string run,type;
        private ulong instanceId;private CargoRole cargoRole;private BossKey bossKey;
        private LootWire acceptedSnapshot;private bool hasAcceptedSnapshot;
        public bool HasAcceptedCurrentSnapshot=>IsSpawned&&(IsServer||hasAcceptedSnapshot&&acceptedSnapshot.Equals(Snapshot.Value));
        private Func<double> presentationClock;
        private void Awake()
        {
            game=NgoGameSession.RequireCurrent();presentationClock=()=>game.Driver.Now;Item=GetComponent<SuckableObject>();
            var pose=GetComponent<NetworkTransform>();
            if(pose==null||GetComponents<NetworkTransform>().Length!=1||pose.AuthorityMode!=NetworkTransform.AuthorityModes.Server||
                GetComponent<NetworkRigidbody>()!=null)
                throw new InvalidOperationException("Use one server NetworkTransform and the domain physics-mode owner.");
            Item.SetWorldFrozen(true);Item.BindPhysicsAuthority(game.HasAuthority);
            if(!game.HasAuthority)ingestion=new ReplicaIngestionBinding(Item);
        }
        public void PrepareNetworkState()
        {
            if(!game.HasAuthority||Item.InstanceId==0||!Item.WorldFrozen)throw new InvalidOperationException("Loot must be registered and frozen.");
            revision=game.Driver.Revision;run=Item.RunId;preparedSnapshot=Capture();preparedForSpawn=true;
        }
        private LootWire preparedSnapshot;
        private bool preparedForSpawn;
        protected override void OnNetworkPreSpawn(ref NetworkManager networkManager)
        {
            base.OnNetworkPreSpawn(ref networkManager);
            if(!networkManager.IsServer)return;
            if(!preparedForSpawn)throw new InvalidOperationException("Prepare the domain snapshot before spawning.");
            // NGO2.13 assigns NetworkManagerOwner before this supported callback. Bind the
            // variable before setting Value; the initial spawn message includes the complete state.
            Snapshot.Initialize(this);
            Snapshot.Value=preparedSnapshot;
        }
        public override void OnNetworkSpawn()
        {
            var state=Snapshot.Value;run=state.Run.ToString();revision=state.Revision;
            if(Item.Definition==null)throw new InvalidOperationException("Loot prefab definition is missing.");
            type=IsServer?Item.TypeId:Item.Definition.TypeId;cargoRole=IsServer?Item.CargoRole:Item.Definition.CargoRole;instanceId=state.InstanceId;
            GameplayStateValidation.RequireLoot(state,run,revision,instanceId,type,cargoRole);
            bossKey=GameplayStateValidation.Provenance(state).BossKey;
            if(!IsServer)Apply(state);
            Snapshot.OnValueChanged+=OnSnapshot;game.Register(this);
            if(IsServer)game.Session.World.SnapshotChanged+=Publish;
        }
        private LootWire Capture()
        {
            var s=new LootWire{Run=new FixedString64Bytes(Item.RunId),Type=new FixedString64Bytes(Item.TypeId),
                Revision=revision,InstanceId=Item.InstanceId,State=(byte)Item.State,Frozen=Item.WorldFrozen,
                CargoRole=(byte)Item.CargoRole,BossRun=new FixedString64Bytes(Item.BossKey.RunId??""),BossId=new FixedString64Bytes(Item.BossKey.ContractBossId??""),BossInstance=Item.BossKey.InstanceId,
                StoredOwner=Item.StoredOwner,LastStorageOwner=Item.LastStorageOwner,LastStorageTier=new FixedString64Bytes(Item.LastStorageTierId??""),ActiveShotId=Item.ActiveShotId};
            var x=Item.Ingestion;
            if(x!=null)
            {
                x.PresentationClock=presentationClock;
                s.Ingesting=true;s.IntakeId=x.IntakeId;s.PlayerId=x.PlayerId;s.Truck=x.IsTruck;s.Started=x.StartedAt;s.Duration=x.Duration;
                s.Size=x.RequiredSize;s.Start=x.StartPosition;s.StartRotation=x.StartRotation;s.Scale=x.StartScale;
                s.Target=x.TargetPosition;s.TargetRotation=x.TargetRotation;s.End=x.EndPosition;
            }
            return s;
        }
        private void Publish(){if(IsServer&&IsSpawned)Snapshot.Value=Capture();}
        private void OnSnapshot(LootWire old,LootWire value){if(!IsServer)Apply(value);}
        private void Apply(LootWire s)
        {
            GameplayStateValidation.RequireLoot(s,run,revision,instanceId,type,cargoRole);
            var provenance=GameplayStateValidation.Provenance(s);
            if(!bossKey.Equals(provenance.BossKey))throw new InvalidOperationException("Loot boss identity changed in place.");
            // SuckableObject validates provenance, binds a replica boss key, then initializes exactly once.
            Item.ApplyReplicaState(run,s.InstanceId,(SuckableState)s.State,s.Frozen,provenance);
            if(!s.Ingesting)ingestion.Dispose();else ApplyIngestion(s);
            if(!IsSpawned||game.IsStopping)return;
            acceptedSnapshot=s;hasAcceptedSnapshot=true;
        }
        private void ApplyIngestion(LootWire s)
        {
            // Receiver may spawn in a different packet; the known endpoint still renders until it resolves.
            var receiver=game.FindReceiver(s.IntakeId);
            ingestion.Apply(receiver,s.IntakeId,s.PlayerId,s.Truck,s.Started,s.Duration,s.Size,s.Start,s.StartRotation,s.Scale,
                s.Target,s.TargetRotation,s.End,presentationClock);
        }
        private void Update()
        {
            if(IsSpawned&&!IsServer&&hasAcceptedSnapshot&&acceptedSnapshot.Ingesting)ApplyIngestion(acceptedSnapshot);
        }
        public override void OnNetworkDespawn()
        {
            Snapshot.OnValueChanged-=OnSnapshot;ingestion?.Dispose();hasAcceptedSnapshot=false;acceptedSnapshot=default;
            if(game!=null){game.Session.World.SnapshotChanged-=Publish;game.Unregister(this);}
        }
    }
}
