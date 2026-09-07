#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HowToSuck.Networking;
using UnityEngine;
using UnityEngine.Rendering;
namespace HowToSuck.Diagnostics
{
    // Instrumentation only. No writes to a player, network variable, intake or animation pose.
    [DefaultExecutionOrder(31000), DisallowMultipleComponent]
    public sealed class RemotePresentationObserver : MonoBehaviour
    {
        public GameplayDiagnosticRunner Runner;
        [Serializable] public sealed class Command
        { public string nonce,run,kind,mode,phase; public int slot,sequence,fps; public float seconds=24; }
        [Serializable] public sealed class Receipt
        { public string nonce,run,mode,phase,status,error,utc,path; public int slot,pid,sequence,frame,rows,wireEvents; public double realtime; }
        [Serializable] public sealed class Sample
        {
            public string nonce,run,wireRun,mode,phase,utc,state,nextState,toolState;
            public int slot,pid,frame,targetFps,vsync,playerId;
            public ulong localClient,ownerClient,objectId,worldTick;
            public uint revision,sequence,readerSequence;
            public double realtime,serverTime,fixedTime,frameTime,lateWall,earlyObserverWall;
            public ulong wireEventId;
            public int wireYawBits,wirePitchBits,wireSpeedBits;
            public float dt,wireSpeed,motorSpeed,animationSpeed,previousMotorSpeed;
            public float wireYaw,wirePitch,readerYaw,readerPitch,rootYaw,bodyYaw,renderPitch;
            public float rootStep,bodyStep,rootAngleStep,bodyAngleStep,toolAngleStep;
            public float bodyRootError,bodyAimYawError,toolAimError,toolMountError,intakePositionError,intakeRotationError,gripL,gripR;
            public bool authority,isClient,isServer,listening,owner,localView,motorAuthority,controllerEnabled,networkTransformEnabled,networkInterpolate;
            public bool frozen,grounded,sprint,transition,animationEnabled,bodyVisible,headVisible,toolVisible,markerPresent;
            public Vector3 root,body,tool,intake,physicalIntake,nozzle,readerMove;
            public Quaternion rootRotation,bodyRotation,toolRotation,aimRotation;
        }
        private sealed class Previous { public Vector3 root,body; public Quaternion rootQ,bodyQ,toolQ; public float speed; }
        private readonly Dictionary<ulong,Previous> previous=new Dictionary<ulong,Previous>();
        private string directory,mode,phase,run,path; private int slot,pid,lastSequence,rows;
        private int oldFps,oldVsync; private bool armed,capturing,settingsOwned;
        private double began,until,nextStatus,nextRead; private int fps;
        private StreamWriter writer;
        private RemoteWireJournal wireJournal=new RemoteWireJournal();
        private double earlyObserverWall;
        private NgoGameSession Game=>Runner.Game;
        private void Start()
        {
            try {
                if(Runner==null||Runner.Game==null||Runner.Startup==null||Runner.Startup.Profile==null)throw new InvalidOperationException("Bind the actual gameplay runner.");
                var profile=Runner.Startup.Profile;slot=profile.Slot;pid=System.Diagnostics.Process.GetCurrentProcess().Id;
                string root=Runner.ReportsRoot;var args=Environment.GetCommandLineArgs();
                for(int i=0;i+1<args.Length;i++)if(args[i]=="--hts-gameplay-reports")root=args[++i];
                directory=Path.Combine(Path.GetFullPath(root),profile.Nonce,"player-"+slot+"-"+pid);Directory.CreateDirectory(directory);
                Write("remote-identity.json",new Receipt{nonce=profile.Nonce,slot=slot,pid=pid,status="READY",utc=DateTime.UtcNow.ToString("O")});
            } catch(Exception e){Fail(e);}
        }
        private void Update()
        {
            if(directory==null)return;
            earlyObserverWall=Time.realtimeSinceStartupAsDouble;
            try {
                wireJournal.Observe(Game);
                if(Time.realtimeSinceStartupAsDouble>=nextRead){nextRead=Time.realtimeSinceStartupAsDouble+.05;ReadCommand();}
                if(!armed)return;
                if(Game.IsStopping||Game.Session.Phase!=SessionPhase.Playing||Game.Session.RunId!=run)throw new InvalidOperationException("Observed run/phase changed during timing.");
                // Explicit renderer throttling: no simulated packet delay/loss and no blocking sleep.
                Application.targetFrameRate=fps==0?((Time.realtimeSinceStartupAsDouble-began)%.8<.50?120:8):fps;
                if(Time.realtimeSinceStartupAsDouble>=until)Finish("EXPIRED", "Missing explicit stop before bounded observation timeout.");
            }catch(Exception e){Fail(e);}
        }
        private void ReadCommand()
        {
            var p=Path.Combine(directory,"remote-command.json");if(!File.Exists(p))return;
            if(!DiagnosticCommandFile.TryRead(p,4096,out var commandJson))return;
            Command c;try{c=JsonUtility.FromJson<Command>(commandJson);}catch(ArgumentException){return;}
            if(c==null||c.sequence<=lastSequence)return;
            if(c.nonce!=Runner.Startup.Profile.Nonce||c.slot!=slot)throw new InvalidOperationException("Observer command routed to another process.");
            lastSequence=c.sequence;
            if(capturing)throw new InvalidOperationException("Capture is still pending; commands must be serialized.");
            if(c.kind=="arm"){
                if(armed||Game.Session.Phase!=SessionPhase.Playing||c.run!=Game.Session.RunId||string.IsNullOrEmpty(c.run))throw new InvalidOperationException("Arm requires the actual new Playing run.");
                if(!(c.fps==0||c.fps==30||c.fps==60||c.fps==120)||float.IsNaN(c.seconds)||c.seconds<3||c.seconds>30)throw new InvalidOperationException("Invalid frame throttle/duration.");
                if(string.IsNullOrEmpty(c.mode)||c.mode.Any(ch=>!char.IsLetterOrDigit(ch)&&ch!='-'))throw new InvalidOperationException("Mode must be a safe evidence label.");
                run=c.run;mode=c.mode;phase="warmup";fps=c.fps;rows=0;previous.Clear();
                path=Path.Combine(directory,"remote-"+c.sequence+"-"+mode+".jsonl");
                writer=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new System.Text.UTF8Encoding(false),65536);
                oldFps=Application.targetFrameRate;oldVsync=QualitySettings.vSyncCount;settingsOwned=true;
                QualitySettings.vSyncCount=0;began=Time.realtimeSinceStartupAsDouble;until=began+c.seconds;armed=true;
            }else if(c.kind=="phase"){
                if(!armed||c.run!=run||string.IsNullOrEmpty(c.phase)||c.phase.Length>48)throw new InvalidOperationException("Phase must label the current armed run.");phase=c.phase;
            }else if(c.kind=="stop"){
                if(!armed||c.run!=run)throw new InvalidOperationException("Stop requires the current armed run.");Finish("STOPPED",null);
             }else if(c.kind=="journal"){
                if(armed||c.run!=run||path==null)throw new InvalidOperationException("Journal export follows both processes' confirmed timing stop.");
                wireJournal.Write(path+".wire.jsonl",Runner.Startup.Profile.Nonce,slot,pid);
            }else if(c.kind=="capture"){
                if(armed||c.run!=Game.Session.RunId||Game.Session.Phase!=SessionPhase.Playing)throw new InvalidOperationException("Capture must be separate from timing in actual Playing.");
                capturing=true;StartCoroutine(Capture(c));return;
            }else throw new InvalidOperationException("Unknown observer command.");
            Write("remote-result-"+c.sequence.ToString("D5")+".json",Status("DONE",null));
        }
        private void LateUpdate()
        {
            if(!armed)return;
            try {
                double lateWall=Time.realtimeSinceStartupAsDouble;
                foreach(var p in Game.Players.Where(x=>x!=null&&x.IsSpawned))SamplePlayer(p,lateWall);
                if(Time.realtimeSinceStartupAsDouble>=nextStatus){nextStatus=Time.realtimeSinceStartupAsDouble+.25;writer.Flush();Write("remote-status.json",Status("RECORDING",null));}
            }catch(Exception e){Fail(e);}
        }
        private static float Pitch(Quaternion q)=>Mathf.DeltaAngle(0,q.eulerAngles.x);
        private static string State(AnimatorStateInfo s){foreach(string n in new[]{"Idle","Walk","Run","JumpStart","JumpAir","JumpLand","VacuumHold","VacuumActive","ExtractHold"})if(s.IsName(n))return n;return s.fullPathHash.ToString();}
        private void SamplePlayer(NetworkPlayerAdapter p,double lateWall)
        {
            var m=p.Motor;var a=p.GetComponent<PlayerAnimationView>();var v=p.GetComponent<PlayerView>();var cc=p.GetComponent<CharacterController>();
            if(m==null||a==null||v==null||a.VisualRoot==null||a.Animator==null||a.RemoteTool==null)throw new InvalidOperationException("Actual player presentation composition is incomplete.");
            var nt=p.GetComponent<Unity.Netcode.Components.NetworkTransform>();
            var w=p.Snapshot.Value;var body=a.VisualRoot;var tool=v.IsLocal?a.LocalTool:a.RemoteTool;var r=m.GetComponent<IntakeReceiver>();var intake=r!=null?r.PresentationTarget:null;
            if(tool==null)throw new InvalidOperationException("Actual player tool is absent.");
            previous.TryGetValue(p.NetworkObjectId,out var prev);
            var aim=a.RenderedAim;float pitch=Pitch(aim);bool validMount=m.TryGetNozzleLocalPosition(pitch,out var mount);
            var expectedTool=m.GetRenderPosition()+Vector3.up*m.CameraLocalMount.y+aim*mount;
            var expectedIntake=r!=null&&m.NozzleAnchor!=null?tool.TransformPoint(m.NozzleAnchor.InverseTransformPoint(r.Position)):Vector3.zero;
            var expectedIntakeQ=r!=null&&m.NozzleAnchor!=null?tool.rotation*Quaternion.Inverse(m.NozzleAnchor.rotation)*r.Rotation:Quaternion.identity;
            var latest=p.Input.LatestIntent;
            var s=new Sample{
                nonce=Runner.Startup.Profile.Nonce,run=run,wireRun=w.Run.ToString(),mode=mode,phase=phase,utc=DateTime.UtcNow.ToString("O"),slot=slot,pid=pid,
                frame=Time.frameCount,targetFps=Application.targetFrameRate,vsync=QualitySettings.vSyncCount,playerId=m.PlayerId,
                localClient=Game.Manager.LocalClientId,ownerClient=p.OwnerClientId,objectId=p.NetworkObjectId,worldTick=Game.Session.World.TickCount,revision=w.Revision,sequence=w.Sequence,readerSequence=latest.Sequence,
                realtime=Time.realtimeSinceStartupAsDouble,serverTime=Game.Driver.Now,fixedTime=Time.fixedTimeAsDouble,dt=Time.unscaledDeltaTime,
                frameTime=Time.unscaledTimeAsDouble,lateWall=lateWall,earlyObserverWall=earlyObserverWall,wireEventId=wireJournal.EventFor(p,w),
                wireYawBits=BitConverter.SingleToInt32Bits(w.Yaw),wirePitchBits=BitConverter.SingleToInt32Bits(w.Pitch),wireSpeedBits=BitConverter.SingleToInt32Bits(w.PlanarSpeed),
                wireSpeed=w.PlanarSpeed,motorSpeed=m.PlanarSpeed,animationSpeed=a.CurrentSpeed,previousMotorSpeed=prev!=null?prev.speed:m.PlanarSpeed,
                wireYaw=w.Yaw,wirePitch=w.Pitch,readerYaw=latest.Yaw,readerPitch=latest.Pitch,readerMove=new Vector3(latest.Move.x,0,latest.Move.y),
                rootYaw=p.transform.eulerAngles.y,bodyYaw=body.eulerAngles.y,renderPitch=pitch,
                rootStep=prev!=null?Vector3.Distance(prev.root,p.transform.position):0,bodyStep=prev!=null?Vector3.Distance(prev.body,body.position):0,
                rootAngleStep=prev!=null?Quaternion.Angle(prev.rootQ,p.transform.rotation):0,bodyAngleStep=prev!=null?Quaternion.Angle(prev.bodyQ,body.rotation):0,toolAngleStep=prev!=null?Quaternion.Angle(prev.toolQ,tool.rotation):0,
                bodyRootError=Vector3.Distance(body.position,m.GetRenderPosition()),bodyAimYawError=Mathf.Abs(Mathf.DeltaAngle(body.eulerAngles.y,aim.eulerAngles.y)),
                toolAimError=Quaternion.Angle(tool.rotation,aim),toolMountError=validMount?Vector3.Distance(tool.position,expectedTool):-1,
                intakePositionError=intake!=null?Vector3.Distance(intake.position,expectedIntake):-1,intakeRotationError=intake!=null?Quaternion.Angle(intake.rotation,expectedIntakeQ):-1,
                gripL=a.Left.Socket!=null&&a.RemoteGripL!=null?Vector3.Distance(a.Left.Socket.position,a.RemoteGripL.position):-1,
                gripR=a.Right.Socket!=null&&a.RemoteGripR!=null?Vector3.Distance(a.Right.Socket.position,a.RemoteGripR.position):-1,
                networkTransformEnabled=nt!=null&&nt.isActiveAndEnabled,networkInterpolate=nt!=null&&nt.Interpolate,
                authority=Game.HasAuthority,isClient=Game.Manager.IsClient,isServer=Game.Manager.IsServer,listening=Game.Manager.IsListening,owner=p.IsOwner,localView=v.IsLocal,motorAuthority=m.HasMovementAuthority,controllerEnabled=cc!=null&&cc.enabled,
                frozen=w.Frozen,grounded=w.Grounded,sprint=w.Sprint,transition=a.Animator.IsInTransition(0),animationEnabled=a.isActiveAndEnabled&&a.Animator.isActiveAndEnabled,
                bodyVisible=a.Body!=null&&a.Body.enabled&&a.Body.gameObject.activeInHierarchy,headVisible=a.Head!=null&&a.Head.enabled&&a.Head.gameObject.activeInHierarchy&&a.Head.shadowCastingMode!=ShadowCastingMode.ShadowsOnly,
                toolVisible=tool.gameObject.activeInHierarchy,markerPresent=intake!=null,
                root=p.transform.position,body=body.position,tool=tool.position,intake=intake!=null?intake.position:Vector3.zero,physicalIntake=r!=null?r.Position:Vector3.zero,nozzle=m.NozzleAnchor!=null?m.NozzleAnchor.position:Vector3.zero,
                rootRotation=p.transform.rotation,bodyRotation=body.rotation,toolRotation=tool.rotation,aimRotation=aim,
                state=State(a.Animator.GetCurrentAnimatorStateInfo(0)),nextState=State(a.Animator.GetNextAnimatorStateInfo(0)),toolState=State(a.Animator.GetCurrentAnimatorStateInfo(1))
            };
            writer.WriteLine(JsonUtility.ToJson(s));rows++;previous[p.NetworkObjectId]=new Previous{root=s.root,body=s.body,rootQ=s.rootRotation,bodyQ=s.bodyRotation,toolQ=s.toolRotation,speed=m.PlanarSpeed};
        }
        private Receipt Status(string status,string error)=>new Receipt{nonce=Runner.Startup.Profile.Nonce,slot=slot,pid=pid,sequence=lastSequence,run=run,mode=mode,phase=phase,status=status,error=error,utc=DateTime.UtcNow.ToString("O"),frame=Time.frameCount,realtime=Time.realtimeSinceStartupAsDouble,rows=rows,path=path,wireEvents=wireJournal!=null?wireJournal.Count:0};
        private void Write(string name,object value){string target=Path.Combine(directory,name),tmp=target+".tmp";File.WriteAllText(tmp,JsonUtility.ToJson(value,true));if(File.Exists(target))File.Replace(tmp,target,null);else File.Move(tmp,target);}
        private void Finish(string status,string error)
        {
            armed=false;
            try {
                writer?.Dispose();writer=null;

            } catch(Exception e) {status="FAILED";error=(error??"")+"\n"+e;}
            finally {RestoreSettings();}
            if(directory!=null) {
                Write("remote-status.json",Status(status,error));
                if(status=="FAILED")Write("remote-error.json",Status(status,error));
            }
        }
        private void RestoreSettings(){if(settingsOwned){Application.targetFrameRate=oldFps;QualitySettings.vSyncCount=oldVsync;settingsOwned=false;}}
        private void Fail(Exception e){Finish("FAILED",e.ToString());if(directory!=null)Write("remote-error.json",Status("FAILED",e.ToString()));Debug.LogException(e,this);enabled=false;}
        private void OnEnable(){if(wireJournal==null)wireJournal=new RemoteWireJournal();}
        private void OnDisable(){try{if(armed)Finish("INTERRUPTED","Observer disabled.");}finally{RestoreSettings();wireJournal?.Dispose();wireJournal=null;}}
        private void OnDestroy(){try{writer?.Dispose();}finally{RestoreSettings();wireJournal?.Dispose();wireJournal=null;}}
        [Serializable] private sealed class CaptureEvidence
        {public string scope,utc,run,ownerPng,remotePng;public int slot,pid,frame,width,height;public ulong remoteOwner,remoteObject;public Vector3 cameraPosition,remotePosition;public Quaternion cameraRotation;public float fov;public bool remoteHeadVisible,bodyInFrustum,wallOccluded;}
        private IEnumerator Capture(Command c)
        {
            Camera cam=null;RenderTexture rt=null;Texture2D tex=null;Exception failure=null;CaptureEvidence evidence=null;
            string stem=Path.Combine(directory,"capture-"+c.sequence),ownerFile=stem+"-owner.png",remoteFile=stem+"-remote.png";
            yield return new WaitForEndOfFrame();
            try{
                if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)throw new InvalidOperationException("Actual graphics device required.");
                var p=Game.Players.Single(x=>x!=null&&x.IsSpawned&&!x.IsOwner);var a=p.GetComponent<PlayerAnimationView>();
                var ownerCamera=Game.Session.LocalPlayer.GetComponentsInChildren<Camera>(true).Single(x=>x.isActiveAndEnabled);
                if(a==null||a.Body==null||a.Head==null||!a.Body.enabled||!a.Head.enabled)throw new InvalidOperationException("Remote body/head unavailable for capture.");
                if(File.Exists(ownerFile)||File.Exists(remoteFile))throw new InvalidOperationException("Capture must be fresh.");
                ScreenCapture.CaptureScreenshot(ownerFile);
                cam=new GameObject("Explicit remote engineering camera").AddComponent<Camera>();cam.CopyFrom(ownerCamera);cam.enabled=false;cam.fieldOfView=55;cam.nearClipPlane=.03f;cam.aspect=1280f/720;
                Vector3 target=p.transform.position+Vector3.up*.95f;bool clear=false;
                foreach(float yaw in new[]{35f,-35f,145f,-145f,90f,-90f,0f,180f}){
                    Vector3 pos=target+Quaternion.Euler(0,p.transform.eulerAngles.y+yaw,0)*new Vector3(0,.35f,3.4f);
                    if(Physics.Linecast(pos,target,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore))continue;
                    cam.transform.SetPositionAndRotation(pos,Quaternion.LookRotation(target-pos));clear=true;break;
                }
                if(!clear)throw new InvalidOperationException("No unobstructed diagnostic camera angle; root must inspect actual scene.");
                rt=new RenderTexture(1280,720,24);rt.Create();cam.targetTexture=rt;cam.Render();
                var old=RenderTexture.active;try{RenderTexture.active=rt;tex=new Texture2D(1280,720,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();File.WriteAllBytes(remoteFile,tex.EncodeToPNG());}finally{RenderTexture.active=old;}
                evidence=new CaptureEvidence{scope="Actual standalone remote mesh rendered by a separate engineering camera. Owner image is ScreenCapture with UI. Timing is disarmed; not a gameplay-camera/art PASS.",utc=DateTime.UtcNow.ToString("O"),run=Game.Session.RunId,slot=slot,pid=pid,frame=Time.frameCount,width=1280,height=720,ownerPng=ownerFile,remotePng=remoteFile,remoteOwner=p.OwnerClientId,remoteObject=p.NetworkObjectId,cameraPosition=cam.transform.position,cameraRotation=cam.transform.rotation,remotePosition=p.transform.position,fov=cam.fieldOfView,remoteHeadVisible=a.Head.gameObject.activeInHierarchy&&a.Head.shadowCastingMode!=ShadowCastingMode.ShadowsOnly,bodyInFrustum=GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam),a.Body.bounds),wallOccluded=false};
            }catch(Exception e){failure=e;}
            finally{if(cam!=null)Destroy(cam.gameObject);if(rt!=null){rt.Release();Destroy(rt);}if(tex!=null)Destroy(tex);}
            double deadline=Time.realtimeSinceStartupAsDouble+5;
            while(failure==null&&(!File.Exists(ownerFile)||new FileInfo(ownerFile).Length<100)&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            capturing=false;
            if(failure==null&&(!File.Exists(ownerFile)||new FileInfo(ownerFile).Length<100))failure=new IOException("Actual owner screenshot did not finish writing.");
            if(failure!=null){Fail(failure);yield break;}
            Write("capture-"+c.sequence+".json",evidence);Write("remote-result-"+c.sequence.ToString("D5")+".json",Status("CAPTURED_REVIEW_REQUIRED",null));
        }
    }
}
#endif
