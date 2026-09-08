using System;
using System.Collections.Generic;
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
        private Vector3 attackDirection,projectilePosition,projectileDirection,navDestination;
        // One-frame checked facing also bounds the following kinematic translation.
        private Quaternion movementRotation;
        private Collider blockedCargo;
        private Vector3 blockedWorldNormal;
        private Vector3 blockedCargoNormal;
        private Rigidbody bypassCargo;
        private float bypassSign;
        private NavMeshPath path;
        private Vector3[] corners=Array.Empty<Vector3>();
        private int corner,targetId;
        private readonly HashSet<int> hitPlayers=new HashSet<int>();
        private EnemyDefinition rules;
        private readonly BossChapterAttackSequence chapterAttack = new BossChapterAttackSequence();
        private bool jumping;
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
            x.MaximumHealth>0&&x.MaximumHealth<=20000&&x.Health>=0&&x.Health<=x.MaximumHealth&&Enum.IsDefined(typeof(EnemyPhase),x.Phase)&&
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
            Health=Math.Max(0,Health-hit.Damage);observedAt=hit.AuthorityTime;
            if(Health==0)
            {
                StopJump();
                chapterAttack.Cancel();SetPhase(EnemyPhase.Defeated,hit.AuthorityTime);shape.enabled=false;projectileActive=false;
                if(BossKey.IsValid)simulation.RecordBossDefeat(this,hit.AuthorityTime);
            }
            else if(!BossKey.IsValid&&(Phase==EnemyPhase.Idle||Phase==EnemyPhase.Move))SetPhase(EnemyPhase.Hit,hit.AuthorityTime);
            Audio.CommittedAudioEvents.Publish(new Audio.CommittedAudioFact(RunId,Audio.CommittedAudioKind.EnemyHit,
                hit.ShotId,hit.Key.InstanceId,InstanceId,hit.OwnerId,0,false,hit.AuthorityTime,0,hit.Damage,hit.Point));
            if(Health==0)Audio.CommittedAudioEvents.Publish(new Audio.CommittedAudioFact(RunId,Audio.CommittedAudioKind.EnemyDefeat,
                hit.ShotId,hit.Key.InstanceId,InstanceId,hit.OwnerId,0,false,hit.AuthorityTime,0,0,hit.Point));
            return true;
        }
        internal void Step(double now,float dt)
        {
            if(!HasAuthority||!initialized||Frozen||Retired||!isActiveAndEnabled||!simulation.CanApplyAt(now))return;
            observedAt=now;movementRotation=body.rotation;
            if(Phase==EnemyPhase.Defeated)
            {if(now-phaseAt>=View.DefeatDuration&&simulation.CompleteDefeat(this))Retired=true;return;}
            StepProjectile(now,dt);
            if(!simulation.CanApplyAt(now))return;
            if(!simulation.InsideWorld(body.position))
            {ReturnHome(now);return;}
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
                    BossChapterAttackSequence.RedirectPauseSeconds:Mathf.Min(rules.RecoverySeconds,Enraged?.45f:BossKey.IsValid?.8f:.6f);
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
            float beginRange=BossKey.IsValid?16:rules.AttackKind==EnemyAttackKind.Spit?Mathf.Max(8,rules.AttackRange):5.5f;
            uint nextAttack=unchecked(AttackRevision+1);if(nextAttack==0)nextAttack=1;
            bool ranged=ProjectileAttack(nextAttack),wave=BossKey.IsValid&&nextAttack%3==0;
            Vector3 toTarget=target.transform.position-body.position;toTarget.y=0;
            if(distance<=beginRange&&LineClear(target)&&(ranged?ProjectileLaneClear(target):wave||LeapLaneClear(toTarget.normalized,distance))&&
                EnemyTurnClearance.CanTurn(shape,body,toTarget,RunId,480*dt))
            {
                BeginAttack(target,now,false);
            }
            else {SetPhase(EnemyPhase.Move,now);Follow(target.transform.position,now,dt);}
        }
        private void BeginAttack(PlayerMotor target,double now,bool followUp)
        {
            unchecked{AttackRevision++;}if(AttackRevision==0)AttackRevision=1;
            if(!followUp)chapterAttack.BeginNormal(rules.ChapterIIPattern,simulation.AdvancedEncounter,BossKey.IsValid,AttackRevision);
            bossAttack=BossKey.IsValid?(int)(AttackRevision%3):0;
            sweep=BossKey.IsValid&&bossAttack==0;hitPlayers.Clear();projectileEmitted=false;
            // Lock the new direction BEFORE the full existing Tell; no homing during either charge.
            attackDirection=target.transform.position-body.position;attackDirection.y=0;
            attackDirection=attackDirection.sqrMagnitude>.0001f?attackDirection.normalized:transform.forward;
            SetPhase(EnemyPhase.Tell,now);
        }
        private void FinishAttack(double now,bool blocked)
        {chapterAttack.Complete(blocked);SetPhase(EnemyPhase.Recover,now);}
        private void BeginJump(double now)
        {
            jumping=true;jumpUntil=now+1.25;body.isKinematic=false;body.useGravity=true;
            body.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            body.constraints=RigidbodyConstraints.FreezeRotation;
            float speed=rules.LeapSpeed*(Enraged?1.25f:1);
            if(simulation.Players.TryGetValue(targetId,out var target)&&target!=null)speed=Mathf.Min(speed,Mathf.Max(3,Horizontal(body.position,target.transform.position)/.65f));
            body.linearVelocity=attackDirection*speed+Vector3.up*rules.LeapHeightVelocity;
        }
        private void StopJump()
        {
            if(body==null)return;
            if(!body.isKinematic){body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}
            body.isKinematic=true;body.useGravity=false;body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;jumping=false;
            // Landing never snaps sideways to a nearby polygon through a door jamb.
            if(HasAuthority&&initialized&&TryGroundPose(body.position,body.rotation,.8f,out var landing)&&WorldPoseClear(landing,body.rotation))body.position=landing;
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
                if(distance>=closest)continue;
                selected=player;closest=distance;
            }
            targetId=selected!=null?selected.PlayerId:0;return selected;
        }
        private bool LineClear(PlayerMotor player)=>!Physics.Linecast(AimTarget.position,player.transform.position+Vector3.up*1,
            LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore);
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
            if(simulation.Players.TryGetValue(targetId,out var p)&&p!=null)projectileDirection=(p.transform.position+Vector3.up- projectilePosition).normalized;
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
            if(now>=nextPathAt||(destination-navDestination).sqrMagnitude>1)
            {
                nextPathAt=now+.3;navDestination=destination;corner=1;corners=Array.Empty<Vector3>();
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
            Face(delta.normalized,dt);
            bool moved=MoveChecked(Vector3.ClampMagnitude(delta,rules.MoveSpeed*dt));
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
            blockedCargo=null;blockedCargoNormal=Vector3.zero;blockedWorldNormal=Vector3.zero;
            if(delta.sqrMagnitude<1e-8f)return true;
            if(!TryNavigationStep(delta,out delta))return false;
            // A shallow floor lip is a step, not a wall. The nav surface supplies the landing height.
            Vector3 center=body.position+movementRotation*shape.center+Vector3.up*.12f;
            Vector3 extent=Vector3.Max(Vector3.one*.01f,shape.size*.5f-new Vector3(.025f,.14f,.025f));
            float length=delta.magnitude;float allowed=length;
            foreach(var hit in Physics.BoxCastAll(center,extent,delta/length,movementRotation,length+.025f,
                LayerMask.GetMask("World","Player","Enemies"),QueryTriggerInteraction.Ignore))
            {
                if(hit.collider==shape||hit.collider.transform.IsChildOf(transform)||hit.normal.y>.6f)continue;
                float clearance=Math.Max(0,hit.distance-.025f);
                if(clearance<allowed){allowed=clearance;blockedWorldNormal=hit.normal;}
            }
            // Use the full solid box for cargo: the legacy .02 shrink must not enter furniture.
            float turnTravel=2*(shape.center.magnitude+shape.size.magnitude*.5f)*Mathf.Sin(Quaternion.Angle(body.rotation,movementRotation)*Mathf.Deg2Rad*.5f);
            foreach(var hit in Physics.BoxCastAll(center,shape.size*.5f+Vector3.one*turnTravel,delta/length,movementRotation,length+.025f,
                LayerMask.GetMask("Items"),QueryTriggerInteraction.Ignore))
            {
                if(!BlocksCargo(hit.collider))continue;
                float clearance=Math.Max(0,hit.distance-.025f);
                if(clearance<allowed){allowed=clearance;blockedCargo=hit.collider;blockedCargoNormal=hit.normal;}
            }
            if(allowed<=.001f)return false;
            Vector3 next=body.position+delta/length*allowed;
            if(!TryGroundPose(next,movementRotation,.30f,out next)||!WorldPoseClear(next,movementRotation))return false;
            if(!CargoPoseClear(next,movementRotation))return false;
            body.MovePosition(next);return !stopWhenClipped||allowed>=length;
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
            movementRotation=next;body.MoveRotation(next);return true;
        }
        private bool BlocksCargo(Collider c)
        {
            if(c==null||c.isTrigger||c.transform.IsChildOf(transform)||c.attachedRigidbody==null)return false;
            var item=c.attachedRigidbody.GetComponent<SuckableObject>();
            // Existing masses, no new kg threshold. Airborne shots keep their real damage contact.
            return item!=null&&item.RunId==RunId&&item.State==SuckableState.Available&&
                c.attachedRigidbody.mass>body.mass;
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
