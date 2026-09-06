using System;
using System.Collections.Generic;
using UnityEngine;

namespace HowToSuck
{
    // Continuous full-rig evaluator. It owns only local bone TRS; never the motor,
    // Animator graph, semantic VisualRoot, camera or the tool.
    public sealed class StopMotionRigV6
    {
        public sealed class Pose
        {
            public Vector3[] Positions, Scales, Points;
            public Quaternion[] Rotations, Orientations;
            public Vector3 VisualPosition;
            public Quaternion VisualRotation;
            public double Time;
        }
        readonly WorkerStopMotionReference reference;
        readonly Transform[] bones;
        readonly Transform visual;
        readonly SkinnedMeshRenderer body;
        readonly Mesh mesh;
        readonly Vector3[][] rigidPoints=new Vector3[2][];
        Pose saved;
        public bool OwnsPose => saved != null;
        public float MaximumReachResidual {get; private set;}
        public float MaximumAnkleError {get; private set;}
        public Vector3 PelvisAdditionalShift {get; private set;}
        public int BoneCount => bones.Length;

        public StopMotionRigV6(PlayerAnimationView view, WorkerStopMotionReference source)
        {
            if (view == null || view.VisualRoot == null || view.Animator == null || source == null || source.Bones == null || source.Bones.Length != 28)
                throw new InvalidOperationException("Stop V6 requires the complete authored 28-bone binding.");
            reference = source; visual = view.VisualRoot; body=view.Body; mesh=body.sharedMesh; bones = new Transform[28];
            var byName = new Dictionary<string, Transform>();
            foreach (var t in view.Animator.GetComponentsInChildren<Transform>(true))
                if (!byName.TryAdd(t.name, t)) throw new InvalidOperationException("Stop V6 duplicate rig name: " + t.name);
            for (int i=0; i<bones.Length; i++)
            {
                var r = reference.Bones[i];
                if (!byName.TryGetValue(r.Name, out bones[i]) || bones[i].parent.name != r.Parent ||
                    (bones[i].lossyScale-Vector3.one).sqrMagnitude > .000001f)
                    throw new InvalidOperationException("Stop V6 rig hierarchy/scale changed: " + r.Name);
            }
        }

