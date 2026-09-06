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
        public float WalkCycleSpeed = 1f, RunCycleSpeed = 1f;
        public float MaxLeftError { get; private set; }
        public float MaxRightError { get; private set; }
        public float CurrentSpeed { get; private set; }
        private VacuumGripAnchors localAnchors,remoteAnchors;
        private bool frozen; private Transform boundRemoteTool;
        private readonly System.Collections.Generic.List<Transform> posedBones=new System.Collections.Generic.List<Transform>();
        private readonly System.Collections.Generic.List<Vector3> posedPositions=new System.Collections.Generic.List<Vector3>();
        private readonly System.Collections.Generic.List<Quaternion> posedRotations=new System.Collections.Generic.List<Quaternion>();
        private Vector3 previousPosition;
        private double previousSampleTime;
        private bool sampled, previousGrounded = true;
        private string locomotion, toolState;
        private float airborneStart, landUntil;
        [Serializable] public sealed class Arm
        {
            public Transform Upper, Forearm, Hand, Socket, ElbowSupport, ShoulderSupport;
            public Quaternion HandRelativeTool = Quaternion.identity;
        }
        private void OnEnable(){RestorePose();sampled=false;locomotion=toolState=null;MaxLeftError=MaxRightError=CurrentSpeed=0;previousGrounded=Motor==null||Motor.IsGrounded;airborneStart=Time.time;landUntil=0;frozen=false;if(Animator!=null)Animator.speed=1;}
        private void Start(){if(World==null)World=FindFirstObjectByType<AuthorityWorld>();}
        private void OnDisable(){RestorePose();if(Animator!=null)Animator.speed=1;}
        private void RestorePose(){for(int i=0;i<posedBones.Count;i++)if(posedBones[i]!=null)posedBones[i].SetLocalPositionAndRotation(posedPositions[i],posedRotations[i]);posedBones.Clear();posedPositions.Clear();posedRotations.Clear();}
        private void SaveArm(Arm arm){SaveBone(arm.Upper);SaveBone(arm.Forearm);SaveBone(arm.Hand);SaveBone(arm.ElbowSupport);SaveBone(arm.ShoulderSupport);} private void SaveBone(Transform bone){if(bone==null)return;posedBones.Add(bone);posedPositions.Add(bone.localPosition);posedRotations.Add(bone.localRotation);}
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
            if(Motor==null||Animator==null||!Animator.isActiveAndEnabled)return;
            bool running=World==null||World.IsRunning;
            if(!running){if(!frozen){Animator.speed=0;frozen=true;}CurrentSpeed=0;sampled=false;locomotion=toolState=null;return;}
            if(frozen){Animator.speed=1;frozen=false;previousGrounded=Motor.IsGrounded;airborneStart=Time.time;landUntil=0;}
            double now=Time.fixedTimeAsDouble;
            if(!sampled){previousPosition=Motor.transform.position;previousSampleTime=now;sampled=true;}
            if(now>previousSampleTime){var delta=Motor.transform.position-previousPosition;delta.y=0;CurrentSpeed=delta.magnitude>.5f?0:delta.magnitude/(float)(now-previousSampleTime);previousPosition=Motor.transform.position;previousSampleTime=now;}
            if(Time.timeAsDouble>now+Time.fixedDeltaTime*2)CurrentSpeed=0;
            bool grounded=Motor.IsGrounded;
            if(previousGrounded&&!grounded)airborneStart=Time.time;
            if(!previousGrounded&&grounded)landUntil=Time.time+.20f;
            var intent=Motor.LastIntent;
            string next=!grounded?(Time.time-airborneStart<.16f&&Motor.VerticalVelocity>0?"JumpStart":"JumpAir"):
                Time.time<landUntil?"JumpLand":CurrentSpeed>.15f?(intent.SprintHeld?"Run":"Walk"):"Idle";
            if(next!=locomotion){float offset=0;if((next=="Walk"||next=="Run")&&(locomotion=="Walk"||locomotion=="Run")){var gait=Animator.IsInTransition(0)?Animator.GetNextAnimatorStateInfo(0):Animator.GetCurrentAnimatorStateInfo(0);offset=Mathf.Repeat(gait.normalizedTime,1f)*(next=="Run"?.8f:1f);}Animator.CrossFadeInFixedTime(next,.12f,0,offset);locomotion=next;}
            Animator.SetFloat("WalkSpeed",Mathf.Clamp(CurrentSpeed/4.5f,.2f,1.8f)*WalkCycleSpeed);
            Animator.SetFloat("RunSpeed",Mathf.Clamp(CurrentSpeed/7f,.2f,1.4f)*RunCycleSpeed);
            string state=intent.InteractHeld?"ExtractHold":intent.VacuumHeld?"VacuumActive":"VacuumHold";
            if(state!=toolState){Animator.CrossFadeInFixedTime(state,.12f,1,0);toolState=state;}
            previousGrounded=grounded;
        }
        private void LateUpdate()
        {
            if(Motor==null||View==null||Animator==null||!Animator.isActiveAndEnabled||VisualRoot==null)return; BindLocalGrips();
            bool local=View.IsLocal;
            Quaternion aim=local&&Motor.CameraPivot!=null?Motor.CameraPivot.rotation:Quaternion.Euler(Motor.LastIntent.Pitch,Motor.LastIntent.Yaw,0);
            Quaternion yaw=Quaternion.Euler(0,aim.eulerAngles.y,0);
            VisualRoot.SetPositionAndRotation(Motor.GetRenderPosition(),yaw);
            if(Body!=null){Body.enabled=true;Body.shadowCastingMode=ShadowCastingMode.On;}
            if(Head!=null){Head.enabled=true;Head.shadowCastingMode=local?ShadowCastingMode.ShadowsOnly:ShadowCastingMode.On;}
            if(RemoteTool!=null){bool valid=Motor.TryGetNozzleLocalPosition(Motor.LastIntent.Pitch,out var remoteMount);RemoteTool.gameObject.SetActive(!local&&valid);if(!local&&valid)RemoteTool.SetPositionAndRotation(Motor.GetRenderPosition()+Vector3.up*Motor.CameraLocalMount.y+aim*remoteMount,aim);}
            Transform tool=local?LocalTool:RemoteTool;
            if(tool==null||!tool.gameObject.activeInHierarchy)return;
            var anchors=local?localAnchors:remoteAnchors;
            if(anchors==null||anchors.Left==null||anchors.Right==null)return;
            Left.HandRelativeTool=anchors.LeftHandRotation;Right.HandRelativeTool=anchors.RightHandRotation;
            SaveArm(Left);SaveArm(Right);
            Solve(Left,anchors.Left,anchors.transform.rotation,yaw*new Vector3(-1,-.2f,.6f));
            Solve(Right,anchors.Right,anchors.transform.rotation,yaw*new Vector3(1,-.2f,.6f));
            if(Left.Socket!=null)MaxLeftError=Mathf.Max(MaxLeftError,Vector3.Distance(Left.Socket.position,anchors.Left.position));
            if(Right.Socket!=null)MaxRightError=Mathf.Max(MaxRightError,Vector3.Distance(Right.Socket.position,anchors.Right.position));
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