using System;
namespace HowToSuck
{
 public enum AimMountStatus { Reachable, InvalidInput, OutsidePitchRange, DisplacementLimit }
 public readonly struct AimMountResult
 {
  public readonly AimMountStatus Status;
  public readonly double Pitch,X,Y,Z,CorrectionLength,ClavicleWeight;
  public bool Reachable=>Status==AimMountStatus.Reachable;
  public AimMountResult(AimMountStatus status,double pitch,double x,double y,double z,double correction,double weight)
  {Status=status;Pitch=pitch;X=x;Y=y;Z=z;CorrectionLength=correction;ClavicleWeight=weight;}
 }
 public readonly struct ArmPoint
 {
  public readonly double X,Y,Z;
  public ArmPoint(double x,double y,double z){X=x;Y=y;Z=z;}
 }
 // Pure staging preset. Both physical nozzle and rendered tool use this SAME position/full pitch.
 // Envelope: current RegripD source, 3866 source/local-TRS poses; extended left bar and joined NozzleV3.
 public static class NozzleAimMountPolicy
 {
  public const double MinimumPitch=-80,MaximumPitch=80,ProfileStep=.5;
  public const double NeutralX=-.17,NeutralY=-.16,NeutralZ=.78,ClavicleTargetElevationDegrees=12;
  private static readonly double[][] Tangents={BuildTangents(0),BuildTangents(1),BuildTangents(2)};
  public static AimMountResult Evaluate(double pitch,double maximumDisplacement)
  {
   if(!Finite(pitch)||!Finite(maximumDisplacement)||maximumDisplacement<0)
    return new AimMountResult(AimMountStatus.InvalidInput,pitch,NeutralX,NeutralY,NeutralZ,0,0);
   if(pitch<MinimumPitch||pitch>MaximumPitch)
    return new AimMountResult(AimMountStatus.OutsidePitchRange,pitch,NeutralX,NeutralY,NeutralZ,0,0);
   double sample=(pitch-MinimumPitch)/ProfileStep;int i=Math.Min((int)sample,FootV3AimMountProfile.Values.Length/3-2);double t=sample-i;
   double dx=Hermite(i,t,0),dy=Hermite(i,t,1),dz=Hermite(i,t,2),length=Math.Sqrt(dx*dx+dy*dy+dz*dz);
   // Invalid/budget-exceeded results are diagnostics; callers MUST NOT apply them.
   return new AimMountResult(length<=maximumDisplacement?AimMountStatus.Reachable:AimMountStatus.DisplacementLimit,pitch,NeutralX+dx,NeutralY+dy,NeutralZ+dz,length,ShoulderWeight(pitch));
  }
  public static double ShoulderWeight(double pitch)
  {
   if(!Finite(pitch)||pitch<MinimumPitch||pitch>MaximumPitch)return double.NaN;
   double t=Math.Clamp((-pitch-30)/50,0,1);return t*t*(3-2*t)+.25*Math.Pow(Math.Sin(Math.PI*t),2);
  }
  // Inputs share one metre space, Y-up. Start from this frame's Animator pose, never last IK frame.
  // Only rotates the existing clavicle: origin and bone length are preserved. Returns a target
  // endpoint; caller applies a FromTo rotation to the original clavicle, NOT a bone translation.
  public static bool TryClavicleEndpoint(double pitch,ArmPoint origin,ArmPoint shoulder,out ArmPoint endpoint)
  {
   endpoint=shoulder;double w=ShoulderWeight(pitch);
   if(!Finite(w)||!Finite(origin.X)||!Finite(origin.Y)||!Finite(origin.Z)||!Finite(shoulder.X)||!Finite(shoulder.Y)||!Finite(shoulder.Z))return false;
   if(w==0)return true;
   double x=shoulder.X-origin.X,y=shoulder.Y-origin.Y,z=shoulder.Z-origin.Z,h=Math.Sqrt(x*x+z*z),length=Math.Sqrt(x*x+y*y+z*z);
   if(h<1e-9||length<1e-9)return false;
   double a0=Math.Atan2(y,h),a=a0+(ClavicleTargetElevationDegrees*Math.PI/180-a0)*w,c=length*Math.Cos(a)/h;
   endpoint=new ArmPoint(origin.X+x*c,origin.Y+length*Math.Sin(a),origin.Z+z*c);return true;
  }
  // Physical regrip around the reference bar axis, with a52.5mm outward left-contact shift.
  public static double RegripAngleDegrees(double pitch,bool left)
  {
   if(!Finite(pitch)||pitch<MinimumPitch||pitch>MaximumPitch)return double.NaN;
   double t=Math.Clamp((left?pitch/20:(pitch-10)/50),0,1);return (left?-90:30)*t*t*(3-2*t);
  }
  public static bool TryRegripPoint(double pitch,bool left,ArmPoint point,ArmPoint centre,ArmPoint axis,out ArmPoint result)
  {
   result=point;double angle=RegripAngleDegrees(pitch,left);
   if(!Finite(angle)||!Finite(point.X)||!Finite(point.Y)||!Finite(point.Z)||!Finite(centre.X)||!Finite(centre.Y)||!Finite(centre.Z)||!Finite(axis.X)||!Finite(axis.Y)||!Finite(axis.Z))return false;
   double n=Math.Sqrt(axis.X*axis.X+axis.Y*axis.Y+axis.Z*axis.Z);if(n<1e-9)return false;
   if(angle==0){double shift=left?.0525:0;result=new(point.X-axis.X/n*shift,point.Y-axis.Y/n*shift,point.Z-axis.Z/n*shift);return true;}
   double x=point.X-centre.X,y=point.Y-centre.Y,z=point.Z-centre.Z,ax=axis.X/n,ay=axis.Y/n,az=axis.Z/n,r=angle*Math.PI/180,c=Math.Cos(r),s=Math.Sin(r),d=ax*x+ay*y+az*z;
   double outward=left?.0525:0;result=new ArmPoint(centre.X-ax*outward+x*c+(ay*z-az*y)*s+ax*d*(1-c),centre.Y-ay*outward+y*c+(az*x-ax*z)*s+ay*d*(1-c),centre.Z-az*outward+z*c+(ax*y-ay*x)*s+az*d*(1-c));return true;
  }
  // Returns a point carried by existing spine/chest rotations. Pelvis and leg TRS are never written.
  // Pass ORIGINAL current-Animator source points, and Idle torso axes in the same root metre space.
  public static bool TryTorsoPoint(double pitch,double pelvisDrop,ArmPoint spineHead,ArmPoint spineTail,ArmPoint chestHead,ArmPoint chestTail,ArmPoint idleSpineAxis,ArmPoint idleChestAxis,ArmPoint point,out ArmPoint result)
  {
   result=point;
   if(!Finite(pitch)||pitch<MinimumPitch||pitch>MaximumPitch||!Finite(pelvisDrop))return false;
   foreach(var q in new[]{spineHead,spineTail,chestHead,chestTail,idleSpineAxis,idleChestAxis,point})if(!Finite(q.X)||!Finite(q.Y)||!Finite(q.Z))return false;
   double w=Smooth((pitch-10)/35)*Smooth((pelvisDrop-.015)/.090);if(w==0)return true;
   double a=-Math.Max(0,Math.Atan2(spineTail.Z-spineHead.Z,spineTail.Y-spineHead.Y)-Math.Atan2(idleSpineAxis.Z,idleSpineAxis.Y)+8*Math.PI/180)*w;
   result=RotateX(point,spineHead,a);chestHead=RotateX(chestHead,spineHead,a);chestTail=RotateX(chestTail,spineHead,a);
   a=-Math.Max(0,Math.Atan2(chestTail.Z-chestHead.Z,chestTail.Y-chestHead.Y)-Math.Atan2(idleChestAxis.Z,idleChestAxis.Y)+8*Math.PI/180)*w;
   result=RotateX(result,chestHead,a);return true;
  }
  private static ArmPoint RotateX(ArmPoint q,ArmPoint pivot,double angle)
  {double y=q.Y-pivot.Y,z=q.Z-pivot.Z,c=Math.Cos(angle),s=Math.Sin(angle);return new(q.X,pivot.Y+c*y-s*z,pivot.Z+s*y+c*z);}
  private static double Smooth(double t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}
  private static double Hermite(int i,double t,int axis)
  {
   var v=FootV3AimMountProfile.Values;var slopes=Tangents[axis];double a=v[3*i+axis],b=v[3*(i+1)+axis],tt=t*t,ttt=tt*t;
   return (2*ttt-3*tt+1)*a+(ttt-2*tt+t)*ProfileStep*slopes[i]+(-2*ttt+3*tt)*b+(ttt-tt)*ProfileStep*slopes[i+1];
  }
  private static double[] BuildTangents(int axis)
  {
   var v=FootV3AimMountProfile.Values;int n=v.Length/3;var m=new double[n];
   for(int i=0;i<n;i++)
   {
    double left=i>0?(v[3*i+axis]-v[3*(i-1)+axis])/ProfileStep:0,right=i<n-1?(v[3*(i+1)+axis]-v[3*i+axis])/ProfileStep:0;
    if(i==0)m[i]=right;else if(i==n-1)m[i]=left;else m[i]=left*right<=0?0:2*left*right/(left+right);
   }
   return m;
  }
  private static bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
 }
}

