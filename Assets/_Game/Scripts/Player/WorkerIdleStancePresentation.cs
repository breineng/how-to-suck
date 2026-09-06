using System;
using UnityEngine;

namespace HowToSuck
{
    public static class WorkerIdleStancePresentation
    {
        public static void ApplyLower(PlayerAnimationView view,WorkerStanceCandidatePolicy.Frame frame,Action<Transform> save)
        {
            if(frame.Weight<=0)return;
            var reference=view.StopMotion.Reference;var bones=view.Body.bones;
            foreach(var bone in bones)save(bone);
            var left=reference.Left;var right=reference.Right;
            var lp=bones[left.Ankle].position;var lq=bones[left.Ankle].rotation;
            var rp=bones[right.Ankle].position;var rq=bones[right.Ankle].rotation;
            // The pelvis moves within the skeleton; both real feet retain their world poses.
            view.RegripD.Pelvis.position+=view.VisualRoot.rotation*frame.PelvisRootOffset;
            SolveLeg(bones[left.Thigh],bones[left.Calf],bones[left.Ankle],lp,lq);
            SolveLeg(bones[right.Thigh],bones[right.Calf],bones[right.Ankle],rp,rq);
            Support(view,reference,bones,"thigh_L","calf_L","foot_L","knee_support_L");
            Support(view,reference,bones,"thigh_R","calf_R","foot_R","knee_support_R");
        }
        private static void SolveLeg(Transform upper,Transform lower,Transform foot,Vector3 goal,Quaternion rotation)
        {
            Vector3 a=upper.position,b=lower.position,c=foot.position,line=goal-a;
            float first=Vector3.Distance(a,b),second=Vector3.Distance(b,c),distance=line.magnitude;
            if(distance<=Mathf.Abs(first-second)+.00001f||distance>=first+second-.00001f)throw new InvalidOperationException("Idle stance leg cannot reach its existing planted foot.");
            Vector3 unit=line/distance,pole=Vector3.ProjectOnPlane(b-a,unit).normalized;
            if(pole.sqrMagnitude<.5f)throw new InvalidOperationException("Idle stance leg plane is degenerate.");
            float along=(first*first-second*second+distance*distance)/(2*distance);
            Vector3 knee=a+unit*along+pole*Mathf.Sqrt(Mathf.Max(0,first*first-along*along));
            upper.rotation=RegripRotation.FromTo(b-a,knee-a)*upper.rotation;
            lower.rotation=RegripRotation.FromTo(foot.position-lower.position,goal-lower.position)*lower.rotation;
            foot.rotation=rotation;
        }
        private static void Support(PlayerAnimationView view,WorkerStopMotionReference reference,Transform[] bones,string a,string b,string c,string name)
        {
            int Index(string value){for(int i=0;i<reference.Bones.Length;i++)if(reference.Bones[i].Name==value)return i;throw new InvalidOperationException(value);}
            var upper=bones[Index(a)];var lower=bones[Index(b)];var end=bones[Index(c)];int s=Index(name);
            var direction=((lower.position-upper.position).normalized+(end.position-lower.position).normalized).normalized;
            bones[s].SetPositionAndRotation(lower.position,RegripRotation.FromTo(view.VisualRoot.rotation*reference.Bones[s].RestAxis,direction)*view.VisualRoot.rotation*reference.Bones[s].RestRotation);
        }
        public static Vector3 ClavicleEndpoint(Vector3 pivot,Vector3 original,Vector3 regular,WorkerStanceCandidatePolicy.Frame frame,bool left)
        {
            if(frame.ClavicleOverrideWeight<=0)return regular;
            var source=original-pivot;float length=source.magnitude;
            var horizontal=Vector3.ProjectOnPlane(source,Vector3.up).normalized;
            horizontal=Quaternion.AngleAxis(left?frame.ClavicleProtractionDegrees:-frame.ClavicleProtractionDegrees,Vector3.up)*horizontal;
            float regularElevation=Mathf.Asin(Mathf.Clamp((regular-pivot).normalized.y,-1,1));
            float elevation=Mathf.Lerp(regularElevation,frame.ClavicleTargetElevationDegrees*Mathf.Deg2Rad,frame.ClavicleOverrideWeight);
            return pivot+(horizontal*Mathf.Cos(elevation)+Vector3.up*Mathf.Sin(elevation))*length;
        }
    }
}
