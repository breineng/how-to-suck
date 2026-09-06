using System;
namespace HowToSuck
{
    public enum StopFootPhase { Tracking, Stopping, Releasing }
    public enum StopFootRelease { None, Movement, Turn, Airborne, Invalid, Recovery }
    // Pure lifecycle gate. No root displacement / dt teleport heuristic and no movement writes.
    [Serializable]
    public sealed class StopFootContactPolicy
    {
        public double CaptureSeconds=.40, ReleaseSeconds=.12;
        public double StopSpeed=.05, MovingSpeed=.30, TurnDegrees=3;
        public StopFootPhase Phase {get;private set;}
        public StopFootRelease Reason {get;private set;}
        public double Began {get;private set;}
        public double ReleaseBegan {get;private set;}
        public double StopYaw {get;private set;}
        public bool CanCapture {get;private set;}
        public bool JustStopped {get;private set;}
        public bool JustReleased {get;private set;}
        public bool HardReset {get;private set;}
        public double ReleaseWeight {get;private set;}
        private bool sawMovement;
        private double lastTime=double.NaN;
        private static bool F(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        private static double Delta(double a,double b)=>a-b-360*Math.Floor((a-b+180)/360);
        public void Reset(StopFootRelease why=StopFootRelease.Recovery)
        {
            Phase=StopFootPhase.Tracking;Reason=why;sawMovement=false;
            Began=ReleaseBegan=StopYaw=0;CanCapture=JustStopped=JustReleased=false;
            HardReset=true;ReleaseWeight=0;lastTime=double.NaN;
        }
        public void Defer(double seconds)
        {
            if(!F(seconds)||seconds<0){Reset(StopFootRelease.Invalid);return;}
            JustStopped=JustReleased=HardReset=CanCapture=false;
            Began+=seconds;ReleaseBegan+=seconds;
            if(!double.IsNaN(lastTime))lastTime+=seconds;
        }
        public void Step(double now,double speed,double moveMagnitude,double yaw,bool grounded,bool active,bool idleTarget,bool identityChanged)
        {
            JustStopped=JustReleased=HardReset=false;CanCapture=false;
            if(!F(now)||!F(speed)||!F(moveMagnitude)||!F(yaw)||speed<0||moveMagnitude<0||
                !F(CaptureSeconds)||CaptureSeconds<.1||CaptureSeconds>.6||
                !F(ReleaseSeconds)||ReleaseSeconds<.06||ReleaseSeconds>.20||
                !F(StopSpeed)||StopSpeed<0||StopSpeed>.1||!F(MovingSpeed)||MovingSpeed<.15||MovingSpeed>.5||
                !F(TurnDegrees)||TurnDegrees<1||TurnDegrees>5||(!double.IsNaN(lastTime)&&now<lastTime))
            {Reset(StopFootRelease.Invalid);return;}
            if(identityChanged||!active){Reset(StopFootRelease.Recovery);lastTime=now;return;}
            if(!grounded){Reset(StopFootRelease.Airborne);lastTime=now;return;}
            lastTime=now;
            bool moving=speed>=MovingSpeed;
            if(moving)sawMovement=true;
            bool restart=speed>StopSpeed||moveMagnitude>.02||!idleTarget;
            if(Phase==StopFootPhase.Stopping&&(restart||Math.Abs(Delta(yaw,StopYaw))>TurnDegrees))
            {
                Phase=StopFootPhase.Releasing;ReleaseBegan=now;JustReleased=true;
                Reason=restart?StopFootRelease.Movement:StopFootRelease.Turn;
            }
            if(Phase==StopFootPhase.Releasing)
            {
                double t=Math.Max(0,Math.Min(1,(now-ReleaseBegan)/ReleaseSeconds));
                ReleaseWeight=1-t*t*(3-2*t);
                if(t>=1){Phase=StopFootPhase.Tracking;ReleaseWeight=0;}
                return;
            }
            if(Phase==StopFootPhase.Tracking&&sawMovement&&!restart)
            {
                Phase=StopFootPhase.Stopping;Began=now;StopYaw=yaw;
                JustStopped=true;Reason=StopFootRelease.None;sawMovement=false;
            }
            CanCapture=Phase==StopFootPhase.Stopping&&now-Began<=CaptureSeconds;
            ReleaseWeight=Phase==StopFootPhase.Stopping?1:0;
        }
    }
}
