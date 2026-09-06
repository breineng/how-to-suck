using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HowToSuck
{
    /// <summary>
    /// Exact V6 trajectory formulas in one immutable captured, Y-up reference frame.
    /// +Z is forward; its validated floor is Y=0. No world/VisualRoot rebinding or IK.
    /// </summary>
    public static class StopTrajectoryV6
    {
        public enum StopKind { Walk, Run }
        public enum Side { Left, Right }
        public enum Contact { Air, EntryHeelOrToe, EntryFlat, EntryToe, IdleToe, IdleFlat }

        public readonly struct D3
        {
            public readonly double X, Y, Z;
            public D3(double x, double y, double z) { X=x; Y=y; Z=z; }
            public double Length => Math.Sqrt(X*X+Y*Y+Z*Z);
            public static D3 operator +(D3 a,D3 b) => new D3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
            public static D3 operator -(D3 a,D3 b) => new D3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
            public static D3 operator *(D3 a,double b) => new D3(a.X*b,a.Y*b,a.Z*b);
            public static D3 operator /(D3 a,double b) => a*(1/b);
            internal static D3 Lerp(D3 a,D3 b,double t) => a+(b-a)*t;
        }
        /// <summary>Hamilton quaternion, Unity component order x,y,z,w; relative to neutral foot basis.</summary>
        public readonly struct Q4
        {
            public readonly double X, Y, Z, W;
            public Q4(double x,double y,double z,double w) { X=x; Y=y; Z=z; W=w; }
            public static Q4 Identity => new Q4(0,0,0,1);
            public D3 Rotate(D3 v)
            {
                D3 q=new D3(X,Y,Z), t=Cross(q,v)*2;
                return v+t*W+Cross(q,t);
            }
            internal static Q4 Product(Q4 a,Q4 b) => new Q4(
                a.W*b.X+a.X*b.W+a.Y*b.Z-a.Z*b.Y,
                a.W*b.Y-a.X*b.Z+a.Y*b.W+a.Z*b.X,
                a.W*b.Z+a.X*b.Y-a.Y*b.X+a.Z*b.W,
                a.W*b.W-a.X*b.X-a.Y*b.Y-a.Z*b.Z);
            internal static Q4 XAngle(double radians) => new Q4(Math.Sin(radians*.5),0,0,Math.Cos(radians*.5));
            internal static Q4 Slerp(Q4 a,Q4 b,double t)
            {
                double dot=a.X*b.X+a.Y*b.Y+a.Z*b.Z+a.W*b.W;
                if(dot<0){b=new Q4(-b.X,-b.Y,-b.Z,-b.W);dot=-dot;}
                dot=Clamp(dot,-1,1);
                if(dot>1-1e-12) return Unit(new Q4(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t,a.W+(b.W-a.W)*t));
                double angle=Math.Acos(dot),den=Math.Sin(angle),u=Math.Sin((1-t)*angle)/den,v=Math.Sin(t*angle)/den;
                return Unit(new Q4(a.X*u+b.X*v,a.Y*u+b.Y*v,a.Z*u+b.Z*v,a.W*u+b.W*v));
            }
        }
        public readonly struct Pose
        {
            public D3 Position {get;}
            public Q4 Rotation {get;}
            public Pose(D3 position,Q4 rotation){Position=position;Rotation=rotation;}
        }
        public sealed class FootInput
        {
            public Pose Entry {get;}
            public Pose Idle {get;}
            public IReadOnlyList<D3> SoleNeutralPoints {get;}
            public D3 AnkleVelocity {get;}
            public double SoleVelocityY {get;}
            public FootInput(Pose entry,Pose idle,IReadOnlyList<D3> soleNeutralPoints,D3 ankleVelocity,double soleVelocityY)
            {
                if(soleNeutralPoints==null)throw new ArgumentNullException(nameof(soleNeutralPoints));
                var copy=new D3[soleNeutralPoints.Count];for(int i=0;i<copy.Length;i++)copy[i]=soleNeutralPoints[i];
                Entry=entry;Idle=idle;SoleNeutralPoints=new ReadOnlyCollection<D3>(copy);
                AnkleVelocity=ankleVelocity;SoleVelocityY=soleVelocityY;
            }
        }
        public sealed class Plan
        {
            public StopKind Kind {get;}
            public Side First {get;}
            public Side Free => First==Side.Left?Side.Right:Side.Left;
            public bool EntryAir {get;}
            public bool EntryBothSupported {get;}
            public double FloorLeft {get;}
            public double FloorRight {get;}
            public double FirstLanding {get;}
            public double FreeLanding {get;}
            public double ToeRoll {get;}
            public double Liftoff {get;}
            public double SecondLanding {get;}
            public double Duration {get;}
            public Pose LandingPose {get;}
            private readonly FootInput Left,Right;
            internal Plan(StopKind kind,Side first,bool air,bool both,double fl,double fr,double firstLand,
                Pose landing,double freeLand,double duration,FootInput left,FootInput right)
            {
                Kind=kind;First=first;EntryAir=air;EntryBothSupported=both;FloorLeft=fl;FloorRight=fr;
                FirstLanding=firstLand;LandingPose=landing;FreeLanding=freeLand;ToeRoll=freeLand+.055;
                Liftoff=freeLand+.105;SecondLanding=duration-.075;Duration=duration;Left=left;Right=right;
            }
            public FootInput Foot(Side side)=>side==Side.Left?Left:side==Side.Right?Right:throw new ArgumentOutOfRangeException(nameof(side));
            public double Floor(Side side)=>side==Side.Left?FloorLeft:side==Side.Right?FloorRight:throw new ArgumentOutOfRangeException(nameof(side));
        }
        public readonly struct FootGoal
        {
            public Pose Pose {get;}
            public Contact Contact {get;}
            public FootGoal(Pose pose,Contact contact){Pose=pose;Contact=contact;}
        }
        public readonly struct Goals
        {
            public FootGoal Left {get;}
            public FootGoal Right {get;}
            public Goals(FootGoal left,FootGoal right){Left=left;Right=right;}
        }

        public static Plan MakePlan(StopKind kind,FootInput left,FootInput right)
        {
            if(kind!=StopKind.Walk&&kind!=StopKind.Run)throw new ArgumentOutOfRangeException(nameof(kind));
            left=Validate(left,nameof(left));right=Validate(right,nameof(right));
            double fl=Minimum(left.Entry,left.SoleNeutralPoints),fr=Minimum(right.Entry,right.SoleNeutralPoints);
            bool sl=fl<=.004,sr=fr<=.004,air=!sl&&!sr;
            Side first=sl&&sr?(fl<=fr?Side.Left:Side.Right):sl?Side.Left:sr?Side.Right:
                FallTime(fl,left.SoleVelocityY)<=FallTime(fr,right.SoleVelocityY)?Side.Left:Side.Right;
            FootInput f=first==Side.Left?left:right;double floor=first==Side.Left?fl:fr;
            Pose landing=f.Entry;double firstLand=0;
            if(air)
            {
                double h=Math.Max(.00001,floor-.0018),vy=f.SoleVelocityY;
                Q4 desired=Q4.Product(Q4.XAngle(Radians(kind==StopKind.Run?-8:-10)),f.Idle.Rotation);
                Q4 q=Q4.Slerp(f.Entry.Rotation,desired,Smooth(h/.08));
                D3 a=new D3(f.Entry.Position.X,-Minimum(new Pose(new D3(),q),f.SoleNeutralPoints)+.0018,Clamp(f.Entry.Position.Z,-.52,.52));
                firstLand=Math.Max(.002,Math.Min(.22,FallTime(floor,vy)));
                if(vy<-.001)firstLand=Math.Min(firstLand,2.5*h/(-vy));
                landing=new Pose(a,q);
            }
            double freeLand=firstLand+(kind==StopKind.Walk?.26:.30),duration=firstLand+(kind==StopKind.Walk?.675:.775);
            var p=new Plan(kind,first,air,sl&&sr,fl,fr,firstLand,landing,freeLand,duration,left,right);
            if(!Finite(firstLand)||!Finite(duration)||duration<=0)throw new ArgumentException("Unrepresentable trajectory inputs.");
            return p;
        }
        public static Pose SwingGoal(Pose start,Pose target,double time,double duration,D3 velocity,D3 pivot,double lift=.035,bool initialContact=false)
        {
            if(!Finite(time)||!Finite(duration)||duration<=0||time<0||time>duration)throw new ArgumentOutOfRangeException(nameof(time));
            if(!Finite(start.Position)||!Finite(target.Position)||!Finite(velocity)||!Finite(pivot)||!Finite(lift))
                throw new ArgumentException("Finite swing geometry required.");
            start=new Pose(start.Position,Validate(start.Rotation,nameof(start)));
            target=new Pose(target.Position,Validate(target.Rotation,nameof(target)));
            double v=Clamp(time/duration,0,1);Hermite(v,out double h00,out double h10,out double h01);
            D3 vel=new D3(0,velocity.Y,velocity.Z);
            D3 a=start.Position*h00+vel*(duration*h10)+target.Position*h01;
            a=new D3(a.X,a.Y+lift*Sin2(Math.PI*v),a.Z);
            Q4 q=Q4.Slerp(start.Rotation,target.Rotation,Smooth(v));
            D3 point=a+q.Rotate(pivot),targetPoint=target.Position+target.Rotation.Rotate(pivot);
            if(initialContact)
            {
                D3 from=start.Position+start.Rotation.Rotate(pivot);double w=Quint((time-.025)/.065);
                point=new D3(from.X+(point.X-from.X)*w,point.Y,from.Z+(point.Z-from.Z)*w);
            }
            double finish=Math.Max(.025,Math.Min(.075,duration*.4)),begin=Math.Max(0,duration-finish-.035);
            double settle=Quint((time-begin)/finish);
            point=new D3(point.X*(1-settle)+targetPoint.X*settle,point.Y,point.Z*(1-settle)+targetPoint.Z*settle);
            return new Pose(point-q.Rotate(pivot),q);
        }

        public static Goals Evaluate(Plan plan,double time)
        {
            if(plan==null)throw new ArgumentNullException(nameof(plan));
            if(!Finite(time)||time<0||time>plan.Duration)throw new ArgumentOutOfRangeException(nameof(time));
            FootInput first=plan.Foot(plan.First),other=plan.Foot(plan.Free);
            Pose land=plan.LandingPose,idle=first.Idle;double duration=plan.Duration;
            double minimum=Minimum(new Pose(new D3(),land.Rotation),first.SoleNeutralPoints);
            D3 pivot=new D3();int count=0;
            foreach(D3 p in first.SoleNeutralPoints)if(land.Rotation.Rotate(p).Y<minimum+.0003){pivot+=p;count++;}
            pivot/=count;
            D3 world=land.Position+land.Rotation.Rotate(pivot),flat=world-idle.Rotation.Rotate(pivot);
            D3 toe=Toe(first.SoleNeutralPoints),toeWorld=flat+idle.Rotation.Rotate(toe);
            Q4 offQ=Q4.Product(Q4.XAngle(Radians(10)),idle.Rotation);
            D3 offA=toeWorld-offQ.Rotate(toe);
            Pose support;Contact firstContact;
            if(plan.EntryAir&&time<plan.FirstLanding)
            {
                support=SwingGoal(first.Entry,land,time,plan.FirstLanding,first.AnkleVelocity,pivot,0);
                double v=time/plan.FirstLanding;Hermite(v,out double h00,out double h10,out double h01);
                double clearance=plan.Floor(plan.First)*h00+first.SoleVelocityY*plan.FirstLanding*h10+.0018*h01;
                support=Clearance(support,first.SoleNeutralPoints,clearance);firstContact=Contact.Air;
            }
            else if(time<plan.FirstLanding+.10)
            {
                Q4 q=Q4.Slerp(land.Rotation,idle.Rotation,Smooth((time-plan.FirstLanding)/.10));
                support=new Pose(world-q.Rotate(pivot),q);firstContact=Contact.EntryHeelOrToe;
            }
            else if(time<plan.ToeRoll){support=new Pose(flat,idle.Rotation);firstContact=Contact.EntryFlat;}
            else if(time<plan.Liftoff)
            {
                Q4 q=Q4.Slerp(idle.Rotation,offQ,Smooth((time-plan.ToeRoll)/(plan.Liftoff-plan.ToeRoll)));
                support=new Pose(toeWorld-q.Rotate(toe),q);firstContact=Contact.EntryToe;
            }
            else if(time<plan.SecondLanding)
            {
                double v=(time-plan.Liftoff)/(plan.SecondLanding-plan.Liftoff);
                D3 a=D3.Lerp(offA,idle.Position,Quint(v));a=new D3(a.X,a.Y+.085*Sin2(Math.PI*v),a.Z);
                Q4 q=Q4.Product(Q4.XAngle(Radians(10)*Sin2(Math.PI*v)),Q4.Slerp(offQ,idle.Rotation,Smooth(v)));
                D3 point=a+q.Rotate(toe);double takeoff=Quint((time-plan.Liftoff-.030)/.055);
                point=new D3(toeWorld.X+(point.X-toeWorld.X)*takeoff,point.Y,toeWorld.Z+(point.Z-toeWorld.Z)*takeoff);
                D3 target=idle.Position+idle.Rotation.Rotate(toe);double settle=Quint((time-(plan.SecondLanding-.110))/.075);
                point=new D3(point.X*(1-settle)+target.X*settle,point.Y,point.Z*(1-settle)+target.Z*settle);
                support=new Pose(point-q.Rotate(toe),q);firstContact=Contact.Air;
            }
            else{support=idle;firstContact=Contact.IdleFlat;}
            D3 freeToe=Toe(other.SoleNeutralPoints),freeToeWorld=other.Idle.Position+other.Idle.Rotation.Rotate(freeToe);
            Q4 landQ=Q4.Product(Q4.XAngle(Radians(12)),other.Idle.Rotation);
            Pose freeLand=new Pose(freeToeWorld-landQ.Rotate(freeToe),landQ);
            Pose moving;Contact freeContact;
            if(time<plan.FreeLanding)
            {
                moving=SwingGoal(other.Entry,freeLand,time,plan.FreeLanding,other.AnkleVelocity,freeToe,.035,plan.Floor(plan.Free)<=.006);
                double v=time/plan.FreeLanding;Hermite(v,out double h00,out double h10,out double h01);
                double initialVy=plan.Floor(plan.Free)<=.006?0:other.SoleVelocityY;
                double finalClearance=Minimum(freeLand,other.SoleNeutralPoints);
                double clearance=plan.Floor(plan.Free)*h00+initialVy*plan.FreeLanding*h10+finalClearance*h01+.045*Sin2(Math.PI*v);
                moving=Clearance(moving,other.SoleNeutralPoints,clearance);freeContact=Contact.Air;
            }
            else if(time<plan.Liftoff){moving=freeLand;freeContact=Contact.IdleToe;}
            else if(time<duration-.035)
            {
                Q4 q=Q4.Slerp(landQ,other.Idle.Rotation,Smooth((time-plan.Liftoff)/(duration-.035-plan.Liftoff)));
                moving=new Pose(freeToeWorld-q.Rotate(freeToe),q);freeContact=Contact.IdleToe;
            }
            else{moving=other.Idle;freeContact=Contact.IdleFlat;}
            return plan.First==Side.Left?new Goals(new FootGoal(support,firstContact),new FootGoal(moving,freeContact)):
                new Goals(new FootGoal(moving,freeContact),new FootGoal(support,firstContact));
        }

        private static Pose Clearance(Pose p,IReadOnlyList<D3> sole,double clearance)
        {
            double correction=clearance-Minimum(p,sole);
            return new Pose(new D3(p.Position.X,p.Position.Y+correction,p.Position.Z),p.Rotation);
        }
        private static double Minimum(Pose p,IReadOnlyList<D3> sole)
        {double min=double.PositiveInfinity;foreach(D3 v in sole)min=Math.Min(min,(p.Position+p.Rotation.Rotate(v)).Y);return min;}
        private static D3 Toe(IReadOnlyList<D3> sole)
        {D3 result=sole[0];for(int i=1;i<sole.Count;i++)if(sole[i].Z>result.Z)result=sole[i];return result;}
        private static double FallTime(double floor,double vy)
        {double h=Math.Max(.00001,floor-.0018);return(vy+Math.Sqrt(vy*vy+24*h))/12;}
        private static void Hermite(double v,out double h00,out double h10,out double h01)
        {h00=2*v*v*v-3*v*v+1;h10=v*v*v-2*v*v+v;h01=-2*v*v*v+3*v*v;}
        private static double Smooth(double t){t=Clamp(t,0,1);return t*t*(3-2*t);}
        private static double Quint(double t){t=Clamp(t,0,1);return t*t*t*(10-15*t+6*t*t);}
        private static double Clamp(double value,double lo,double hi)=>Math.Max(lo,Math.Min(hi,value));
        private static double Sin2(double value){double s=Math.Sin(value);return s*s;}
        private static double Radians(double degrees)=>degrees*(Math.PI/180);
        private static bool Finite(double v)=>!double.IsNaN(v)&&!double.IsInfinity(v);
        private static bool Finite(D3 v)=>Finite(v.X)&&Finite(v.Y)&&Finite(v.Z);
        private static D3 Cross(D3 a,D3 b)=>new D3(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
        private static Q4 Unit(Q4 q)
        {double n=Math.Sqrt(q.X*q.X+q.Y*q.Y+q.Z*q.Z+q.W*q.W);return new Q4(q.X/n,q.Y/n,q.Z/n,q.W/n);}
        private static Q4 Validate(Q4 q,string name)
        {
            double n=q.X*q.X+q.Y*q.Y+q.Z*q.Z+q.W*q.W;
            if(!Finite(n)||Math.Abs(n-1)>1e-5)throw new ArgumentException("Rotation must be finite and unit (neutral-basis quaternion).",name);
            return Unit(q);
        }
        private static FootInput Validate(FootInput f,string name)
        {
            if(f==null)throw new ArgumentNullException(name);
            if(!Finite(f.Entry.Position)||!Finite(f.Idle.Position)||!Finite(f.AnkleVelocity)||!Finite(f.SoleVelocityY)||f.SoleNeutralPoints.Count==0)
                throw new ArgumentException("Finite positions/velocities and non-empty real sole geometry required.",name);
            foreach(D3 p in f.SoleNeutralPoints)if(!Finite(p))throw new ArgumentException("Non-finite sole geometry.",name);
            return new FootInput(new Pose(f.Entry.Position,Validate(f.Entry.Rotation,name)),new Pose(f.Idle.Position,Validate(f.Idle.Rotation,name)),f.SoleNeutralPoints,f.AnkleVelocity,f.SoleVelocityY);
        }
    }
}