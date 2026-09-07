#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HowToSuck.Networking;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
namespace HowToSuck.Diagnostics
{
    // Explicit test assembly only. Commands operate normal UI endpoints or real owner virtual devices.
    // Positive commands use real owner devices. Explicit engineer-* delegates reproduce V10 checked arrangements; no HP/time/reward writes.
    public sealed class GameplayDiagnosticRunner : MonoBehaviour
    {
        public NgoGameSession Game;
        public MppmGameplayStartup Startup;
        public string ReportsRoot="Tools/Staging/Networking/Gameplay/Diagnostic/evidence";
        private GameplayVirtualDevices devices;
        private LiveGuestProbe current;
        private GameplayCommand active;
        private GameplayCommandResult result;
        private string directory,lastStatus,lastDetail;
        private int pid,slot,lastSequence;
        private double nextObserve,commandUntil,sweepStarted;
        private float sweepBaseYaw,sweepBasePitch;
        private ulong startingFrames,neutralStartingFrames;
        private readonly List<string> runtimeErrors=new List<string>();
        private Vector3 capturedAim,capturedGoal;
        private SuctionSystem selectionProbe;
        private ulong trackedTargetId,sharedSelectionTicks,lastSharedTick;
        private GameplayTargetSelection[] targetSelections=Array.Empty<GameplayTargetSelection>(),lastSharedSelections=Array.Empty<GameplayTargetSelection>();
        private bool hasStarted,disabledByError;
        private void Awake()=>Application.logMessageReceived+=RecordRuntimeError;
        private void RecordRuntimeError(string message,string stack,LogType type)
        {if((type==LogType.Error||type==LogType.Exception||type==LogType.Assert||message.IndexOf("SceneEventInProgress",StringComparison.OrdinalIgnoreCase)>=0)&&runtimeErrors.Count<32)runtimeErrors.Add(message.Length>500?message.Substring(0,500):message);}
        [Serializable] private sealed class InputWitness{public string utc,detail;public int frame;}
        private void RecordInputWitness(string detail)
        {if(directory!=null)File.AppendAllText(Path.Combine(directory,"input-lifecycle.jsonl"),JsonUtility.ToJson(new InputWitness{utc=DateTime.UtcNow.ToString("O"),detail=detail,frame=Time.frameCount})+Environment.NewLine);}
        private void Start()
        {
            try
            {
                if(Game==null||Startup==null||Startup.Profile==null)throw new InvalidOperationException("Bind diagnostic Game/Startup after role resolution.");
                current=GetComponent<LiveGuestProbe>();if(current==null)throw new InvalidOperationException("Protocol8 diagnostic companion required.");current.Bind(Game);
                selectionProbe=new SuctionSystem(Game.Session.World.Loot);Game.Session.World.SnapshotChanged+=CaptureAuthoritySelection;
                slot=Startup.Profile.Slot;pid=System.Diagnostics.Process.GetCurrentProcess().Id;
                string root=ReportsRoot;
                var args=Environment.GetCommandLineArgs();
                for(int i=0;i<args.Length;i++)if(args[i]=="--hts-gameplay-reports")
                {if(i+1>=args.Length)throw new ArgumentException("Missing report path.");root=args[++i];}
                directory=Path.Combine(Path.GetFullPath(root),Startup.Profile.Nonce,"player-"+slot+"-"+pid);
                Directory.CreateDirectory(directory);
                var lifecycle=new GameObject("Gameplay diagnostic lifecycle observer");DontDestroyOnLoad(lifecycle);lifecycle.AddComponent<GameplayProcessLifecycleObserver>().Initialize(directory);
                devices=new GameplayVirtualDevices(Startup.Profile.Nonce,slot,RecordInputWitness);
                Atomic("identity.json",JsonUtility.ToJson(new Identity{nonce=Startup.Profile.Nonce,slot=slot,pid=pid,keyboard=devices.KeyboardId,mouse=devices.MouseId,
                    evidence="virtual-device/focus injection; not physical OS focus; same production InputReader"},true));
            }
            catch(Exception error){DisableOnError(error);}
        }
        [Serializable] private sealed class Identity {public string nonce,evidence;public int slot,pid,keyboard,mouse;}
        private void Update()
        {
            if(disabledByError||directory==null)return;
            try
            {
                var local=Game.Session.LocalPlayer;
                if(local!=null&&local.GetComponent<PlayerInputReader>().IsInitialized)devices.Bind(local);
                ReadCommand();TickCommand();
                if(Time.realtimeSinceStartupAsDouble>=nextObserve)
                {nextObserve=Time.realtimeSinceStartupAsDouble+.1;var json=JsonUtility.ToJson(Observe(),true);Atomic("observation.json",json);File.AppendAllText(Path.Combine(directory,"observations.jsonl"),JsonUtility.ToJson(Observe())+Environment.NewLine);}
            }
            catch(Exception error){if(active!=null)Complete("FAIL",error.GetType().Name+": "+error.Message);else DisableOnError(error);}
        }
        private void ReadCommand()
        {
            string path=Path.Combine(directory,"command.json");if(active!=null||!File.Exists(path))return;
            if(!DiagnosticCommandFile.TryRead(path,8192,out var commandJson))return;
            GameplayCommand command;try{command=JsonUtility.FromJson<GameplayCommand>(commandJson);}catch(ArgumentException){return;}
            if(command==null||command.sequence<=lastSequence)return;
            if(command.nonce!=Startup.Profile.Nonce||command.slot!=slot)throw new InvalidOperationException("Command routing does not match this process.");
            lastSequence=command.sequence;active=command;hasStarted=false;devices.Neutral();
            result=new GameplayCommandResult{nonce=command.nonce,run=Game.Session.RunId,kind=command.kind,sequence=command.sequence,slot=slot,pid=pid,
                startAt=Game.Driver.Now,startMoney=Game.Session.ContractState.DeliveredValue,startPosition=Position()};
            startingFrames=devices.QueuedDynamicFrames;
            if(!Finite(command.seconds)||command.seconds<0||command.seconds>15||!Finite(command.stopDistance)||command.stopDistance<.08f||command.stopDistance>2||
                double.IsNaN(command.startAtServerTime)||double.IsInfinity(command.startAtServerTime)||command.startAtServerTime>Game.Driver.Now+10||
                !Finite(command.destination.x)||!Finite(command.destination.y)||!Finite(command.destination.z))
            {Complete("REJECTED","Invalid bounded command values.");return;}
            if(command.kind=="sweep"&&(!Finite(command.sweepYaw)||!Finite(command.sweepPitch)||!Finite(command.sweepHz)||command.sweepYaw<0||command.sweepYaw>60||command.sweepPitch<0||command.sweepPitch>35||command.sweepHz<.1f||command.sweepHz>.6f||command.forward||command.backward||command.left||command.right||command.jump||command.vacuum||command.interact))
            {Complete("REJECTED","Sweep is bounded stationary real mouse input only.");return;}
            bool needsRun=command.kind.StartsWith("engineer-",StringComparison.Ordinal)||command.kind=="watch-shot"||command.kind=="enemy-aim"||command.kind=="aim-point"||command.kind=="flat-probe"||command.kind=="flat-move"||command.kind=="sweep"||command.kind=="keys"||command.kind=="move"||command.kind=="collect"||command.kind=="aim"||command.kind=="guest-gates"||command.kind=="stale-rpc";
            if(needsRun&&(Game.Session.Phase!=SessionPhase.Playing||command.run!=Game.Session.RunId||string.IsNullOrEmpty(command.run)))
            {Complete("REJECTED","Current running RunId is required.");return;}
        }
        private void TickCommand()
        {
            if(active==null)return;
            if(Game.Driver.Now<active.startAtServerTime)return;
            if(!hasStarted)
            {
                hasStarted=true;commandUntil=Time.realtimeSinceStartupAsDouble+Math.Max(.1,active.seconds);
                switch(active.kind)
                {
                    case "ready":Game.SetLocalReady(true);Complete("DONE","Normal ready endpoint invoked; server confirmation must be observed.");return;
                    case "start":
                        if(!Game.HasAuthority||Game.Session.CurrentContract!=null){Complete("REJECTED","Host lobby required.");return;}
                        var contract=Game.Session.Catalog.Contracts.FirstOrDefault(c=>c!=null&&c.ContractId==active.contractId);
                        if(contract==null||contract.ContractId!="old_house")throw new InvalidOperationException("Exact current authored old_house required; no quota/time override.");
                        Complete(Game.Session.StartContract(contract)?"DONE":"REJECTED","Normal StartContract; preparation/Playing must be observed.");return;
                    case "neutral":devices.Neutral();neutralStartingFrames=devices.QueuedDynamicFrames;commandUntil=Time.realtimeSinceStartupAsDouble+2;break;
                    case "return":Complete(Game.Session.ReturnToMenu()?"DONE":"REJECTED","Normal ReturnToMenu endpoint.");return;
                    case "leave":if(Game.HasAuthority){Complete("REJECTED","Use normal abort UI separately; leave command is guest-only.");return;}Game.LeaveGuest();Complete("DONE","Guest intentional leave initiated; actual offline scene still requires observation.");return;
                    case "guest-gates":GuestGates();return;
                    case "stale-rpc":StaleRpc();return;
                    case "frame":
                        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null){Complete("REJECTED","No render device; no visual acceptance.");return;}
                        ScreenCapture.CaptureScreenshot(Path.Combine(directory,"frame-"+active.sequence+".png"));Complete("REQUESTED","Asynchronous real frame request; file and visual quality need separate verification.");return;
                    case "flat-probe":
                        var routePlayer=active.routePlayerId==0?Game.Session.LocalPlayer:
                            Game.Players.FirstOrDefault(p=>p.Motor.PlayerId==active.routePlayerId)?.Motor;
                        if(active.routePlayerId!=0&&!Game.HasAuthority){Complete("REJECTED","Only host may query a different actual PlayerObject.");return;}
                        result.flatRoute=GameplayFlatRouteProbe.Check(Game,routePlayer,active.destination);
                        Complete(result.flatRoute.clear?"CHECKED":"REJECTED",result.flatRoute.reason);return;
                    case "flat-move":
                        if(active.routePlayerId!=0||active.jump||active.vacuum||active.interact||active.forward||active.backward||active.left||active.right)
                        {Complete("REJECTED","Flat movement is owner-only existing move/Shift input, without extra controls.");return;}
                        result.flatRoute=GameplayFlatRouteProbe.Check(Game,Game.Session.LocalPlayer,active.destination);
                        if(!result.flatRoute.clear){Complete("REJECTED",result.flatRoute.reason);return;}
                        capturedGoal=active.destination;break;
                    case "move":
                        if(Game.Session.LocalPlayer==null||!ClearWalk(Game.Session.LocalPlayer,active.destination))
                        {Complete("REJECTED","Current authored World collision/ground probe blocks this segment.");return;}
                        capturedGoal=active.destination;break;
                    case "collect":
                        var item=FindItem(active.itemId);
                        if(item==null||item.Item.State!=SuckableState.Available||!item.Item.CanBeSwallowedByPlayer||
                            item.Item.RequiredIntakeSize>Game.Session.LocalPlayer.GetComponent<VacuumEmitter>().Definition.IntakeSize)
                        {Complete("REJECTED","Target is not an available player-eligible authored item.");return;}
                        trackedTargetId=active.itemId;sharedSelectionTicks=lastSharedTick=0;targetSelections=lastSharedSelections=Array.Empty<GameplayTargetSelection>();
                        capturedAim=BoundsOf(item.Item).center;capturedGoal=capturedAim;capturedGoal.y=Position().y;break;
                    case "aim-point":
                        var aimOwner=Game.Session.LocalPlayer;
                        if(aimOwner==null||active.forward||active.backward||active.left||active.right||active.jump||active.interact||active.sprint)
                        {Complete("REJECTED","Aim-point uses existing stationary owner mouse and optional vacuum only.");return;}
                        float aimRange=Vector3.Distance(active.destination,aimOwner.transform.position+Vector3.up*aimOwner.Settings.EyeHeight);
                        if(aimRange<.5f||aimRange>8){Complete("REJECTED","Bounded .5..8m actual owner aim point required.");return;}break;
                    case "aim":
                        if(FindItem(active.itemId)==null){Complete("REJECTED","Aim target is absent.");return;}
                        // Diagnostic observation only: establish the fixed target while real owners aim without suction.
                        trackedTargetId=active.itemId;sharedSelectionTicks=lastSharedTick=0;targetSelections=lastSharedSelections=Array.Empty<GameplayTargetSelection>();break;
                    case "sweep":
                        if(Game.Session.LocalPlayer==null){Complete("REJECTED","Actual owner is absent.");return;}
                        var sweepReader=Game.Session.LocalPlayer.GetComponent<PlayerInputReader>();
                        sweepBaseYaw=sweepReader.LatestIntent.Yaw;sweepBasePitch=sweepReader.LatestIntent.Pitch;
                        if(Mathf.Abs(sweepBasePitch)+active.sweepPitch>75){Complete("REJECTED","Neutralize real aim before sweep; avoid pitch clamp coverage artifacts.");return;}
                        sweepStarted=Time.realtimeSinceStartupAsDouble;break;
                    case "keys":break;
                    case "enemy-aim":if(current.Enemy(active.enemyId)==null){Complete("REJECTED","Current live target missing");return;}break;
                    case "watch-shot":case "engineer-player":case "engineer-near":case "engineer-intake":
                        var okay=current.Command(active,out var why);Complete(okay?"CHECKED":"REJECTED",why);return;
                    default:Complete("REJECTED","Unknown explicit command.");return;
                }
            }
            if(active==null)return;
            if(active.kind=="neutral")
            {
                devices.Neutral();
                if(devices.QueuedDynamicFrames>=neutralStartingFrames+2&&devices.NeutralReleaseObserved)
                    Complete("DONE","At least two real dynamic release frames; virtual controls, actions and Reader intent observed neutral.");
                else if(Time.realtimeSinceStartupAsDouble>=commandUntil)Complete("FAIL","Actual neutral release was not observed within two seconds.");
                return;
            }
            if(Game.IsStopping||Game.Session.Phase!=SessionPhase.Playing||active.run!=Game.Session.RunId)
            {Complete("INTERRUPTED","Run/phase changed; devices neutralized.");return;}
            var owner=Game.Session.LocalPlayer;if(owner==null)throw new InvalidOperationException("Local owned PlayerObject is missing.");
            if(active.kind=="sweep")
            {
                double elapsed=Time.realtimeSinceStartupAsDouble-sweepStarted;
                float wave=elapsed>=active.seconds-.5?0:(float)Math.Sin(elapsed*active.sweepHz*Math.PI*2);
                // Feed the existing virtual Mouse -> InputReader -> owner RPC. Never assign aim/pose.
                devices.Aim=true;devices.LookPoint=owner.transform.position+Vector3.up*owner.Settings.EyeHeight+
                    Quaternion.Euler(sweepBasePitch+active.sweepPitch*wave,sweepBaseYaw+active.sweepYaw*wave,0)*Vector3.forward*3;
            }
            else if(active.kind=="keys"||active.kind=="aim"||active.kind=="aim-point"||active.kind=="enemy-aim")
            {devices.Forward=active.forward;devices.Backward=active.backward;devices.Left=active.left;devices.Right=active.right;
             devices.Fire=active.fire;devices.Vacuum=active.vacuum;devices.Interact=active.interact;devices.Jump=active.jump;devices.Sprint=active.sprint;
             if(active.kind=="enemy-aim"){var enemy=current.Enemy(active.enemyId);if(enemy==null||!enemy.IsAlive){Complete("INTERRUPTED","Target retired; inspect actual shot witness.");return;}devices.Aim=true;devices.LookPoint=enemy.GetComponent<BoxCollider>().bounds.center;}
             if(active.kind=="aim-point"){devices.Aim=true;devices.LookPoint=active.destination;}
             if(active.kind=="aim"){var aimTarget=FindItem(active.itemId);if(aimTarget==null){Complete("OBSERVED_REMOVAL","Aim target removed; verify host ledger.");return;}devices.Aim=true;devices.LookPoint=BoundsOf(aimTarget.Item).center;}}
            else
            {
                bool collect=active.kind=="collect";var target=collect?FindItem(active.itemId):null;
                if(collect&&target!=null&&target.Item.State==SuckableState.Stored){Complete(target.Item.StoredOwner==owner.PlayerId?"STORED":"REJECTED","Same-key committed storage owner observed; no delivery or money implied.");return;}
                if(collect&&(target==null||target.Item.State==SuckableState.Delivered))
                {Complete("OBSERVED_REMOVAL","Target disappeared; only host collection ledger may prove success.");return;}
                devices.Aim=true;devices.LookPoint=collect?BoundsOf(target.Item).center:capturedGoal+Vector3.up*owner.Settings.EyeHeight;
                devices.Vacuum=collect;devices.Sprint=!collect&&active.sprint;
                var delta=capturedGoal-Position();delta.y=0;float stop=collect?1.55f:active.stopDistance;
                if(!collect&&delta.magnitude<=stop){Complete("DONE","Reached measured target through owner input.");return;}
                bool ingesting=collect&&target.Item.State==SuckableState.Ingesting;
                float desired=Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg;
                devices.Forward=!ingesting&&delta.magnitude>stop&&Mathf.Abs(Mathf.DeltaAngle(owner.GetComponent<PlayerInputReader>().LatestIntent.Yaw,desired))<12;
                if(devices.Forward)
                {
                    var step=Position()+delta.normalized*Mathf.Min(.6f,delta.magnitude-stop);
                    if(active.kind=="flat-move")
                    {
                        var currentRoute=GameplayFlatRouteProbe.Check(Game,owner,step);result.flatRouteChecks++;
                        if(!currentRoute.clear){result.flatRoute=currentRoute;Complete("BLOCKED",currentRoute.reason);return;}
                    }
                    else if(!ClearWalk(owner,step)){Complete("BLOCKED","Measured next movement segment is obstructed; no pose override.");return;}
                }
            }
            if(Time.realtimeSinceStartupAsDouble>=commandUntil)Complete(active.kind=="enemy-aim"||active.kind=="aim-point"||active.kind=="sweep"||active.kind=="keys"||active.kind=="aim"?"DONE":"TIMEOUT","Bounded input command ended; physical outcome must be observed.");
        }
        private void GuestGates()
        {
            if(Game.HasAuthority){Complete("REJECTED","Guest-only negative-authority engineering probe.");return;}
            var owner=Game.Session.LocalPlayer;var item=Game.Items.FirstOrDefault(i=>i.Item.State==SuckableState.Available);
            if(owner==null||item==null)throw new InvalidOperationException("Prepared guest player and available item required.");
            bool frozen=item.Item.WorldFrozen;Vector3 before=owner.transform.position;
            bool kinematic,transition;
            try{item.Item.SetWorldFrozen(false);kinematic=item.Item.Body.isKinematic;transition=item.Item.TryTransition(SuckableState.Available,SuckableState.Ingesting);}
            finally{item.Item.SetWorldFrozen(frozen);}
            var intent=new PlayerIntent{RunId=Game.Session.RunId,Sequence=uint.MaxValue-1,Move=Vector2.up,Yaw=owner.LastIntent.Yaw};
            owner.Step(intent,.02f);bool submitted=Game.Session.World.SubmitIntent(owner.PlayerId,intent);
            bool passed=kinematic&&!transition&&!submitted&&owner.transform.position==before&&!owner.HasMovementAuthority&&!owner.GetComponent<CharacterController>().enabled&&Game.Session.Campaign==null;
            Complete(passed?"CHECKED":"FAIL","Negative authority only: guest unfreeze stays kinematic, transition/intent reject, Motor.Step no movement, no campaign. Not gameplay success.");
        }
        private void StaleRpc()
        {
            if(Game.HasAuthority){Complete("REJECTED","Run this engineering old-run packet probe on guest owner.");return;}
            var player=Game.Players.Single(p=>p.IsOwner);var current=player.Input.LatestIntent;
            // Separate fault injection through the actual NGO-generated owner RPC; never used for positive gameplay evidence.
            var packet=new IntentWire{Run=new FixedString64Bytes("00000000000000000000000000000000"),Sequence=uint.MaxValue-2,Move=Vector2.up,Yaw=current.Yaw,Pitch=current.Pitch,Vacuum=true,Interact=true};
            var method=typeof(NetworkPlayerAdapter).GetMethod("SubmitIntentRpc",BindingFlags.Instance|BindingFlags.NonPublic);
            if(method==null)throw new InvalidOperationException("Reviewed owner RPC binding changed.");
            method.Invoke(player,new object[]{packet,default(RpcParams)});
            Complete("SENT_ENGINEERING_PROBE","Injected one mismatched-run packet via owner RPC. Host post-snapshot is required to prove rejection.");
        }
        private void CaptureAuthoritySelection()
        {
            if(Game==null||!Game.HasAuthority||!Game.Session.World.IsRunning||selectionProbe==null||trackedTargetId==0)return;
            var target=FindItem(trackedTargetId);var values=new List<GameplayTargetSelection>();
            if(target==null||target.Item.State!=SuckableState.Available){targetSelections=Array.Empty<GameplayTargetSelection>();return;}
            foreach(var player in Game.Players)
            {
                if(player==null)continue;var emitter=player.GetComponent<VacuumEmitter>();var receiver=player.GetComponent<IntakeReceiver>();
                var value=new GameplayTargetSelection{targetId=trackedTargetId,clientId=player.OwnerClientId,playerId=player.Motor.PlayerId,worldTick=Game.Session.World.TickCount,
                    sourceActive=emitter!=null&&emitter.Active,bodySimulating=target.Item.HasPhysicsAuthority&&!target.Item.Body.isKinematic&&!target.Item.WorldFrozen};
                if(emitter!=null)
                {
                    value.source=emitter.Position;value.forward=emitter.Forward;
                    // Public, read-only production geometry queries. Do not activate the emitter or apply any force.
                    value.sourcePathClear=emitter.HasClearSourcePath();
                    value.targetSurfaceVisible=selectionProbe.TryFindSurface(emitter,target.Item,out var visiblePoint);
                    value.visiblePoint=visiblePoint;
                    // Invoke the real shared selector through an isolated query cache. Never call Apply/AddForce/Admit or mutate world state.
                    foreach(var contact in selectionProbe.Collect(emitter))if(contact.Item==target.Item)
                    {value.selectedBySharedCollector=true;value.selectedPoint=contact.Point;value.distance=contact.Distance;break;}
                    value.intakeGeometryEligible=receiver!=null&&receiver.CanAdmit&&receiver.Accepts(target.Item)&&
                        selectionProbe.TryFindIntakeSurface(emitter,target.Item,receiver.Position,receiver.AdmissionRadius,out _);
                }
                values.Add(value);
            }
            targetSelections=values.ToArray();
            if(values.Count(v=>v.sourceActive&&v.bodySimulating&&v.selectedBySharedCollector)>=2)
            {sharedSelectionTicks++;lastSharedTick=Game.Session.World.TickCount;lastSharedSelections=targetSelections;}
        }
        private NetworkLootAdapter FindItem(ulong id)=>Game.Items.FirstOrDefault(i=>i!=null&&i.Item.InstanceId==id);
        private Vector3 Position()=>Game.Session.LocalPlayer!=null?Game.Session.LocalPlayer.transform.position:Vector3.zero;
        private static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
        private static Bounds BoundsOf(SuckableObject item)
        {var colliders=item.GameplayColliders.Where(c=>c!=null&&!c.isTrigger).ToArray();var bounds=colliders.Length>0&&colliders[0].enabled?colliders[0].bounds:new Bounds(item.transform.position,Vector3.zero);foreach(var c in colliders.Skip(1))bounds.Encapsulate(c.bounds);return bounds;}
        private static bool ClearWalk(PlayerMotor player,Vector3 goal)
        {
            int mask=LayerMask.GetMask("World");if(mask==0)return false;
            var controller=player.GetComponent<CharacterController>();var origin=player.transform.position;
            if(Mathf.Abs(goal.y-origin.y)>.3f)return false;
            var delta=goal-origin;delta.y=0;float distance=delta.magnitude;if(distance>6)return false;
            float radius=controller.radius*.9f;var center=origin+controller.center;
            Vector3 lower=center-Vector3.up*(controller.height*.5f-radius-.06f),upper=center+Vector3.up*(controller.height*.5f-radius);
            if(distance>.01f&&Physics.CapsuleCast(lower,upper,radius,delta.normalized,distance,mask,QueryTriggerInteraction.Ignore))return false;
            for(float d=0;d<=distance+.2f;d+=.2f)
            {var point=origin+(distance>.01f?delta.normalized*Mathf.Min(d,distance):Vector3.zero);
             if(!Physics.Raycast(point+Vector3.up*.5f,Vector3.down,out var hit,1.1f,mask,QueryTriggerInteraction.Ignore)||hit.normal.y<.65f)return false;}
            return true;
        }
        private GameplayObservation Observe()
        {
            var session=Game.Session;var state=session.ContractState;
            var level=session.CurrentLevel!=null?session.CurrentLevel:FindFirstObjectByType<LevelContext>();
            var area=level!=null&&level.ExtractionZone!=null?level.ExtractionZone.Area:null;var truck=level!=null?level.Truck:null;
            var players=Game.Players.Where(p=>p!=null).Select(p=>new GameplayPlayerObservation{clientId=p.OwnerClientId,objectId=p.NetworkObjectId,playerId=p.Motor.PlayerId,
                readerSequence=p.Input.LatestIntent.Sequence,readerMove=p.Input.LatestIntent.Move,motorMove=p.Motor.LastIntent.Move,readerVacuum=p.Input.LatestIntent.VacuumHeld,readerInteract=p.Input.LatestIntent.InteractHeld,
                position=p.transform.position,yaw=p.Motor.LastIntent.Yaw,pitch=p.Motor.LastIntent.Pitch,sequence=p.Motor.LastIntent.Sequence,jumpSequence=p.Motor.LastIntent.JumpPressSequence,
                owner=p.IsOwner,controllerEnabled=p.GetComponent<CharacterController>().enabled,motorAuthority=p.Motor.HasMovementAuthority,inputInitialized=p.Input.IsInitialized,
                inputEnabled=p.Input.enabled,inputAvailable=p.Input.GameplayAvailable,menu=p.Input.MenuOpen,vacuum=p.Motor.LastIntent.VacuumHeld,interact=p.Motor.LastIntent.InteractHeld,
                rigPresent=p.GetComponent<PlayerAnimationView>()?.Body!=null}).ToArray();
            var items=Game.Items.Where(i=>i!=null).Select(i=>{var b=BoundsOf(i.Item);return new GameplayItemObservation{id=i.Item.InstanceId,objectId=i.NetworkObjectId,type=i.Item.TypeId,
                state=i.Item.State.ToString(),position=i.transform.position,center=b.center,size=b.size,kinematic=i.Item.Body.isKinematic,authority=i.Item.HasPhysicsAuthority,frozen=i.Item.WorldFrozen,
                playerEligible=i.Item.CanBeSwallowedByPlayer,truckEligible=i.Item.CanBeSwallowedByTruck,requiredSize=i.Item.RequiredIntakeSize,value=i.Item.Value};}).ToArray();
            var records=Game.HasAuthority?session.World.Ingestion.Records.Select(v=>new GameplayCollectionObservation{run=v.RunId,type=v.TypeId,id=v.InstanceId,playerId=v.PlayerId,intakeId=v.IntakeId,value=v.Value,truck=v.IsTruck}).ToArray():Array.Empty<GameplayCollectionObservation>();
            var clear=new List<Vector3>();if(session.LocalPlayer!=null)for(int n=0;n<8;n++){var point=Position()+Quaternion.Euler(0,n*45,0)*Vector3.forward*1.1f;if(ClearWalk(session.LocalPlayer,point))clear.Add(point);}
            return new GameplayObservation{current=current.Observe(),actionFire=devices.ActualActionFire,nonce=Startup.Profile.Nonce,slot=slot,pid=pid,clientId=Game.Manager.LocalClientId,utc=DateTime.UtcNow.ToString("O"),frame=Time.frameCount,
                scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,canStart=session.CanStartContract,localReady=Game.Control!=null&&Game.Control.LocalReady,run=session.RunId,phase=session.Phase.ToString(),terminal=state.Phase.ToString(),authority=Game.HasAuthority,running=session.World.IsRunning,worldTicks=session.World.TickCount,
                inputFrames=devices.QueuedDynamicFrames,playerCount=players.Length,itemCount=items.Length,realtime=Time.realtimeSinceStartupAsDouble,serverTime=Game.Driver.Now,
                startedAt=state.StartedAt,deadline=state.Deadline,observedAt=state.ObservedAt,hold=state.ExtractHoldProgress,remaining=state.RemainingSeconds,money=state.DeliveredValue,
                quota=state.Quota,balance=session.DisplayedBalance,payout=session.Result?.Payout??0,localCampaignExists=session.Campaign!=null,allInside=session.World.AllPlayersInExtraction,
                localInside=session.LocalPlayer!=null&&session.World.IsPlayerInExtraction(session.LocalPlayer.PlayerId),pendingPayout=Game.Control!=null&&Game.Control.Snapshot.Value.PendingPayout,
                observedTargetId=trackedTargetId,targetSharedSelectionTicks=sharedSelectionTicks,targetLastSharedTick=lastSharedTick,targetSelections=targetSelections,targetLastSharedSelections=lastSharedSelections,
                targetWitnessScope="Authority end-of-step read-only shared Collect selection eligibility, not a record of forces previously applied.",
                boundsLostCount=level!=null&&level.BoundsGuard!=null?level.BoundsGuard.TotalLost:0,
                boundsMinimum=level!=null&&level.BoundsGuard!=null?level.BoundsGuard.AllowedBounds.min:Vector3.zero,boundsMaximum=level!=null&&level.BoundsGuard!=null?level.BoundsGuard.AllowedBounds.max:Vector3.zero,
                runtimeErrors=runtimeErrors.ToArray(),focusReacquisitions=devices.FocusReacquisitions,cloneBindings=devices.CloneBindings,boundActionInstanceId=devices.BoundActionInstanceId,
                readerFocused=devices.ReaderFocused,actionsEnabled=devices.ActionsEnabled,actionMove=devices.ActualActionMove,actionVacuum=devices.ActualActionVacuum,actionInteract=devices.ActualActionInteract,
                neutralDynamicFrames=devices.ConsecutiveNeutralDynamicFrames,neutralReleaseObserved=devices.NeutralReleaseObserved,
                extractionCenter=area!=null?area.bounds.center:Vector3.zero,extractionSize=area!=null?area.bounds.size:Vector3.zero,
                truckSource=truck!=null&&truck.Emitter.Source!=null?truck.Emitter.Source.position:Vector3.zero,truckForward=truck!=null&&truck.Emitter.Source!=null?truck.Emitter.Source.forward:Vector3.zero,
                players=players,items=items,collections=records,clearMoveDestinations=clear.ToArray(),lastSequence=lastSequence,activeSequence=active?.sequence??0,
                lastCommandStatus=lastStatus,lastCommandDetail=lastDetail,renderEvidence="Not accepted by this observer; real frames require separate review."};
        }
        private void Complete(string status,string detail)
        {
            devices?.Neutral();if(active==null)return;lastStatus=status;lastDetail=detail;
            result.status=status;result.detail=detail;result.utc=DateTime.UtcNow.ToString("O");result.endAt=Game.Driver.Now;result.endPosition=Position();result.endMoney=Game.Session.ContractState.DeliveredValue;
            result.queuedInputFrames=devices.QueuedDynamicFrames-startingFrames;Atomic("result-"+active.sequence.ToString("D5")+".json",JsonUtility.ToJson(result,true));active=null;result=null;
        }
        private void Atomic(string name,string json)
        {string target=Path.Combine(directory,name),temporary=target+".tmp";File.WriteAllText(temporary,json);if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);}
        private void DisableOnError(Exception error)
        {disabledByError=true;devices?.Dispose();devices=null;Debug.LogException(error,this);if(directory!=null)Atomic("harness-error.txt",error.GetType().Name+": "+error.Message);}
        private void OnDestroy(){if(Game!=null&&Game.Session!=null)Game.Session.World.SnapshotChanged-=CaptureAuthoritySelection;Application.logMessageReceived-=RecordRuntimeError;devices?.Dispose();devices=null;}
        private void OnDisable(){devices?.Dispose();devices=null;disabledByError=true;}
    }
}
#endif
