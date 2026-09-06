using System;
using System.Collections.Generic;
using UnityEngine;

namespace HowToSuck
{
    // First stage: vertical floor collision correction only. No foot planting or horizontal slip claim.
    [Serializable]
    public sealed class PlayerLegContactView
    {
        [Serializable] public sealed class Leg
        {
            public Transform Thigh,Calf,Foot,KneeSupport;
            [NonSerialized] internal Vector3[] Sole;
            [NonSerialized] internal Transform[] Bound;
            [NonSerialized] internal Vector3[] SavedPositions;
            [NonSerialized] internal Quaternion[] SavedRotations;
            [NonSerialized] internal bool Owned;
            public LegContactStatus Status {get;internal set;}
            public float RawPenetration {get;internal set;}
            public float AppliedLift {get;internal set;}
            public int SampleCount=>Sole!=null?Sole.Length:0;
            public bool HasOwnedPose=>Owned;
            internal bool Matches()=>Thigh!=null&&Calf!=null&&Foot!=null&&KneeSupport!=null&&Bound!=null&&Bound.Length==4&&
                Bound[0]==Thigh&&Bound[1]==Calf&&Bound[2]==Foot&&Bound[3]==KneeSupport;
            internal void Save()
            {
                for(int i=0;i<4;i++){SavedPositions[i]=Bound[i].localPosition;SavedRotations[i]=Bound[i].localRotation;}
                Owned=true;
            }
            internal void Restore()
            {
                if(!Owned)return;
                for(int i=0;i<4;i++)if(Bound[i]!=null)Bound[i].SetLocalPositionAndRotation(SavedPositions[i],SavedRotations[i]);
                Owned=false;
            }
        }
        public bool Enabled=true;
        [Range(.001f,.06f)] public float MaximumLift=.06f;
        [Range(0f,.003f)] public float PenetrationTolerance=.0005f,Clearance=.0005f;
        [Range(.99f,1f)] public float MinimumFloorNormalY=.9999f;
        public Leg Left=new Leg(),Right=new Leg();
        private SkinnedMeshRenderer boundBody;
        private Mesh boundMesh;

