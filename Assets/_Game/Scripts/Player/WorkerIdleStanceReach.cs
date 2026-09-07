using System;
namespace HowToSuck
{
    // Limit only the optional pelvis translation; never stretch bones or move planted targets.
    public static class WorkerIdleStanceReach
    {
        public static double Limit(double x,double y,double z,double ox,double oy,double oz,double first,double second)
        {
            foreach(double v in new[]{x,y,z,ox,oy,oz,first,second})
                if(double.IsNaN(v)||double.IsInfinity(v))throw new InvalidOperationException("Idle stance reach input is not finite.");
            if(first<.001||second<.001)throw new InvalidOperationException("Idle stance leg segment is invalid.");
            double d2=x*x+y*y+z*z,d=Math.Sqrt(d2),minimum=Math.Abs(first-second),maximum=first+second;
            if(d<minimum-.0001||d>maximum+.0001)throw new InvalidOperationException("Existing idle stance leg geometry is inconsistent.");
            // A straight/folded base pose has no room for this decorative translation.
            double inner=minimum+.0001,outer=maximum-.0001;
            if(d<=inner||d>=outer)return 0;
            double a=ox*ox+oy*oy+oz*oz;if(a<1e-16)return 1;
            double b=-2*(x*ox+y*oy+z*oz),limit=1;
            foreach(double radius in new[]{inner,outer}){
                double c=d2-radius*radius,disc=b*b-4*a*c;
                if(disc<0)continue;
                double root=Math.Sqrt(disc);
                foreach(double t in new[]{(-b-root)/(2*a),(-b+root)/(2*a)})
                    if(t>=0&&t<limit)limit=t;
            }
            return limit<1?Math.Max(0,limit*.999):1;
        }
    }
}
