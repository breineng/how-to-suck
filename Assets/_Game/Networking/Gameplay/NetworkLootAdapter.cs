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
        private string run;
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
            if(!Guid.TryParseExact(run,"N",out _)||revision==0||state.InstanceId==0||state.Type.ToString()!=Item.Definition.TypeId)
                throw new InvalidOperationException("Invalid initial loot snapshot.");
            if(!IsServer)Apply(state);
            Snapshot.OnValueChanged+=OnSnapshot;game.Register(this);
            if(IsServer)game.Session.World.SnapshotChanged+=Publish;
        }
        private LootWire Capture()
        {
            var s=new LootWire{Run=new FixedString64Bytes(Item.RunId),Type=new FixedString64Bytes(Item.Definition.TypeId),
                Revision=revision,InstanceId=Item.InstanceId,State=(byte)Item.State,Frozen=Item.WorldFrozen};
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
            if(s.Run.ToString()!=run||s.Revision!=revision||s.Type.ToString()!=Item.Definition.TypeId)throw new InvalidOperationException("Loot identity changed.");
            Item.ApplyReplicaState(run,s.InstanceId,(SuckableState)s.State,s.Frozen);
            if(!s.Ingesting){ingestion.Dispose();return;}
            ApplyIngestion(s);
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
            if(IsSpawned&&!IsServer&&Snapshot.Value.Ingesting)ApplyIngestion(Snapshot.Value);
        }
        public override void OnNetworkDespawn()
        {
            Snapshot.OnValueChanged-=OnSnapshot;ingestion?.Dispose();
            if(game!=null){game.Session.World.SnapshotChanged-=Publish;game.Unregister(this);}
        }
    }
}