        public void RestorePose(){Left?.Restore();Right?.Restore();}
        public void ResetBinding()
        {
            RestorePose();boundBody=null;boundMesh=null;
            ClearLeg(Left);ClearLeg(Right);
        }
        private static void ClearLeg(Leg leg)
        {
            if(leg==null)return;
            leg.Sole=null;leg.Bound=null;leg.SavedPositions=null;leg.SavedRotations=null;
            leg.Status=LegContactStatus.Unbound;leg.RawPenetration=leg.AppliedLift=0;
        }
        public void Apply(SkinnedMeshRenderer body,PlayerMotor motor,Vector3 forward)
        {
            // Idempotent within a frame; restore the original Animator pose before measuring.
            RestorePose();
            if(!Enabled||motor==null||!motor.isActiveAndEnabled||body==null)
            {SetStatus(LegContactStatus.Disabled);return;}
            if(body!=boundBody||body.sharedMesh!=boundMesh||!ValidBindings())
            {
                ResetBinding();
                if(!TryBind(body)){SetStatus(LegContactStatus.InvalidGeometry);return;}
            }
            if(!motor.IsGrounded){SetStatus(LegContactStatus.Airborne);return;}
            int world=LayerMask.GetMask("World");
            if(world==0||!Finite(forward)||!LegContactMath.F(MaximumLift)||MaximumLift<=0||MaximumLift>.060001f||
                !LegContactMath.F(PenetrationTolerance)||PenetrationTolerance<0||PenetrationTolerance>.003f||
                !LegContactMath.F(Clearance)||Clearance<0||Clearance>.003f||
                !LegContactMath.F(MinimumFloorNormalY)||MinimumFloorNormalY<.99f||MinimumFloorNormalY>1f)
            {SetStatus(LegContactStatus.InvalidGeometry);return;}
            ApplyLeg(Left,world,forward);ApplyLeg(Right,world,forward);
        }
        private bool ValidBindings()=>Left!=null&&Right!=null&&Left.Matches()&&Right.Matches();
        private void SetStatus(LegContactStatus status){SetLegStatus(Left,status);SetLegStatus(Right,status);}
        private static void SetLegStatus(Leg leg,LegContactStatus status)
        {if(leg!=null){leg.Status=status;leg.RawPenetration=leg.AppliedLift=0;}}
        private bool TryBind(SkinnedMeshRenderer body)
        {
            var mesh=body.sharedMesh;
            if(mesh==null||!mesh.isReadable||Left==null||Right==null)return false;
            if(!BindLeg(body,mesh,Left)||!BindLeg(body,mesh,Right))return false;
            var unique=new HashSet<Transform>();
            foreach(var t in Left.Bound)if(!unique.Add(t))return false;
            foreach(var t in Right.Bound)if(!unique.Add(t))return false;
            boundBody=body;boundMesh=mesh;return true;
        }
        private static bool BindLeg(SkinnedMeshRenderer body,Mesh mesh,Leg leg)
        {
            if(leg.Thigh==null||leg.Calf==null||leg.Foot==null||leg.KneeSupport==null||
                leg.Calf.parent!=leg.Thigh||leg.Foot.parent!=leg.Calf||
                leg.Thigh.IsChildOf(leg.KneeSupport)||leg.Calf.IsChildOf(leg.KneeSupport)||
                (leg.Thigh.lossyScale-Vector3.one).sqrMagnitude>.000001f||
                (leg.Calf.lossyScale-Vector3.one).sqrMagnitude>.000001f||
                (leg.Foot.lossyScale-Vector3.one).sqrMagnitude>.000001f)return false;
            var skin=body.bones;int bone=Array.IndexOf(skin,leg.Foot);
            var vertices=mesh.vertices;var weights=mesh.boneWeights;var binds=mesh.bindposes;
            if(bone<0||bone>=binds.Length||weights.Length!=vertices.Length)return false;
            var used=new HashSet<int>(mesh.triangles);var rigid=new List<int>();float bottom=float.PositiveInfinity;
            for(int i=0;i<weights.Length;i++)
            {
                if(!used.Contains(i))continue;
                var w=weights[i];float amount=0;
                if(w.boneIndex0==bone)amount+=w.weight0;if(w.boneIndex1==bone)amount+=w.weight1;
                if(w.boneIndex2==bone)amount+=w.weight2;if(w.boneIndex3==bone)amount+=w.weight3;
                if(amount<.99999f)continue;
                rigid.Add(i);bottom=Mathf.Min(bottom,body.transform.TransformPoint(vertices[i]).y);
            }
            var points=new List<Vector3>();
            foreach(int index in rigid)
            {
                if(body.transform.TransformPoint(vertices[index]).y>bottom+.012f)continue;
                Vector3 point=binds[bone].MultiplyPoint3x4(vertices[index]);if(!Finite(point))return false;
                bool duplicate=false;foreach(var p in points)if((p-point).sqrMagnitude<1e-12f){duplicate=true;break;}
                if(!duplicate)points.Add(point);
            }
            if(points.Count<6)return false;
            leg.Sole=points.ToArray();leg.Bound=new[]{leg.Thigh,leg.Calf,leg.Foot,leg.KneeSupport};
            leg.SavedPositions=new Vector3[4];leg.SavedRotations=new Quaternion[4];
            return true;
        }
        private void ApplyLeg(Leg leg,int mask,Vector3 forward)
        {
            leg.RawPenetration=leg.AppliedLift=0;
            bool support=false;float penetration=float.NegativeInfinity;
            // Actual rigid skin samples, deduplicated at bind. Each ray checks its own World support;
            // a raised swing foot gets no pull toward the floor and a foot over a ledge gets no fake plane.
            foreach(var point in leg.Sole)
            {
                Vector3 world=leg.Foot.TransformPoint(point);
                if(!Finite(world)){leg.Status=LegContactStatus.InvalidGeometry;return;}
                if(!Physics.Raycast(world+Vector3.up*(MaximumLift+.08f),Vector3.down,out var hit,
                    MaximumLift+.16f,mask,QueryTriggerInteraction.Ignore)||hit.normal.y<MinimumFloorNormalY)continue;
                support=true;penetration=Mathf.Max(penetration,hit.point.y-world.y);
            }
            if(!support)penetration=0;
            leg.RawPenetration=penetration;
            leg.Status=LegContactMath.Lift(true,support,penetration,MaximumLift,PenetrationTolerance,Clearance,out double lift);
            if(leg.Status!=LegContactStatus.Corrected)return;
            Vector3 hip=leg.Thigh.position,knee=leg.Calf.position,ankle=leg.Foot.position;
            Vector3 target=ankle+Vector3.up*(float)lift;
            if(!LegContactMath.Knee(V(hip),V(knee),V(ankle),V(target),V(forward),out var solution))
            {leg.Status=LegContactStatus.Unreachable;return;}
            Vector3 solved=new Vector3((float)solution.X,(float)solution.Y,(float)solution.Z);
            float upperLength=Vector3.Distance(hip,knee),lowerLength=Vector3.Distance(knee,ankle);
            Quaternion upper=leg.Thigh.rotation,lower=leg.Calf.rotation,foot=leg.Foot.rotation,kneeSupport=leg.KneeSupport.rotation;
            leg.Save();
            leg.Thigh.rotation=Quaternion.FromToRotation(knee-hip,solved-hip)*upper;
            leg.Calf.rotation=Quaternion.FromToRotation(leg.Foot.position-leg.Calf.position,target-leg.Calf.position)*leg.Calf.rotation;
            Quaternion upperDelta=leg.Thigh.rotation*Quaternion.Inverse(upper),lowerDelta=leg.Calf.rotation*Quaternion.Inverse(lower);
            leg.KneeSupport.position=leg.Calf.position;
            leg.KneeSupport.rotation=Quaternion.Slerp(upperDelta,lowerDelta,.5f)*kneeSupport;
            leg.Foot.rotation=foot; // Preserve authored heel/toe roll and ankle orientation.
            if(Vector3.Distance(leg.Foot.position,target)>.00015f||
                Mathf.Abs(Vector3.Distance(leg.Thigh.position,leg.Calf.position)-upperLength)>.00015f||
                Mathf.Abs(Vector3.Distance(leg.Calf.position,leg.Foot.position)-lowerLength)>.00015f)
            {leg.Restore();leg.Status=LegContactStatus.InvalidGeometry;return;}
            leg.AppliedLift=(float)lift;
        }
        private static bool Finite(Vector3 v)=>LegContactMath.F(v.x)&&LegContactMath.F(v.y)&&LegContactMath.F(v.z);
        private static LegContactMath.V V(Vector3 v)=>new LegContactMath.V(v.x,v.y,v.z);
    }
}
