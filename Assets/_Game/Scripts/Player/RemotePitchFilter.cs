using System;
namespace HowToSuck
{
    // Render-only pitch. Velocity persists across wire updates to soften delayed packet batches.
    // This is the closed-form critically damped response (omega=2/.14s), not a frame-rate speed cap.
    public sealed class RemotePitchFilter
    {
        private double value,velocity;
        public float Value => (float)value;
        public double Velocity => velocity;
        private static bool Finite(double x) => !double.IsNaN(x)&&!double.IsInfinity(x);
        public void Reset(float target)
        {
            value=Finite(target)?Math.Max(-80,Math.Min(80,target)):0;
            velocity=0;
        }
        public float Step(float target,float deltaTime)
        {
            if(!Finite(target)||!Finite(deltaTime)||deltaTime<=0)return Value;
            double t=Math.Max(-80,Math.Min(80,target)),change=value-t,omega=2/.14;
            double temporary=(velocity+omega*change)*deltaTime,decay=Math.Exp(-omega*deltaTime);
            double next=t+(change+temporary)*decay;
            double nextVelocity=(velocity-omega*temporary)*decay;
            if((t-value)*(next-t)>0){next=t;nextVelocity=0;}
            if(next>80){next=80;nextVelocity=0;}else if(next< -80){next=-80;nextVelocity=0;}
            value=next;velocity=nextVelocity;return Value;
        }
    }
}