        public bool Matches(PlayerAnimationView view)
        {
            if(view==null||view.VisualRoot!=visual||view.Body!=body||body==null||body.sharedMesh!=mesh||
                visual==null||reference==null||reference.Bones==null||reference.Bones.Length!=28||
                (visual.lossyScale-Vector3.one).sqrMagnitude>.000001f)return false;
            var current=body.bones;if(current.Length!=28)return false;
            for(int i=0;i<28;i++)if(bones[i]==null||current[i]!=bones[i]||bones[i].parent==null||
                bones[i].parent.name!=reference.Bones[i].Parent||bones[i].name!=reference.Bones[i].Name||
                (bones[i].lossyScale-Vector3.one).sqrMagnitude>.000001f)return false;
            return true;
        }
        public Vector3[] RigidNeutralPoints(bool left)
        {
            int side=left?0:1;if(rigidPoints[side]!=null)return rigidPoints[side];
            var foot=left?reference.Left:reference.Right;var list=new List<Vector3>();
            var vertices=mesh.vertices;var weights=mesh.boneWeights;var used=new HashSet<int>(mesh.triangles);
            var bind=mesh.bindposes[foot.Ankle];
            for(int i=0;i<vertices.Length;i++) {
                if(!used.Contains(i))continue;var w=weights[i];float amount=0;
                if(w.boneIndex0==foot.Ankle)amount+=w.weight0;if(w.boneIndex1==foot.Ankle)amount+=w.weight1;
                if(w.boneIndex2==foot.Ankle)amount+=w.weight2;if(w.boneIndex3==foot.Ankle)amount+=w.weight3;
                if(amount>=.99999f)list.Add(foot.NeutralRotation*bind.MultiplyPoint3x4(vertices[i]));
            }
            if(list.Count<6)throw new InvalidOperationException("Actual rigid boot mesh required.");
            rigidPoints[side]=list.ToArray();return rigidPoints[side];
        }
        public void ActualFoot(bool left,out Vector3 position,out Quaternion rotation)
        {
            var f=left?reference.Left:reference.Right;
            position=bones[f.Ankle].position;rotation=bones[f.Ankle].rotation*Quaternion.Inverse(f.NeutralRotation);
        }
        public Pose Capture(double time)
        {
            var p = new Pose {Positions=new Vector3[28], Rotations=new Quaternion[28], Scales=new Vector3[28],
                Points=new Vector3[28], Orientations=new Quaternion[28], Time=time,
                VisualPosition=visual.position, VisualRotation=visual.rotation};
            for (int i=0;i<28;i++) {
                p.Positions[i]=bones[i].localPosition; p.Rotations[i]=bones[i].localRotation; p.Scales[i]=bones[i].localScale;
                p.Points[i]=visual.InverseTransformPoint(bones[i].position);
                p.Orientations[i]=Quaternion.Inverse(visual.rotation)*bones[i].rotation;
            }
            return p;
        }
        public void Restore()
        {
            if (saved == null) return;
            for (int i=0;i<28;i++) if (bones[i] != null) {
                bones[i].SetLocalPositionAndRotation(saved.Positions[i], saved.Rotations[i]);
                bones[i].localScale=saved.Scales[i];
            }
            saved=null;
        }
        // Release the last presented base pose toward the live Animator, so an
        // interrupted authored step cannot discard the pose in one frame.
        public void BlendTowardAnimator(Pose from, float progress)
        {
            Restore(); saved=Capture(Time.timeAsDouble);
            float weight=Quint(progress);
            for(int i=0;i<28;i++) {
                bones[i].SetLocalPositionAndRotation(Vector3.Lerp(from.Positions[i],saved.Positions[i],weight),
                    Quaternion.Slerp(from.Rotations[i],saved.Rotations[i],weight));
                bones[i].localScale=Vector3.Lerp(from.Scales[i],saved.Scales[i],weight);
            }
        }
        public static float Quint(float t) {t=Mathf.Clamp01(t);return t*t*t*(10-15*t+6*t*t);}
        public Quaternion FootRotation(Pose p, bool left)
        {
            var f=left?reference.Left:reference.Right;
            return p.Orientations[f.Ankle]*Quaternion.Inverse(f.NeutralRotation);
        }
        public Vector3 FootPosition(Pose p,bool left) => p.Points[(left?reference.Left:reference.Right).Ankle];
        public float SoleHeight(Pose p,bool left)
        {
            var f=left?reference.Left:reference.Right; var q=FootRotation(p,left); var a=FootPosition(p,left);
            float y=float.PositiveInfinity;foreach(var point in f.SoleNeutralPoints)y=Mathf.Min(y,(a+q*point).y);return y;
        }
        int Index(string name) {for(int i=0;i<28;i++)if(reference.Bones[i].Name==name)return i;throw new InvalidOperationException(name);}
        static Vector3 Normal(Vector3 hip,Vector3 knee,Vector3 ankle)
        {
            var n=Vector3.Cross(ankle-hip,knee-hip);return n.magnitude>1e-7f?n.normalized:Vector3.right;
        }
        Vector3 BendNormal(Pose p, WorkerStopMotionReference.Foot f) => Normal(p.Points[f.Thigh],p.Points[f.Calf],p.Points[f.Ankle]);
        Vector3 IdleNormal(WorkerStopMotionReference.Foot f) => Normal(reference.Bones[f.Thigh].IdlePosition,reference.Bones[f.Calf].IdlePosition,reference.Bones[f.Ankle].IdlePosition);

