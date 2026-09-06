using System;
namespace HowToSuck
{
    public enum AimMountStatus { Reachable, InvalidInput, OutsidePitchRange, DisplacementLimit }
    public readonly struct AimMountResult
    {
        public readonly AimMountStatus Status;
        public readonly double Pitch, X, Y, Z, CorrectionLength;
        public bool Reachable => Status == AimMountStatus.Reachable;
        public AimMountResult(AimMountStatus status, double pitch, double x, double y, double z, double correction)
        { Status=status; Pitch=pitch; X=x; Y=y; Z=z; CorrectionLength=correction; }
    }

    // Pure per-tool preset: fixed original aim angle, unit rig scale, no Animator/Unity state.
    // Profile covers ONLY the frozen C28 MK1 and the 45 imported source poses in the adjacent evidence.
    public static class NozzleAimMountPolicy
    {
        public const double MinimumPitch=-80, MaximumPitch=80, ProfileStep=.5;
        public const double NeutralX=-.17, NeutralY=-.16, NeutralZ=.78;
        private static readonly double[] TangentY=BuildTangents(0), TangentZ=BuildTangents(1);

        // Failure returns the proposed position for diagnostics; callers MUST NOT apply it.
        // A caller-selected limit is mandatory. The current sampled geometry fits .45 m.
        public static AimMountResult Evaluate(double pitch, double maximumDisplacement)
        {
            if(!Finite(pitch)||!Finite(maximumDisplacement)||maximumDisplacement<0)
                return new AimMountResult(AimMountStatus.InvalidInput,pitch,NeutralX,NeutralY,NeutralZ,0);
            if(pitch<MinimumPitch||pitch>MaximumPitch)
                return new AimMountResult(AimMountStatus.OutsidePitchRange,pitch,NeutralX,NeutralY,NeutralZ,0);
            double sample=(pitch-MinimumPitch)/ProfileStep;
            int i=Math.Min((int)sample,C28AimMountProfile.Values.Length/2-2);
            double t=sample-i;
            double dy=Hermite(i,t,0,TangentY),dz=Hermite(i,t,1,TangentZ);
            double correction=Math.Sqrt(dy*dy+dz*dz);
            var status=correction<=maximumDisplacement?AimMountStatus.Reachable:AimMountStatus.DisplacementLimit;
            return new AimMountResult(status,pitch,NeutralX,NeutralY+dy,NeutralZ+dz,correction);
        }
        private static double Hermite(int i,double t,int axis,double[] slopes)
        {
            var v=C28AimMountProfile.Values;double a=v[2*i+axis],b=v[2*(i+1)+axis];
            double tt=t*t,ttt=tt*t;
            return (2*ttt-3*tt+1)*a+(ttt-2*tt+t)*ProfileStep*slopes[i]+
                   (-2*ttt+3*tt)*b+(ttt-tt)*ProfileStep*slopes[i+1];
        }
        private static double[] BuildTangents(int axis)
        {
            var v=C28AimMountProfile.Values;int n=v.Length/2;var m=new double[n];
            for(int i=0;i<n;i++)
            {
                double left=i>0?(v[2*i+axis]-v[2*(i-1)+axis])/ProfileStep:0;
                double right=i<n-1?(v[2*(i+1)+axis]-v[2*i+axis])/ProfileStep:0;
                if(i==0)m[i]=right;else if(i==n-1)m[i]=left;
                else m[i]=left*right<=0?0:2*left*right/(left+right);
            }
            return m;
        }
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
    }
}
