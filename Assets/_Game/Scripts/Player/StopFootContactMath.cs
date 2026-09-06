using System;
using System.Collections.Generic;
namespace HowToSuck
{
    public static class StopFootContactMath
    {
        public static bool Budget(double horizontal,double fromAnimatorY,double fromVerticalBaseY,double maxHorizontal,double maxLift)
        {
            return LegContactMath.F(horizontal)&&LegContactMath.F(fromAnimatorY)&&LegContactMath.F(fromVerticalBaseY)&&
                LegContactMath.F(maxHorizontal)&&LegContactMath.F(maxLift)&&maxHorizontal>=.05&&maxHorizontal<=.25&&
                maxLift>0&&maxLift<=.060001&&horizontal>=0&&horizontal<=maxHorizontal&&
                Math.Abs(fromVerticalBaseY)<=maxLift&&fromAnimatorY<=maxLift+.00001;
        }
        // Actual near-floor vertices must span a sole area, not one heel edge or a toe point.
        public static bool Footprint(IReadOnlyList<LegContactMath.V> points)
        {
            if(points==null||points.Count<3)return false;
            double longest=0;LegContactMath.V start=default,end=default;
            for(int i=0;i<points.Count;i++) {
                if(!points[i].Finite)return false;
                for(int j=i+1;j<points.Count;j++) {
                    double x=points[j].X-points[i].X,z=points[j].Z-points[i].Z,d=x*x+z*z;
                    if(d>longest){longest=d;start=points[i];end=points[j];}
                }
            }
            if(longest<.0196)return false;
            double area=0,dx=end.X-start.X,dz=end.Z-start.Z;
            foreach(var p in points)area=Math.Max(area,Math.Abs(dx*(p.Z-start.Z)-dz*(p.X-start.X)));
            return area>=.003; // Twice area: >=15cm² with a >=14cm span.
        }
        public static LegContactMath.V Release(LegContactMath.V native,LegContactMath.V priorOffset,double weight)
        {
            if(!native.Finite||!priorOffset.Finite||!LegContactMath.F(weight)||weight<0||weight>1)throw new ArgumentException("Finite release input required.");
            // Native swing height is untouched; this never pulls an airborne leg to a floor.
            return new LegContactMath.V(native.X+priorOffset.X*weight,native.Y,native.Z+priorOffset.Z*weight);
        }
    }
}
