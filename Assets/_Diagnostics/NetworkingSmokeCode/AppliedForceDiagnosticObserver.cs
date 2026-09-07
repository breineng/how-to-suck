#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using HowToSuck.Networking;
using UnityEngine;
namespace HowToSuck.Diagnostics
{
    // Separate authored diagnostic only. Reads journal values; no physics API or mutable journal access.
    public sealed class AppliedForceDiagnosticObserver:MonoBehaviour
    {
        public NgoGameSession Game;
        public MppmGameplayStartup Startup;
        public string ReportsRoot="Tools/Staging/Networking/Gameplay/AppliedForceFollowup/evidence";
        [Serializable] private sealed class Force
        {
            public string run;public ulong tick,targetId,clientId;public int sequence,emitterId,sourceInstanceId,targetBodyInstanceId,playerId;
            public bool playerSource,bodyActive;public Vector3 force,point;
        }
        [Serializable] private sealed class Step
        {public string nonce,run,utc,scope;public ulong tick;public int slot,pid,frame,dropped;public bool authority;public Force[] forces;}
        private AuthorityWorld world;private string directory,lastRun;private ulong lastTick;private int slot,pid;private bool failed,attached;
        private void Start()
        {
            try
            {
                if(Game==null||Game.Session==null||Startup==null||Startup.Profile==null)throw new InvalidOperationException("Bind one prepared diagnostic Game/Startup.");
                if(GetComponents<AppliedForceDiagnosticObserver>().Length!=1)throw new InvalidOperationException("Duplicate actual-force observer on one composition.");
                world=Game.Session.World;slot=Startup.Profile.Slot;pid=System.Diagnostics.Process.GetCurrentProcess().Id;
                string root=ReportsRoot;var args=Environment.GetCommandLineArgs();
                for(int i=0;i<args.Length;i++)if(args[i]=="--hts-gameplay-reports")
                {if(i+1>=args.Length)throw new ArgumentException("Missing reports path.");root=args[++i];}
                directory=Path.Combine(Path.GetFullPath(root),Startup.Profile.Nonce,"player-"+slot+"-"+pid);Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory,"applied-force-observer.json"),JsonUtility.ToJson(new Step{nonce=Startup.Profile.Nonce,slot=slot,pid=pid,authority=Game.HasAuthority,
                    utc=DateTime.UtcNow.ToString("O"),scope="Value-only journal appended immediately after actual AddForceAtPosition returned. No inference from Collect; no isolation of resulting PhysX motion."}));
                world.SnapshotChanged+=Capture;attached=true;
            }
            catch(Exception error){Fail(error);}
        }
        private void Capture()
        {
            if(!isActiveAndEnabled||failed||world==null||Game==null||!Game.HasAuthority)return;
            try
            {
                var journal=world.DiagnosticAppliedForces;
                if(journal==null||journal.Tick==0||string.IsNullOrEmpty(journal.RunId)||journal.RunId!=world.RunId||journal.Tick!=world.TickCount||
                    (journal.RunId==lastRun&&journal.Tick==lastTick))return;
                lastRun=journal.RunId;lastTick=journal.Tick;
                if(journal.Count==0&&journal.Dropped==0)return;
                var mapping=new Dictionary<int,NetworkPlayerAdapter>();
                foreach(var player in Game.Players)if(player!=null)
                {var source=player.GetComponent<VacuumEmitter>();if(source!=null)mapping.Add(source.GetInstanceID(),player);}
                var values=new Force[journal.Count];
                for(int i=0;i<values.Length;i++)
                {
                    var record=journal.Read(i);mapping.TryGetValue(record.SourceInstanceId,out var player);
                    bool isPlayer=player!=null&&player.Motor.PlayerId==record.EmitterId;
                    values[i]=new Force{run=record.RunId,tick=record.Tick,sequence=record.Sequence,emitterId=record.EmitterId,sourceInstanceId=record.SourceInstanceId,
                        targetId=record.TargetId,targetBodyInstanceId=record.TargetBodyInstanceId,force=record.Force,point=record.Point,bodyActive=record.BodyActive,
                        playerSource=isPlayer,clientId=isPlayer?player.OwnerClientId:0,playerId=isPlayer?player.Motor.PlayerId:0};
                }
                var step=new Step{nonce=Startup.Profile.Nonce,run=journal.RunId,tick=journal.Tick,slot=slot,pid=pid,frame=Time.frameCount,utc=DateTime.UtcNow.ToString("O"),
                    authority=true,dropped=journal.Dropped,forces=values,scope="Actual returned AddForceAtPosition calls (ForceMode.Force); brake-only AddForce paths are not recorded."};
                File.AppendAllText(Path.Combine(directory,"applied-force-steps.jsonl"),JsonUtility.ToJson(step)+Environment.NewLine);
            }
            catch(Exception error){Fail(error);} // Observation failure must not abort the authority physics loop.
        }
        private void Fail(Exception error)
        {
            failed=true;string message="Actual-force diagnostic failed: "+error.GetType().Name+": "+error.Message;
            if(directory!=null)try{File.WriteAllText(Path.Combine(directory,"applied-force-error.txt"),message);}catch{}
            Debug.LogError(message,this);
        }
        private void OnDestroy(){if(attached&&world!=null)world.SnapshotChanged-=Capture;}
    }
}
#endif
