using System;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
namespace HowToSuck.Networking
{
    public struct EnemyWire : INetworkSerializable,IEquatable<EnemyWire>
    {
        public FixedString64Bytes Run,Type,BossRun,BossType;
        public ulong InstanceId,BossInstanceId;
        public uint Revision,PhaseRevision,AttackRevision;
        public int Health,MaximumHealth;
        public byte Phase;
        public double PhaseStarted,Observed;
        public bool Frozen,Sweep,Projectile;
        public Vector3 ProjectilePosition;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T:IReaderWriter
        {
            s.SerializeValue(ref Run);s.SerializeValue(ref Type);s.SerializeValue(ref BossRun);s.SerializeValue(ref BossType);
            s.SerializeValue(ref InstanceId);s.SerializeValue(ref BossInstanceId);s.SerializeValue(ref Revision);
            s.SerializeValue(ref PhaseRevision);s.SerializeValue(ref AttackRevision);s.SerializeValue(ref Health);s.SerializeValue(ref MaximumHealth);
            s.SerializeValue(ref Phase);s.SerializeValue(ref PhaseStarted);s.SerializeValue(ref Observed);
            s.SerializeValue(ref Frozen);s.SerializeValue(ref Sweep);s.SerializeValue(ref Projectile);s.SerializeValue(ref ProjectilePosition);
        }
        public bool Equals(EnemyWire x)=>Run.Equals(x.Run)&&Type.Equals(x.Type)&&BossRun.Equals(x.BossRun)&&BossType.Equals(x.BossType)&&
            InstanceId==x.InstanceId&&BossInstanceId==x.BossInstanceId&&Revision==x.Revision&&PhaseRevision==x.PhaseRevision&&AttackRevision==x.AttackRevision&&
            Health==x.Health&&MaximumHealth==x.MaximumHealth&&Phase==x.Phase&&PhaseStarted.Equals(x.PhaseStarted)&&Observed.Equals(x.Observed)&&
            Frozen==x.Frozen&&Sweep==x.Sweep&&Projectile==x.Projectile&&ProjectilePosition.Equals(x.ProjectilePosition);
        public EnemySnapshot RequireSnapshot()
        {
            string run=Run.ToString(),type=Type.ToString();SaveIdentity.RequireGuid(run,nameof(Run));SaveIdentity.RequireTier(type);
            bool boss=BossInstanceId!=0;
            if(Revision==0||boss&&(BossRun.ToString()!=run||BossType.ToString()!=type)||!boss&&(BossRun.Length!=0||BossType.Length!=0))
                throw new InvalidOperationException("Partial or inconsistent enemy identity payload.");
            var key=boss?new BossKey(BossRun.ToString(),BossType.ToString(),BossInstanceId):default;
            var value=new EnemySnapshot(run,type,InstanceId,key,Health,MaximumHealth,(EnemyPhase)Phase,PhaseRevision,AttackRevision,
                PhaseStarted,Observed,Frozen,Sweep,Projectile,ProjectilePosition);
            if(!EnemyActor.ValidSnapshot(value))throw new InvalidOperationException("Invalid enemy state payload.");return value;
        }
        public static EnemyWire Capture(EnemySnapshot x,uint revision)=>new EnemyWire {
            Run=new FixedString64Bytes(x.RunId),Type=new FixedString64Bytes(x.EnemyId),InstanceId=x.InstanceId,Revision=revision,
            BossRun=new FixedString64Bytes(x.BossKey.RunId??""),BossType=new FixedString64Bytes(x.BossKey.ContractBossId??""),BossInstanceId=x.BossKey.InstanceId,
            Health=x.Health,MaximumHealth=x.MaximumHealth,Phase=(byte)x.Phase,PhaseRevision=x.PhaseRevision,AttackRevision=x.AttackRevision,
            PhaseStarted=x.PhaseStartedAt,Observed=x.ObservedAt,Frozen=x.Frozen,Sweep=x.Sweep,Projectile=x.ProjectileActive,ProjectilePosition=x.ProjectilePosition};
    }
    [DisallowMultipleComponent,RequireComponent(typeof(NetworkObject),typeof(EnemyActor))]
    public sealed class EnemyNetworkAdapter : NetworkBehaviour,IPreparedNetworkSpawn
    {
        public readonly NetworkVariable<EnemyWire> Snapshot=new NetworkVariable<EnemyWire>(default,
            NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        public bool IsPlayerObject=>false;
        public ulong OwnerConnectionId=>NetworkManager.ServerClientId;
        public EnemyActor Actor {get;private set;}
        private NgoGameSession game;
        private uint revision;
        private string run,type;
        private ulong instanceId;
        private bool prepared;
        private EnemyWire initial;
        private EnemyWire acceptedSnapshot;private bool hasAcceptedSnapshot;
        public bool HasAcceptedCurrentSnapshot=>IsSpawned&&(IsServer||hasAcceptedSnapshot&&acceptedSnapshot.Equals(Snapshot.Value));
        private void Awake()
        {
            game=NgoGameSession.RequireCurrent();Actor=GetComponent<EnemyActor>();
            var pose=GetComponent<NetworkTransform>();
            if(pose==null||GetComponents<NetworkTransform>().Length!=1||pose.AuthorityMode!=NetworkTransform.AuthorityModes.Server||GetComponent<NetworkRigidbody>()!=null)
                throw new InvalidOperationException("Enemy requires one server NetworkTransform and its domain body owner.");
            Actor.BindAuthority(game.HasAuthority);
        }
        public void PrepareNetworkState()
        {
            if(!game.HasAuthority||!Actor.Frozen||Actor.InstanceId==0||Actor.RunId!=game.Session.World.RunId)
                throw new InvalidOperationException("Initialize and freeze this authority enemy before publication.");
            revision=game.Driver.Revision;initial=EnemyWire.Capture(Actor.Snapshot,revision);initial.RequireSnapshot();prepared=true;
        }
        protected override void OnNetworkPreSpawn(ref NetworkManager networkManager)
        {
            base.OnNetworkPreSpawn(ref networkManager);if(!networkManager.IsServer)return;
            if(!prepared)throw new InvalidOperationException("Enemy was not prepared before NGO spawn.");
            Snapshot.Initialize(this);Snapshot.Value=initial;
        }
        public override void OnNetworkSpawn()
        {
            var wire=Snapshot.Value;var value=wire.RequireSnapshot();
            run=value.RunId;type=value.EnemyId;instanceId=value.InstanceId;revision=wire.Revision;
            if(Actor.Definition==null||Actor.Definition.EnemyId!=type||Actor.Definition.IsBoss!=value.BossKey.IsValid)
                throw new InvalidOperationException("Enemy prefab differs from the host's frozen identity.");
            if(!IsServer){Actor.ApplyReplica(value);acceptedSnapshot=wire;hasAcceptedSnapshot=true;}
            Snapshot.OnValueChanged+=Changed;game.Register(this);
            if(IsServer)game.Session.World.SnapshotChanged+=Publish;
        }
        private void Publish(){if(IsServer&&IsSpawned)Snapshot.Value=EnemyWire.Capture(Actor.Snapshot,revision);}
        private void Changed(EnemyWire previous,EnemyWire current)
        {
            if(IsServer)return;
            if(current.Revision!=revision||current.Run.ToString()!=run||current.Type.ToString()!=type||current.InstanceId!=instanceId)
                throw new InvalidOperationException("Spawned enemy cannot change run/type/instance identity.");
            Actor.ApplyReplica(current.RequireSnapshot());
            if(!IsSpawned||game.IsStopping)return;
            acceptedSnapshot=current;hasAcceptedSnapshot=true;
        }
        public override void OnNetworkDespawn()
        {
            hasAcceptedSnapshot=false;acceptedSnapshot=default;
            Snapshot.OnValueChanged-=Changed;
            if(game!=null){game.Session.World.SnapshotChanged-=Publish;game.Unregister(this);}
        }
    }
}
