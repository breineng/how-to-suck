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
        public int Health=>Segments;
        public readonly int RepairCharges;
        public readonly float RepairProgress;
        public readonly double InvulnerableUntil;
        public readonly bool RecoveryPending;
        public PlayerSuitSnapshot(string run,int owner,int segments,double until,bool pending,int repairs=0,float repairProgress=0)
        {RunId=run;OwnerId=owner;Segments=segments;InvulnerableUntil=until;RecoveryPending=pending;RepairCharges=repairs;RepairProgress=repairProgress;}
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
        private sealed class Suit {public int Segments=100,UsedRepairs,Reviver;public double InvulnerableUntil,RetryAt,RepairStarted=-1;public bool Pending,SelfReviveArmed;public Vector3 DownPosition;public ulong AudioOccurrence;}
        public const double ReviveSeconds=3;
        public const double SelfRevivePenalty=15;
        public const float ReviveDistance=2.2f;
        public const double ArrivalGraceSeconds=30;
        private sealed class Dormant {public EnemySpawnPoint Spawn;public SuckableObject Cover;public Vector3 Position;public ulong Id;public bool Awakened;}
        private readonly List<Dormant> dormant=new List<Dormant>();
        private EnemySpawnPoint bossSpawn;
        private ulong bossId;
        private int encounterCrew;
        private EnemySpawnPoint[] reinforcementSpawns=Array.Empty<EnemySpawnPoint>();
        private ulong nextEnemyId;
        private double nextReinforcementAt;
        private int reinforcementCursor,reinforcementsSpawned;
        private int EarnedRepairs=>1+(contract!=null?(int)Math.Min(3,contract.State.DeliveredValue*4/Math.Max(1,contract.State.Quota)):0);
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
        public int Stage=>CampaignBalance.Stage(contract?.State.ContractId);
        // Re-evaluate against the active roster, including after a revive or disconnect.
        public bool AllPlayersDown=>world.Players.Count>0&&world.Players.All(p=>p.Value!=null&&suits.TryGetValue(p.Key,out var s)&&s.Pending);
        public PlayerSuitSnapshot SuitSnapshot(int owner)=>suits.TryGetValue(owner,out var s)?new PlayerSuitSnapshot(run,owner,s.Segments,s.InvulnerableUntil,s.Pending,
            Math.Max(0,EarnedRepairs-s.UsedRepairs),s.RepairStarted>=0?Mathf.Clamp01((float)((now-s.RepairStarted)/(s.Pending?ReviveSeconds:1.5))):0):default;
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
            encounterCrew=crewSize;ulong next=0;
            var covers=world.Loot.Items.Values.Where(x=>x!=null&&x.CargoRole==CargoRole.OrdinaryLoot&&x.CanBeSwallowedByPlayer).OrderBy(x=>UnityEngine.Random.value).ToList();
            foreach(var point in entries[0].Spawns)
            {
                ulong id=++next;
                if(point.Prefab.GetComponent<EnemyActor>().Definition.IsBoss){bossSpawn=point;bossId=id;continue;}
                var cover=covers.Count>0?covers[0]:null;if(cover!=null)covers.RemoveAt(0);
                dormant.Add(new Dormant{Spawn=point,Cover=cover,Position=cover!=null?cover.transform.position:point.transform.position,Id=id});
            }
            nextEnemyId=next;nextReinforcementAt=0;reinforcementCursor=0;reinforcementsSpawned=0;
            reinforcementSpawns=entries[0].Spawns.Where(p=>!p.Prefab.GetComponent<EnemyActor>().Definition.IsBoss).ToArray();
        }
        private bool TrySpawn(EnemySpawnPoint point,Vector3 position,ulong id,bool boss,bool hiddenFromCrew=false)
        {
            var template=point.Prefab.GetComponent<EnemyActor>();var shape=point.Prefab.GetComponent<BoxCollider>();
            for(int sample=0;sample<17;sample++)
            {
                float angle=sample*Mathf.PI*.25f,radius=sample==0?0:sample<=8?.65f:1.3f;
                var candidate=position+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*radius;
                if(!UnityEngine.AI.NavMesh.SamplePosition(candidate,out var nav,1.5f,UnityEngine.AI.NavMesh.AllAreas)||!InsideWorld(nav.position))continue;
                if(hiddenFromCrew&&!HiddenFromCrew(nav.position))continue;
                bool blocked=false;
                foreach(var c in Physics.OverlapBox(nav.position+point.transform.rotation*shape.center+Vector3.up*.06f,
                    Vector3.Max(Vector3.one*.02f,shape.size*.5f-Vector3.one*.04f),point.transform.rotation,LayerMask.GetMask("World","Items","Player","Enemies"),QueryTriggerInteraction.Ignore))
                    if(c.bounds.max.y>nav.position.y+.16f){blocked=true;break;}
                if(blocked)continue;
                var instance=spawner.Spawn(point.Prefab,nav.position,point.transform.rotation);
                if(instance==null)throw new InvalidOperationException("Enemy spawn failed.");
                try
                {
                    var actor=instance.GetComponent<EnemyActor>();var key=boss?new BossKey(run,template.Definition.EnemyId,id):default;
                    actor.Initialize(this,run,id,key,encounterCrew,nav.position);actors.Add(id,actor);
                    if(boss&&!contract.TryAssignBoss(key))throw new InvalidOperationException("Delayed boss assignment failed.");
                    // Publish the prepared frozen snapshot first, as required by the network spawn adapter.
                    (spawner as IWorldSpawnCommitter)?.CommitSpawn(instance);actor.SetFrozen(false);
                    return true;
                }
                catch{actors.Remove(id);spawner.Despawn(instance);throw;}
            }
            return false;
        }
        private void StepSpawns()
        {
            // Allow the crew to arrive and orient themselves before any encounter.
            // Settling furniture and automatic truck intake never count as provocation.
            if(now-contract.State.StartedAt<ArrivalGraceSeconds)return;
            int alive=actors.Values.Count(a=>a!=null&&a.IsAlive&&!a.BossKey.IsValid);
            int cap=CampaignBalance.EnemyCap(Stage,encounterCrew);
            if(contract.State.Boss.Status==BossObjectiveStatus.Active)cap=CampaignBalance.BossEscortCap(Stage,encounterCrew);
            for(int i=dormant.Count-1;i>=0;i--)
            {
                var hidden=dormant[i];
                if(hidden.Cover!=null&&hidden.Cover.HasReceivedPlayerSuction)hidden.Awakened=true;
                if(now-contract.State.StartedAt>ArrivalGraceSeconds+35+hidden.Id*18)hidden.Awakened=true;
                // A disturbed object may be next to its collector: use an authored
                // entrance, with the same distance/visibility checks as reinforcements.
                if(hidden.Awakened&&alive<cap&&SpawnHiddenFromCrew(hidden.Spawn,hidden.Id)){dormant.RemoveAt(i);alive++;}
            }
            if(bossSpawn!=null&&contract.State.Boss.Status==BossObjectiveStatus.Unassigned&&contract.CanRevealBoss)
                if(TrySpawn(bossSpawn,bossSpawn.transform.position,bossId,true))bossSpawn=null;
            // Bounded pressure, with regular quiet intervals and no reinforcements
            // on top of players or after the boss dies. All spawn points are authored.
            if(contract.State.Boss.Status!=BossObjectiveStatus.Active||reinforcementSpawns.Length==0||AllPlayersDown||
                reinforcementsSpawned>=CampaignBalance.ReinforcementBudget[Stage])return;
            if(nextReinforcementAt==0){nextReinforcementAt=now+CampaignBalance.ReinforcementSeconds[Stage];return;}
            if(now<nextReinforcementAt||alive>=cap)return;
            if((now-contract.State.StartedAt)%90<18){nextReinforcementAt=now+5;return;}
            if(suits.Values.Any(s=>s.Pending)||suits.Values.All(s=>s.Segments<=25)){nextReinforcementAt=now+10;return;}
            nextReinforcementAt=now+CampaignBalance.ReinforcementSeconds[Stage];
            for(int i=0;i<reinforcementSpawns.Length;i++)
            {
                var point=reinforcementSpawns[reinforcementCursor++%reinforcementSpawns.Length];
                if(SpawnHiddenFromCrew(point,nextEnemyId+1)){nextEnemyId++;reinforcementsSpawned++;break;}
            }
        }
        private bool SpawnHiddenFromCrew(EnemySpawnPoint point,ulong id)
        {
            return TrySpawn(point,point.transform.position,id,false,true);
        }
        private bool HiddenFromCrew(Vector3 point)=>!world.Players.Values.Any(p=>p!=null&&((p.transform.position-point).sqrMagnitude<64||
            Vector3.Dot(p.AimRay.direction,(point+Vector3.up-p.AimRay.origin).normalized)>.25f&&
            !Physics.Linecast(p.AimRay.origin,point+Vector3.up,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)));
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
                var suit=pair.Value;if(!suit.Pending||!world.Players.TryGetValue(pair.Key,out var motor)||motor==null)continue;
                bool selfRescue=AllPlayersDown;
                var intent=world.RecoveryIntent(pair.Key);
                if(!intent.InteractHeld)suit.SelfReviveArmed=true;
                int helper=selfRescue?(suit.SelfReviveArmed&&intent.InteractHeld?pair.Key:0):FindReviver(pair.Key,motor);
                if(helper==0){suit.RepairStarted=-1;suit.Reviver=0;continue;}
                if(suit.RepairStarted<0||suit.Reviver!=helper){suit.RepairStarted=now;suit.Reviver=helper;continue;}
                if(now-suit.RepairStarted<ReviveSeconds||!TryRecoveryPosition(motor,suit.DownPosition,out var recoveryPosition))continue;
                if(helper==pair.Key&&contract.State.Deadline-now<=SelfRevivePenalty)
                {
                    contract.ApplyDeadlinePenalty(SelfRevivePenalty,now);
                    return;
                }
                if(world.TryRecoverCombatPlayer(motor,recoveryPosition,now))
                {suit.Segments=CampaignBalance.ReviveHealth;suit.Pending=false;suit.RepairStarted=-1;suit.Reviver=0;suit.InvulnerableUntil=now+2;
                    if(helper==pair.Key)contract.ApplyDeadlinePenalty(SelfRevivePenalty,now);
                    if(suit.AudioOccurrence<ulong.MaxValue)Audio.CommittedAudioEvents.Publish(new Audio.CommittedAudioFact(
                        run,Audio.CommittedAudioKind.SuitRecovered,++suit.AudioOccurrence,0,0,pair.Key,0,false,now,0,suit.Segments,motor.transform.position));}
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
            StepSpawns();
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
        internal bool TryDamagePlayer(EnemyActor enemy,PlayerMotor player,double time,bool ranged=false)
        {
            if(!CanApplyAt(time)||enemy==null||!enemy.IsAlive||enemy.RunId!=run||!actors.TryGetValue(enemy.InstanceId,out var current)||current!=enemy||
                player==null||!player.isActiveAndEnabled||!world.Players.TryGetValue(player.PlayerId,out var actual)||actual!=player||
                !suits.TryGetValue(player.PlayerId,out var suit)||suit.Pending||time<suit.InvulnerableUntil)return false;
            int damage=ranged?enemy.RangedDamage:enemy.ContactDamage;
            suit.Segments=Math.Max(0,suit.Segments-Mathf.Clamp(damage,1,100));suit.InvulnerableUntil=time+.65;suit.RepairStarted=-1;
            if(suit.AudioOccurrence<ulong.MaxValue)Audio.CommittedAudioEvents.Publish(new Audio.CommittedAudioFact(
                run,Audio.CommittedAudioKind.SuitHit,++suit.AudioOccurrence,0,enemy.InstanceId,player.PlayerId,0,false,time,0,suit.Segments,player.transform.position));
            if(suit.Segments==0)
            {
                suit.Pending=true;suit.DownPosition=player.transform.position;suit.SelfReviveArmed=false;suit.Reviver=0;
                // A lethal hit during a jump revives on the floor directly below,
                // while the ragdoll itself still starts at the actual hit position.
                if(Physics.Raycast(suit.DownPosition+Vector3.up*.1f,Vector3.down,out var floor,
                    Mathf.Max(3,player.Settings.JumpHeight+2),LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)&&
                    floor.normal.y>.6f&&suit.DownPosition.y-floor.point.y>.2f)
                    suit.DownPosition.y=floor.point.y+.025f;
                var emitter=player.GetComponent<VacuumEmitter>();if(emitter!=null)emitter.Active=false;
                world.Ingestion.CancelOwner(player.PlayerId);player.SetDowned(true);
            }
            return true;
        }
        public void StepRepairs()
        {
            if(!CanApplyAt(now))return;
            foreach(var pair in suits)
            {
                var suit=pair.Value;
                if(suit.Pending)continue; // The revival hold has its own clock and must survive this pass.
                if(!world.Players.TryGetValue(pair.Key,out var player)||player==null||suit.Pending||suit.Segments>=100||
                    suit.UsedRepairs>=EarnedRepairs||!world.IsPlayerInExtraction(pair.Key)||!player.LastIntent.InteractHeld||
                    contract.State.ObjectivesComplete||now<suit.InvulnerableUntil)
                {suit.RepairStarted=-1;continue;}
                if(suit.RepairStarted<0){suit.RepairStarted=now;continue;}
                if(now-suit.RepairStarted<1.5)continue;
                suit.Segments=Math.Min(100,suit.Segments+45);suit.UsedRepairs++;suit.RepairStarted=-1;
                if(suit.AudioOccurrence<ulong.MaxValue)Audio.CommittedAudioEvents.Publish(new Audio.CommittedAudioFact(
                    run,Audio.CommittedAudioKind.SuitRecovered,++suit.AudioOccurrence,0,0,pair.Key,0,false,now,0,suit.Segments,player.transform.position));
            }
        }
        private int FindReviver(int owner,PlayerMotor fallen)
        {
            int selected=0;float nearest=ReviveDistance*ReviveDistance;
            foreach(var candidate in world.Players)
            {
                var helper=candidate.Value;
                if(candidate.Key==owner||helper==null||!suits.TryGetValue(candidate.Key,out var state)||state.Pending||
                    now<state.InvulnerableUntil||!world.RecoveryIntent(candidate.Key).InteractHeld||helper.PlanarSpeed>.5f)continue;
                float distance=(helper.transform.position-fallen.transform.position).sqrMagnitude;
                if(distance>nearest||Physics.Linecast(helper.transform.position+Vector3.up*.9f,fallen.transform.position+Vector3.up*.45f,
                    LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore))continue;
                // One held interaction can lift only the nearest fallen teammate.
                bool nearer=false;
                foreach(var other in suits)if(other.Key!=owner&&other.Value.Pending&&
                    world.Players.TryGetValue(other.Key,out var body)&&body!=null&&
                    (body.transform.position-helper.transform.position).sqrMagnitude<distance){nearer=true;break;}
                if(nearer)continue;
                selected=candidate.Key;nearest=distance;
            }
            return selected;
        }
        private bool TryRecoveryPosition(PlayerMotor motor,Vector3 down,out Vector3 position)
        {
            position=down;
            if(InsideWorld(down)&&StandingRoom(motor,down))return true;
            // A disabled downed capsule can finish inside a door lip. Find nearby
            // floor on the same side of the wall instead of waiting forever there.
            for(int ring=0;ring<=3;ring++)for(int direction=0;direction<(ring==0?1:8);direction++)
            {
                float angle=direction*Mathf.PI*.25f;
                Vector3 candidate=down+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(ring*.4f);
                if(!Physics.Raycast(candidate+Vector3.up*.6f,Vector3.down,out var floor,1.2f,
                    LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)||floor.normal.y<.6f)continue;
                candidate.y=floor.point.y+.035f;
                if(!InsideWorld(candidate)||Mathf.Abs(candidate.y-down.y)>.5f||
                    Physics.Linecast(down+Vector3.up*.6f,candidate+Vector3.up*.6f,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)||
                    !StandingRoom(motor,candidate))continue;
                position=candidate;return true;
            }
            return false;
        }
        private static bool StandingRoom(PlayerMotor motor,Vector3 position)
        {
            float radius=motor.Settings.CapsuleRadius*.92f;
            return !Physics.CheckCapsule(position+Vector3.up*(radius+.10f),
                position+Vector3.up*(motor.Settings.CapsuleHeight-radius),radius,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore);
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
            dormant.Clear();bossSpawn=null;bossId=0;reinforcementSpawns=Array.Empty<EnemySpawnPoint>();nextEnemyId=0;
            run=null;level=null;contract=null;spawner=null;
        }
    }
}
