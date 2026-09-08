using System;
using UnityEngine;
namespace HowToSuck
{
    // Owns the single OS display beyond any disabled/destroyed menu or SessionRoot.
    // Every native video write, including trial start, goes through its one state/apply path.
    [DefaultExecutionOrder(-19010),DisallowMultipleComponent]
    public sealed class LocalVideoApplicationOwner:MonoBehaviour
    {
        static LocalVideoApplicationOwner instance;
        static bool quitting;
        VideoApplicationState state;
        public static bool Pending=>instance!=null&&instance.state!=null&&instance.state.Pending;
        public static string Message=>instance!=null&&instance.state!=null?instance.state.Message:"";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetLifetime(){instance=null;quitting=false;}
        static void Ensure()
        {
            if(quitting)throw new InvalidOperationException("The application is closing.");
            if(instance==null)instance=FindFirstObjectByType<LocalVideoApplicationOwner>(FindObjectsInactive.Include);
            if(instance==null)
            {
                var go=new GameObject("Personal video application owner");go.hideFlags=HideFlags.HideInHierarchy|HideFlags.DontSave;
                instance=go.AddComponent<LocalVideoApplicationOwner>();DontDestroyOnLoad(go);
            }
            if(instance.state==null)instance.state=new VideoApplicationState(ApplyNative,Matches,SafeDefault);
        }
        public static bool TryGet(out VideoApplicationState value)
        {value=null;if(quitting)return false;Ensure();value=instance.state;return true;}
        public static bool IsCurrent(VideoApplicationState value,ulong ticket)=>instance!=null&&ReferenceEquals(instance.state,value)&&value!=null&&value.IsCurrent(ticket);
        public static LocalVideoSettings Current()
        {
            var refresh=Screen.currentResolution.refreshRateRatio;
            return new LocalVideoSettings{Width=Math.Max(320,Screen.width),Height=Math.Max(200,Screen.height),Mode=Screen.fullScreenMode==FullScreenMode.MaximizedWindow?3:(int)Screen.fullScreenMode,
                Quality=QualitySettings.GetQualityLevel(),Vsync=QualitySettings.vSyncCount,RefreshNumerator=refresh.numerator==0?60:refresh.numerator,RefreshDenominator=refresh.denominator==0?1:refresh.denominator};
        }
        public static LocalVideoSettings SafeDefault()
        {
            var current=Current();int width=Screen.currentResolution.width,height=Screen.currentResolution.height;
            current.Width=Math.Max(320,Math.Min(1280,width>0?width:1280));current.Height=Math.Max(200,Math.Min(720,height>0?height:720));current.Mode=3;
            current.Quality=Mathf.Clamp(current.Quality,0,Math.Max(0,QualitySettings.names.Length-1));current.Vsync=1;return current;
        }
        static void ApplyNative(LocalVideoSettings value)
        {
            QualitySettings.SetQualityLevel(value.Quality,true);QualitySettings.vSyncCount=value.Vsync;
            Screen.SetResolution(value.Width,value.Height,(FullScreenMode)value.Mode,new RefreshRate{numerator=value.RefreshNumerator,denominator=value.RefreshDenominator});
        }
        public static bool Matches(LocalVideoSettings value)
        {
            int mode=Screen.fullScreenMode==FullScreenMode.MaximizedWindow?3:(int)Screen.fullScreenMode;
            if(value==null||Screen.width!=value.Width||Screen.height!=value.Height||mode!=value.Mode||QualitySettings.GetQualityLevel()!=value.Quality||QualitySettings.vSyncCount!=value.Vsync)return false;
            if(value.Mode!=0)return true;var actual=Screen.currentResolution.refreshRateRatio;
            return actual.denominator>0&&Math.Abs((double)actual.numerator/actual.denominator-(double)value.RefreshNumerator/value.RefreshDenominator)<.01;
        }
        void Update()=>state?.Tick(Time.realtimeSinceStartupAsDouble,Time.frameCount);
        void OnApplicationQuit(){quitting=true;}
        void OnDestroy(){if(instance==this)instance=null;}
    }
}
