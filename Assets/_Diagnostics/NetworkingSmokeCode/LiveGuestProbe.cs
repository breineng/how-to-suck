#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using HowToSuck.Networking;
using HowToSuck.Audio;
using UnityEngine;
namespace HowToSuck.Diagnostics
{
 [Serializable] public sealed class LiveGuestNear {
  public string run,detail;public int sequence,owner,candidate,rejected;public ulong enemy;public bool checkedPose;
  public Vector3 position,nozzle,launch;public float horizontalDistance;
  public string firstFlightBlocker,lastWatchFlightBlocker;public int firstFlightBlockedCandidate;
 }
 [Serializable] public sealed class LiveGuestAudioReceipt {
  public string run;public ulong sequence,occurrence,item,enemy;public int kind,owner,intake,amount;
  public bool truck;public double at;public float size,duration;public Vector3 position;
 }
 [Serializable] public sealed class LiveGuestObservation {
  public int schema=8,protocol=(int)NetworkConfiguration.ProtocolVersion;
  public LiveGuestNear near;
  public bool sessionAccepted,steamRuntimePresent,objectivesComplete;
  public string contract,bossRun,bossId,bossStatus,lastError,fireFailure;
  public ulong bossInstance,launches;
  public LiveGuestPlayer[] players;public LiveGuestLoot[] loot;public LiveGuestEnemy[] enemies;
  public LiveGuestShot[] shots;public AudioPlaybackWitness[] played;public LiveGuestAudioReceipt[] issued;public int audioRootId;
  public string[] errors;public int audioObserverErrors;
 }
 [Serializable] public sealed class LiveGuestPlayer {
  public int owner,count,reserved,capacity,suit;public ulong objectId;
  public bool accepted,storageKnown,suitKnown,recovery,suppressFire;
  public uint readerFire,motorFire;public string nextType;public double suitAt;
 }
 [Serializable] public sealed class LiveGuestLoot {
  public ulong id,objectId,shot,bossInstance;public int storedOwner,lastOwner;
  public string run,type,state,role,tier,bossRun,bossId;public long value;public float mass;
  public bool accepted,authority,kinematic;public Vector3 position;
 }
 [Serializable] public sealed class LiveGuestEnemy {
  public ulong id,objectId;public string run,type,phase;public int hp,maximum;
  public bool accepted,authority,boss;public Vector3 center;
 }
 [Serializable] public sealed class LiveGuestContact {
  public string run,state,collider;public ulong item,shot,enemy;public int body,owner,frame;public float relativeSpeed;
 }
 [Serializable] public sealed class LiveGuestShot {
  public string run,status="WAITING",error;public ulong item,shot,expected,victim;public int owner,body,damage;
  public bool intercepted;public LiveGuestEnemy[] before,after;public LiveGuestContact[] contacts=Array.Empty<LiveGuestContact>();
 }
 // Native collision observation only; callbacks do not hit, consume, relocate or apply force.
 public sealed class LiveGuestCollision : MonoBehaviour {
  public SuckableObject Item;public LiveGuestProbe Probe;
  private void OnCollisionEnter(Collision hit){if(!isActiveAndEnabled||Item==null||Probe==null||Item.State!=SuckableState.InFlight)return;
   var enemy=hit.collider.GetComponentInParent<EnemyActor>();
   Probe.Contact(new LiveGuestContact{run=Item.RunId,item=Item.InstanceId,body=Item.Body.GetInstanceID(),shot=Item.ActiveShotId,owner=Item.ShotOwner,
    enemy=enemy!=null?enemy.InstanceId:0,frame=Time.frameCount,state=Item.State.ToString(),collider=hit.collider.name,relativeSpeed=hit.relativeVelocity.magnitude});}
 }
 public sealed class LiveGuestProbe : MonoBehaviour {
  private LiveGuestNear lastNear;
  private NgoGameSession game;private bool bound;private readonly List<string> errors=new List<string>();
  private readonly List<AudioPlaybackWitness> audioPlays=new List<AudioPlaybackWitness>();
  private GameAudioRoot audioOwner;private int audioOwnerId;
  private readonly List<LiveGuestAudioReceipt> issuedAudio=new List<LiveGuestAudioReceipt>();
  private readonly List<LiveGuestShot> shots=new List<LiveGuestShot>();private readonly List<LiveGuestCollision> taps=new List<LiveGuestCollision>();
  private readonly Dictionary<LootKey,ItemLaunchGeometry> geometry=new Dictionary<LootKey,ItemLaunchGeometry>();
  private LiveGuestShot active;private SuckableObject projectile;private EnemyActor[] actors;
  private readonly Dictionary<ulong,LiveGuestEnemy> retained=new Dictionary<ulong,LiveGuestEnemy>();private double watchUntil;
  public void Bind(NgoGameSession value){if(bound||game!=null||value==null||!isActiveAndEnabled)throw new InvalidOperationException("One explicit live guest probe owner.");
   var owner=value.GetComponent<GameAudioRoot>();if(owner==null)throw new InvalidOperationException("Actual bound GameAudioRoot required.");
   game=value;audioOwner=owner;audioOwnerId=owner.GetInstanceID();bound=true;AudioPlaybackDiagnostics.Played+=Played;audioOwner.AuthorityCommittedAudio+=Issued;}
  private void Played(AudioPlaybackWitness value){if(!bound||value.rootId!=audioOwnerId||value.committedSequence==0)return;if(audioPlays.Count==2048){Error("Audio journal overflow");return;}audioPlays.Add(value);}
  // Read-only value copy of the existing post-ledger event, whether the host source was audible or culled.
  // Retained through Results, no IO/callback/playback or domain mutation inside the producer notification.
  private void Issued(CommittedAudioReceipt receipt){if(!bound)return;var f=receipt.Fact;
   if(game==null||!game.HasAuthority||!f.IsValid||f.Run!=game.Session.RunId||receipt.Sequence==0){Error("Invalid authority audio receipt owner/run");return;}
   if(issuedAudio.Count==2048){Error("Issued audio journal overflow");return;}
   issuedAudio.Add(new LiveGuestAudioReceipt{run=f.Run,sequence=receipt.Sequence,kind=(int)f.Kind,occurrence=f.Occurrence,item=f.Item,enemy=f.Enemy,owner=f.Owner,intake=f.Intake,truck=f.Truck,at=f.At,size=f.Size,amount=f.Amount,position=f.Position,duration=f.Duration});}
  private void Error(string value){if(errors.Count<32)errors.Add(value);}
  public EnemyActor Enemy(ulong id)=>FindObjectsByType<EnemyNetworkAdapter>(FindObjectsSortMode.None).Where(x=>x.Actor!=null&&x.Actor.RunId==game.Session.RunId&&x.Actor.InstanceId==id).Select(x=>x.Actor).FirstOrDefault();
  private NetworkLootAdapter Loot(ulong id)=>game.Items.FirstOrDefault(x=>x!=null&&x.Item.InstanceId==id&&x.Item.RunId==game.Session.RunId);
  private static LiveGuestEnemy EnemyRow(EnemyActor a){var adapter=a.GetComponent<EnemyNetworkAdapter>();var box=a.GetComponent<BoxCollider>();return new LiveGuestEnemy{
   id=a.InstanceId,objectId=adapter.NetworkObjectId,run=a.RunId,type=a.EnemyId,phase=a.Phase.ToString(),hp=a.Health,maximum=a.MaximumHealth,
   accepted=adapter.HasAcceptedCurrentSnapshot,authority=a.HasAuthority,boss=a.BossKey.IsValid,center=box!=null&&box.enabled?box.bounds.center:a.transform.position};}
  public LiveGuestObservation Observe(){
   var s=game.Session;var state=s.ContractState;
   return new LiveGuestObservation{near=lastNear,sessionAccepted=game.Control!=null&&game.Control.HasAcceptedCurrentSnapshot,steamRuntimePresent=game.Connection.ActiveSteamRuntime!=null,
    contract=state.ContractId,bossRun=state.Boss.Key.RunId,bossId=state.Boss.Key.ContractBossId,bossInstance=state.Boss.Key.InstanceId,bossStatus=state.Boss.Status.ToString(),
    objectivesComplete=state.ObjectivesComplete,lastError=s.LastError,fireFailure=s.World.ItemFire?.LastFailure,launches=s.World.ItemFire?.LaunchCount??0,
    players=game.Players.Where(x=>x!=null).Select(x=>{var storage=x.GetComponent<PlayerStorageView>()?.Value??default;var suit=x.GetComponent<PlayerSuitView>()?.Value??default;
     return new LiveGuestPlayer{owner=x.Motor.PlayerId,objectId=x.NetworkObjectId,accepted=x.HasAcceptedCurrentSnapshot,storageKnown=storage.IsKnown,count=storage.Count,reserved=storage.Reserved,capacity=storage.Capacity,nextType=storage.NextTypeId,
      suitKnown=suit.IsKnown,suit=suit.State.Segments,recovery=suit.State.RecoveryPending,suitAt=suit.ObservedAt,readerFire=x.Input.LatestIntent.FirePressSequence,motorFire=x.Motor.LastIntent.FirePressSequence,suppressFire=(bool)typeof(PlayerIntent).GetField("SuppressFire",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(x.Motor.LastIntent)};}).ToArray(),
    loot=game.Items.Where(x=>x!=null).Select(x=>{var i=x.Item;return new LiveGuestLoot{id=i.InstanceId,objectId=x.NetworkObjectId,run=i.RunId,type=i.TypeId,value=i.Value,mass=i.Body.mass,state=i.State.ToString(),role=i.CargoRole.ToString(),shot=i.ActiveShotId,storedOwner=i.StoredOwner,lastOwner=i.LastStorageOwner,tier=i.LastStorageTierId,
      bossRun=i.BossKey.RunId,bossId=i.BossKey.ContractBossId,bossInstance=i.BossKey.InstanceId,accepted=x.HasAcceptedCurrentSnapshot,authority=i.HasPhysicsAuthority,kinematic=i.Body.isKinematic,position=i.transform.position};}).ToArray(),
    enemies=FindObjectsByType<EnemyNetworkAdapter>(FindObjectsSortMode.None).Where(x=>x.Actor!=null&&x.Actor.RunId==s.RunId).Select(x=>EnemyRow(x.Actor)).ToArray(),shots=shots.ToArray(),played=audioPlays.ToArray(),issued=issuedAudio.ToArray(),audioRootId=audioOwnerId,errors=errors.ToArray(),audioObserverErrors=AudioPlaybackDiagnostics.ObserverErrors};
  }
  public void Contact(LiveGuestContact c){if(active==null||c.run!=active.run||c.item!=active.item||c.body!=active.body||c.owner!=active.owner)return;
   if(active.contacts.Length>=128){Error("Native contact journal overflow");return;}active.contacts=active.contacts.Concat(new[]{c}).ToArray();}
  private void LateUpdate(){if(!bound||active==null)return;try{
   for(int n=0;n<actors.Length;n++){var a=actors[n];if(a!=null)retained[a.InstanceId]=EnemyRow(a);else if(retained[active.before[n].id].hp>0)throw new InvalidOperationException("Pre-shot actor destroyed without observed zero HP");}
   if(projectile==null||projectile.RunId!=active.run||projectile.InstanceId!=active.item||projectile.Body.GetInstanceID()!=active.body)throw new InvalidOperationException("Original key/body destroyed or replaced before terminal witness");
   if(projectile.State==SuckableState.InFlight){if(active.shot==0)active.shot=projectile.ActiveShotId;else if(active.shot!=projectile.ActiveShotId)throw new InvalidOperationException("Watched active ShotId changed");}
   foreach(var c in active.contacts)if(active.shot==0&&c.shot!=0)active.shot=c.shot;
   if(active.shot==0){if(Time.realtimeSinceStartupAsDouble>watchUntil)throw new TimeoutException("Fresh guest press did not launch: "+game.Session.World.ItemFire.LastFailure);return;}
   if(projectile.State==SuckableState.InFlight){if(Time.realtimeSinceStartupAsDouble>watchUntil)throw new TimeoutException("Flight did not reach terminal state");return;}
   active.after=active.before.Select(x=>retained[x.id]).ToArray();
   var changed=active.before.Where(x=>retained[x.id].hp!=x.hp).ToArray();
   if(projectile.State==SuckableState.Spent){
    if(changed.Length!=1)throw new InvalidOperationException("Spent must change exactly one pre-shot actual actor");
    var victim=changed[0];var after=retained[victim.id];
    if(victim.hp<=0||after.hp!=Math.Max(0,victim.hp-active.damage)||!active.contacts.Any(c=>c.shot==active.shot&&c.enemy==victim.id&&c.relativeSpeed>=ItemFireRules.MinimumHitSpeed))throw new InvalidOperationException("Actual same-shot collision / exact mass HP decrement mismatch");
    active.victim=victim.id;active.intercepted=victim.id!=active.expected;active.status="HIT";
   }else if(projectile.State==SuckableState.Available&&changed.Length==0)active.status="MISS";
   else throw new InvalidOperationException("Unproved terminal projectile state: "+projectile.State+"; intake/truck/loss is classified separately, not accepted as miss");
   if(projectile.ActiveShotId!=0)throw new InvalidOperationException("Terminal shot provenance not cancelled");
   active=null;projectile=null;actors=null;
  }catch(Exception e){if(active!=null){active.status="FAILED";active.error=e.Message;}Error(e.Message);active=null;}}
  public bool Command(GameplayCommand c,out string why){why="";
   if(!game.HasAuthority||!game.Session.World.IsRunning||c.run!=game.Session.RunId){why="Current authority run required";return false;}
   var player=game.Players.FirstOrDefault(x=>x.Motor.PlayerId==c.routePlayerId)?.Motor;
   if(c.kind=="watch-shot"){
    if(active!=null||player==null||shots.Count>=24){why="Only one armed original projectile, max24 per process";return false;}
    var store=player.GetComponent<IntakeReceiver>().Storage;
    if(store==null||!store.TryPeek(out var key,out var item)||item.InstanceId!=c.itemId||item.CargoRole!=CargoRole.OrdinaryLoot){why="Exact FIFO ordinary cargo required";return false;}
    var receiver=player.GetComponent<IntakeReceiver>();var nozzle=player.NozzleAnchor;
    if(!geometry.TryGetValue(key,out var shape)||!player.NozzlePoseValid||nozzle==null||!player.GetComponent<VacuumEmitter>().HasClearSourcePath()){why="Original Available geometry / current authored nozzle source path required";return false;}
    float front=Mathf.Max(.025f,Vector3.Dot(receiver.Position-nozzle.position,nozzle.forward)+receiver.AdmissionRadius+.025f);
    if(!shape.TryLaunchPose(nozzle,player.transform,front,out var launchPose,out why))return false;
    if(!FlightPathClear(item,launchPose,nozzle.forward,Enemy(c.enemyId),player.transform,out why)){
     if(lastNear!=null)lastNear.lastWatchFlightBlocker=why;return false;
    } // Real Fire/collision/HP remain the only outcome witnesses.
    actors=game.Session.World.Combat.Actors.Values.Where(a=>a!=null).ToArray();retained.Clear();foreach(var a in actors)retained[a.InstanceId]=EnemyRow(a);
    active=new LiveGuestShot{run=c.run,item=item.InstanceId,body=item.Body.GetInstanceID(),owner=player.PlayerId,expected=c.enemyId,damage=ItemFireRules.Damage(item.Body.mass),before=actors.Select(EnemyRow).ToArray()};
    shots.Add(active);projectile=item;watchUntil=Time.realtimeSinceStartupAsDouble+7;
    var tap=item.GetComponent<LiveGuestCollision>();if(tap==null){tap=item.gameObject.AddComponent<LiveGuestCollision>();taps.Add(tap);}tap.Item=item;tap.Probe=this;
    why="Native original-body witness armed, all current enemy HP copied; no shot emitted";return true;
   }
   Physics.SyncTransforms();
   if(c.kind=="engineer-intake"){
    var item=Loot(c.itemId)?.Item;var receiver=c.routePlayerId==0?game.Session.CurrentLevel.Truck.Receiver:player!=null?player.GetComponent<IntakeReceiver>():null;
    if(item==null||item.State!=SuckableState.Available||receiver==null||!receiver.Accepts(item)){why="Available original registered item and accepting real receiver required";return false;}
    if(!geometry.ContainsKey(item.Key)){if(!ItemLaunchGeometry.TryCapture(item,out var shape,out why))return false;geometry.Add(item.Key,shape);}
    var cs=item.GameplayColliders.Where(x=>x!=null&&x.enabled&&!x.isTrigger).ToArray();if(cs.Length==0){why="Actual enabled geometry required";return false;}
    var b=cs[0].bounds;foreach(var x in cs.Skip(1))b.Encapsulate(x.bounds);var offset=b.center-item.Body.position;
    var goals=new[]{.10f,.22f,.36f}.Select(d=>new Pose(receiver.Position+receiver.Rotation*Vector3.forward*d-offset,item.Body.rotation)).ToArray();
    if(!game.Session.World.ItemFire.TryFindSafePose(item,goals,out var pose,out why))return false;
    bool okay=item.TryRelocateAvailable(item.Key,pose.position,pose.rotation);why=okay?"ENGINEERING: same Available item relocated to checked receiver pose; normal physics must admit/deliver":"Relocation rejected";return okay;
   }
   if(player==null){why="Actual registered player required";return false;}
   if(game.Session.World.Combat.SuitSnapshot(player.PlayerId).RecoveryPending){why="Actual suit recovery pending; fixture must wait for normal recovery";return false;}
   IEnumerable<Vector3> desired=new[]{c.destination};var enemy=Enemy(c.enemyId);
   if(c.kind=="engineer-near"){
    if(enemy==null||!enemy.IsAlive){why="Live selected target required";return false;}
    lastNear=new LiveGuestNear{run=c.run,sequence=c.sequence,owner=player.PlayerId,enemy=enemy.InstanceId};
    desired=new[]{3.4f,4.2f}.SelectMany(r=>Enumerable.Range(0,16).Select(n=>enemy.transform.position+new Vector3(Mathf.Cos(n*Mathf.PI/8),0,Mathf.Sin(n*Mathf.PI/8))*r));
   }
   GameObject preview=null;int candidate=0;
   try{
    if(c.kind=="engineer-near"){preview=new GameObject("Diagnostic candidate nozzle (no physics)"){hideFlags=HideFlags.HideAndDontSave};preview.SetActive(false);}
    foreach(var pos in desired){candidate++;
     if(!SafePlayer(player,pos,out var safe,out why)){if(lastNear!=null&&preview!=null){lastNear.rejected++;lastNear.detail=why;}continue;}
     if(preview!=null){
      if(!ClearTarget(player,safe,enemy)){why="Target center ray blocked";lastNear.rejected++;lastNear.detail=why;continue;}
      if(!CandidateLaunch(player,safe,enemy,preview.transform,out var launch,out why)){lastNear.rejected++;lastNear.detail=why;
        if(why!=null&&why.StartsWith("Flight ",StringComparison.Ordinal)&&string.IsNullOrEmpty(lastNear.firstFlightBlocker)){
         lastNear.firstFlightBlocker=why;lastNear.firstFlightBlockedCandidate=candidate;
        }continue;}
      lastNear.position=safe;lastNear.nozzle=preview.transform.position;lastNear.launch=launch.position;lastNear.candidate=candidate;
      lastNear.horizontalDistance=new Vector2(safe.x-enemy.transform.position.x,safe.z-enemy.transform.position.z).magnitude;
     }
     if(player.RecoverAt(safe,player.LastIntent)){Physics.SyncTransforms();if(preview!=null)lastNear.checkedPose=true;
      why=preview!=null?"ENGINEERING: checked supported pose; predicted actual held-item launch clear; actual guest aim and watch-shot must recheck":"ENGINEERING: existing RecoverAt at supported capsule; subsequent RMB/LMB only actual guest devices";if(preview!=null)lastNear.detail=why;return true;}}
    return false;
   }finally{if(preview!=null)Destroy(preview);}
  }
  private bool CandidateLaunch(PlayerMotor p,Vector3 pos,EnemyActor target,Transform preview,out Pose launch,out string why){
   launch=default;why="Expected initialized MK1 physical mount/storage binding";
   var receiver=p.GetComponent<IntakeReceiver>();var emitter=p.GetComponent<VacuumEmitter>();var cc=p.GetComponent<CharacterController>();
   if(receiver==null||receiver.IsTruck||receiver.Target!=p.NozzleAnchor||receiver.Storage==null||!receiver.Storage.TryPeek(out var key,out var item)||
    item.CargoRole!=CargoRole.OrdinaryLoot||item.State!=SuckableState.Stored||item.StoredOwner!=p.PlayerId||!geometry.TryGetValue(key,out var shape)||
    emitter==null||emitter.Definition==null||emitter.Definition.TierId!="mk1"||emitter.Source!=p.NozzleAnchor||emitter.OriginGuard!=p.AuthoritativeAim||
    cc==null||!cc.enabled||p.AuthoritativeAim==null||p.NozzleAnchor==null||p.NozzleAnchor.parent!=p.AuthoritativeAim||p.AuthoritativeAim.parent!=p.transform||
    (p.AuthoritativeAim.localPosition-Vector3.up*p.Settings.EyeHeight).sqrMagnitude>1e-8f||
    (p.transform.lossyScale-Vector3.one).sqrMagnitude>1e-8f||(p.AuthoritativeAim.localScale-Vector3.one).sqrMagnitude>1e-8f||(p.NozzleAnchor.localScale-Vector3.one).sqrMagnitude>1e-8f)return false;
   var box=target.GetComponent<BoxCollider>();if(box==null||!box.enabled){why="Current target box unavailable";return false;}
   Vector3 eye=pos+Vector3.up*p.Settings.EyeHeight,d=box.bounds.center-eye;
   float horizontal=new Vector2(pos.x-target.transform.position.x,pos.z-target.transform.position.z).magnitude;
   if(horizontal<3.35f||horizontal>4.25f){why="Candidate left the existing far-ring distance interval";return false;}
   float yaw=Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg,pitch=-Mathf.Atan2(d.y,new Vector2(d.x,d.z).magnitude)*Mathf.Rad2Deg;
   if(Mathf.Abs(pitch)>80||!p.TryGetNozzleLocalPosition(pitch,out var mount)){why="Current physical mount rejects predicted actual mouse aim";return false;}
   var aim=Quaternion.Euler(pitch,yaw,0);preview.SetPositionAndRotation(eye+aim*mount,aim*p.NozzleAnchor.localRotation);
   var sourceDelta=preview.position-eye;
   if(Physics.CheckSphere(preview.position,.015f,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)||
    sourceDelta.sqrMagnitude>=.0001f&&Physics.SphereCast(eye,.04f,sourceDelta.normalized,out _,sourceDelta.magnitude,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore))
    {why="Candidate physical source path blocked";return false;}
   if(!shape.TryLaunchPose(preview,p.transform,receiver.AdmissionRadius+.025f,out launch,out why))return false;
   // The production gate sees the current real CC; additionally test that same CC at the proposed player pose.
   foreach(var c in item.GameplayColliders){if(c==null||c.isTrigger)continue;
    var relative=item.transform.InverseTransformPoint(c.transform.position);var rotation=Quaternion.Inverse(item.transform.rotation)*c.transform.rotation;
    if(Physics.ComputePenetration(c,launch.position+launch.rotation*relative,launch.rotation*rotation,cc,pos,Quaternion.Euler(0,yaw,0),out _,out float depth)&&depth>0)
     {why="Candidate launch intersects the proposed shooter capsule";return false;}}
   return FlightPathClear(item,launch,preview.forward,target,p.transform,out why);
  }
  private struct FlightShape {
   public Collider Collider;public int Kind;public Vector3 Offset,Half,Axis;public Quaternion Rotation;public float Radius,HalfSegment,Padding;
  }
  // TryLaunchPose has just validated the original Stored collider frames/parameters. Never read disabled bounds.
  // This is read-only route rejection, not a native hit forecast. Primitive casts; convex meshes use tight local bounds.
  private static bool FlightPathClear(SuckableObject item,Pose launch,Vector3 forward,EnemyActor target,Transform shooter,out string why){
   why="Flight requires the checked original Stored body and live target";
   if(item==null||item.Body==null||item.State!=SuckableState.Stored||target==null||!target.IsAlive)return false;
   var targetBox=target.GetComponent<BoxCollider>();if(targetBox==null||!targetBox.enabled)return false;
   var body=item.Body;float dt=Time.fixedDeltaTime,damping=body.linearDamping,maxSpeed=body.maxLinearVelocity;
   Vector3 gravity=body.useGravity?Physics.gravity:Vector3.zero;
   if(!FiniteFlight(gravity)||!FiniteFlight(forward)||Mathf.Abs(forward.sqrMagnitude-1)>.001f||
    !ItemFireRules.Finite(dt)||dt<.001f||dt>.1f||!ItemFireRules.Finite(damping)||damping<0||
    !ItemFireRules.Finite(maxSpeed)||maxSpeed<ItemFireRules.LaunchSpeed||(body.constraints&RigidbodyConstraints.FreezePosition)!=0){
    why="Flight unsupported native body timestep/damping/speed/position constraint";return false;}
   var bounds=targetBox.bounds;float depth=Vector3.Dot(bounds.center-launch.position,forward)+Vector3.Dot(bounds.extents,AbsFlight(forward));
   if(!ItemFireRules.Finite(depth)||depth<=0||depth>6){why="Flight target depth outside the existing short route";return false;}
   var shapes=new List<FlightShape>();
   foreach(var c in item.GameplayColliders){
    if(c==null||c.isTrigger)continue;
    var scale=c.transform.lossyScale;var rotation=launch.rotation*Quaternion.Inverse(item.transform.rotation)*c.transform.rotation;
    var offset=launch.rotation*item.transform.InverseTransformPoint(c.transform.position);
    var shape=new FlightShape{Collider=c,Rotation=rotation,Padding=.005f+c.contactOffset};Vector3 centre;
    if(c is BoxCollider box){shape.Kind=0;centre=box.center;shape.Half=Vector3.Scale(box.size,scale)*.5f;}
    else if(c is SphereCollider sphere){shape.Kind=1;centre=sphere.center;shape.Radius=sphere.radius*Mathf.Max(scale.x,scale.y,scale.z);}
    else if(c is CapsuleCollider capsule){shape.Kind=2;centre=capsule.center;int d=capsule.direction;
     shape.Radius=capsule.radius*Mathf.Max(scale[(d+1)%3],scale[(d+2)%3]);shape.HalfSegment=Mathf.Max(0,capsule.height*scale[d]*.5f-shape.Radius);
     shape.Axis=rotation*(d==0?Vector3.right:d==1?Vector3.up:Vector3.forward);}
    else if(c is MeshCollider mesh&&mesh.convex&&mesh.sharedMesh!=null){shape.Kind=0;var b=mesh.sharedMesh.bounds;centre=b.center;shape.Half=Vector3.Scale(b.size,scale)*.5f;}
    else{why="Flight unsupported authored collider "+c.name;return false;}
    shape.Offset=offset+rotation*Vector3.Scale(centre,scale);shapes.Add(shape);
   }
   if(shapes.Count==0||shapes.Count>32){why="Flight requires 1..32 authored collider shapes";return false;}
   Vector3 position=launch.position,velocity=forward*ItemFireRules.LaunchSpeed;float elapsed=0;
   // Production Stored->InFlight zeroes angular velocity. No future AI/contact/force prediction.
   // PhysX unconstrained integration order: gravity, damping, speed cap, position.
   for(int step=0;step<32&&elapsed<.5f;step++){
    velocity=AdvanceFlightVelocity(velocity,gravity,damping,dt,maxSpeed);Vector3 next=position+velocity*dt;
    float along=Vector3.Dot(next-launch.position,forward);bool done=along>=depth;
    if(done){float previous=Vector3.Dot(position-launch.position,forward);next=Vector3.Lerp(position,next,(depth-previous)/(along-previous));}
    Vector3 delta=next-position;float distance=delta.magnitude;Collider first=null;float firstDistance=float.PositiveInfinity,targetDistance=float.PositiveInfinity;
    foreach(var shape in shapes){
     Vector3 centre=position+shape.Offset;float radius=shape.Radius+shape.Padding;var half=shape.Half+Vector3.one*shape.Padding;
     targetDistance=Mathf.Min(targetDistance,FlightTargetDistance(shape,centre,delta,distance,step==0,targetBox));
     if(step==0){
      Collider[] overlaps=shape.Kind==1?Physics.OverlapSphere(centre,radius,~0,QueryTriggerInteraction.Ignore):
       shape.Kind==2?Physics.OverlapCapsule(centre+shape.Axis*shape.HalfSegment,centre-shape.Axis*shape.HalfSegment,radius,~0,QueryTriggerInteraction.Ignore):
       Physics.OverlapBox(centre,half,shape.Rotation,~0,QueryTriggerInteraction.Ignore);
      foreach(var other in overlaps)if(FlightObstacle(other,item,target,shooter)&&(first==null||other.GetInstanceID()<first.GetInstanceID())){first=other;firstDistance=0;}
     }
     if(distance<=0)continue;
     RaycastHit[] hits=shape.Kind==1?Physics.SphereCastAll(centre,radius,delta/distance,distance,~0,QueryTriggerInteraction.Ignore):
      shape.Kind==2?Physics.CapsuleCastAll(centre+shape.Axis*shape.HalfSegment,centre-shape.Axis*shape.HalfSegment,radius,delta/distance,distance,~0,QueryTriggerInteraction.Ignore):
      Physics.BoxCastAll(centre,half,delta/distance,shape.Rotation,distance,~0,QueryTriggerInteraction.Ignore);
     foreach(var hit in hits)if(FlightObstacle(hit.collider,item,target,shooter)&&(hit.distance<firstDistance||hit.distance==firstDistance&&(first==null||hit.collider.GetInstanceID()<first.GetInstanceID()))){first=hit.collider;firstDistance=hit.distance;}
    }
    // Selected target is a query terminator, not an obstacle to ignore until its far plane.
    // Unpadded target geometry must be strictly before every padded obstacle; an equal-distance tie rejects.
    if(FlightTargetFirst(targetDistance,firstDistance)){
     why="Flight clear to queried target entrance step="+step+" distance="+targetDistance.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" ["+targetBox.GetInstanceID()+"]; native hit still required";return true;
    }
    if(first!=null){var point=position+(distance>0?delta/distance*firstDistance:Vector3.zero);
     why="Flight step="+step+" t="+elapsed.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" blocked by "+first.name+" ["+first.GetType().Name+"/"+first.GetInstanceID()+"] at body "+point.ToString("F3");return false;}
    if(done){why="Flight passed target depth without an actual target-shape query intersection";return false;}
    position=next;elapsed+=dt;
   }
   why="Flight target depth not reached within 32 steps / 0.5 seconds";return false;
  }
  // The obstacle casts retain their existing contact margin; target termination uses the unexpanded authored shape.
  private static float FlightTargetDistance(FlightShape shape,Vector3 centre,Vector3 delta,float distance,bool checkOrigin,BoxCollider target){
   float found=float.PositiveInfinity;
   if(checkOrigin){
    Collider[] overlaps=shape.Kind==1?Physics.OverlapSphere(centre,shape.Radius,~0,QueryTriggerInteraction.Ignore):
     shape.Kind==2?Physics.OverlapCapsule(centre+shape.Axis*shape.HalfSegment,centre-shape.Axis*shape.HalfSegment,shape.Radius,~0,QueryTriggerInteraction.Ignore):
     Physics.OverlapBox(centre,shape.Half,shape.Rotation,~0,QueryTriggerInteraction.Ignore);
    foreach(var c in overlaps)if(c==target)return 0;
   }
   if(distance<=0)return found;
   RaycastHit[] hits=shape.Kind==1?Physics.SphereCastAll(centre,shape.Radius,delta/distance,distance,~0,QueryTriggerInteraction.Ignore):
    shape.Kind==2?Physics.CapsuleCastAll(centre+shape.Axis*shape.HalfSegment,centre-shape.Axis*shape.HalfSegment,shape.Radius,delta/distance,distance,~0,QueryTriggerInteraction.Ignore):
    Physics.BoxCastAll(centre,shape.Half,delta/distance,shape.Rotation,distance,~0,QueryTriggerInteraction.Ignore);
   foreach(var hit in hits)if(hit.collider==target&&hit.distance>=0)found=Mathf.Min(found,hit.distance);
   return found;
  }
  internal static bool FlightTargetFirst(float targetDistance,float obstacleDistance)=>
   ItemFireRules.Finite(targetDistance)&&targetDistance>=0&&targetDistance<obstacleDistance;
  internal static Vector3 AdvanceFlightVelocity(Vector3 velocity,Vector3 gravity,float damping,float dt,float maximum){
   velocity=(velocity+gravity*dt)*Mathf.Max(0,1-damping*dt);
   return velocity.sqrMagnitude>maximum*maximum?velocity.normalized*maximum:velocity;
  }
  private static bool FlightObstacle(Collider c,SuckableObject item,EnemyActor target,Transform shooter)=>c!=null&&!c.isTrigger&&
   c.transform!=item.transform&&!c.transform.IsChildOf(item.transform)&&c.transform!=shooter&&!c.transform.IsChildOf(shooter)&&c.GetComponentInParent<EnemyActor>()!=target;
  private static Vector3 AbsFlight(Vector3 v)=>new Vector3(Mathf.Abs(v.x),Mathf.Abs(v.y),Mathf.Abs(v.z));
  private static bool FiniteFlight(Vector3 v)=>ItemFireRules.Finite(v.x)&&ItemFireRules.Finite(v.y)&&ItemFireRules.Finite(v.z);
  private bool SafePlayer(PlayerMotor p,Vector3 desired,out Vector3 position,out string why){position=default;why="No supported floor";
   if(!Physics.Raycast(desired+Vector3.up*.7f,Vector3.down,out var floor,1.6f,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)||floor.normal.y<.85f)return false;
   position=floor.point+Vector3.up*.06f;var cc=p.GetComponent<CharacterController>();var centre=position+cc.center;float h=Mathf.Max(0,cc.height*.5f-cc.radius);var a=centre+Vector3.up*h;var b=centre-Vector3.up*h;var pad=Vector3.one*(cc.radius+.025f);
   var bounds=game.Session.CurrentLevel.BoundsGuard.AllowedBounds;if(!bounds.Contains(Vector3.Max(a,b)+pad)||!bounds.Contains(Vector3.Min(a,b)-pad)){why="Outside authored bounds";return false;}
   foreach(var x in Physics.OverlapCapsule(a,b,cc.radius+.025f,LayerMask.GetMask("World","Items","Player","Enemies"),QueryTriggerInteraction.Ignore))if(x!=cc&&!x.transform.IsChildOf(p.transform)){why="Occupied capsule: "+x.name;return false;}
   why="Supported capsule";return true;
  }
  private static bool ClearTarget(PlayerMotor p,Vector3 pos,EnemyActor e){var shape=e.GetComponent<BoxCollider>();if(shape==null||!shape.enabled)return false;var origin=pos+Vector3.up*p.Settings.EyeHeight;var d=shape.bounds.center-origin;
   if(d.magnitude<1.5f||d.magnitude>5)return false;
   foreach(var h in Physics.RaycastAll(origin,d.normalized,d.magnitude,~0,QueryTriggerInteraction.Ignore).OrderBy(x=>x.distance)){if(h.collider.transform.IsChildOf(p.transform))continue;return h.collider==shape||h.collider.GetComponentInParent<EnemyActor>()==e;}return false;}
  private void OnDisable(){if(bound)AudioPlaybackDiagnostics.Played-=Played;if(!ReferenceEquals(audioOwner,null))audioOwner.AuthorityCommittedAudio-=Issued;bound=false;foreach(var tap in taps)if(tap!=null){tap.enabled=false;Destroy(tap);}taps.Clear();if(active!=null){active.status="FAILED";active.error="Probe disabled while native shot pending";}active=null;}
  private void OnDestroy()=>OnDisable();
 }
}
#endif
