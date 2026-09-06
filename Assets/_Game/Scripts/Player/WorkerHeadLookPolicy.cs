using System;
namespace HowToSuck
{
    // Final HeadLookV1D, presentation only; camera/nozzle retain their complete +/-80deg pitch.
    public static class WorkerHeadLookPolicy
    {
        public const float UpLimitDegrees = 30f, DownLimitDegrees = 25f;
        private static double Cubic(double t) => 1.5d*t-.5d*t*t*t;
        public static void Evaluate(float pitchDegrees, out float neckDegrees, out float headDegrees)
        {
            if (float.IsNaN(pitchDegrees) || float.IsInfinity(pitchDegrees) || Math.Abs(pitchDegrees)>80.001f)
                throw new ArgumentOutOfRangeException(nameof(pitchDegrees),"Expected rendered aim pitch in [-80,80] degrees.");
            double p=pitchDegrees,total;
            if(p>=0) total=25d*Cubic(Math.Min(1d,p/80d));
            else if(p<=-4) total=30d*Cubic(Math.Max(-1d,p/45d));
            else {
                // C1 Hermite join: retain measured fast up response and slower down response.
                double t=(p+4d)/4d,t2=t*t,t3=t2*t;
                double y0=30d*Cubic(-4d/45d);
                double m0=1d-(4d/45d)*(4d/45d),m1=25d*1.5d/80d;
                total=(2d*t3-3d*t2+1d)*y0+(t3-2d*t2+t)*4d*m0+(t3-t2)*4d*m1;
            }
            neckDegrees=(float)(total/3d);headDegrees=(float)(total*(2d/3d));
        }
    }
}