        public void Apply(Pose entry, double elapsed, double duration, bool firstRight,
            Vector3 leftGoalWorld, Quaternion leftRotationWorld, Vector3 rightGoalWorld, Quaternion rightRotationWorld,
            Quaternion capturedYaw)
        {
            Restore(); saved=Capture(Time.timeAsDouble);
            float u=(float)(elapsed/duration), w=Quint(u);
            for(int i=0;i<28;i++) {
                var r=reference.Bones[i];bones[i].SetLocalPositionAndRotation(Vector3.Lerp(entry.Positions[i],r.IdleLocalPosition,w),
                    Quaternion.Slerp(entry.Rotations[i],r.IdleLocalRotation,w));
                bones[i].localScale=Vector3.Lerp(entry.Scales[i],r.IdleLocalScale,w);
            }
            MaximumReachResidual=MaximumAnkleError=0;PelvisAdditionalShift=Vector3.zero;
            // Source endpoints are exact whole-rig poses, without an extra support solve.
            // Runtime nevertheless solves t=0 when the visual origin has moved since capture.
            if(elapsed<=1e-10 && (visual.position-entry.VisualPosition).sqrMagnitude<1e-12f && Quaternion.Angle(visual.rotation,entry.VisualRotation)<.001f) return;
            if(elapsed>=duration-1e-10) {
                ActualFoot(true,out var left,out var leftQ);ActualFoot(false,out var right,out var rightQ);
                bool settled=Vector3.Distance(left,leftGoalWorld)<.00005f&&Vector3.Distance(right,rightGoalWorld)<.00005f;
                foreach(var axis in new[]{Vector3.up,Vector3.forward})settled&=Vector3.Distance(leftQ*axis,leftRotationWorld*axis)<.00001f&&Vector3.Distance(rightQ*axis,rightRotationWorld*axis)<.00001f;
                if(settled)return;
                // A still-moving visual origin does not release planted world features.
                // Continue solving the unchanged final goals until the origin reaches its endpoint.
            }
            float pulse=Mathf.Pow(Mathf.Sin(Mathf.PI*u),2);
            float sign=firstRight?1:-1;
            Vector3 shift=new Vector3(sign*(.016f*Mathf.Pow(Mathf.Sin(Mathf.PI*Mathf.Min(1,(float)(elapsed/(duration*.52)))),2)
                -.010f*Mathf.Pow(Mathf.Sin(Mathf.PI*Mathf.Max(0,(float)((elapsed-duration*.48)/(duration*.52)))),2)),
                -.009f*pulse,.012f*pulse);
            int pelvis=Index("pelvis");bones[pelvis].position+=visual.rotation*shift;
            Vector3[] goals={visual.InverseTransformPoint(leftGoalWorld),visual.InverseTransformPoint(rightGoalWorld)};
            var feet=new[]{reference.Left,reference.Right};
            Vector3[] hips={visual.InverseTransformPoint(bones[feet[0].Thigh].position),visual.InverseTransformPoint(bones[feet[1].Thigh].position)};
            float[] lengths=new float[2];
            for(int i=0;i<2;i++)lengths[i]=Vector3.Distance(bones[feet[i].Thigh].position,bones[feet[i].Calf].position)
                +Vector3.Distance(bones[feet[i].Calf].position,bones[feet[i].Ankle].position)-.00005f-.010f*pulse;
            Vector3 correction=Vector3.zero,low=new Vector3(-.035f-shift.x,-.060f-shift.y,-.120f-shift.z),high=new Vector3(.035f-shift.x,.010f-shift.y,.120f-shift.z);
            float epsilon=.0003f*pulse;
            for(int iteration=0;iteration<18;iteration++) {
                for(int i=0;i<2;i++) {
                    Vector3 line=hips[i]+correction-goals[i];float distance=line.magnitude,excess=distance-lengths[i];
                    float amount=.5f*(excess+Mathf.Sqrt(excess*excess+epsilon*epsilon));
                    if(distance>1e-8f)correction-=line*(amount/distance);
                }
                for(int i=0;i<3;i++)correction[i]=Mathf.Clamp(correction[i],low[i],high[i]);
            }
            for(int i=0;i<2;i++)MaximumReachResidual=Mathf.Max(MaximumReachResidual,(hips[i]+correction-goals[i]).magnitude-lengths[i]);
            if(MaximumReachResidual>.00002f)throw new InvalidOperationException("Stop V6 internal pelvis cannot preserve leg reach.");
            bones[pelvis].position+=visual.rotation*correction;PelvisAdditionalShift=shift+correction;
            for(int i=0;i<2;i++) {
                var f=feet[i];Vector3 na=BendNormal(entry,f), nb=IdleNormal(f);
                Vector3 normal=capturedYaw*(Quaternion.Slerp(Quaternion.identity,RegripRotation.FromTo(na,nb),w)*na);
                Solve(f.Thigh,f.Calf,f.Ankle,i==0?leftGoalWorld:rightGoalWorld,
                    (i==0?leftRotationWorld:rightRotationWorld)*f.NeutralRotation,normal,true);
            }
            foreach(string side in new[]{"L","R"}) {
                int upper=Index("upper_arm_"+side),lower=Index("forearm_"+side),hand=Index("hand_"+side);
                // Hold the captured base hands in the current visual frame; D regrip follows this base.
                Vector3 target=visual.TransformPoint(entry.Points[hand]);
                Solve(upper,lower,hand,target,visual.rotation*entry.Orientations[hand],Vector3.zero,false);
            }
            UpdateSupport("thigh_L","calf_L","foot_L","knee_support_L");
            UpdateSupport("thigh_R","calf_R","foot_R","knee_support_R");
            UpdateSupport("upper_arm_L","forearm_L","hand_L","elbow_support_L");
            UpdateSupport("upper_arm_R","forearm_R","hand_R","elbow_support_R");
        }
        void Solve(int upper,int lower,int end,Vector3 target,Quaternion rotation,Vector3 normal,bool stable)
        {
            var a=bones[upper].position;var b=bones[lower].position;var c=bones[end].position;
            float l1=(b-a).magnitude,l2=(c-b).magnitude;Vector3 line=target-a;float distance=line.magnitude;
            float excess=Mathf.Max(0,distance-(l1+l2-.00005f));MaximumReachResidual=Mathf.Max(MaximumReachResidual,excess);
            // The later D layer owns final hand grips; an unreachable intermediate hand target keeps the blended base arm.
            if(!stable&&(distance<=Mathf.Abs(l1-l2)+.00005f||excess>.00002f))return;
            if(distance<=Mathf.Abs(l1-l2)+.00005f||excess>.00002f)throw new InvalidOperationException("Stop V6 unreachable existing-length limb.");
            Vector3 unit=line/distance,pole=stable?Vector3.Cross(normal,unit):Vector3.ProjectOnPlane(b-a,unit);
            if(!stable&&pole.magnitude<1e-6f)pole=Vector3.ProjectOnPlane(visual.forward,unit);
            if(pole.magnitude<1e-5f)throw new InvalidOperationException("Stop V6 degenerate oriented bend plane.");
            pole.Normalize();float along=(l1*l1-l2*l2+distance*distance)/(2*distance);
            Vector3 knee=a+unit*along+pole*Mathf.Sqrt(Mathf.Max(0,l1*l1-along*along));
            bones[upper].rotation=RegripRotation.FromTo(b-a,knee-a)*bones[upper].rotation;
            bones[lower].rotation=RegripRotation.FromTo(bones[end].position-bones[lower].position,target-bones[lower].position)*bones[lower].rotation;
            bones[end].rotation=rotation;
            float error=Vector3.Distance(bones[end].position,target);MaximumAnkleError=Mathf.Max(MaximumAnkleError,error);
            if(error>.00015f||Mathf.Abs(Vector3.Distance(bones[upper].position,bones[lower].position)-l1)>.00015f||Mathf.Abs(Vector3.Distance(bones[lower].position,bones[end].position)-l2)>.00015f)
                throw new InvalidOperationException("Stop V6 limb endpoint/length validation failed.");
        }
        void UpdateSupport(string upperName,string lowerName,string endName,string supportName)
        {
            int upper=Index(upperName),lower=Index(lowerName),end=Index(endName),support=Index(supportName);
            Vector3 direction=((bones[lower].position-bones[upper].position).normalized+(bones[end].position-bones[lower].position).normalized).normalized;
            var rest=reference.Bones[support];
            bones[support].SetPositionAndRotation(bones[lower].position,
                RegripRotation.FromTo(visual.rotation*rest.RestAxis,direction)*visual.rotation*rest.RestRotation);
        }
    }
}
