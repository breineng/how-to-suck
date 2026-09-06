using System;
using UnityEngine;
namespace HowToSuck
{
    [Serializable] public struct ToolGripDatum
    {
        public Vector3 Centre, Axis, Contact, SocketLocal;
        public Quaternion HandRotation;
        public Vector3 Wrist(float angle)
        { return Centre+Quaternion.AngleAxis(angle,Axis)*(Contact-Centre-HandRotation*SocketLocal); }
        public bool IsValid => ToolTierMountProfile.Finite(Centre)&&ToolTierMountProfile.Finite(Contact)&&
            ToolTierMountProfile.Finite(SocketLocal)&&ToolTierMountProfile.Finite(Axis)&&Mathf.Abs(Axis.sqrMagnitude-1)<.001f&&
            ToolTierMountProfile.Finite(HandRotation.x)&&ToolTierMountProfile.Finite(HandRotation.y)&&ToolTierMountProfile.Finite(HandRotation.z)&&ToolTierMountProfile.Finite(HandRotation.w)&&
            Mathf.Abs(HandRotation.x*HandRotation.x+HandRotation.y*HandRotation.y+HandRotation.z*HandRotation.z+HandRotation.w*HandRotation.w-1)<.001f;
    }
    [CreateAssetMenu(menuName="How to Suck/Tool tier mount")]
    public sealed class ToolTierMountProfile : ScriptableObject
    {
        public string TierId,SourceSha256,ReferencePrefabSha256;
        public bool NativePoseVerified;
        public ToolGripDatum ReferenceLeft,ReferenceRight,TargetLeft,TargetRight;
        public Vector3 EndPoint;
        [Range(.01f,.6f)] public float MaximumDisplacement=.6f;
        public static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        public static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
        public bool TryEvaluate(float pitch,WorkerStanceCandidatePolicy.Frame stance,out Vector3 result,bool requireVerified=true)
        {
            result=default;
            if((requireVerified&&!NativePoseVerified)||(TierId!="mk2"&&TierId!="mk3"&&TierId!="mk4")||
                !Finite(pitch)||pitch< -80||pitch>80||!Finite(MaximumDisplacement)||MaximumDisplacement<=0||MaximumDisplacement>.6f||
                !ReferenceLeft.IsValid||!ReferenceRight.IsValid||!TargetLeft.IsValid||!TargetRight.IsValid||!Finite(EndPoint))return false;
            var baseline=NozzleAimMountPolicy.Evaluate(pitch,MaximumDisplacement);if(!baseline.Reachable)return false;
            float left=(float)NozzleAimMountPolicy.RegripAngleDegrees(pitch,true);
            float right=stance.Weight>0?stance.RightRegripDegrees:(float)NozzleAimMountPolicy.RegripAngleDegrees(pitch,false);
            Vector3 delta=(ReferenceLeft.Wrist(left)+ReferenceRight.Wrist(right)-TargetLeft.Wrist(left)-TargetRight.Wrist(right))*.5f;
            // Preserve the known wrist midpoint using authored handle geometry, without moving either handle.
            // This calibration is not a reach proof; the asset remains unusable until the native pose probe passes.
            Vector3 neutral=(ReferenceLeft.Wrist(0)+ReferenceRight.Wrist(0)-TargetLeft.Wrist(0)-TargetRight.Wrist(0))*.5f;
            result=new Vector3((float)baseline.X,(float)baseline.Y,(float)baseline.Z)+delta+stance.AdditionalMountAimOffset;
            Vector3 origin=new Vector3((float)NozzleAimMountPolicy.NeutralX,(float)NozzleAimMountPolicy.NeutralY,(float)NozzleAimMountPolicy.NeutralZ)+neutral;
            return Finite(result)&&Vector3.Distance(result,origin)<=MaximumDisplacement;
        }
    }
}
