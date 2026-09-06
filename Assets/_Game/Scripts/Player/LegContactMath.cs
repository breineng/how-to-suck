using System;
namespace HowToSuck
{
    public enum LegContactStatus
    { Unbound, Disabled, Airborne, NoSupport, NoPenetration, Corrected, OverBudget, Unreachable, InvalidGeometry }

    // Pure geometric rules; no transforms, clocks, authority or Unity callbacks.
    public static class LegContactMath
    {
        public readonly struct V
        {
            public readonly double X,Y,Z;
            public V(double x,double y,double z){X=x;Y=y;Z=z;}
            public static V operator +(V a,V b)=>new V(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
            public static V operator -(V a,V b)=>new V(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
            public static V operator *(V a,double b)=>new V(a.X*b,a.Y*b,a.Z*b);
            public double Length=>Math.Sqrt(Dot(this,this));
            public bool Finite=>F(X)&&F(Y)&&F(Z);
            public static double Dot(V a,V b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
        }
        public static bool F(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
        public static LegContactStatus Lift(bool grounded,bool support,double penetration,
            double maximum,double tolerance,double clearance,out double lift)
        {
            lift=0;
            if(!F(penetration)||!F(maximum)||!F(tolerance)||!F(clearance)||
                maximum<=0||maximum>.060001||tolerance<0||clearance<0||clearance>.003)
                return LegContactStatus.InvalidGeometry;
            if(!grounded)return LegContactStatus.Airborne;
            if(!support)return LegContactStatus.NoSupport;
            if(penetration<=tolerance)return LegContactStatus.NoPenetration;
            double required=penetration+clearance;
            if(required>maximum)return LegContactStatus.OverBudget;
            lift=required;return LegContactStatus.Corrected;
        }
        public static bool Knee(V hip,V knee,V ankle,V target,V fallback,out V solved)
        {
            solved=default;
            if(!hip.Finite||!knee.Finite||!ankle.Finite||!target.Finite||!fallback.Finite)return false;
            double upper=(knee-hip).Length,lower=(ankle-knee).Length,d=(target-hip).Length;
            if(upper<.001||lower<.001||d<.00001||
                d>upper+lower-.00001||d<Math.Abs(upper-lower)+.00001)return false;
            V direction=(target-hip)*(1/d);
            V pole=(knee-hip)-direction*V.Dot(knee-hip,direction);
            if(pole.Length<.00001)pole=fallback-direction*V.Dot(fallback,direction);
            if(pole.Length<.00001)return false;
            pole=pole*(1/pole.Length);
            double along=(upper*upper-lower*lower+d*d)/(2*d);
            solved=hip+direction*along+pole*Math.Sqrt(Math.Max(0,upper*upper-along*along));
            return solved.Finite&&Math.Abs((solved-hip).Length-upper)<.000001&&
                Math.Abs((target-solved).Length-lower)<.000001;
        }
    }
}
