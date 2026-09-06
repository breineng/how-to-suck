using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace HowToSuck
{
    public readonly struct PlayerSuitSnapshot
    {
        public readonly string RunId;
        public readonly int OwnerId, Segments;
        public readonly double InvulnerableUntil;
        public readonly bool RecoveryPending;
        public PlayerSuitSnapshot(string run,int owner,int segments,double until,bool pending)
        {RunId=run;OwnerId=owner;Segments=segments;InvulnerableUntil=until;RecoveryPending=pending;}
    }
    // One authority world owns the clock, attacks, suits, recovery and enemy/cargo lifetimes.
    // MonoBehaviour animation/collision callbacks cannot advance this service.
    public sealed class EnemySimulationService
    {
        private readonly AuthorityWorld world;
        private IWorldSpawner spawner;
        private ContractController contract;
        private LevelContext level;
        private string run;
        private bool running,inStep;
        private double now;
        private readonly Dictionary<ulong,EnemyActor> actors=new Dictionary<ulong,EnemyActor>();
        private readonly Dictionary<int,Suit> suits=new Dictionary<int,Suit>();
        private readonly List<EnemyActor> retired=new List<EnemyActor>();
        private readonly Dictionary<ulong,SuckableObject> pendingCargo=new Dictionary<ulong,SuckableObject>();
        private readonly List<Pose> cargoCandidates=new List<Pose>();
        private readonly List<SuckableObject> recoverCargo=new List<SuckableObject>();
        private readonly Dictionary<ulong,double> cargoRetry=new Dictionary<ulong,double>();
        private sealed class Suit {public int Segments=3;public double InvulnerableUntil,RetryAt;public bool Pending;public ulong AudioOccurrence;}
        public IReadOnlyDictionary<ulong,EnemyActor> Actors=>actors;
        public bool AdvancedEncounter=>contract!=null&&contract.State.ContractId.EndsWith("_ii",StringComparison.Ordinal);
        public IReadOnlyDictionary<int,PlayerMotor> Players=>world.Players;
        public EnemySimulationService(AuthorityWorld owner){world=owner??throw new ArgumentNullException(nameof(owner));}
        public bool CanApplyAt(double time)=>inStep&&running&&world.HasAuthority&&world.IsRunning&&contract!=null&&contract.IsRunning&&
            run==world.RunId&&contract.State.RunId==run&&time==now&&time<contract.State.Deadline;
        public bool RegisteredActor(EnemyActor actor)=>actor!=null&&actor.RunId==run&&actors.TryGetValue(actor.InstanceId,out var actual)&&actual==actor;
        public bool RegisteredItem(SuckableObject item)=>item!=null&&item.RunId==run&&world.Loot.Items.TryGetValue(item.InstanceId,out var actual)&&actual==item;
        public bool CanDefeat(BossKey key)=>contract!=null&&contract.State.Boss.Status==BossObjectiveStatus.Active&&contract.State.Boss.Key.Equals(key);
        public bool RequiresRecovery(int owner)=>suits.TryGetValue(owner,out var suit)&&suit.Pending;
        public PlayerSuitSnapshot SuitSnapshot(int owner)=>suits.TryGetValue(owner,out var s)?new PlayerSuitSnapshot(run,owner,s.Segments,s.InvulnerableUntil,s.Pending):default;
        public bool InsideWorld(Vector3 point)=>level!=null&&level.BoundsGuard!=null&&point.y>=level.BoundsGuard.MinimumY&&level.BoundsGuard.AllowedBounds.Contains(point);
        public void Prepare(LevelContext context,IWorldSpawner spawnOwner,ContractController controller,int crewSize)
        {
            if(!world.HasAuthority||!string.IsNullOrEmpty(run)||controller==null||controller.State.Phase!=ContractPhase.Preparing||crewSize<1||crewSize>4)
                throw new InvalidOperationException("Prepare one fresh authority encounter before the run starts.");
            if(context==null||context.EnemyEncounters==null||context.CargoDropPoints==null||context.CargoDropPoints.Length==0||context.CargoDropPoints.Any(p=>p==null))
                throw new InvalidOperationException("Main levels require encounters and authored truck cargo drop points.");
            var entries=context.EnemyEncounters.Where(x=>x!=null&&x.ContractId==controller.State.ContractId).ToArray();
            if(entries.Length!=1||entries[0].Spawns==null||entries[0].Spawns.Length<1||entries[0].Spawns.Length>32||
                entries[0].Spawns.Distinct().Count()!=entries[0].Spawns.Length)
                throw new InvalidOperationException("Select exactly one bounded unique authored encounter for this contract.");
            var definitions=new List<EnemyActor>();
            foreach(var point in entries[0].Spawns)
            {
                var actor=point!=null&&point.Prefab!=null?point.Prefab.GetComponent<EnemyActor>():null;
                if(actor==null||!actor.TryValidate(out _))throw new InvalidOperationException("Encounter has an invalid enemy prefab.");
                definitions.Add(actor);
            }
            string required=controller.RequiredBossId;
            if(definitions.Count(a=>a.Definition.IsBoss)!=1||definitions.Single(a=>a.Definition.IsBoss).Definition.EnemyId!=required)
                throw new InvalidOperationException("This encounter must contain exactly the contract's required boss type.");
            level=context;spawner=spawnOwner??throw new ArgumentNullException(nameof(spawnOwner));contract=controller;run=controller.State.RunId;
            ulong next=0;
            foreach(var point in entries[0].Spawns)
            {
                var instance=spawner.Spawn(point.Prefab,point.transform.position,point.transform.rotation);
                if(instance==null)throw new InvalidOperationException("Enemy spawn failed.");
                try
                {
                    var actor=instance.GetComponent<EnemyActor>();ulong id=++next;
                    var key=actor.Definition.IsBoss?new BossKey(run,required,id):default;
                    actor.Initialize(this,run,id,key,crewSize,point.transform.position);actors.Add(id,actor);
                    if(key.IsValid&&!contract.TryAssignBoss(key))throw new InvalidOperationException("Current boss assignment failed.");
                    (spawner as IWorldSpawnCommitter)?.CommitSpawn(instance);
                }
                catch{spawner.Despawn(instance);throw;}
            }
        }
        public void RegisterPlayer(int owner)
        {if(string.IsNullOrEmpty(run)||owner<1||owner>4)throw new InvalidOperationException("Register suit in the prepared run.");suits.Add(owner,new Suit());}
        public void RemovePlayer(int owner)=>suits.Remove(owner);
        public void SetRunning(bool value)
        {
            if(value&&(contract==null||!contract.IsRunning||!world.IsRunning))throw new InvalidOperationException("Combat follows the real authority run.");
            running=value;foreach(var actor in actors.Values)if(actor!=null)actor.SetFrozen(!value);
            if(!value)inStep=false;
        }
        public void BeginStep(double time)
        {if(inStep)throw new InvalidOperationException("Combat step is not reentrant.");now=time;inStep=true;}
        public void EndStep()=>inStep=false;
        public void StepRecovery()
        {
            if(!CanApplyAt(now))return;
            foreach(var pair in suits)
            {
                var suit=pair.Value;if(!suit.Pending||now<suit.RetryAt||!world.Players.TryGetValue(pair.Key,out var motor)||motor==null)continue;
                suit.RetryAt=now+.25;
                if(level.BoundsGuard.TryFindRecoveryPosition(motor,out var position)&&world.TryRecoverCombatPlayer(motor,position,now))
                {suit.Segments=3;suit.Pending=false;suit.InvulnerableUntil=now+2;
                    if(suit.AudioOccurrence<ulong.MaxValue)Audio.CommittedAudioEvents.Publish(new Audio.CommittedAudioFact(
                        run,Audio.CommittedAudioKind.SuitRecovered,++suit.AudioOccurrence,0,0,pair.Key,0,false,now,0,3,motor.transform.position));}
            }
            // A disconnected owner's original FIFO survives until each item has a checked physical return pose.
            foreach(var storage in world.Storages.Values)
            {
                if(!storage.IsDetached||!storage.TryPeek(out var key,out var item))continue;
                BuildCargoCandidates(item,null);
                if(world.ItemFire.TryFindSafePose(item,cargoCandidates,out var pose,out _)&&storage.TryReturnFirst(key,pose.position,pose.rotation,out _))Physics.SyncTransforms();
            }
            recoverCargo.Clear();
            foreach(var item in world.Loot.Items.Values)
                if(item!=null&&item.CargoRole==CargoRole.BossBody&&item.State==SuckableState.Available&&!item.WorldFrozen&&!InsideWorld(item.transform.position))recoverCargo.Add(item);
            foreach(var item in recoverCargo)
            {
                if(cargoRetry.TryGetValue(item.InstanceId,out double retry)&&now<retry)continue;cargoRetry[item.InstanceId]=now+.25;
                BuildCargoCandidates(item,null);
                if(world.ItemFire.TryFindSafePose(item,cargoCandidates,out var pose,out _)&&item.TryRelocateAvailable(item.Key,pose.position,pose.rotation))
                {cargoRetry.Remove(item.InstanceId);Physics.SyncTransforms();}
            }
        }
        public void StepActors(float dt)
        {
            if(!CanApplyAt(now))return;retired.Clear();
            foreach(var actor in actors.Values)
            {
                if(!CanApplyAt(now))break;
                if(actor==null)throw new InvalidOperationException("An authority enemy was destroyed outside its lifetime owner.");
                actor.Step(now,dt);if(actor.Retired)retired.Add(actor);
            }
            foreach(var actor in retired){actors.Remove(actor.InstanceId);spawner.Despawn(actor.gameObject);}retired.Clear();
        }
        internal void RecordBossDefeat(EnemyActor actor,double time)
        {
            if(!CanApplyAt(time)||actor==null||actor.Health!=0||!actors.TryGetValue(actor.InstanceId,out var current)||current!=actor||
                !contract.TryDefeatBoss(actor.BossKey,time))throw new InvalidOperationException("Real current boss defeat lost its exact authority identity.");
        }
        internal bool TryDamagePlayer(EnemyActor enemy,PlayerMotor player,double time)
        {
            if(!CanApplyAt(time)||enemy==null||!enemy.IsAlive||enemy.RunId!=run||!actors.TryGetValue(enemy.InstanceId,out var current)||current!=enemy||
                player==null||!player.isActiveAndEnabled||!world.Players.TryGetValue(player.PlayerId,out var actual)||actual!=player||
                !suits.TryGetValue(player.PlayerId,out var suit)||suit.Pending||time<suit.InvulnerableUntil)return false;
            suit.Segments--;suit.InvulnerableUntil=time+1.5;
            if(suit.AudioOccurrence<ulong.MaxValue)Audio.CommittedAudioEvents.Publish(new Audio.CommittedAudioFact(
                run,Audio.CommittedAudioKind.SuitHit,++suit.AudioOccurrence,0,enemy.InstanceId,player.PlayerId,0,false,time,0,suit.Segments,player.transform.position));
            if(suit.Segments==0)
            {
                suit.Pending=true;suit.RetryAt=time;
                var emitter=player.GetComponent<VacuumEmitter>();if(emitter!=null)emitter.Active=false;
                world.Ingestion.CancelOwner(player.PlayerId);player.SuspendForRecovery(player.LastIntent);
                // This transition happens once per depletion; timeout may synchronously freeze the whole world.
                contract.ApplyDeadlinePenalty(15,time);
            }
            return true;
        }
        internal bool CompleteDefeat(EnemyActor actor)
        {
            if(!CanApplyAt(now)||actor==null||actor.Health!=0)return false;
            if(!actor.BossKey.IsValid)return true;
            if(contract.State.Boss.Status!=BossObjectiveStatus.Defeated||!contract.State.Boss.Key.Equals(actor.BossKey))return false;
            if(!pendingCargo.TryGetValue(actor.InstanceId,out var cargo))
            {
                var instance=spawner.Spawn(actor.DefeatedCargoPrefab,actor.transform.position,actor.transform.rotation);
                if(instance==null)throw new InvalidOperationException("Boss body spawn failed.");
                try
                {
                    cargo=instance.GetComponent<SuckableObject>();cargo.BindBossCargo(actor.BossKey);world.Loot.Register(cargo);world.ItemFire.Track(cargo);
                    cargo.SetWorldFrozen(true);pendingCargo.Add(actor.InstanceId,cargo);
                    // Remain unpublished and invisible until the original body has a checked destination.
                    cargo.VisualRoot.gameObject.SetActive(false);foreach(var c in cargo.GameplayColliders)c.enabled=false;
                }
                catch{if(cargo!=null&&RegisteredItem(cargo))world.Loot.Unregister(cargo);spawner.Despawn(instance);throw;}
            }
            if(cargoRetry.TryGetValue(cargo.InstanceId,out var retryAt)&&now<retryAt)return false;cargoRetry[cargo.InstanceId]=now+.25;
            BuildCargoCandidates(cargo,actor.transform.position);
            if(!world.ItemFire.TryFindSafePose(cargo,cargoCandidates,out var pose,out _)||!cargo.TryRelocateAvailable(cargo.Key,pose.position,pose.rotation))return false;
            cargo.VisualRoot.gameObject.SetActive(true);foreach(var c in cargo.GameplayColliders)c.enabled=true;
            (spawner as IWorldSpawnCommitter)?.CommitSpawn(cargo.gameObject);
            cargo.SetWorldFrozen(false);pendingCargo.Remove(actor.InstanceId);cargoRetry.Remove(cargo.InstanceId);Physics.SyncTransforms();return true;
        }
        private void BuildCargoCandidates(SuckableObject item,Vector3? preferred)
        {
            cargoCandidates.Clear();var anchors=new List<Vector3>();if(preferred.HasValue)anchors.Add(preferred.Value);
            foreach(var point in level.CargoDropPoints)anchors.Add(point.position);
            float lift=.06f+Mathf.Max(.1f,item.RequiredIntakeSize*.5f);
            for(int ring=0;ring<=2;ring++)for(int i=0;i<(ring==0?1:8);i++)
                foreach(var anchor in anchors)
                {
                    float angle=i*Mathf.PI*.25f;var sample=anchor+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*ring*1.25f;
                    if(!Physics.Raycast(sample+Vector3.up*2,Vector3.down,out var floor,4,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)||floor.normal.y<.85f)continue;
                    var position=floor.point+Vector3.up*lift;if(!InsideWorld(position))continue;
                    cargoCandidates.Add(new Pose(position,item.Body.rotation));if(cargoCandidates.Count==32)return;
                }
        }
        internal bool TryReturnEnemyHome(EnemyActor actor,Vector3 home)
        {
            if(!CanApplyAt(now))return false;var box=actor.GetComponent<BoxCollider>();var rotation=actor.transform.rotation;
            foreach(var collider in Physics.OverlapBox(home+rotation*box.center,Vector3.Max(Vector3.one*.01f,box.size*.5f-Vector3.one*.02f),rotation,
                LayerMask.GetMask("World","Items","Player","Enemies"),QueryTriggerInteraction.Ignore))
                if(collider!=box&&!collider.transform.IsChildOf(actor.transform))return false;
            actor.GetComponent<Rigidbody>().position=home;Physics.SyncTransforms();return true;
        }
        public void Clear()
        {
            running=false;inStep=false;
            foreach(var actor in actors.Values)if(actor!=null)spawner?.Despawn(actor.gameObject);
            // Pending cargo is registered; AuthorityWorld's ordinary loot teardown owns its one destruction.
            actors.Clear();suits.Clear();retired.Clear();pendingCargo.Clear();cargoCandidates.Clear();recoverCargo.Clear();cargoRetry.Clear();
            run=null;level=null;contract=null;spawner=null;
        }
    }
}
