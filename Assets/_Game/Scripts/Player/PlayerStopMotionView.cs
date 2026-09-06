using System;
using System.Collections.Generic;
using UnityEngine;
using T = HowToSuck.StopTrajectoryV6;

namespace HowToSuck
{
    [Serializable] public sealed class PlayerStopMotionView
    {
        public bool Enabled;
        public WorkerStopMotionReference Reference;
        public bool Active {get;private set;}
        public string Status {get;private set;} = "Tracking";
        public string Failure {get;private set;}
        public int Started {get;private set;}
        public int Completed {get;private set;}
        public int Interrupted {get;private set;}
        public double Elapsed {get;private set;}
        public double Duration => plan != null ? plan.Duration : 0;
        public float MaximumNearFloorTravel {get;private set;}
        public float MaximumAnkleError => rig != null ? rig.MaximumAnkleError : 0;
        public Vector3 PelvisAdditionalShift => rig != null ? rig.PelvisAdditionalShift : Vector3.zero;
        public readonly ContactWitness Left = new ContactWitness(), Right = new ContactWitness();
        public sealed class ContactWitness
        {
            public bool Touching;
            public double AcquiredAt;
            public Vector3 Point;
            public string TrajectoryPhase;
        }
        [NonSerialized] StopMotionRigV6 rig;
        [NonSerialized] T.Plan plan;
        [NonSerialized] StopMotionRigV6.Pose entry;
        [NonSerialized] readonly List<StopMotionRigV6.Pose> history = new List<StopMotionRigV6.Pose>(3);
        [NonSerialized] readonly Dictionary<Collider, Matrix4x4> supports = new Dictionary<Collider, Matrix4x4>();
        [NonSerialized] PlayerAnimationView boundView;
        [NonSerialized] WorkerStopMotionReference boundReference;
        [NonSerialized] Mesh boundMesh;
        [NonSerialized] int motorId, animatorId, controllerId;
        [NonSerialized] uint reset;
        [NonSerialized] Vector3 origin, finalRoot;
        [NonSerialized] Quaternion yaw;
        [NonSerialized] double observedTime;
        [NonSerialized] bool wasMoving, wasRunning, lastSprint;
        [NonSerialized] Vector3[][] previousSoles;
        [NonSerialized] readonly Collider[] obstacles=new Collider[64];
        [NonSerialized] Vector3[][] previousRigid;
        [NonSerialized] T.Goals currentGoals;
        [NonSerialized] Vector3 leftGoalWorld,rightGoalWorld;
        public float ActualGoalError {get;private set;}
        [NonSerialized] StopMotionRigV6.Pose lastPresentedPose, releaseFrom;
        [NonSerialized] double releaseElapsed;
        const double ReleaseDuration=.18;
        public bool Releasing => releaseFrom!=null;
        void BeginRelease()
        {
            releaseFrom=lastPresentedPose;releaseElapsed=0;
        }

        public void RestorePose() => rig?.Restore();
        public void Reset()
        {
            RestorePose();rig=null;boundView=null;boundReference=null;boundMesh=null;history.Clear();supports.Clear();
            lastPresentedPose=releaseFrom=null;releaseElapsed=0;
            Active=wasMoving=false;plan=null;entry=null;Status="Tracking";Failure=null;previousSoles=null;previousRigid=null;
            Left.Touching=Right.Touching=false;
        }
        bool Bind(PlayerAnimationView v)
        {
            var a=v.Animator;var m=v.Motor;
            if(rig!=null&&boundView==v&&boundReference==Reference&&boundMesh==v.Body.sharedMesh&&
                motorId==m.GetInstanceID()&&animatorId==a.GetInstanceID()&&controllerId==(a.runtimeAnimatorController!=null?a.runtimeAnimatorController.GetInstanceID():0)&&reset==m.PresentationResetRevision&&rig.Matches(v))return true;
            Reset();
            rig=new StopMotionRigV6(v,Reference);boundView=v;boundReference=Reference;boundMesh=v.Body.sharedMesh;
            motorId=m.GetInstanceID();animatorId=a.GetInstanceID();controllerId=a.runtimeAnimatorController!=null?a.runtimeAnimatorController.GetInstanceID():0;
            reset=m.PresentationResetRevision;observedTime=Time.timeAsDouble;wasRunning=v.World==null||v.World.IsRunning;return true;
        }
        public void ApplyBase(PlayerAnimationView v)
        {
            RestorePose();
            if(!Enabled||Reference==null||v==null||v.Motor==null||!v.Motor.isActiveAndEnabled||v.Animator==null||!v.Animator.isActiveAndEnabled||v.Body==null||v.VisualRoot==null)
            {Reset();return;}
            try {
                Bind(v);
                double now=Time.timeAsDouble;bool running=v.World==null||v.World.IsRunning;
                bool moving=v.Motor.PlanarSpeed>.15f||v.Motor.LastIntent.Move.sqrMagnitude>.0001f;
                if(Active&&running&&(moving||!v.Motor.IsGrounded||Mathf.Abs(Mathf.DeltaAngle(yaw.eulerAngles.y,v.VisualRoot.eulerAngles.y))>2f)) {
                    BeginRelease();Active=false;Interrupted++;Status="Interrupted";history.Clear();supports.Clear();previousSoles=null;
                    Left.Touching=Right.Touching=false;
                }
                if(!Active&&releaseFrom==null&&running&&!moving&&wasMoving&&v.Motor.IsGrounded) Begin(v);
                if(!Active&&releaseFrom!=null) {
                    rig.BlendTowardAnimator(releaseFrom,(float)(releaseElapsed/ReleaseDuration));
                    if(releaseElapsed>=ReleaseDuration)releaseFrom=null;
                    else if(running&&wasRunning)releaseElapsed+=Math.Max(0,now-observedTime);
                }
                if(Active) {
                    if(running&&wasRunning)Elapsed+=Math.Max(0,now-observedTime);
                    Elapsed=Math.Min(Elapsed,plan.Duration);
                    foreach(var item in supports)if(item.Key==null||!item.Key.enabled||!item.Key.gameObject.activeInHierarchy||item.Key.attachedRigidbody!=null||item.Key.transform.localToWorldMatrix!=item.Value)
                        throw new InvalidOperationException("Captured support changed or disappeared.");
                    var goals=T.Evaluate(plan,Elapsed);currentGoals=goals;
                    Vector3 l=origin+yaw*V(goals.Left.Pose.Position),r=origin+yaw*V(goals.Right.Pose.Position);
                    Quaternion lq=yaw*Q(goals.Left.Pose.Rotation),rq=yaw*Q(goals.Right.Pose.Rotation);
                    // Only compensate ordinary render-origin interpolation. The authored step is
                    // already the base motion; contact correction budgets remain unchanged.
                    Vector3 drift=v.Motor.transform.position-v.VisualRoot.position;
                    if(Vector3.ProjectOnPlane(drift,Vector3.up).magnitude>.22f||Mathf.Abs(drift.y)>.06f)
                        throw new InvalidOperationException("Render-origin compensation exceeds the existing contact budget.");
                    CheckSpace(l,lq,r,rq);
                    rig.Apply(entry,Elapsed,plan.Duration,plan.First==T.Side.Right,l,lq,r,rq,yaw);
                    leftGoalWorld=l;rightGoalWorld=r;Status="Stopping";
                }
                observedTime=now;wasRunning=running;
            } catch(Exception error) {
                rig?.Restore();if(Active)BeginRelease();Active=false;
                if(releaseFrom!=null)rig.BlendTowardAnimator(releaseFrom,(float)(releaseElapsed/ReleaseDuration));
                history.Clear();supports.Clear();previousSoles=null;
                Left.Touching=Right.Touching=false;Status="Rejected";
                string text=error.GetType().Name+": "+error.Message;
                if(Failure!=text)Debug.LogWarning("Stop V6: "+text,v);Failure=text;
            }
        }
        public void ObserveAfterLeg(PlayerAnimationView v)
        {
            if(!Enabled||rig==null||v==null||v.Motor==null)return;
            if(!rig.Matches(v)){Reset();return;}
            bool running=v.World==null||v.World.IsRunning;
            bool moving=v.Motor.PlanarSpeed>.15f||v.Motor.LastIntent.Move.sqrMagnitude>.0001f;
            if(Active) {
                try {
                    rig.ActualFoot(true,out var l,out var lq);rig.ActualFoot(false,out var r,out var rq);
                    ActualGoalError=Mathf.Max(Vector3.Distance(l,leftGoalWorld),Vector3.Distance(r,rightGoalWorld));
                    if(ActualGoalError>.00015f)throw new InvalidOperationException("Actual post-Leg sole differs from its authored world goal.");
                    Witness(Left,Reference.Left,l,lq,currentGoals.Left.Contact,Time.timeAsDouble);
                    Witness(Right,Reference.Right,r,rq,currentGoals.Right.Contact,Time.timeAsDouble);
                    MeasureNearFloor(l,lq,r,rq);
                    if(Elapsed>=plan.Duration&&(v.VisualRoot.position-finalRoot).magnitude<.00005f) {
                        Active=false;Completed++;Status="Settled";history.Clear();supports.Clear();
                    }
                } catch(Exception error) {
                    v.LegContact?.RestorePose();rig.Restore();BeginRelease();Active=false;
                    if(releaseFrom!=null)rig.BlendTowardAnimator(releaseFrom,0);
                    v.LegContact?.Apply(v.Body,v.Motor,v.VisualRoot.forward);history.Clear();supports.Clear();
                    Status="Rejected";Failure=error.GetType().Name+": "+error.Message;
                    Left.Touching=Right.Touching=false;previousSoles=null;previousRigid=null;
                    Debug.LogWarning("Stop V6: "+Failure,v);
                }
            }
            if(running&&!Active&&moving&&v.Motor.IsGrounded) {
                history.Add(rig.Capture(Time.timeAsDouble));if(history.Count>3)history.RemoveAt(0);
                lastSprint=v.Motor.LastIntent.SprintHeld;
            }
            lastPresentedPose=rig.Capture(Time.timeAsDouble);
            if(running)wasMoving=moving;
        }
        void Begin(PlayerAnimationView v)
        {
            if(history.Count<3)throw new InvalidOperationException("Three real preceding base poses are required.");
            entry=history[2];double t0=history[0].Time-entry.Time,t1=history[1].Time-entry.Time;
            if(t0>=t1||t1>=0||-t0>.2||Time.timeAsDouble-entry.Time>.2||Time.timeAsDouble<entry.Time)throw new InvalidOperationException("Preceding pose cadence is stale or nonmonotonic.");
            double[] weights={-t1/(t0*(t0-t1)),-t0/(t1*(t1-t0)),-(t0+t1)/(t0*t1)};
            yaw=entry.VisualRotation;origin=entry.VisualPosition;finalRoot=v.Motor.transform.position;
            supports.Clear();
            float plane=FindPlane(entry,true);
            float otherPlane=FindPlane(entry,false);
            if(Mathf.Abs(plane-otherPlane)>.001f)throw new InvalidOperationException("The V6 step requires a witnessed common horizontal floor.");
            origin.y=plane;
            Vector3 entryOffset=Quaternion.Inverse(yaw)*(entry.VisualPosition-origin);
            Vector3 idleOffset=Quaternion.Inverse(yaw)*(finalRoot-origin);
            T.FootInput Input(bool left) {
                var f=left?Reference.Left:Reference.Right;Vector3 velocity=Vector3.zero;double soleVy=0;
                for(int i=0;i<3;i++) {velocity+=rig.FootPosition(history[i],left)*(float)weights[i];soleVy+=rig.SoleHeight(history[i],left)*weights[i];}
                var points=new T.D3[f.SoleNeutralPoints.Length];for(int i=0;i<points.Length;i++)points[i]=D(f.SoleNeutralPoints[i]);
                Vector3 idle=Reference.Bones[f.Ankle].IdlePosition+idleOffset;
                // Verify the future Idle support with the same actual floor, before committing a plan.
                CheckFloor(idle,Reference.Bones[f.Ankle].IdleRotation*Quaternion.Inverse(f.NeutralRotation),f.SoleNeutralPoints,false);
                return new T.FootInput(new T.Pose(D(rig.FootPosition(entry,left)+entryOffset),D(rig.FootRotation(entry,left))),
                    new T.Pose(D(idle),D(Reference.Bones[f.Ankle].IdleRotation*Quaternion.Inverse(f.NeutralRotation))),points,D(velocity),soleVy);
            }
            plan=T.MakePlan(lastSprint?T.StopKind.Run:T.StopKind.Walk,Input(true),Input(false));
            releaseFrom=null;Elapsed=0;observedTime=Time.timeAsDouble;Active=true;Started++;Failure=null;MaximumNearFloorTravel=0;
            previousSoles=null;previousRigid=null;Left.Touching=Right.Touching=false;
        }
        float FindPlane(StopMotionRigV6.Pose pose,bool left)
        {
            var f=left?Reference.Left:Reference.Right;var a=pose.VisualPosition+pose.VisualRotation*rig.FootPosition(pose,left);
            var q=pose.VisualRotation*rig.FootRotation(pose,left);float plane=float.NaN;
            foreach(var local in f.SoleNeutralPoints) {
                var p=a+q*local;
                if(!Physics.Raycast(p+Vector3.up*.15f,Vector3.down,out var hit,.9f,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)||hit.normal.y<.9999f||hit.rigidbody!=null)
                    throw new InvalidOperationException("Actual entry sole lacks static horizontal support.");
                if(float.IsNaN(plane))plane=hit.point.y;
                if(Mathf.Abs(plane-hit.point.y)>.001f||p.y-hit.point.y<-.00075f)throw new InvalidOperationException("Entry sole floor is stepped or penetrated.");
                supports[hit.collider]=hit.collider.transform.localToWorldMatrix;
            }
            return plane;
        }
        void CheckFloor(Vector3 localPosition,Quaternion rotation,Vector3[] points,bool touching)
        {
            foreach(var p in points) {
                var world=origin+yaw*(localPosition+rotation*p);
                if(!Physics.Raycast(world+Vector3.up*.15f,Vector3.down,out var hit,.9f,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)||hit.normal.y<.9999f||hit.rigidbody!=null||Mathf.Abs(hit.point.y-origin.y)>.001f)
                    throw new InvalidOperationException("Stop landing lacks the captured horizontal support.");
                if(touching&&world.y<hit.point.y-.00075f)throw new InvalidOperationException("Authored stopping sole penetrates the real floor.");
                supports[hit.collider]=hit.collider.transform.localToWorldMatrix;
            }
        }
        void Witness(ContactWitness witness,WorkerStopMotionReference.Foot foot,Vector3 a,Quaternion q,T.Contact phase,double now)
        {
            float lowest=float.PositiveInfinity;Vector3 point=Vector3.zero;int count=0;
            foreach(var p in foot.SoleNeutralPoints)lowest=Mathf.Min(lowest,(a+q*p).y-origin.y);
            bool touching=lowest<=.004f;
            if(touching) {
                CheckFloor(Quaternion.Inverse(yaw)*(a-origin),Quaternion.Inverse(yaw)*q,foot.SoleNeutralPoints,true);
                foreach(var p in foot.SoleNeutralPoints)if((a+q*p).y-origin.y<=lowest+.0003f){point+=a+q*p;count++;}
                if(!witness.Touching)witness.AcquiredAt=now;
                witness.Point=point/count;
            }
            witness.Touching=touching;witness.TrajectoryPhase=phase.ToString();
        }
        void CheckSpace(Vector3 l,Quaternion lq,Vector3 r,Quaternion rq)
        {
            var next=new Vector3[2][];
            for(int side=0;side<2;side++) {
                var points=rig.RigidNeutralPoints(side==0);next[side]=new Vector3[points.Length];
                Vector3 min=new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity),max=-min;
                for(int i=0;i<points.Length;i++) {
                    var world=(side==0?l:r)+(side==0?lq:rq)*points[i];next[side][i]=world;
                    min=Vector3.Min(min,world);max=Vector3.Max(max,world);
                    if(previousRigid!=null){min=Vector3.Min(min,previousRigid[side][i]);max=Vector3.Max(max,previousRigid[side][i]);}
                }
                min.y=Mathf.Max(min.y+.0008f,origin.y+.0008f);
                Vector3 size=max-min;
                if(size.x<=0||size.y<=0||size.z<=0||Physics.OverlapBoxNonAlloc((min+max)*.5f,size*.5f,obstacles,
                    Quaternion.identity,LayerMask.GetMask("World","Items"),QueryTriggerInteraction.Ignore)!=0)
                    throw new InvalidOperationException("Actual rigid boot path is obstructed.");
            }
            previousRigid=next;
        }
        void MeasureNearFloor(Vector3 l,Quaternion lq,Vector3 r,Quaternion rq)
        {
            var next=new Vector3[2][];
            for(int side=0;side<2;side++) {
                var f=side==0?Reference.Left:Reference.Right;next[side]=new Vector3[f.SoleNeutralPoints.Length];
                for(int i=0;i<next[side].Length;i++) {
                    var p=(side==0?l:r)+(side==0?lq:rq)*f.SoleNeutralPoints[i];next[side][i]=p;
                    if(previousSoles!=null&&p.y-origin.y<.003f&&previousSoles[side][i].y-origin.y<.003f)
                        MaximumNearFloorTravel=Mathf.Max(MaximumNearFloorTravel,Vector3.ProjectOnPlane(p-previousSoles[side][i],Vector3.up).magnitude);
                }
            }
            previousSoles=next;
        }
        static T.D3 D(Vector3 p)=>new T.D3(p.x,p.y,p.z);
        static T.Q4 D(Quaternion p)=>new T.Q4(p.x,p.y,p.z,p.w);
        static Vector3 V(T.D3 p)=>new Vector3((float)p.X,(float)p.Y,(float)p.Z);
        static Quaternion Q(T.Q4 p)=>new Quaternion((float)p.X,(float)p.Y,(float)p.Z,(float)p.W);
    }
}
