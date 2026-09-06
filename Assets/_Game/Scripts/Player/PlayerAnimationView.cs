using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace HowToSuck
{
    // One full skeleton for both local and remote presentation; runs after the rendered camera/tool.
    [DefaultExecutionOrder(1500), DisallowMultipleComponent]
    public sealed class PlayerAnimationView : MonoBehaviour
    {
        public PlayerMotor Motor; public AuthorityWorld World;
        public PlayerView View;
        public Animator Animator;
        public Transform VisualRoot;
        public SkinnedMeshRenderer Body, Head;
        public Transform Spine, Chest;
        public Transform LocalTool, RemoteTool;
        public Transform LocalGripL, LocalGripR, RemoteGripL, RemoteGripR;
        public Arm Left = new Arm(), Right = new Arm();
        public WorkerRegripDBindings RegripD = new WorkerRegripDBindings();
        public WorkerGripStatus GripStatus {get;private set;}
        public string GripFailure {get;private set;}
        public bool HeadLookApplied {get;private set;}
        private string loggedGripFailure;
        public PlayerLegContactView LegContact = new PlayerLegContactView();
        public PlayerStopFootContactView StopFootContact = new PlayerStopFootContactView();
        public PlayerStopMotionView StopMotion = new PlayerStopMotionView();
        public LocomotionTransitionPolicy LocomotionTransitions = new LocomotionTransitionPolicy();
        private readonly System.Collections.Generic.Dictionary<string,float> transitionClips=new System.Collections.Generic.Dictionary<string,float>();
        public float WalkCycleSpeed = 1f, RunCycleSpeed = 1f;
        public float MaxLeftError { get; private set; }
        public float MaxRightError { get; private set; }
        public float CurrentSpeed { get; private set; }
        private VacuumGripAnchors localAnchors,remoteAnchors;
        private bool frozen; private Transform boundRemoteTool;
        private readonly System.Collections.Generic.List<Transform> posedBones=new System.Collections.Generic.List<Transform>();
        private readonly System.Collections.Generic.List<Vector3> posedPositions=new System.Collections.Generic.List<Vector3>();
        private readonly System.Collections.Generic.List<Quaternion> posedRotations=new System.Collections.Generic.List<Quaternion>();
        private bool previousGrounded = true;
        private bool remotePoseInitialized;
        private float remoteYaw, remotePitch;
        private readonly RemotePitchFilter remotePitchFilter=new RemotePitchFilter();
        private int remoteMotorId; private uint remoteMotorResetRevision;
        private Transform remoteIntake;
        private IntakeReceiver remoteReceiver;
        public Quaternion RenderedAim { get; private set; } = Quaternion.identity;
        private string locomotion, toolState;
        private float airborneStart, landUntil;
        [Serializable] public sealed class Arm
        {
            public Transform Upper, Forearm, Hand, Socket, ElbowSupport, ShoulderSupport;
            public Transform Clavicle;
            public Quaternion RestHandRootRotation=Quaternion.identity,RestForearmRootRotation=Quaternion.identity;
            public Vector3 ForearmLocalAxis=Vector3.up,HandLocalAxis=Vector3.up;
            public Quaternion HandRelativeTool = Quaternion.identity;
        }
        private void OnEnable(){GripFailure=loggedGripFailure=null;HeadLookApplied=false;GripStatus=WorkerGripStatus.Legacy;RestorePose();ResetLocomotionTransition();remotePoseInitialized=false;locomotion=toolState=null;MaxLeftError=MaxRightError=CurrentSpeed=0;previousGrounded=Motor==null||Motor.IsGrounded;airborneStart=Time.time;landUntil=0;frozen=false;if(Animator!=null)Animator.speed=1;}
        private void Start(){if(World==null)World=FindFirstObjectByType<AuthorityWorld>();}
        private void OnDisable(){HeadLookApplied=false;RestorePose();ResetLocomotionTransition();ReleaseRemoteIntake();remotePoseInitialized=false;if(Animator!=null)Animator.speed=1;}
        private void OnDestroy(){ResetLocomotionTransition();LegContact?.ResetBinding();ReleaseRemoteIntake();if(remoteIntake!=null)Destroy(remoteIntake.gameObject);}
        private void ReleaseRemoteIntake(){if(remoteReceiver!=null&&remoteReceiver.PresentationTarget==remoteIntake)remoteReceiver.PresentationTarget=null;}
        private void PublishRemoteIntake()
        {
            var receiver=Motor.GetComponent<IntakeReceiver>();
            if(remoteReceiver!=receiver||(remoteIntake!=null&&remoteIntake.parent!=RemoteTool)){
                ReleaseRemoteIntake();if(remoteIntake!=null)Destroy(remoteIntake.gameObject);remoteIntake=null;
            }
            remoteReceiver=receiver;
            if(receiver==null||Motor.NozzleAnchor==null)return;
            if(remoteIntake==null){
                remoteIntake=new GameObject("RenderedRemoteIntake").transform;
                remoteIntake.gameObject.hideFlags=HideFlags.DontSave;
                remoteIntake.SetParent(RemoteTool,false);
                // Both authored tool roots are at the nozzle. Refresh the physical intake offset;
                // only the marker follows the rendered tool, never the authority/admission target.
            }
            remoteIntake.localPosition=Motor.NozzleAnchor.InverseTransformPoint(receiver.Position);
            remoteIntake.localRotation=Quaternion.Inverse(Motor.NozzleAnchor.rotation)*receiver.Rotation;
            receiver.PresentationTarget=remoteIntake;
        }
        private void RestorePose(){RestoreSavedBones();StopFootContact?.RestorePose();LegContact?.RestorePose();StopMotion?.RestorePose();}
        private void RestoreSavedBones(){for(int i=0;i<posedBones.Count;i++)if(posedBones[i]!=null)posedBones[i].SetLocalPositionAndRotation(posedPositions[i],posedRotations[i]);posedBones.Clear();posedPositions.Clear();posedRotations.Clear();}
        private void SaveArm(Arm arm){SaveBone(arm.Clavicle);SaveBone(arm.Upper);SaveBone(arm.Forearm);SaveBone(arm.Hand);SaveBone(arm.ElbowSupport);SaveBone(arm.ShoulderSupport);} private void SaveBone(Transform bone){if(bone==null||posedBones.Contains(bone))return;posedBones.Add(bone);posedPositions.Add(bone.localPosition);posedRotations.Add(bone.localRotation);}
        private void BindLocalGrips(){
            if(View!=null&&(LocalTool!=View.ViewModelRoot||localAnchors==null)){
                LocalTool=View.ViewModelRoot;localAnchors=LocalTool!=null?LocalTool.GetComponentInChildren<VacuumGripAnchors>(true):null;
                LocalGripL=localAnchors!=null?localAnchors.Left:null;LocalGripR=localAnchors!=null?localAnchors.Right:null;
            }
            if(boundRemoteTool!=RemoteTool||remoteAnchors==null){boundRemoteTool=RemoteTool;remoteAnchors=RemoteTool!=null?RemoteTool.GetComponentInChildren<VacuumGripAnchors>(true):null;RemoteGripL=remoteAnchors!=null?remoteAnchors.Left:null;RemoteGripR=remoteAnchors!=null?remoteAnchors.Right:null;}
        }
        private void Update()
        {
            RestorePose();
            if(Motor==null||Animator==null||!Animator.isActiveAndEnabled){ResetLocomotionTransition();return;}
            bool running=World==null||World.IsRunning;
            if(!running){if(!frozen){ResetLocomotionTransition(true);Animator.speed=0;frozen=true;}CurrentSpeed=0;locomotion=toolState=null;return;}
            if(frozen){Animator.speed=1;frozen=false;previousGrounded=Motor.IsGrounded;airborneStart=Time.time;landUntil=0;}
            if(LocomotionTransitions==null)LocomotionTransitions=new LocomotionTransitionPolicy();
            if(LocomotionTransitions.Bind(Motor.GetInstanceID(),Animator.GetInstanceID(),
                Animator.runtimeAnimatorController!=null?Animator.runtimeAnimatorController.GetInstanceID():0,Motor.PresentationResetRevision)){
                transitionClips.Clear();locomotion=null;previousGrounded=Motor.IsGrounded;airborneStart=Time.time;landUntil=0;
            }
            // Animator.Rebind can reset the graph without replacing the component/controller.
            if(locomotion!=null&&!Animator.IsInTransition(0)&&!Animator.GetCurrentAnimatorStateInfo(0).IsName(locomotion)){
                LocomotionTransitions.ResetCapture();transitionClips.Clear();locomotion=null;
            }
            CurrentSpeed=Motor.isActiveAndEnabled?Motor.PlanarSpeed:0f;
            bool grounded=Motor.IsGrounded;
            if(previousGrounded&&!grounded)airborneStart=Time.time;
            if(!previousGrounded&&grounded)landUntil=Time.time+.20f;
            var intent=Motor.LastIntent;
            float walkRate=Mathf.Clamp(CurrentSpeed/4.5f,.2f,1.8f)*WalkCycleSpeed;
            float runRate=Mathf.Clamp(CurrentSpeed/7f,.2f,1.4f)*RunCycleSpeed;
            Animator.SetFloat("WalkSpeed",walkRate);Animator.SetFloat("RunSpeed",runRate);
            string next=!grounded?(Time.time-airborneStart<.16f&&Motor.VerticalVelocity>0?"JumpStart":"JumpAir"):
                Time.time<landUntil?"JumpLand":CurrentSpeed>.15f?(intent.SprintHeld?"Run":"Walk"):"Idle";
            if(next!=locomotion){
                var source=Animator.IsInTransition(0)?Animator.GetNextAnimatorStateInfo(0):Animator.GetCurrentAnimatorStateInfo(0);
                string sourceName=LocomotionName(source);
                // CrossFadeInFixedTime offsets are effective state seconds: native Animator
                // multiplies them by the target speed parameter. Preserve normalized gait phase.
                float targetRate=next=="Walk"?walkRate:next=="Run"?runRate:1f;
                LocomotionTransitions.Begin(sourceName,next,source.normalizedTime,TransitionClipLength(next)/targetRate,Time.timeAsDouble);
                Animator.CrossFadeInFixedTime(next,LocomotionTransitions.Duration,0,LocomotionTransitions.FixedOffset);
                locomotion=next;
            }
            string state=intent.InteractHeld?"ExtractHold":intent.VacuumHeld?"VacuumActive":"VacuumHold";
            if(state!=toolState){Animator.CrossFadeInFixedTime(state,.12f,1,0);toolState=state;}
            previousGrounded=grounded;
        }
        // Also available to an explicit replica teleport notification; no distance-per-frame heuristic.
        public void ResetLocomotionTransition(bool preserveStopContact=false){if(!preserveStopContact){StopFootContact?.Reset();StopMotion?.Reset();remotePoseInitialized=false;}LocomotionTransitions?.Reset();transitionClips.Clear();locomotion=null;}
        private static string LocomotionName(AnimatorStateInfo state){
            if(state.IsName("Idle"))return "Idle";if(state.IsName("Walk"))return "Walk";if(state.IsName("Run"))return "Run";
            if(state.IsName("JumpStart"))return "JumpStart";if(state.IsName("JumpAir"))return "JumpAir";if(state.IsName("JumpLand"))return "JumpLand";
            return null;
        }
        private float TransitionClipLength(string name){
            if(transitionClips.TryGetValue(name,out float length))return length;
            if(Animator.runtimeAnimatorController!=null)foreach(var clip in Animator.runtimeAnimatorController.animationClips)
                if(clip!=null&&clip.name==name){transitionClips[name]=clip.length;return clip.length;}
            throw new InvalidOperationException("Missing authored locomotion clip: "+name);
        }
#if UNITY_EDITOR
        public static Action<PlayerAnimationView,string> DiagnosticLegPose;
#endif
        private void LateUpdate()
        {
            HeadLookApplied=false;
            if(Motor==null||View==null||Animator==null||!Animator.isActiveAndEnabled||VisualRoot==null){StopFootContact?.Reset();LegContact?.RestorePose();ResetLocomotionTransition();ReleaseRemoteIntake();remotePoseInitialized=false;return;} BindLocalGrips();
            bool local=View.IsLocal;
            Quaternion aim;
            if(local&&Motor.CameraPivot!=null){aim=Motor.CameraPivot.rotation;remotePoseInitialized=false;ReleaseRemoteIntake();}
            else{
                // NGO already interpolates a replica root's yaw. Host remote players instead
                // use the authoritative intent; both feed this one render-only aim/mount path.
                float targetYaw=Motor.HasMovementAuthority?Motor.LastIntent.Yaw:Motor.transform.eulerAngles.y;
                float targetPitch=Motor.LastIntent.Pitch;
                if(!remotePoseInitialized||remoteMotorId!=Motor.GetInstanceID()||remoteMotorResetRevision!=Motor.PresentationResetRevision){remoteYaw=targetYaw;remotePitch=targetPitch;remotePitchFilter.Reset(targetPitch);remoteMotorId=Motor.GetInstanceID();remoteMotorResetRevision=Motor.PresentationResetRevision;remotePoseInitialized=true;}
                float blend=1f-Mathf.Exp(-Time.deltaTime/.05f);
                remoteYaw=Mathf.LerpAngle(remoteYaw,targetYaw,blend);remotePitch=remotePitchFilter.Step(targetPitch,Time.deltaTime);
                aim=Quaternion.Euler(remotePitch,remoteYaw,0);
            }
            RenderedAim=aim;
            Quaternion yaw=Quaternion.Euler(0,aim.eulerAngles.y,0);
            VisualRoot.SetPositionAndRotation(Motor.GetRenderPosition(),yaw);
            StopFootContact?.RestorePose();
#if UNITY_EDITOR
            DiagnosticLegPose?.Invoke(this,"afterAnimator");
#endif
            StopMotion?.ApplyBase(this);
            LegContact?.Apply(Body,Motor,yaw*Vector3.forward);
#if UNITY_EDITOR
            DiagnosticLegPose?.Invoke(this,"afterLegContact");
#endif
            StopMotion?.ObserveAfterLeg(this);
            if(StopMotion==null||!StopMotion.Enabled)StopFootContact?.Apply(this,yaw*Vector3.forward);
            if(Body!=null){Body.enabled=true;Body.shadowCastingMode=ShadowCastingMode.On;}
            if(Head!=null){Head.enabled=true;Head.shadowCastingMode=local?ShadowCastingMode.ShadowsOnly:ShadowCastingMode.On;}
            if(RemoteTool!=null){
                bool valid=Motor.TryGetNozzleLocalPosition(local?Motor.LastIntent.Pitch:remotePitch,out var remoteMount);
                RemoteTool.gameObject.SetActive(!local&&valid);
                if(!local&&valid){RemoteTool.SetPositionAndRotation(Motor.GetRenderPosition()+Vector3.up*Motor.CameraLocalMount.y+aim*remoteMount,aim);PublishRemoteIntake();}
                else ReleaseRemoteIntake();
            }else ReleaseRemoteIntake();
            Transform tool=local?LocalTool:RemoteTool;
            var anchors=local?localAnchors:remoteAnchors;
            if(RegripD!=null&&RegripD.Enabled){ApplyRegripD(tool,anchors);return;}
            GripStatus=WorkerGripStatus.Legacy;
            if(tool==null||!tool.gameObject.activeInHierarchy)return;
            if(anchors==null||anchors.Left==null||anchors.Right==null)return;
            Left.HandRelativeTool=anchors.LeftHandRotation;Right.HandRelativeTool=anchors.RightHandRotation;
            SaveArm(Left);SaveArm(Right);
            Solve(Left,anchors.Left,anchors.transform.rotation,yaw*new Vector3(-1,-.2f,.6f));
            Solve(Right,anchors.Right,anchors.transform.rotation,yaw*new Vector3(1,-.2f,.6f));
            if(Left.Socket!=null)MaxLeftError=Mathf.Max(MaxLeftError,Vector3.Distance(Left.Socket.position,anchors.Left.position));
            if(Right.Socket!=null)MaxRightError=Mathf.Max(MaxRightError,Vector3.Distance(Right.Socket.position,anchors.Right.position));
        }
        private void ApplyRegripD(Transform tool,VacuumGripAnchors anchors)
        {
            try {
                float pitch=WorkerRegripDPresentation.ReadRenderedPitch(RenderedAim);
                WorkerHeadLookPresentation.CaptureBeforeTorso(RegripD.NeckBone,RegripD.HeadBone,SaveBone);
                SaveBone(Spine);SaveBone(Chest);SaveArm(Left);SaveArm(Right);
                var idleStance=GetComponent<PlayerIdleStanceView>();
                var stance=idleStance!=null?idleStance.Frame(pitch):default;
                WorkerIdleStancePresentation.ApplyLower(this,stance,SaveBone);
                WorkerRegripDPresentation.ApplyTorso(this,pitch,stance);
                if(tool!=null&&tool.gameObject.activeInHierarchy){
                    if(anchors==null||!anchors.RegripDReady||anchors.Left==null||anchors.Right==null)
                        throw new InvalidOperationException("RegripD: active tool has no authored final D bindings");
                    Left.HandRelativeTool=anchors.LeftHandRotation;Right.HandRelativeTool=anchors.RightHandRotation;
                    MaxLeftError=Mathf.Max(MaxLeftError,WorkerRegripDPresentation.ApplyArm(Left,anchors.Left,anchors,VisualRoot,pitch,true,stance));
                    MaxRightError=Mathf.Max(MaxRightError,WorkerRegripDPresentation.ApplyArm(Right,anchors.Right,anchors,VisualRoot,pitch,false,stance));
                    GripStatus=WorkerGripStatus.Applied;
                }else GripStatus=WorkerGripStatus.NoTool;
                // Tool-related absence never suppresses the one final head/neck presentation call.
                WorkerHeadLookPresentation.ApplyAfterTorso(RegripD.NeckBone,RegripD.HeadBone,VisualRoot.right,pitch);
                HeadLookApplied=true;GripFailure=null;loggedGripFailure=null;
            }catch(Exception error){
                // Keep lower-body contact layers; unwind every upper pose owned by this frame.
                RestoreSavedBones();GripStatus=WorkerGripStatus.Failed;GripFailure=error.GetType().Name+": "+error.Message;HeadLookApplied=false;
                if(loggedGripFailure!=GripFailure){Debug.LogError(GripFailure,this);loggedGripFailure=GripFailure;}
            }
        }
        private static void Solve(Arm arm,Transform target,Quaternion toolRotation,Vector3 fallbackPole)
        {
            if(arm.Upper==null||arm.Forearm==null||arm.Hand==null||arm.Socket==null||target==null)return;
            Quaternion handRotation=toolRotation*arm.HandRelativeTool;
            Vector3 socketOffset=arm.Hand.InverseTransformPoint(arm.Socket.position);
            Vector3 goal=target.position-handRotation*Vector3.Scale(socketOffset,arm.Hand.lossyScale);
            Vector3 a=arm.Upper.position,b=arm.Forearm.position,c=arm.Hand.position;
            float first=Vector3.Distance(a,b),second=Vector3.Distance(b,c);
            Vector3 line=goal-a;float raw=line.magnitude;if(first<.001f||second<.001f||raw<.001f)return;
            Vector3 direction=line/raw;
            float length=Mathf.Clamp(raw,Mathf.Abs(first-second)+.0001f,first+second-.0001f);
            Vector3 pole=Vector3.ProjectOnPlane(b-a,direction);
            if(pole.sqrMagnitude<.0001f)pole=Vector3.ProjectOnPlane(fallbackPole,direction);
            pole.Normalize();float along=(first*first-second*second+length*length)/(2*length);
            Vector3 elbow=a+direction*along+pole*Mathf.Sqrt(Mathf.Max(0,first*first-along*along));
            Quaternion oldUpper=arm.Upper.rotation,oldLower=arm.Forearm.rotation;
            Quaternion oldShoulder=arm.ShoulderSupport!=null?arm.ShoulderSupport.rotation:Quaternion.identity;
            Quaternion oldElbow=arm.ElbowSupport!=null?arm.ElbowSupport.rotation:Quaternion.identity;
            arm.Upper.rotation=Quaternion.FromToRotation(b-a,elbow-a)*oldUpper;
            arm.Forearm.rotation=Quaternion.FromToRotation(arm.Hand.position-arm.Forearm.position,a+direction*length-arm.Forearm.position)*arm.Forearm.rotation;
            Quaternion upperDelta=arm.Upper.rotation*Quaternion.Inverse(oldUpper),lowerDelta=arm.Forearm.rotation*Quaternion.Inverse(oldLower);
            if(arm.ShoulderSupport!=null){arm.ShoulderSupport.position=arm.Upper.position;arm.ShoulderSupport.rotation=Quaternion.Slerp(Quaternion.identity,upperDelta,.5f)*oldShoulder;}
            if(arm.ElbowSupport!=null){arm.ElbowSupport.position=arm.Forearm.position;arm.ElbowSupport.rotation=Quaternion.Slerp(upperDelta,lowerDelta,.5f)*oldElbow;}
            arm.Hand.rotation=handRotation;
        }
    }
}