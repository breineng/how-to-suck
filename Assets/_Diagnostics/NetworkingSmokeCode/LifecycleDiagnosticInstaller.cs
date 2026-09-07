#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using HowToSuck.Networking;
using UnityEngine;
namespace HowToSuck.Diagnostics
{
    // Attach only to the authored diagnostic bootstrap. The separate observer intentionally survives old-root destruction.
    public sealed class LifecycleDiagnosticInstaller:MonoBehaviour
    {
        public NgoGameSession Game;
        public MppmGameplayStartup Startup;
        public string ReportsRoot="Tools/Staging/Networking/Gameplay/Lifecycle/evidence";
        private void Start()
        {
            if(Game==null||Startup==null||Startup.Profile==null||Game.Driver==null)
                throw new InvalidOperationException("Resolve the real diagnostic session before lifecycle observation.");
            string reports=ReportsRoot;var args=Environment.GetCommandLineArgs();
            for(int i=0;i<args.Length;i++)if(args[i]=="--hts-gameplay-reports")
            {if(i+1>=args.Length)throw new ArgumentException("Missing diagnostic reports directory.");reports=args[++i];}
            var go=new GameObject("Network lifecycle diagnostic observer");DontDestroyOnLoad(go);
            go.AddComponent<LifecycleDiagnosticProbe>().Initialize(Game,Startup,Path.GetFullPath(reports));
        }
    }
}
#endif
