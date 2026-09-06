using System;
using System.Collections.Generic;
using UnityEngine;
namespace HowToSuck
{
    public enum StopFootStatus { Tracking, NoContact, Anchored, Releasing, Released, Invalid, NoSupport, Obstructed, OverBudget, Unreachable }
    // Presentation only, applied after the existing vertical LegContact. Native Animator is restored first each frame.
    [Serializable]
    public sealed class PlayerStopFootContactView
    {
        [Serializable] public sealed class Foot
        {
            public StopFootStatus Status {get;internal set;}
            public bool IsAnchored {get;internal set;}
            public bool HasOwnedPose=>owned;
            public Vector3 Anchor {get;internal set;}
            public double CapturedAt {get;internal set;}
            public float AnchorError {get;internal set;}
            public float AppliedHorizontal {get;internal set;}
            public float AppliedVerticalFromAnimator {get;internal set;}
            [NonSerialized] internal PlayerLegContactView.Leg leg;
            [NonSerialized] internal Transform[] bones;
            [NonSerialized] internal Vector3[] positions,rigid;
            [NonSerialized] internal Quaternion[] rotations;
            [NonSerialized] internal bool owned,rejected,hasPrevious;
            [NonSerialized] internal Quaternion anchorRotation,previousBaseRotation,releaseDelta;
            [NonSerialized] internal Vector3 previousBase,releaseOffset,previousFinal;
            [NonSerialized] internal Quaternion previousFinalRotation;
            [NonSerialized] internal float supportPlane;
            [NonSerialized] internal Collider support;
            [NonSerialized] internal Matrix4x4 supportMatrix;
            internal bool Matches()=>leg!=null&&leg.Matches()&&bones!=null&&bones.Length==4&&
                bones[0]!=null&&bones[1]!=null&&bones[2]!=null&&bones[3]!=null&&
                bones[0]==leg.Thigh&&bones[1]==leg.Calf&&bones[2]==leg.Foot&&bones[3]==leg.KneeSupport;
            internal void Save(){for(int i=0;i<4;i++){positions[i]=bones[i].localPosition;rotations[i]=bones[i].localRotation;}owned=true;}
            internal void Restore(){if(!owned)return;for(int i=0;i<4;i++)if(bones[i]!=null)bones[i].SetLocalPositionAndRotation(positions[i],rotations[i]);owned=false;}
        }
        public bool Enabled=true;
        [Range(.05f,.25f)] public float MaximumHorizontalOffset=.22f;
        [Range(.001f,.006f)] public float CaptureGap=.004f;
        public StopFootContactPolicy Policy=new StopFootContactPolicy();
        public Foot Left=new Foot(),Right=new Foot();
        private SkinnedMeshRenderer body;
        private Mesh mesh;
        private int motorId,animatorId,controllerId;
        private uint resetRevision,graphReset;
        private bool bound,wasFrozen,hasAnimatorSample,previousTransition;
        private int previousStateHash;
        private float previousNormalized;
        private double lastObserved=double.NaN;
        private readonly Collider[] overlaps=new Collider[32];
        private readonly List<LegContactMath.V> contacts=new List<LegContactMath.V>(128);
        public void RestorePose(){Left?.Restore();Right?.Restore();}
        public void Reset()
        {
            RestorePose();bound=false;wasFrozen=hasAnimatorSample=previousTransition=false;lastObserved=double.NaN;body=null;mesh=null;Policy?.Reset();
            Clear(Left);Clear(Right);
        }
        private static void Clear(Foot f)
        {
            if(f==null)return;
            f.IsAnchored=f.owned=f.rejected=f.hasPrevious=false;f.Status=StopFootStatus.Tracking;
            f.leg=null;f.bones=null;f.positions=null;f.rotations=null;f.rigid=null;f.support=null;
            f.AnchorError=f.AppliedHorizontal=f.AppliedVerticalFromAnimator=0;
        }
        public void Apply(PlayerAnimationView view,Vector3 forward)
        {
            RestorePose();
            var motor=view!=null?view.Motor:null;var animator=view!=null?view.Animator:null;
            var floor=view!=null?view.LegContact:null;
            if(!Enabled||view==null||!view.isActiveAndEnabled||motor==null||!motor.isActiveAndEnabled||
                animator==null||!animator.isActiveAndEnabled||view.VisualRoot==null||view.Body==null||
                floor==null||!floor.Enabled||floor.Left==null||floor.Right==null||Left==null||Right==null||
                !F(MaximumHorizontalOffset)||MaximumHorizontalOffset<.05f||MaximumHorizontalOffset>.25f||
                !F(CaptureGap)||CaptureGap<.001f||CaptureGap>.006f)
            {Reset();return;}
            if(Policy==null)Policy=new StopFootContactPolicy();
            int controller=animator.runtimeAnimatorController!=null?animator.runtimeAnimatorController.GetInstanceID():0;
            uint graph=view.LocomotionTransitions!=null?view.LocomotionTransitions.ResetCount:0;
            var state=animator.GetCurrentAnimatorStateInfo(0);
            bool transition=animator.IsInTransition(0);
            bool phaseReset=hasAnimatorSample&&!transition&&!previousTransition&&state.fullPathHash==previousStateHash&&
                state.normalizedTime+.0001f<previousNormalized;
            bool running=view.World==null||view.World.IsRunning;
            bool freezeBridge=wasFrozen||!running;
            bool identity=!bound||body!=view.Body||mesh!=view.Body.sharedMesh||motorId!=motor.GetInstanceID()||
                animatorId!=animator.GetInstanceID()||controllerId!=controller||resetRevision!=motor.PresentationResetRevision||(!freezeBridge&&(graphReset!=graph||phaseReset))||
                !Left.Matches()||!Right.Matches();
            if(identity) {
                Reset();
                if(!Bind(view)){Left.Status=Right.Status=StopFootStatus.Invalid;return;}
                motorId=motor.GetInstanceID();animatorId=animator.GetInstanceID();controllerId=controller;
                resetRevision=motor.PresentationResetRevision;graphReset=graph;
            }
            if(freezeBridge)graphReset=graph;
            previousStateHash=state.fullPathHash;previousNormalized=state.normalizedTime;previousTransition=transition;hasAnimatorSample=true;
            bool idle=animator.IsInTransition(0)?animator.GetNextAnimatorStateInfo(0).IsName("Idle"):state.IsName("Idle");
            double now=Time.timeAsDouble;
            if(!running) {
                if(!double.IsNaN(lastObserved))Policy.Defer(now-lastObserved);
            }else Policy.Step(now,motor.PlanarSpeed,motor.LastIntent.Move.magnitude,view.VisualRoot.eulerAngles.y,
                motor.IsGrounded,true,idle,identity);
            lastObserved=now;wasFrozen=!running;
            if(Policy.HardReset){ReleaseImmediately(Left);ReleaseImmediately(Right);RememberBase(Left);RememberBase(Right);return;}
            if(Policy.JustStopped) {
                StartStop(Left);StartStop(Right);
            }
            if(Policy.JustReleased) {
                BeginRelease(Left);BeginRelease(Right);
            }
            if(Policy.Phase==StopFootPhase.Tracking){ReleaseImmediately(Left);ReleaseImmediately(Right);}
            else {
                ApplyFoot(Left,Right,true,view,forward);
                ApplyFoot(Right,Left,false,view,forward);
            }
            RememberBase(Left);RememberBase(Right);
        }
        private static void StartStop(Foot f){f.IsAnchored=f.rejected=false;f.Status=StopFootStatus.NoContact;f.support=null;}
        private static void ReleaseImmediately(Foot f)
        {f.IsAnchored=false;f.support=null;f.rejected=false;f.Status=StopFootStatus.Tracking;f.AnchorError=f.AppliedHorizontal=f.AppliedVerticalFromAnimator=0;}
        private static void RememberBase(Foot f)
        {
            if(!f.Matches())return;
            // Save the input native+vertical pose, not this helper's final IK output.
            if(f.owned){
                // Input ankle world position can be reconstructed without changing the rendered skeleton.
                var m=f.bones[0].parent.localToWorldMatrix*
                    Matrix4x4.TRS(f.positions[0],f.rotations[0],f.bones[0].localScale)*
                    Matrix4x4.TRS(f.positions[1],f.rotations[1],f.bones[1].localScale)*
                    Matrix4x4.TRS(f.positions[2],f.rotations[2],f.bones[2].localScale);
                f.previousBase=m.MultiplyPoint3x4(Vector3.zero);
                f.previousBaseRotation=f.bones[0].parent.rotation*f.rotations[0]*f.rotations[1]*f.rotations[2];
            }else {f.previousBase=f.leg.Foot.position;f.previousBaseRotation=f.leg.Foot.rotation;}
            f.previousFinal=f.leg.Foot.position;f.previousFinalRotation=f.leg.Foot.rotation;
            f.hasPrevious=true;
        }
        private static void BeginRelease(Foot f)
        {
            if(!f.IsAnchored||!f.hasPrevious){f.IsAnchored=false;return;}
            // Release follows the current native moving foot; it never holds a world point against new movement.
            f.releaseOffset=f.Anchor-f.previousBase;f.releaseOffset.y=0;
            f.releaseDelta=f.anchorRotation*Quaternion.Inverse(f.previousBaseRotation);
            f.IsAnchored=false;f.Status=StopFootStatus.Releasing;
        }
        private bool Bind(PlayerAnimationView view)
        {
            body=view.Body;mesh=body.sharedMesh;
            if(mesh==null||!mesh.isReadable||!BindFoot(Left,view.LegContact.Left)||!BindFoot(Right,view.LegContact.Right))return false;
            bound=true;return true;
        }
        private bool BindFoot(Foot f,PlayerLegContactView.Leg leg)
        {
            if(f==null||leg==null||leg.Thigh==null||leg.Thigh.parent==null||!leg.Matches()||leg.Sole==null||leg.Sole.Length<6)return false;
            int bone=Array.IndexOf(body.bones,leg.Foot);var vertices=mesh.vertices;var weights=mesh.boneWeights;var bind=mesh.bindposes;
            if(bone<0||bone>=bind.Length||weights.Length!=vertices.Length)return false;
            var used=new HashSet<int>(mesh.triangles);var points=new List<Vector3>();
            for(int i=0;i<weights.Length;i++) {
                if(!used.Contains(i))continue;var w=weights[i];float amount=0;
                if(w.boneIndex0==bone)amount+=w.weight0;if(w.boneIndex1==bone)amount+=w.weight1;
                if(w.boneIndex2==bone)amount+=w.weight2;if(w.boneIndex3==bone)amount+=w.weight3;
                if(amount>=.99999f)points.Add(bind[bone].MultiplyPoint3x4(vertices[i]));
            }
            if(points.Count<6)return false;
            f.leg=leg;f.bones=new[]{leg.Thigh,leg.Calf,leg.Foot,leg.KneeSupport};
            f.positions=new Vector3[4];f.rotations=new Quaternion[4];f.rigid=points.ToArray();return true;
        }
        private void ApplyFoot(Foot f,Foot other,bool left,PlayerAnimationView view,Vector3 forward)
        {
            f.AppliedHorizontal=f.AppliedVerticalFromAnimator=f.AnchorError=0;
            if(f.rejected||!f.Matches())return;
            var leg=f.leg;Vector3 ankle=leg.Foot.position;Quaternion rotation=leg.Foot.rotation;
            bool releasing=Policy.Phase==StopFootPhase.Releasing;
            if(!releasing&&!f.IsAnchored) {
                if(!Policy.CanCapture)return;
                if(!Contact(f,ankle,rotation,out var support,out float plane)){f.Status=StopFootStatus.NoContact;return;}
                if(!Space(f,ankle,rotation,plane)){f.Status=StopFootStatus.Obstructed;return;}
                f.Anchor=ankle;f.CapturedAt=Time.timeAsDouble;f.anchorRotation=rotation;f.support=support;f.supportPlane=plane;f.supportMatrix=support.transform.localToWorldMatrix;
                f.IsAnchored=true;
            }
            if(!f.IsAnchored&&f.Status!=StopFootStatus.Releasing)return;
            Vector3 target=releasing?Q(StopFootContactMath.Release(V(ankle),V(f.releaseOffset),Policy.ReleaseWeight)):f.Anchor;
            Quaternion targetRotation=releasing?Quaternion.Slerp(Quaternion.identity,f.releaseDelta,(float)Policy.ReleaseWeight)*rotation:f.anchorRotation;
            if(!releasing&&(f.support==null||!f.support.enabled||!f.support.gameObject.activeInHierarchy||
                f.support.attachedRigidbody!=null||f.support.transform.localToWorldMatrix!=f.supportMatrix))
            {Reject(f,StopFootStatus.NoSupport);return;}
            // The .06m limit is measured from the native Animator ankle, including any existing vertical lift.
            float totalUp=target.y-(ankle.y-leg.AppliedLift);
            float horizontal=Vector3.ProjectOnPlane(target-ankle,Vector3.up).magnitude;
            if(!StopFootContactMath.Budget(horizontal,totalUp,target.y-ankle.y,MaximumHorizontalOffset,view.LegContact.MaximumLift)||
                leg.Status==LegContactStatus.OverBudget||leg.Status==LegContactStatus.Unreachable||leg.Status==LegContactStatus.InvalidGeometry)
            {Reject(f,StopFootStatus.OverBudget);return;}
            Vector3 lateral=view.VisualRoot.right;
            float side=Vector3.Dot(target-view.VisualRoot.position,lateral);
            if(left?side>-.015f:side<.015f){Reject(f,StopFootStatus.Invalid);return;}
            Vector3 otherPoint=other.IsAnchored?other.Anchor:other.leg.Foot.position;
            if(Vector3.ProjectOnPlane(target-otherPoint,Vector3.up).sqrMagnitude<.01f){Reject(f,StopFootStatus.Invalid);return;}
            if(!releasing) {
                if(!Contact(f,target,targetRotation,out var support,out float plane)||support!=f.support)
                {Reject(f,StopFootStatus.NoSupport);return;}
                if(!Space(f,target,targetRotation,plane)){Reject(f,StopFootStatus.Obstructed);return;}
            }else {
                // Releasing a moving/swing foot never pulls its Y toward a floor or invents support.
                // Reject any sole penetration and any obstacle within the conservative rigid boot volume.
                if(!ReleaseSpace(f,target,targetRotation)){Reject(f,StopFootStatus.Obstructed);return;}
            }
            Vector3 hip=leg.Thigh.position,knee=leg.Calf.position;
            if(!LegContactMath.Knee(V(hip),V(knee),V(ankle),V(target),V(forward),out var solved))
            {Reject(f,StopFootStatus.Unreachable);return;}
            float upperLength=Vector3.Distance(hip,knee),lowerLength=Vector3.Distance(knee,ankle);
            Quaternion upper=leg.Thigh.rotation,lower=leg.Calf.rotation,supportRotation=leg.KneeSupport.rotation;
            f.Save();
            leg.Thigh.rotation=Quaternion.FromToRotation(knee-hip,Q(solved)-hip)*upper;
            leg.Calf.rotation=Quaternion.FromToRotation(leg.Foot.position-leg.Calf.position,target-leg.Calf.position)*leg.Calf.rotation;
            Quaternion a=leg.Thigh.rotation*Quaternion.Inverse(upper),b=leg.Calf.rotation*Quaternion.Inverse(lower);
            leg.KneeSupport.position=leg.Calf.position;leg.KneeSupport.rotation=Quaternion.Slerp(a,b,.5f)*supportRotation;
            leg.Foot.rotation=targetRotation;
            if(Vector3.Distance(leg.Foot.position,target)>.00015f||
                Mathf.Abs(Vector3.Distance(leg.Thigh.position,leg.Calf.position)-upperLength)>.00015f||
                Mathf.Abs(Vector3.Distance(leg.Calf.position,leg.Foot.position)-lowerLength)>.00015f)
            {f.Restore();Reject(f,StopFootStatus.Invalid);return;}
            f.Status=releasing?StopFootStatus.Releasing:StopFootStatus.Anchored;
            f.AppliedHorizontal=horizontal;f.AppliedVerticalFromAnimator=totalUp;
            f.AnchorError=releasing?0:Vector3.Distance(leg.Foot.position,f.Anchor);
        }
        private static void Reject(Foot f,StopFootStatus status){f.IsAnchored=false;f.rejected=true;f.Status=status;f.support=null;}
        private bool Contact(Foot f,Vector3 position,Quaternion rotation,out Collider support,out float plane)
        {
            support=null;plane=0;contacts.Clear();
            float low=float.PositiveInfinity,high=float.NegativeInfinity;
            foreach(var p in f.leg.Sole) {
                Vector3 w=position+rotation*p;
                if(!Finite(w)||!Physics.Raycast(w+Vector3.up*.03f,Vector3.down,out var hit,.10f,
                    LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)||hit.normal.y<.9999f||hit.rigidbody!=null)return false;
                if(support==null){support=hit.collider;plane=hit.point.y;}
                if(hit.collider!=support||Mathf.Abs(hit.point.y-plane)>.001f)return false;
                float gap=w.y-hit.point.y;low=Mathf.Min(low,gap);high=Mathf.Max(high,gap);
                if(gap>=-.00075f&&gap<=CaptureGap)contacts.Add(V(w));
            }
            if(low<-.00075f||low>CaptureGap||high>.020f||contacts.Count<3)return false;
            return StopFootContactMath.Footprint(contacts);
        }
        private bool ReleaseSpace(Foot f,Vector3 position,Quaternion rotation)
        {
            float floor=float.NegativeInfinity;
            foreach(var p in f.leg.Sole) {
                Vector3 w=position+rotation*p;
                if(Physics.Raycast(w+Vector3.up*.07f,Vector3.down,out var hit,.14f,LayerMask.GetMask("World"),QueryTriggerInteraction.Ignore)) {
                    if(hit.point.y>w.y+.00075f)return false;
                    floor=Mathf.Max(floor,hit.point.y);
                }
            }
            return Space(f,position,rotation,Mathf.Min(floor,f.supportPlane),true);
        }
        private bool Space(Foot f,Vector3 position,Quaternion rotation,float plane,bool includePrevious=false)
        {
            Vector3 min=new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity),max=-min;
            foreach(var p in f.rigid){Vector3 w=position+rotation*p;if(!Finite(w))return false;min=Vector3.Min(min,w);max=Vector3.Max(max,w);}
            if(includePrevious&&f.hasPrevious) {
                foreach(var p in f.rigid) {
                    Vector3 w=f.previousFinal+f.previousFinalRotation*p;
                    if(!Finite(w))return false;min=Vector3.Min(min,w);max=Vector3.Max(max,w);
                }
                // The union rejects obstacles between consecutive rendered positions as well as at the endpoint.
                // Include the horizontal arc bulge of a rotating rigid boot; no new physics geometry is created.
                float radius=0;foreach(var p in f.rigid)radius=Mathf.Max(radius,p.magnitude);
                float margin=radius*(1-Mathf.Cos(Quaternion.Angle(f.previousFinalRotation,rotation)*Mathf.Deg2Rad*.5f));
                min.x-=margin;min.z-=margin;max.x+=margin;max.z+=margin;
            }
            // Conservatively include the actual rigid boot. Remove only the <=0.8mm contact band from the query.
            min.y=Mathf.Max(min.y+.0008f,plane+.0008f);
            Vector3 size=max-min;if(size.x<=0||size.y<=0||size.z<=0)return false;
            int count=Physics.OverlapBoxNonAlloc((min+max)*.5f,size*.5f,overlaps,Quaternion.identity,
                LayerMask.GetMask("World","Items"),QueryTriggerInteraction.Ignore);
            return count==0; // Buffer saturation also rejects; no ignored floor collider hiding a wall on the same mesh.
        }
        private static bool F(float x)=>LegContactMath.F(x);
        private static bool Finite(Vector3 v)=>F(v.x)&&F(v.y)&&F(v.z);
        private static LegContactMath.V V(Vector3 v)=>new LegContactMath.V(v.x,v.y,v.z);
        private static Vector3 Q(LegContactMath.V v)=>new Vector3((float)v.X,(float)v.Y,(float)v.Z);
    }
}
