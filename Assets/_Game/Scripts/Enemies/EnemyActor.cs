using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
namespace HowToSuck
{
    [DisallowMultipleComponent, RequireComponent(typeof(Rigidbody),typeof(BoxCollider))]
    public sealed class EnemyActor : MonoBehaviour, IItemDamageReceiver
    {
        public EnemyDefinition Definition;
        public EnemyAnimationView View;
        public Transform AttackOrigin, AimTarget;
        public string RunId {get;private set;}
        public string EnemyId {get;private set;}
        public ulong InstanceId {get;private set;}
        public BossKey BossKey {get;private set;}
        public int Health {get;private set;}
        public int MaximumHealth {get;private set;}
        public EnemyPhase Phase {get;private set;}
        public uint PhaseRevision {get;private set;}
        public uint AttackRevision {get;private set;}
        public bool Frozen {get;private set;}=true;
        public bool HasAuthority {get;private set;}=true;
        public bool Retired {get;private set;}
        public Vector3 Home {get;private set;}
        public GameObject DefeatedCargoPrefab=>rules!=null?rules.DefeatedCargoPrefab:null;
        public bool IsAlive=>initialized&&!Retired&&Health>0&&Phase!=EnemyPhase.Defeated;
        public EnemySnapshot Snapshot=>new EnemySnapshot(RunId,EnemyId,InstanceId,BossKey,Health,MaximumHealth,Phase,
            PhaseRevision,AttackRevision,phaseAt,observedAt,Frozen,sweep,projectileActive,projectilePosition);
        private EnemySimulationService simulation;
        private Rigidbody body;
        private BoxCollider shape;
        private bool initialized,roleBound,sweep,projectileActive,projectileEmitted;
        private double phaseAt,observedAt,nextPathAt,projectileExpires;
        private Vector3 attackDirection,projectilePosition,projectileDirection,navDestination,lockedAim;
        private int lastAttacker;
        private double aggroUntil;
        private float simulationDelta=.02f;
        // One-frame checked facing also bounds the following kinematic translation.
        private Quaternion movementRotation;
        private Collider blockedCargo;
        private Vector3 blockedWorldNormal;
        private Vector3 blockedCargoNormal;
        private Rigidbody bypassCargo;
        private float bypassSign;
        private EnemyActor trafficPeer,blockedEnemy;
        private Vector3 trafficIntent,yieldDirection,yieldPocket;
        private bool hasYieldPocket;
        private double trafficAt,yieldUntil;
        private double movementPlanAt=double.NegativeInfinity;
        private Vector3 plannedPosition;
        private Quaternion plannedRotation;
        private void BeginMovementPlan(double time)
        {
            if(movementPlanAt==time)return;
            movementPlanAt=time;plannedPosition=body.position;plannedRotation=body.rotation;
        }
        private NavMeshPath path;
        private Vector3[] corners=Array.Empty<Vector3>();
        private int corner,targetId;
        private readonly HashSet<int> hitPlayers=new HashSet<int>();
        private EnemyDefinition rules;
        private readonly BossChapterAttackSequence chapterAttack = new BossChapterAttackSequence();
        private bool jumping,steppingDown;
        private double jumpUntil;
        private int bossAttack;
        public bool Enraged=>BossKey.IsValid&&Health<=MaximumHealth/2;
        internal int ContactDamage=>rules.ContactDamage;
        internal int RangedDamage=>rules.RangedDamage;
        private void Awake(){path=new NavMeshPath();body=GetComponent<Rigidbody>();shape=GetComponent<BoxCollider>();body.isKinematic=true;body.useGravity=false;if(GetComponent<EnemyFeedbackView>()==null)gameObject.AddComponent<EnemyFeedbackView>();}
        public bool TryValidate(out string error)
        {
            if(Definition==null||!Definition.TryValidate(out error)){error="Enemy definition is invalid.";return false;}
            if(View==null||!View.TryValidate(out error)){error="Enemy animation view is invalid.";return false;}
            var box=GetComponent<BoxCollider>();
            if(box==null||box.isTrigger||box.size.x<=0||box.size.y<=0||box.size.z<=0||AttackOrigin==null||AimTarget==null||
                (transform.lossyScale-Vector3.one).sqrMagnitude>.00001f)
            {error="Enemy needs one solid identity-scale box body and authored attack/aim markers.";return false;}
            error=null;return true;
        }
        public void BindAuthority(bool authority)
        {
            if((roleBound||initialized)&&authority!=HasAuthority)throw new InvalidOperationException("Enemy authority cannot change in place.");
            HasAuthority=authority;roleBound=true;
        }
        public void Initialize(EnemySimulationService owner,string run,ulong id,BossKey boss,int crewSize,Vector3 home)
        {
            string error=null;
            if(!HasAuthority||initialized||owner==null||!new LootKey(run,id).IsValid||!TryValidate(out error))
                throw new InvalidOperationException("Prepare a new valid authority enemy: "+error);
            if(Definition.IsBoss!=boss.IsValid||boss.IsValid&&(boss.RunId!=run||boss.InstanceId!=id||boss.ContractBossId!=Definition.EnemyId)||!boss.IsValid&&(boss.InstanceId!=0||!string.IsNullOrEmpty(boss.RunId)||!string.IsNullOrEmpty(boss.ContractBossId)))throw new InvalidOperationException("Boss identity must name this exact active instance.");
            if(!NavMesh.SamplePosition(home,out var point,.4f,NavMesh.AllAreas)||Vector3.Distance(point.position,home)>.4f)
                throw new InvalidOperationException("Enemy home must be on the authored walkable navigation surface.");
            // Freeze the authored rules for this instance; live asset edits cannot change an ongoing attack.
            rules=Instantiate(Definition);rules.hideFlags=HideFlags.HideAndDontSave;
            int stage=owner.Stage;
            rules.BaseHealth=rules.IsBoss?CampaignBalance.BossHealth[stage]:Mathf.RoundToInt(rules.BaseHealth*(1+.18f*stage));
            rules.MoveSpeed*=1+.045f*stage;
            rules.ContactDamage=Mathf.Min(45,Mathf.RoundToInt(rules.ContactDamage*(1+.06f*stage)));
            rules.RangedDamage=Mathf.Min(38,Mathf.RoundToInt(rules.RangedDamage*(1+.06f*stage)));
            simulation=owner;RunId=run;EnemyId=rules.EnemyId;InstanceId=id;BossKey=boss;Home=point.position;
            Health=MaximumHealth=rules.HealthForCrew(crewSize);body.position=Home;initialized=true;Phase=EnemyPhase.Idle;
            PhaseRevision=1;shape.enabled=true;Frozen=true;
        }
        public void SetFrozen(bool value)
        {if(!HasAuthority)throw new InvalidOperationException("Only authority freezes its enemy.");if(value){CancelChapterAttack();StopJump();}Frozen=value;}
        public void ApplyReplica(EnemySnapshot value)
        {
            if(HasAuthority||!ValidSnapshot(value)||Definition==null||value.EnemyId!=Definition.EnemyId||Definition.IsBoss!=value.BossKey.IsValid)
                throw new InvalidOperationException("Invalid enemy replica.");
            if(initialized&&(value.RunId!=RunId||value.InstanceId!=InstanceId||value.EnemyId!=EnemyId||!value.BossKey.Equals(BossKey)||value.MaximumHealth!=MaximumHealth))
                throw new InvalidOperationException("Enemy replica identity changed.");
            if(initialized&&(value.ObservedAt<observedAt||value.Health>Health))return;
            RunId=value.RunId;EnemyId=value.EnemyId;InstanceId=value.InstanceId;BossKey=value.BossKey;Health=value.Health;MaximumHealth=value.MaximumHealth;
            Phase=value.Phase;PhaseRevision=value.PhaseRevision;AttackRevision=value.AttackRevision;phaseAt=value.PhaseStartedAt;observedAt=value.ObservedAt;
            Frozen=value.Frozen;sweep=value.Sweep;projectileActive=value.ProjectileActive;projectilePosition=value.ProjectilePosition;
            initialized=true;shape.enabled=Health>0;
        }
        public static bool ValidSnapshot(EnemySnapshot x)=>new LootKey(x.RunId,x.InstanceId).IsValid&&!string.IsNullOrWhiteSpace(x.EnemyId)&&
            x.MaximumHealth>0&&x.MaximumHealth<=100000&&x.Health>=0&&x.Health<=x.MaximumHealth&&Enum.IsDefined(typeof(EnemyPhase),x.Phase)&&
            x.PhaseRevision>0&&Finite(x.ObservedAt)&&Finite(x.PhaseStartedAt)&&x.PhaseStartedAt<=x.ObservedAt&&
            (x.Health==0)==(x.Phase==EnemyPhase.Defeated)&&(x.BossKey.IsValid?x.BossKey.RunId==x.RunId&&x.BossKey.InstanceId==x.InstanceId&&x.BossKey.ContractBossId==x.EnemyId:x.BossKey.InstanceId==0&&string.IsNullOrEmpty(x.BossKey.RunId)&&string.IsNullOrEmpty(x.BossKey.ContractBossId))&&
            Finite(x.ProjectilePosition);
        private static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        private static bool Finite(Vector3 p)=>Finite(p.x)&&Finite(p.y)&&Finite(p.z);
        private void LateUpdate(){if(initialized&&View!=null)View.Present(Snapshot);}
        public bool TryApplyItemHit(in ItemHitContext hit)
        {
            if(!HasAuthority||!IsAlive||Frozen||!isActiveAndEnabled||simulation==null||!simulation.RegisteredActor(this)||!simulation.CanApplyAt(hit.AuthorityTime)||
                hit.RunId!=RunId||hit.Item==null||hit.Item.RunId!=RunId||!hit.Item.Key.Equals(hit.Key)||
                hit.Item.State!=SuckableState.InFlight||hit.Item.ActiveShotId!=hit.ShotId||hit.ShotId==0||hit.Item.ShotOwner!=hit.OwnerId||
                hit.Item.CargoRole!=CargoRole.OrdinaryLoot||hit.Item.WorldFrozen||hit.Damage<1||hit.Damage>ItemFireRules.MaximumDamage||
                hit.RelativeSpeed<3||!Finite(hit.RelativeSpeed)||!Finite(hit.Point)||!simulation.RegisteredItem(hit.Item))return false;
            if(BossKey.IsValid&&!simulation.CanDefeat(BossKey))return false;
            // Consume damage provenance once; preserve the exact physical item and its value for reuse.
            if(!hit.Item.TryTransition(SuckableState.InFlight,SuckableState.Available))return false;
            hit.Item.DirectedIntakeId=0;
            int damage=BossKey.IsValid?CampaignBalance.BossHitDamage(hit.Damage,Phase==EnemyPhase.Recover):hit.Damage;
            Health=Math.Max(0,Health-damage);observedAt=hit.AuthorityTime;lastAttacker=hit.OwnerId;aggroUntil=hit.AuthorityTime+5;
            if(Health==0)
            {
                StopJump();
                chapterAttack.Cancel();SetPhase(EnemyPhase.Defeated,hit.AuthorityTime);shape.enabled=false;projectileActive=false;
                if(BossKey.IsValid)simulation.RecordBossDefeat(this,hit.AuthorityTime);
            }
            else if(!BossKey.IsValid&&(Phase==EnemyPhase.Idle||Phase==EnemyPhase.Move))SetPhase(EnemyPhase.Hit,hit.AuthorityTime);
            Audio.CommittedAudioEvents.Publish(new Audio.CommittedAudioFact(RunId,Audio.CommittedAudioKind.EnemyHit,
                hit.ShotId,hit.Key.InstanceId,InstanceId,hit.OwnerId,0,false,hit.AuthorityTime,0,damage,hit.Point));
            if(Health==0)Audio.CommittedAudioEvents.Publish(new Audio.CommittedAudioFact(RunId,Audio.CommittedAudioKind.EnemyDefeat,
                hit.ShotId,hit.Key.InstanceId,InstanceId,hit.OwnerId,0,false,hit.AuthorityTime,0,0,hit.Point));
            return true;
        }
        internal void Step(double now,float dt)
        {
            if(!HasAuthority||!initialized||Frozen||Retired||!isActiveAndEnabled||!simulation.CanApplyAt(now))return;
            observedAt=now;movementRotation=body.rotation;simulationDelta=dt;BeginMovementPlan(now);
            if(IsAlive)EnemyCargoPush.Push(shape,body,Phase==EnemyPhase.Tell||Phase==EnemyPhase.Attack?attackDirection:transform.forward,dt,RunId,BossKey.IsValid);
            if(Phase==EnemyPhase.Defeated)
            {if(now-phaseAt>=View.DefeatDuration&&simulation.CompleteDefeat(this))Retired=true;return;}
            StepProjectile(now,dt);
            if(!simulation.CanApplyAt(now))return;
            if(!simulation.InsideWorld(body.position))
            {ReturnHome(now);return;}
            if(!jumping&&StepOffRaisedCargo(now))return;
            if(Phase==EnemyPhase.Tell)
            {
                if(!Face(attackDirection,dt)){FinishAttack(now,true);return;}
                if(now-phaseAt>=(sweep?rules.SweepTellSeconds:rules.TellSeconds)&&
                    Quaternion.Angle(movementRotation,Quaternion.LookRotation(attackDirection))<1)
                {
                    if(!sweep&&!ProjectileAttack(AttackRevision)&&!LeapLaneClear(attackDirection,
                        simulation.Players.TryGetValue(targetId,out var lockedTarget)&&lockedTarget!=null?Horizontal(body.position,lockedTarget.transform.position):3))
                    {FinishAttack(now,true);nextPathAt=0;return;}
                    SetPhase(EnemyPhase.Attack,now);
                    if(BossKey.IsValid&&bossAttack==1||!BossKey.IsValid&&rules.AttackKind==EnemyAttackKind.Spit&&AttackRevision%2==1)EmitProjectile(now);
                    else if(!sweep)BeginJump(now);
                }
                return;
            }
            if(Phase==EnemyPhase.Attack)
            {
                if(jumping)
                {
                    Melee(now,BossKey.IsValid?1.65f:1.15f,180);
                    bool landed=now-phaseAt>.18&&body.linearVelocity.y<=0&&Physics.Raycast(body.position+Vector3.up*.12f,Vector3.down,.24f,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore);
                    if(landed||now>=jumpUntil){StopJump();FinishAttack(now,false);}return;
                }
                if(sweep)
                {
                    // Expanding ground pulse; one contact per player, walls still block damage.
                    float radius=Mathf.Lerp(.6f,Enraged?6.5f:5f,Mathf.Clamp01((float)((now-phaseAt)/rules.SweepAttackSeconds)));
                    Melee(now,radius,360);
                    if(now-phaseAt>=rules.SweepAttackSeconds)FinishAttack(now,false);return;
                }
                if(BossKey.IsValid&&bossAttack==1)
                {if(now-phaseAt>=(Enraged?.45:.65))FinishAttack(now,false);return;}
                if(now-phaseAt>=(sweep?rules.SweepAttackSeconds:rules.AttackSeconds)){FinishAttack(now,false);return;}
                if(chapterAttack.IsLunge)
                {
                    float step=BossChapterAttackSequence.LungeStep(now-phaseAt,dt,rules.AttackSeconds);
                    if(step>0)
                    {
                        if(!chapterAttack.LungeStopped&&!MoveChecked(attackDirection*BossChapterAttackSequence.LungeSpeed*step,true))
                            chapterAttack.StopLunge(); // Keep the full authored landing after a blocked hop.
                        Melee(now,BossChapterAttackSequence.LungeRange,BossChapterAttackSequence.LungeAngle);
                    }
                }
                else if(sweep||rules.AttackKind==EnemyAttackKind.Melee)Melee(now,sweep?rules.SweepRange:rules.AttackRange,sweep?160:90);
                else if(rules.AttackKind==EnemyAttackKind.Charge)
                {bool moved=MoveChecked(attackDirection*rules.ChargeSpeed*dt,chapterAttack.Active);Melee(now,rules.AttackRange,100);if(!moved)FinishAttack(now,true);}
                return;
            }
            if(Phase==EnemyPhase.Recover||Phase==EnemyPhase.Hit)
            {
                double wait=Phase==EnemyPhase.Hit?.3:chapterAttack.PendingFollowUp?
                    BossChapterAttackSequence.RedirectPauseSeconds:rules.RecoverySeconds*(Enraged?.72f:1f);
                if(now-phaseAt<wait)return;
                if(Phase==EnemyPhase.Recover&&chapterAttack.TryBeginFollowUp())
                {
                    var follow=SelectTarget();
                    if(follow!=null&&Horizontal(body.position,follow.transform.position)<=Math.Max(3,rules.AttackRange)&&LineClear(follow)&&
                        EnemyTurnClearance.CanTurn(shape,body,follow.transform.position-body.position,RunId,480*dt))
                    {BeginAttack(follow,now,true);return;}
                    chapterAttack.Cancel();
                }
                SetPhase(EnemyPhase.Idle,now);return;
            }
            var target=SelectTarget();
            if(target==null)
            {
                if(Horizontal(body.position,Home)>.15f){SetPhase(EnemyPhase.Move,now);Follow(Home,now,dt);}
                else SetPhase(EnemyPhase.Idle,now);
                return;
            }
            float distance=Horizontal(body.position,target.transform.position);
            uint nextAttack=unchecked(AttackRevision+1);if(nextAttack==0)nextAttack=1;
            bool ranged=ProjectileAttack(nextAttack),wave=BossKey.IsValid&&nextAttack%3==0;
            float beginRange=ranged?(BossKey.IsValid?16:12):wave?(Enraged?6:4.6f):BossKey.IsValid?7:6;
            Vector3 toTarget=target.transform.position-body.position;toTarget.y=0;
            bool visible=LineClear(target),lane=ranged?ProjectileLaneClear(target):wave||LeapLaneClear(toTarget.normalized,distance);
            // A target against a wall may have no safe landing for the boss's
            // next leap. Select its next ground pulse, keeping the published
            // revision/pattern consistent with the full visible warning.
            if(BossKey.IsValid&&!ranged&&!wave&&!lane&&visible&&distance<(Enraged?6:4.6f))
            {nextAttack=unchecked(nextAttack+1);if(nextAttack==0)nextAttack=3;wave=true;lane=true;beginRange=Enraged?6:4.6f;}
            bool roomToTurn=EnemyTurnClearance.CanTurn(shape,body,toTarget,RunId,480*dt);
            if(distance<=beginRange&&visible&&lane&&roomToTurn)
            {
                AttackRevision=nextAttack-1;
                BeginAttack(target,now,false);
            }
            else if(visible&&distance<4&&!roomToTurn)
            {
                // Create room to turn instead of repeatedly pressing the same
                // corner with a facing pose that can never fit there.
                SetPhase(EnemyPhase.Move,now);var retreat=-toTarget.normalized;
                if(!MoveChecked(retreat*rules.MoveSpeed*dt))
                {var side=Vector3.Cross(Vector3.up,retreat);if(!MoveChecked(side*rules.MoveSpeed*dt))MoveChecked(-side*rules.MoveSpeed*dt);}
                nextPathAt=0;
            }
            else {SetPhase(EnemyPhase.Move,now);Follow(PursuitPoint(target,distance),now,dt);}
        }
        private void BeginAttack(PlayerMotor target,double now,bool followUp)
        {
            unchecked{AttackRevision++;}if(AttackRevision==0)AttackRevision=1;
            if(!followUp)chapterAttack.BeginNormal(rules.ChapterIIPattern,simulation.AdvancedEncounter,BossKey.IsValid,AttackRevision);
            bossAttack=BossKey.IsValid?(int)(AttackRevision%3):0;
            sweep=BossKey.IsValid&&bossAttack==0;hitPlayers.Clear();projectileEmitted=false;
            // Lock the new direction BEFORE the full existing Tell; no homing during either charge.
            lockedAim=PredictTarget(target,ProjectileAttack(AttackRevision)?Mathf.Min(.7f,Horizontal(body.position,target.transform.position)/rules.ProjectileSpeed):.30f)+Vector3.up;
            attackDirection=lockedAim-body.position;attackDirection.y=0;
            attackDirection=attackDirection.sqrMagnitude>.0001f?attackDirection.normalized:transform.forward;
            SetPhase(EnemyPhase.Tell,now);
        }
        private void FinishAttack(double now,bool blocked)
        {chapterAttack.Complete(blocked);SetPhase(EnemyPhase.Recover,now);}
        private void BeginJump(double now)
        {
            float flightTime=2*rules.LeapHeightVelocity/Mathf.Max(1,Mathf.Abs(Physics.gravity.y));
            jumping=true;jumpUntil=now+Mathf.Max(1.25f,flightTime+.4f);body.isKinematic=false;body.useGravity=true;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            body.constraints=RigidbodyConstraints.FreezeRotation;
            float speed=rules.LeapSpeed*(Enraged?1.25f:1);
            // Match the locked landing point to the real ballistic flight. A fixed
            // .65 s divisor overshoots close targets and strands bosses on fences.
            speed=Mathf.Min(speed,Horizontal(body.position,lockedAim)/flightTime);
            body.linearVelocity=attackDirection*speed+Vector3.up*rules.LeapHeightVelocity;
        }
        private void StopJump()
        {
            if(body==null)return;
            if(!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
            body.isKinematic=true;body.useGravity=false;body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;jumping=false;steppingDown=false;
            // Landing never snaps sideways to a nearby polygon through a door jamb.
            if(HasAuthority&&initialized&&TryGroundPose(body.position,body.rotation,.8f,out var landing)&&
                WorldPoseClear(landing,body.rotation)&&CargoPoseClear(landing,body.rotation)&&EnemyPoseClear(landing,body.rotation))body.position=landing;
        }
        private bool StepOffRaisedCargo(double now)
        {
            bool floorFound=Physics.Raycast(body.position+Vector3.up*.12f,Vector3.down,out var floor,3,
                LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)&&floor.normal.y>.6f;
            bool aboveNavigation=NavMesh.SamplePosition(body.position,out var navigation,2f,NavMesh.AllAreas)&&body.position.y-navigation.position.y>.5f;
            bool aboveFloor=floorFound&&body.position.y-floor.point.y>.5f||aboveNavigation;
            if(!steppingDown&&!aboveFloor)return false;
            if(steppingDown&&!aboveNavigation&&floorFound&&body.position.y-floor.point.y<.18f&&body.linearVelocity.y<=0)
            {StopJump();nextPathAt=0;SetPhase(EnemyPhase.Recover,now);return true;}
            if(!steppingDown)
            {
                steppingDown=true;chapterAttack.Cancel();SetPhase(EnemyPhase.Recover,now);
                body.isKinematic=false;body.useGravity=true;body.constraints=RigidbodyConstraints.FreezeRotation;
                body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            }
            // After landing on furniture, the floor path lies too far below for a
            // walking step. PhysX carries the body off the edge and down; walls
            // and cargo stay solid throughout, with no snap through the obstacle.
            var target=SelectTarget();Vector3 toward=(target!=null?target.transform.position:Home)-body.position;toward.y=0;
            // A leap can also land on the player's capsule. Moving toward that
            // capsule keeps the enemy balanced on it forever; step away to fall.
            if(target!=null&&toward.sqrMagnitude<4)toward=toward.sqrMagnitude>.01f?-toward:transform.right;
            // A player standing beside a fixed counter can block a straight drop
            // toward them. Pick a reachable edge of the supporting item first.
            if(Physics.Raycast(body.position+Vector3.up*.2f,Vector3.down,out var support,.65f,LayerMask.GetMask("Items"),QueryTriggerInteraction.Ignore)&&support.rigidbody!=null)
            {
                var item=support.rigidbody.GetComponent<SuckableObject>();
                if(item!=null&&item.GameplayColliders.Length>0)
                {
                    var bounds=item.GameplayColliders[0].bounds;foreach(var c in item.GameplayColliders)bounds.Encapsulate(c.bounds);
                    float margin=Mathf.Max(shape.size.x,shape.size.z)*.7f+.15f,best=float.PositiveInfinity;
                    var exits=new[]{new Vector3(bounds.min.x-margin,body.position.y,body.position.z),new Vector3(bounds.max.x+margin,body.position.y,body.position.z),
                        new Vector3(body.position.x,body.position.y,bounds.min.z-margin),new Vector3(body.position.x,body.position.y,bounds.max.z+margin)};
                    foreach(var exit in exits)
                    {
                        Vector3 move=exit-body.position;float length=move.magnitude;if(length>=best)continue;
                        if(!Physics.Raycast(exit+Vector3.up*.2f,Vector3.down,4,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore))continue;
                        bool blocked=Physics.BoxCastAll(body.position+body.rotation*shape.center,shape.size*.5f-Vector3.one*.02f,move.normalized,body.rotation,length,
                            LayerMask.GetMask("World","Items","Player","Enemies"),QueryTriggerInteraction.Ignore)
                            .Any(h=>h.collider!=shape&&h.collider.bounds.max.y>body.position.y+.15f&&h.normal.y<.6f);
                        if(blocked)continue;best=length;toward=move;
                    }
                }
            }
            Vector3 velocity=toward.sqrMagnitude>.01f?toward.normalized*Mathf.Min(3,rules.MoveSpeed):Vector3.zero;
            velocity.y=body.linearVelocity.y;body.linearVelocity=velocity;
            return true;
        }
        private void CancelChapterAttack()
        {
            bool active=chapterAttack.Active;
            chapterAttack.Cancel();
            if(active&&initialized&&HasAuthority&&(Phase==EnemyPhase.Tell||Phase==EnemyPhase.Attack))
                SetPhase(EnemyPhase.Recover,observedAt);
        }
        private void OnDisable(){CancelChapterAttack();if(HasAuthority)StopJump();}
        private PlayerMotor SelectTarget()
        {
            PlayerMotor selected=null;float closest=float.PositiveInfinity;
            foreach(var pair in simulation.Players)
            {
                var player=pair.Value;
                if(player==null||!player.isActiveAndEnabled||simulation.RequiresRecovery(pair.Key))continue;
                float distance=Horizontal(body.position,player.transform.position);
                if(pair.Key==targetId)distance*=.8f;
                if(pair.Key==lastAttacker&&observedAt<aggroUntil)distance*=.65f;
                if(!LineClear(player))distance+=4;
                if(distance>=closest)continue;
                selected=player;closest=distance;
            }
            targetId=selected!=null?selected.PlayerId:0;return selected;
        }
        private bool LineClear(PlayerMotor player)=>!Physics.Linecast(AimTarget.position,player.transform.position+Vector3.up*1,
            LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore);
        private Vector3 PredictTarget(PlayerMotor player,float seconds)
        {
            var move=player.LastIntent.Move;
            Vector3 direction=Quaternion.Euler(0,player.LastIntent.Yaw,0)*new Vector3(move.x,0,move.y);
            Vector3 predicted=player.transform.position+Vector3.ClampMagnitude(direction,1)*Mathf.Min(player.PlanarSpeed,player.Settings.SprintSpeed)*seconds;
            return Physics.Linecast(player.transform.position+Vector3.up,predicted+Vector3.up,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)?player.transform.position:predicted;
        }
        private Vector3 PursuitPoint(PlayerMotor player,float distance)
        {
            Vector3 point=PredictTarget(player,.35f);
            if(!BossKey.IsValid&&distance>3&&distance<10&&InstanceId%3!=0)
            {
                Vector3 side=Vector3.Cross(Vector3.up,(point-body.position).normalized)*(InstanceId%2==0?1:-1)*1.2f;
                if(NavMesh.SamplePosition(point+side,out var flank,.5f,NavMesh.AllAreas)&&
                    !Physics.Linecast(point+Vector3.up,flank.position+Vector3.up,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore))point=flank.position;
            }
            return point;
        }
        private bool ProjectileAttack(uint revision)=>BossKey.IsValid?revision%3==1:rules.AttackKind==EnemyAttackKind.Spit&&revision%2==1;
        private bool ProjectileLaneClear(PlayerMotor target)
        {
            Vector3 delta=target.transform.position+Vector3.up-AttackOrigin.position;
            return !Physics.SphereCast(AttackOrigin.position,BossKey.IsValid?.3f:.17f,delta.normalized,out _,delta.magnitude,
                LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore);
        }
        private bool LeapLaneClear(Vector3 direction,float distance)
        {
            if(direction.sqrMagnitude<.5f)return true;
            var rotation=Quaternion.LookRotation(direction);
            // Check the actual body width, including its arc above the floor. A ray
            // grazing a wall is no longer sufficient permission to start a leap.
            var extent=Vector3.Max(shape.size*.5f-new Vector3(.015f,.10f,.015f),Vector3.one*.025f);
            float travel=Mathf.Max(0,distance-.45f);
            for(int i=0;i<3;i++)
            {
                var center=body.position+rotation*shape.center+Vector3.up*(.12f+i*.3f);
                if(Physics.CheckBox(center,extent,rotation,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore))return false;
                foreach(var hit in Physics.BoxCastAll(center,extent,direction,rotation,travel,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore))
                    if(hit.normal.y<.6f)return false;
            }
            return true;
        }
        private void Melee(double now,float range,float angle)
        {
            foreach(var pair in simulation.Players)
            {
                var p=pair.Value;if(p==null||hitPlayers.Contains(pair.Key)||simulation.RequiresRecovery(pair.Key))continue;
                Vector3 delta=p.transform.position-body.position;delta.y=0;
                if(sweep&&p.transform.position.y-body.position.y>.65f)continue; // Jump over the ground wave.
                if(delta.magnitude>range+p.Settings.CapsuleRadius||delta.sqrMagnitude>.0001f&&Vector3.Dot(attackDirection,delta.normalized)<Mathf.Cos(angle*.5f*Mathf.Deg2Rad)||!LineClear(p))continue;
                // An invulnerable contact still consumes this attack's one contact for this player.
                hitPlayers.Add(pair.Key);simulation.TryDamagePlayer(this,p,now);
                if(!simulation.CanApplyAt(now))return;
            }
        }
        private void EmitProjectile(double now)
        {
            if(projectileEmitted)return;projectileEmitted=true;projectileActive=true;projectilePosition=AttackOrigin.position;
            projectileDirection=attackDirection;
            projectileDirection=(lockedAim-projectilePosition).normalized;
            projectileExpires=now+rules.ProjectileLifetime;
        }
        private void StepProjectile(double now,float dt)
        {
            if(!projectileActive)return;if(now>=projectileExpires){projectileActive=false;return;}
            Vector3 delta=projectileDirection*Mathf.Max(rules.ProjectileSpeed,BossKey.IsValid?(Enraged?13:10):7)*dt;
            var hits=Physics.SphereCastAll(projectilePosition,BossKey.IsValid?.3f:.17f,projectileDirection,delta.magnitude,LayerMask.GetMask("World","Player"),QueryTriggerInteraction.Ignore);
            Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            if(hits.Length>0)
            {
                projectilePosition=hits[0].point;projectileActive=false;var p=hits[0].collider.GetComponentInParent<PlayerMotor>();
                if(p!=null)simulation.TryDamagePlayer(this,p,now,true);
            }
            else projectilePosition+=delta;
        }
        private void Follow(Vector3 destination,double now,float dt)
        {
            BeginMovementPlan(now);
            simulationDelta=dt;
            if(now>=nextPathAt||(destination-navDestination).sqrMagnitude>1)
            {
                nextPathAt=now+.18;navDestination=destination;corner=1;corners=Array.Empty<Vector3>();
                if(NavMesh.SamplePosition(destination,out var end,2f,NavMesh.AllAreas)&&NavMesh.SamplePosition(body.position,out var start,1.5f,NavMesh.AllAreas)&&NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path))
                {
                    corners=path.corners;
                    // A physical leap can land outside the clearance baked around a
                    // tree. Walk back to the path entrance before taking its next bend.
                    if(Horizontal(body.position,start.position)>.025f)corner=0;
                }
            }
            // A nearby bend is not reached yet: the remaining centimetres can be
            // the clearance around a wall. Never discard it on proximity alone.
            while(corner<corners.Length&&Horizontal(body.position,corners[corner])<.01f)corner++;
            if(corner>0&&corner+1<corners.Length&&Horizontal(body.position,corners[corner])<shape.size.magnitude*.5f+.2f)
            {
                if(WalkingShortcutClear(corners[corner+1]))corner++;
            }
            if(corner>=corners.Length)return;
            Vector3 delta=corners[corner]-body.position;delta.y=0;if(delta.sqrMagnitude<.0001f)return;
            if(YieldForTraffic(delta,now,dt))return;
            Face(delta.normalized,dt);
            bool moved=MoveChecked(Vector3.ClampMagnitude(delta,rules.MoveSpeed*(Enraged?1.16f:1)*dt));
            if(blockedEnemy!=null)return; // The next traffic step resolves peers, not a wall-slide ping-pong.
            if(!moved&&blockedWorldNormal.sqrMagnitude>.1f)
            {
                var slide=Vector3.ProjectOnPlane(delta.normalized,blockedWorldNormal);slide.y=0;
                if(slide.sqrMagnitude<.03f)
                {slide=Vector3.Cross(Vector3.up,blockedWorldNormal);if(Vector3.Dot(slide,destination-body.position)<0)slide=-slide;}
                if(!MoveChecked(slide.normalized*rules.MoveSpeed*dt))MoveChecked(-slide.normalized*rules.MoveSpeed*dt);
                nextPathAt=Math.Min(nextPathAt,now+.1);return;
            }
            if(blockedCargo==null){bypassCargo=null;return;}
            if(moved)return; // Never add a second step after even a partially accepted move.
            var obstacle=blockedCargo;var tangent=Vector3.Cross(Vector3.up,blockedCargoNormal).normalized;
            if(tangent.sqrMagnitude<.5f)return;
            if(bypassCargo!=obstacle.attachedRigidbody){bypassCargo=obstacle.attachedRigidbody;bypassSign=Vector3.Dot(tangent,delta)<0?-1:1;}
            // Local wall-slide only while ordinary Follow is blocked. No charge/lunge steering.
            if(!MoveChecked(tangent*(bypassSign*rules.MoveSpeed*dt)))
                if(MoveChecked(tangent*(-bypassSign*rules.MoveSpeed*dt)))bypassSign=-bypassSign;
        }
        private bool WalkingShortcutClear(Vector3 destination)
        {
            if(!NavMesh.SamplePosition(body.position,out var start,1.5f,NavMesh.AllAreas)||
                Horizontal(body.position,start.position)>.025f||NavMesh.Raycast(start.position,destination,out _,NavMesh.AllAreas))return false;
            Vector3 ahead=destination-body.position;ahead.y=0;
            return LeapLaneClear(ahead.normalized,ahead.magnitude);
        }
        private bool MoveChecked(Vector3 delta,bool stopWhenClipped=false)
        {
            blockedCargo=null;blockedEnemy=null;blockedCargoNormal=Vector3.zero;blockedWorldNormal=Vector3.zero;
            if(delta.sqrMagnitude<1e-8f)return true;
            EnemyCargoPush.Push(shape,body,delta,simulationDelta,RunId,BossKey.IsValid);
            if(!TryNavigationStep(delta,out delta))return false;
            // A shallow floor lip is a step, not a wall. The nav surface supplies the landing height.
            Vector3 center=body.position+movementRotation*shape.center+Vector3.up*.12f;
            Vector3 extent=Vector3.Max(Vector3.one*.01f,shape.size*.5f-new Vector3(.025f,.14f,.025f));
            float length=delta.magnitude;float allowed=length;
            foreach(var hit in Physics.BoxCastAll(center,extent,delta/length,movementRotation,length+.025f,
                LayerMask.GetMask("World","Player","Enemies"),QueryTriggerInteraction.Ignore))
            {
                if(hit.collider==shape||hit.collider.transform.IsChildOf(transform)||hit.normal.y>.6f)continue;
                // PhysX reports zero-distance sweeps for a body already touching
                // the query volume, including a peer behind us. Allow separation;
                // the full final pose below still rejects entering that peer.
                if(hit.distance<=.001f&&hit.collider.gameObject.layer==LayerMask.NameToLayer("Enemies")&&
                    !Physics.ComputePenetration(shape,body.position+delta,movementRotation,hit.collider,
                        hit.collider.transform.position,hit.collider.transform.rotation,out _,out _))continue;
                float clearance=Math.Max(0,hit.distance-.025f);
                if(clearance<allowed){allowed=clearance;blockedWorldNormal=hit.normal;blockedEnemy=hit.collider.GetComponentInParent<EnemyActor>();}
            }
            // Use the full solid box for cargo: the legacy .02 shrink must not enter furniture.
            float turnTravel=2*(shape.center.magnitude+shape.size.magnitude*.5f)*Mathf.Sin(Quaternion.Angle(body.rotation,movementRotation)*Mathf.Deg2Rad*.5f);
            foreach(var hit in Physics.BoxCastAll(center,shape.size*.5f+Vector3.one*turnTravel,delta/length,movementRotation,length+.025f,
                LayerMask.GetMask("Items"),QueryTriggerInteraction.Ignore))
            {
                if(!BlocksCargo(hit.collider))continue;
                if(hit.distance<=.001f&&!Physics.ComputePenetration(shape,body.position+delta,movementRotation,hit.collider,
                    hit.collider.transform.position,hit.collider.transform.rotation,out _,out _))continue;
                float clearance=Math.Max(0,hit.distance-.025f);
                if(clearance<allowed){allowed=clearance;blockedCargo=hit.collider;blockedCargoNormal=hit.normal;}
            }
            if(allowed<=.001f)return false;
            Vector3 next=body.position+delta/length*allowed;
            if(!TryGroundPose(next,movementRotation,.30f,out next))
            {blockedWorldNormal=-delta.normalized;return false;}
            if(!WorldPoseClear(next,movementRotation))return false;
            if(!CargoPoseClear(next,movementRotation))return false;
            if(!EnemyPoseClear(next,movementRotation))return false;
            plannedPosition=next;body.MovePosition(next);return !stopWhenClipped||allowed>=length;
        }
        private bool TryNavigationStep(Vector3 requested,out Vector3 delta)
        {
            delta=requested;
            if(!NavMesh.SamplePosition(body.position,out var start,1.5f,NavMesh.AllAreas)||
                !NavMesh.SamplePosition(body.position+delta,out var end,1.5f,NavMesh.AllAreas))return false;
            float startGap=Horizontal(body.position,start.position),endGap=Horizontal(body.position+delta,end.position);
            if(Mathf.Abs(end.position.y-body.position.y)>.5f)return false;
            if(endGap<=.025f)return true;
            if(startGap>.025f)
            {
                // Intermediate positions need not already be on the mesh. Accept
                // only progress toward it; full collision/ground checks still follow.
                return endGap<startGap-.0001f;
            }
            Vector3 correction=end.position-body.position;correction.y=0;
            delta=Vector3.ClampMagnitude(correction,requested.magnitude);
            // Resolve the navigation adjustment BEFORE sweeping, so collision checks
            // cover the direction and distance that are actually applied.
            return delta.sqrMagnitude>1e-8f&&Vector3.Dot(delta,requested)>0;
        }
        private void ReturnHome(double now)
        {
            if(!simulation.TryReturnEnemyHome(this,Home))return;
            chapterAttack.Cancel();targetId=0;corners=Array.Empty<Vector3>();projectileActive=false;SetPhase(EnemyPhase.Idle,now);
        }
        private bool Face(Vector3 direction,float dt)
        {
            direction.y=0;if(direction.sqrMagnitude<=.0001f)return true;
            var next=Quaternion.RotateTowards(body.rotation,Quaternion.LookRotation(direction),480*dt);
            // A correctly oriented body needs no swept rotation clearance.
            if(Quaternion.Angle(body.rotation,next)<=.001f)return true;
            // The attack admission check uses these exact same ground-level rules.
            if(!EnemyTurnClearance.CanStep(shape,body,body.rotation,next,RunId))return false;
            if(!EnemyPoseClear(body.position,next))return false;
            plannedRotation=next;movementRotation=next;body.MoveRotation(next);return true;
        }
        private bool BlocksCargo(Collider c)
        {
            if(c==null||c.isTrigger||c.transform.IsChildOf(transform)||c.attachedRigidbody==null)return false;
            var item=c.attachedRigidbody.GetComponent<SuckableObject>();
            // Wait for the shove to clear a real contact; never walk through cargo.
            // Airborne shots keep their ordinary damage contact.
            return item!=null&&item.RunId==RunId&&item.State==SuckableState.Available&&
                !item.WorldFrozen&&(item.IsMounted||!c.attachedRigidbody.isKinematic);
        }
        private bool CargoPoseClear(Vector3 position,Quaternion rotation)
        {
            foreach(var c in Physics.OverlapBox(position+rotation*shape.center,shape.size*.5f+Vector3.one*.025f,rotation,
                LayerMask.GetMask("Items"),QueryTriggerInteraction.Ignore))
            {
                if(!BlocksCargo(c)||!Physics.ComputePenetration(shape,position,rotation,c,c.transform.position,c.transform.rotation,out var normal,out float depth))continue;
                // Permit an existing overlap to get strictly shallower; never push farther into it.
                bool existing=Physics.ComputePenetration(shape,body.position,body.rotation,c,c.transform.position,c.transform.rotation,out _,out float oldDepth);
                if(existing&&depth<oldDepth)continue;
                blockedCargo=c;blockedCargoNormal=normal;return false;
            }
            return true;
        }
        private bool EnemyPoseClear(Vector3 position,Quaternion rotation)
        {
            // MovePosition is committed by PhysX only after all actors have stepped.
            // Reserve accepted poses so two enemies cannot both choose the same gap.
            foreach(var c in Physics.OverlapBox(position+rotation*shape.center,shape.size*.5f+new Vector3(.5f,.05f,.5f),rotation,LayerMask.GetMask("Enemies"),QueryTriggerInteraction.Ignore))
            {
                if(c==shape||c.transform.IsChildOf(transform))continue;
                var peer=c.GetComponentInParent<EnemyActor>();
                bool planned=peer!=null&&c==peer.shape&&peer.movementPlanAt==movementPlanAt;
                var otherPosition=planned?peer.plannedPosition:c.transform.position;var otherRotation=planned?peer.plannedRotation:c.transform.rotation;
                if(!Physics.ComputePenetration(shape,position,rotation,c,otherPosition,otherRotation,out _,out float depth)||depth<.008f)continue;
                if(Physics.ComputePenetration(shape,body.position,body.rotation,c,otherPosition,otherRotation,out _,out float old)&&depth<old-.001f)continue;
                blockedEnemy=c.GetComponentInParent<EnemyActor>();return false;
            }
            return true;
        }
        private bool YieldForTraffic(Vector3 requested,double now,float dt)
        {
            trafficIntent=requested.normalized;trafficAt=now;
            if(trafficPeer!=null&&(!trafficPeer.IsAlive||trafficPeer.Frozen||now>yieldUntil||
                Horizontal(body.position,trafficPeer.body.position)>6||
                Vector3.Dot(trafficPeer.body.position-body.position,yieldDirection)<-1.5f))trafficPeer=null;
            if(trafficPeer==null)
            {
                EnemyActor peer=null;float nearest=float.PositiveInfinity;
                foreach(var hit in Physics.BoxCastAll(body.position+movementRotation*shape.center,shape.size*.5f+Vector3.one*.06f,
                    trafficIntent,movementRotation,.8f,LayerMask.GetMask("Enemies"),QueryTriggerInteraction.Ignore))
                {
                    var other=hit.collider.GetComponentInParent<EnemyActor>();
                    if(other==null||other==this||!other.IsAlive||other.RunId!=RunId||hit.distance>=nearest)continue;
                    if(Vector3.Dot(other.body.position-body.position,trafficIntent)<-.05f)continue;
                    peer=other;nearest=hit.distance;
                }
                if(peer==null)return false;
                bool peerWalking=now-peer.trafficAt<.6;
                // An idle neighbour beside the path does not own the whole
                // look-ahead corridor. Yield only when it blocks the next step.
                if(!peerWalking&&nearest>rules.MoveSpeed*dt+.025f)return false;
                var otherIntent=peerWalking?peer.trafficIntent:peer.transform.forward;
                // A following enemy queues behind its leader. Opposing traffic
                // uses a stable right of way, so both cannot dodge the same way.
                bool following=peerWalking&&Vector3.Dot(trafficIntent,otherIntent)>.55f;
                if(following&&peer.trafficPeer!=this)return true;
                if(peerWalking&&InstanceId<peer.InstanceId)return false;
                trafficPeer=peer;yieldDirection=trafficIntent;yieldUntil=now+6;hasYieldPocket=false;
            }
            if(hasYieldPocket)
            {
                Vector3 remaining=yieldPocket-body.position;remaining.y=0;
                if(remaining.magnitude>.06f)
                {if(!MoveChecked(Vector3.ClampMagnitude(remaining,rules.MoveSpeed*dt)))hasYieldPocket=false;}
                else if(now-trafficPeer.trafficAt>.6||Horizontal(body.position,trafficPeer.body.position)>3)
                {trafficPeer=null;nextPathAt=0;return false;}
                return true;
            }
            Vector3 right=Vector3.Cross(Vector3.up,yieldDirection);float clearance=(shape.size.x+trafficPeer.shape.size.x)*.5f+.3f;
            // First look for a clear pocket beside the passing lane. Inside a
            // one-body doorway, retreat until such a pocket becomes reachable.
            for(int side=0;side<2;side++)for(int back=0;back<3;back++)
            {
                Vector3 candidate=body.position+right*(side==0?clearance:-clearance)-yieldDirection*(back*.8f);
                if(!NavMesh.SamplePosition(candidate,out var nav,.3f,NavMesh.AllAreas)||Mathf.Abs(nav.position.y-body.position.y)>.5f)continue;
                candidate=nav.position;Vector3 move=candidate-body.position;move.y=0;
                if(!WorldPoseClear(candidate,movementRotation)||!CargoPoseClear(candidate,movementRotation)||!EnemyPoseClear(candidate,movementRotation))continue;
                bool peerAcross=Physics.BoxCastAll(body.position+movementRotation*shape.center,shape.size*.5f+Vector3.one*.025f,
                    move.normalized,movementRotation,move.magnitude,LayerMask.GetMask("Enemies"),QueryTriggerInteraction.Ignore)
                    .Any(h=>h.collider!=shape&&!h.collider.transform.IsChildOf(transform));
                if(peerAcross)continue;
                bool wall=false;
                foreach(var hit in Physics.BoxCastAll(body.position+movementRotation*shape.center+Vector3.up*.12f,
                    Vector3.Max(Vector3.one*.02f,shape.size*.5f-new Vector3(.025f,.14f,.025f)),move.normalized,movementRotation,move.magnitude,LayerMask.GetMask("World","Enemies"),QueryTriggerInteraction.Ignore))
                    if(hit.collider!=shape&&!hit.collider.transform.IsChildOf(transform)&&hit.normal.y<.6f){wall=true;break;}
                if(wall)continue;
                yieldPocket=candidate;hasYieldPocket=true;
                MoveChecked(Vector3.ClampMagnitude(move,rules.MoveSpeed*dt));return true;
            }
            // The priority peer can turn after entering a room. Retreat away
            // from its current body, rather than back into its changed route.
            Vector3 away=body.position-trafficPeer.body.position;away.y=0;
            if(away.sqrMagnitude<.001f)away=-yieldDirection;
            away.Normalize();
            if(!MoveChecked(away*rules.MoveSpeed*dt))
            {
                var tangent=Vector3.Cross(Vector3.up,away);
                if(!MoveChecked(tangent*rules.MoveSpeed*dt))MoveChecked(-tangent*rules.MoveSpeed*dt);
            }
            return true;
        }
        private bool TryGroundPose(Vector3 position,Quaternion rotation,float step,out Vector3 grounded)
        {
            grounded=position;float floor=float.NegativeInfinity;
            Vector3 half=shape.size*.5f;
            // Support the whole footprint, including its outer edges. Sparse inset
            // rays lower a wide body while its heel is still above a porch, or miss
            // the first centimetres of a step and mistake the lip for a wall.
            Vector3 origin=position+rotation*shape.center;origin.y=position.y+step+.12f;
            var footprint=new Vector3(Mathf.Max(.01f,half.x-.002f),.01f,Mathf.Max(.01f,half.z-.002f));
            foreach(var hit in Physics.BoxCastAll(origin,footprint,Vector3.down,rotation,step+1.1f,
                LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore))
                if(hit.normal.y>.6f)floor=Mathf.Max(floor,hit.point.y);
            if(float.IsNegativeInfinity(floor))return false;
            grounded.y=floor-(shape.center.y-half.y)+.035f;
            return grounded.y-position.y<=step+.01f&&position.y-grounded.y<=1;
        }
        private bool WorldPoseClear(Vector3 position,Quaternion rotation)
        {
            foreach(var c in Physics.OverlapBox(position+rotation*shape.center,shape.size*.5f,rotation,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore))
            {
                if(!Physics.ComputePenetration(shape,position,rotation,c,c.transform.position,c.transform.rotation,out var normal,out float depth)||depth<.015f)continue;
                if(Physics.ComputePenetration(shape,body.position,body.rotation,c,c.transform.position,c.transform.rotation,out _,out float previous)&&depth<previous-.001f)continue;
                blockedWorldNormal=normal;return false;
            }
            return true;
        }
        private void SetPhase(EnemyPhase phase,double now)
        {if(Phase==phase)return;Phase=phase;phaseAt=now;unchecked{PhaseRevision++;}if(PhaseRevision==0)PhaseRevision=1;}
        private static float Horizontal(Vector3 a,Vector3 b)=>new Vector2(a.x-b.x,a.z-b.z).magnitude;
        private void OnDestroy(){chapterAttack.Cancel();if(rules!=null)Destroy(rules);}
    }
}
