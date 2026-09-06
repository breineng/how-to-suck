using System;
namespace HowToSuck
{
    // Pure, one-step translation calculation. No Unity state, item types, prices or intake sizes.
    public readonly struct SuctionStepVector
    {
        public readonly double X,Y,Z;
        public SuctionStepVector(double x,double y,double z){X=x;Y=y;Z=z;}
        public double Length=>Math.Sqrt(X*X+Y*Y+Z*Z);
        public bool IsFinite=>Finite(X)&&Finite(Y)&&Finite(Z);
        private static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        public static SuctionStepVector operator +(SuctionStepVector a,SuctionStepVector b)=>new SuctionStepVector(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static SuctionStepVector operator -(SuctionStepVector a,SuctionStepVector b)=>new SuctionStepVector(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static SuctionStepVector operator *(SuctionStepVector a,double scale)=>new SuctionStepVector(a.X*scale,a.Y*scale,a.Z*scale);
    }
    public readonly struct SuctionStableStep
    {
        public readonly double AttractionGain;
        public readonly SuctionStepVector CorrectionForce,PredictedVelocity;
        public readonly bool Limited;
        public SuctionStableStep(double gain,SuctionStepVector correction,SuctionStepVector velocity,bool limited)
        {AttractionGain=gain;CorrectionForce=correction;PredictedVelocity=velocity;Limited=limited;}
    }
    public static class SuctionStabilityMath
    {
        public static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        public static SuctionStableStep Solve(double mass,double stiffness,double step,double distance,SuctionStepVector velocity,SuctionStepVector totalForce)
        {
            if(!Finite(mass)||mass<=0||!Finite(stiffness)||stiffness<=0||!Finite(step)||step<=0||
                !Finite(distance)||distance<0||!velocity.IsFinite||!totalForce.IsFinite)
                throw new ArgumentOutOfRangeException("A finite dynamic body, stiffness, step and visible distance are required.");
            // Implicit Euler of m*x'' + c*x' + K*x = sum(K_i*target_i), c=2*sqrt(K*m).
            // Aggregate once, not a capped damping factor followed by unbounded explicit attraction.
            double omega=Math.Sqrt(stiffness/mass),u=omega*step;
            double gain=1/(1+2*u+u*u);
            var predicted=(velocity+totalForce*(step/mass))*gain;
            // Translation-only safety reserve: an already-fast body must not cross the remaining
            // visible surface distance in one step. Angular/contact impulses are native-test scope.
            double maximum=.8*distance/step,length=predicted.Length;
            bool limited=length>maximum;
            if(limited)predicted=predicted*(maximum/length);
            var correction=(predicted-velocity)*(mass/step)-totalForce*gain;
            if(!Finite(gain)||!predicted.IsFinite||!correction.IsFinite)
                throw new ArgumentOutOfRangeException("Suction step exceeds finite numerical range.");
            return new SuctionStableStep(gain,correction,predicted,limited);
        }
    }
}
