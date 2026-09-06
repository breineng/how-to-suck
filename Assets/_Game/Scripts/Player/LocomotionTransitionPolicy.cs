using System;
namespace HowToSuck
{
    // Native Animator transition timing/state only. Owns no bone, player or camera transform.
    [Serializable]
    public sealed class LocomotionTransitionPolicy
    {
        public float StartSeconds=.28f, StopSeconds=.30f, GaitSeconds=.12f;
        public bool HasCapture {get;private set;}
        public string Source {get;private set;}
        public string Target {get;private set;}
        public double StartedAt {get;private set;}
        public float Duration {get;private set;}
        public float SourcePhase {get;private set;}
        public float FixedOffset {get;private set;}
        public uint ResetCount {get;private set;}
        private int motorId,animatorId,controllerId;
        private uint resetRevision;
        private bool bound;
        public static bool IsGait(string value)=>value=="Walk"||value=="Run";
        private static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
        private static float Safe(float value,float fallback,float minimum=.18f)=>Finite(value)&&value>=minimum&&value<=.45f?value:fallback;
        public static float Phase(float normalized)=>Finite(normalized)?normalized-(float)Math.Floor(normalized):0;
        // Uses an explicit reset revision; normal 7m/s under a long frame is never classified as teleport.
        public bool Bind(int motor,int animator,int controller,uint revision)
        {
            bool changed=!bound||motorId!=motor||animatorId!=animator||controllerId!=controller||resetRevision!=revision;
            if(changed){Reset();motorId=motor;animatorId=animator;controllerId=controller;resetRevision=revision;bound=true;}
            return changed;
        }
        public void Reset(){ResetCapture();bound=false;}
        public void ResetCapture()
        {
            HasCapture=false;Source=Target=null;StartedAt=0;Duration=SourcePhase=FixedOffset=0;
            unchecked{ResetCount++;}
        }
        public void Begin(string source,string target,float sourceNormalized,float targetCycleSeconds,double now)
        {
            if(string.IsNullOrEmpty(target)||!Finite(now)||!Finite(sourceNormalized)||!Finite(targetCycleSeconds)||targetCycleSeconds<=0)
                throw new ArgumentException("Finite actual Animator phase/time/clip duration required.");
            Source=source;Target=target;StartedAt=now;SourcePhase=Phase(sourceNormalized);
            FixedOffset=IsGait(source)&&IsGait(target)?SourcePhase*targetCycleSeconds:0;
            // Existing jump/landing and tool transitions retain their reviewed timing.
            Duration=source=="Idle"&&IsGait(target)?Safe(StartSeconds,.28f):
                IsGait(source)&&target=="Idle"?Safe(StopSeconds,.30f):
                IsGait(source)&&IsGait(target)?Safe(GaitSeconds,.12f,.08f):.12f;
            HasCapture=true;
        }
        // Diagnostic progress; the actual pose is still blended solely by Animator.CrossFadeInFixedTime.
        public float Progress(double now)=>!HasCapture?1f:!Finite(now)?0f:
            (float)Math.Max(0,Math.Min(1,(now-StartedAt)/Duration));
    }
}
