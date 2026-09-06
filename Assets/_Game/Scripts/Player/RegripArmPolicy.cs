using System;
namespace HowToSuck
{
 // Existing-length elbow solve in the same Unity root metre space as the mount policy.
 // Inputs must already include this frame's torso and clavicle rotations. No bone translation/scale.
 public static class RegripArmPolicy
 {
  public static double PoleAndForearmRollWeight(double pitch)
  {
   if (!Finite(pitch) || pitch < -80 || pitch > 80) return double.NaN;
   double t=Math.Clamp(pitch/20,0,1);return t*t*(3-2*t);
  }
  public static bool TryElbow(double pitch,bool left,ArmPoint shoulder,ArmPoint sourceElbow,
   ArmPoint wrist,ArmPoint heldHandAxis,double upperLength,double lowerLength,out ArmPoint elbow)
  {
   elbow=sourceElbow;double weight=PoleAndForearmRollWeight(pitch);
   if(!Finite(weight)||!Finite(shoulder)||!Finite(sourceElbow)||!Finite(wrist)||!Finite(heldHandAxis)
    ||!Finite(upperLength)||!Finite(lowerLength)||upperLength<=0||lowerLength<=0)return false;
   ArmPoint dv=Sub(wrist,shoulder);double distance=Length(dv);
   // Reject an unreachable input. Never stretch, clamp pitch, or move the physical wrist silently.
   if(distance<Math.Abs(upperLength-lowerLength)+.0005 || distance>upperLength+lowerLength-.0005)return false;
   ArmPoint direction=Mul(dv,1/distance),source=Sub(sourceElbow,shoulder);
   ArmPoint pole=Sub(source,Mul(direction,Dot(source,direction)));double pl=Length(pole);
   if(pl<.0001)return false;pole=Mul(pole,1/pl);
   if(weight>0)
   {
    double hl=Length(heldHandAxis);if(hl<1e-9)return false;
    ArmPoint h=Mul(heldHandAxis,1/hl),desired=Mul(Sub(h,Mul(direction,Dot(h,direction))),-1);double dl=Length(desired);
    if(dl>1e-6)
    {
     desired=Mul(desired,1/dl);double angle=Math.Atan2(Dot(direction,Cross(pole,desired)),Dot(pole,desired));
     // Fixed physical Unity chirality for the left elbow; avoids the recorded shortest-atan branch jump.
     if(left && angle<0)angle+=2*Math.PI;
     pole=Rotate(pole,direction,angle*weight);
    }
   }
   double along=(upperLength*upperLength-lowerLength*lowerLength+distance*distance)/(2*distance);
   double height=Math.Sqrt(Math.Max(0,upperLength*upperLength-along*along));
   elbow=Add(shoulder,Add(Mul(direction,along),Mul(pole,height)));return true;
  }
  public static bool TryHeldDirection(double pitch,bool left,ArmPoint referenceDirection,ArmPoint physicalBarAxis,out ArmPoint heldDirection)
  {
   heldDirection=referenceDirection;double angle=NozzleAimMountPolicy.RegripAngleDegrees(pitch,left),n=Length(physicalBarAxis);
   if(!Finite(angle)||!Finite(referenceDirection)||!Finite(physicalBarAxis)||n<1e-9)return false;
   heldDirection=Rotate(referenceDirection,Mul(physicalBarAxis,1/n),angle*Math.PI/180);return true;
  }
  private static ArmPoint Rotate(ArmPoint v,ArmPoint axis,double a)
  {double c=Math.Cos(a),s=Math.Sin(a);return Add(Add(Mul(v,c),Mul(Cross(axis,v),s)),Mul(axis,Dot(axis,v)*(1-c)));}
  private static ArmPoint Add(ArmPoint a,ArmPoint b)=>new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
  private static ArmPoint Sub(ArmPoint a,ArmPoint b)=>new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
  private static ArmPoint Mul(ArmPoint a,double s)=>new(a.X*s,a.Y*s,a.Z*s);
  private static double Dot(ArmPoint a,ArmPoint b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
  private static double Length(ArmPoint a)=>Math.Sqrt(Dot(a,a));
  private static ArmPoint Cross(ArmPoint a,ArmPoint b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
  private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
  private static bool Finite(ArmPoint p)=>Finite(p.X)&&Finite(p.Y)&&Finite(p.Z);
 }
}
