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
        private NavMeshPath path;
        private Vector3[] corners=Array.Empty<Vector3>();
        private int corner,targetId;
        private readonly HashSet<int> hitPlayers=new HashSet<int>();
        private EnemyDefinition rules;
        private readonly BossChapterAttackSequence chapterAttack = new BossChapterAttackSequence();
        private void Awake(){path=new NavMeshPath();body=GetComponent<Rigidbody>();shape=GetComponent<BoxCollider>();body.isKinematic=true;body.useGravity=false;}
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
        {if(!HasAuthority)throw new InvalidOperationException("Only authority freezes its enemy.");if(value)CancelChapterAttack();Frozen=value;}
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
                hit.Item.CargoRole!=CargoRole.OrdinaryLoot||hit.Item.WorldFrozen||hit.Damage<1||hit.Damage>100||
                hit.RelativeSpeed<3||!Finite(hit.RelativeSpeed)||!Finite(hit.Point)||!simulation.RegisteredItem(hit.Item))return false;
            if(BossKey.IsValid&&!simulation.CanDefeat(BossKey))return false;
            // Exact shot spends its original cargo before any health write or callback.
            if(!hit.Item.TryTransition(SuckableState.InFlight,SuckableState.Spent))return false;
            Health=Math.Max(0,Health-hit.Damage);observedAt=hit.AuthorityTime;
            if(Health==0)
            {
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
            observedAt=now;
            if(Phase==EnemyPhase.Defeated)
            {if(now-phaseAt>=View.DefeatDuration&&simulation.CompleteDefeat(this))Retired=true;return;}
            StepProjectile(now,dt);
            if(!simulation.CanApplyAt(now))return;
            if(!simulation.InsideWorld(body.position))
            {ReturnHome(now);return;}
            if(Phase==EnemyPhase.Tell)
            {
                Face(attackDirection,dt);
                if(now-phaseAt>=(sweep?rules.SweepTellSeconds:rules.TellSeconds))
                {SetPhase(EnemyPhase.Attack,now);if(!sweep&&rules.AttackKind==EnemyAttackKind.Spit)EmitProjectile(now);}
                return;
            }
            if(Phase==EnemyPhase.Attack)
            {
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
                    BossChapterAttackSequence.RedirectPauseSeconds:rules.RecoverySeconds;
                if(now-phaseAt<wait)return;
                if(Phase==EnemyPhase.Recover&&chapterAttack.TryBeginFollowUp())
                {
                    var follow=SelectTarget();
                    if(follow!=null&&Horizontal(body.position,follow.transform.position)<=Math.Max(3,rules.AttackRange)&&LineClear(follow))
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
            float beginRange=rules.AttackKind==EnemyAttackKind.Charge?Math.Max(3,rules.AttackRange):rules.AttackRange;
            if(distance<=beginRange&&LineClear(target))
            {
                BeginAttack(target,now,false);
            }
            else {SetPhase(EnemyPhase.Move,now);Follow(target.transform.position,now,dt);}
        }
        private void BeginAttack(PlayerMotor target,double now,bool followUp)
        {
            unchecked{AttackRevision++;}if(AttackRevision==0)AttackRevision=1;
            if(!followUp)chapterAttack.BeginNormal(rules.ChapterIIPattern,simulation.AdvancedEncounter,BossKey.IsValid,AttackRevision);
            sweep=rules.HasSweep&&simulation.AdvancedEncounter&&AttackRevision%3==0;hitPlayers.Clear();projectileEmitted=false;
            // Lock the new direction BEFORE the full existing Tell; no homing during either charge.
            attackDirection=target.transform.position-body.position;attackDirection.y=0;
            attackDirection=attackDirection.sqrMagnitude>.0001f?attackDirection.normalized:transform.forward;
            SetPhase(EnemyPhase.Tell,now);
        }
        private void FinishAttack(double now,bool blocked)
        {chapterAttack.Complete(blocked);SetPhase(EnemyPhase.Recover,now);}
        private void CancelChapterAttack()
        {
            bool active=chapterAttack.Active;
            chapterAttack.Cancel();
            if(active&&initialized&&HasAuthority&&(Phase==EnemyPhase.Tell||Phase==EnemyPhase.Attack))
                SetPhase(EnemyPhase.Recover,observedAt);
        }
        private void OnDisable(){CancelChapterAttack();}
        private PlayerMotor SelectTarget()
        {
            PlayerMotor selected=null;float closest=float.PositiveInfinity;
            foreach(var pair in simulation.Players)
            {
                var player=pair.Value;
                if(player==null||!player.isActiveAndEnabled||simulation.RequiresRecovery(pair.Key)||Horizontal(player.transform.position,Home)>rules.HomeRadius)continue;
                float distance=Horizontal(body.position,player.transform.position);
                if(distance>rules.DetectionRange*(pair.Key==targetId?1.5f:1)||distance>=closest||pair.Key!=targetId&&!LineClear(player))continue;
                selected=player;closest=distance;
            }
            targetId=selected!=null?selected.PlayerId:0;return selected;
        }
        private bool LineClear(PlayerMotor player)=>!Physics.Linecast(AimTarget.position,player.transform.position+Vector3.up*1,
            LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore);
        private void Melee(double now,float range,float angle)
        {
            foreach(var pair in simulation.Players)
            {
                var p=pair.Value;if(p==null||hitPlayers.Contains(pair.Key)||simulation.RequiresRecovery(pair.Key))continue;
                Vector3 delta=p.transform.position-body.position;delta.y=0;
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
            Vector3 delta=projectileDirection*rules.ProjectileSpeed*dt;
            var hits=Physics.SphereCastAll(projectilePosition,.12f,projectileDirection,delta.magnitude,LayerMask.GetMask("World","Player"),QueryTriggerInteraction.Ignore);
            Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            if(hits.Length>0)
            {
                projectilePosition=hits[0].point;projectileActive=false;var p=hits[0].collider.GetComponentInParent<PlayerMotor>();
                if(p!=null)simulation.TryDamagePlayer(this,p,now);
            }
            else projectilePosition+=delta;
        }
        private void Follow(Vector3 destination,double now,float dt)
        {
            if(now>=nextPathAt||(destination-navDestination).sqrMagnitude>1)
            {
                nextPathAt=now+.3;navDestination=destination;corner=1;corners=Array.Empty<Vector3>();
                if(NavMesh.SamplePosition(destination,out var end,.8f,NavMesh.AllAreas)&&NavMesh.CalculatePath(body.position,end.position,NavMesh.AllAreas,path)&&path.status==NavMeshPathStatus.PathComplete)corners=path.corners;
            }
            while(corner<corners.Length&&Horizontal(body.position,corners[corner])<.15f)corner++;
            if(corner>=corners.Length)return;
            Vector3 delta=corners[corner]-body.position;delta.y=0;if(delta.sqrMagnitude<.0001f)return;
            Face(delta.normalized,dt);MoveChecked(Vector3.ClampMagnitude(delta,rules.MoveSpeed*dt));
        }
        private bool MoveChecked(Vector3 delta,bool stopWhenClipped=false)
        {
            if(delta.sqrMagnitude<1e-8f)return true;
            Vector3 center=body.position+body.rotation*shape.center;
            Vector3 extent=Vector3.Max(Vector3.one*.01f,shape.size*.5f-Vector3.one*.02f);
            float length=delta.magnitude;float allowed=length;
            foreach(var hit in Physics.BoxCastAll(center,extent,delta/length,body.rotation,length+.025f,
                LayerMask.GetMask("World","Player","Enemies"),QueryTriggerInteraction.Ignore))
            {
                if(hit.collider==shape||hit.collider.transform.IsChildOf(transform)||hit.normal.y>.6f)continue;
                allowed=Math.Min(allowed,Math.Max(0,hit.distance-.025f));
            }
            if(allowed<=.001f)return false;
            Vector3 next=body.position+delta/length*allowed;
            if(!NavMesh.SamplePosition(next,out var surface,.35f,NavMesh.AllAreas)||Horizontal(next,surface.position)>.05f)return false;
            next.y=surface.position.y;body.MovePosition(next);return !stopWhenClipped||allowed>=length;
        }
        private void ReturnHome(double now)
        {
            if(!simulation.TryReturnEnemyHome(this,Home))return;
            chapterAttack.Cancel();targetId=0;corners=Array.Empty<Vector3>();projectileActive=false;SetPhase(EnemyPhase.Idle,now);
        }
        private void Face(Vector3 direction,float dt)
        {direction.y=0;if(direction.sqrMagnitude>.0001f)body.MoveRotation(Quaternion.RotateTowards(body.rotation,Quaternion.LookRotation(direction),480*dt));}
        private void SetPhase(EnemyPhase phase,double now)
        {if(Phase==phase)return;Phase=phase;phaseAt=now;unchecked{PhaseRevision++;}if(PhaseRevision==0)PhaseRevision=1;}
        private static float Horizontal(Vector3 a,Vector3 b)=>new Vector2(a.x-b.x,a.z-b.z).magnitude;
        private void OnDestroy(){chapterAttack.Cancel();if(rules!=null)Destroy(rules);}
    }
}
