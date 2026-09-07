#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HowToSuck.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace HowToSuck.Diagnostics
{
    public sealed class LifecycleDiagnosticProbe:MonoBehaviour
    {
        [Serializable] private sealed class Command{public string nonce=null,kind=null,run=null;public int slot=0,sequence=0;public bool reenter=false;}
        [Serializable] private sealed class Event{public string utc,kind,detail;public int frame;public double realtime;}
        [Serializable] private sealed class Observation
        {
            public string nonce,utc,caseName,activeScene,menuScene,oldPhase,connectionPhase,lastError,screenshotStatus;
            public int slot,pid,frame,originalSessionId,freshSessionId,managerCount,maxManagerCount,sessionCount,maxSessionCount,lossNotifications,
                stoppingTransitions,clientStopped,serverStopped,reentrantRequests,reentrantGuardWitnesses,resultCount,pendingNative,trackedNative,
                completedNative,gateFrame,releaseFrame,firstOfflineFrame,firstShutdownWhilePendingFrame,roster;
            public double realtime,gateProgress,gateCapturedAt,gateReleasedAt,nativeCompletedAt;
            public long finalPayout;
            public bool oldRootAlive,oldWorldRunning,gateCaptured,gateHeld,lossWhileNativePending,freshOfflineMenu,menuStructureActive,
                staleContinuationObserved,secondManagerObserved,newOwnerBeforeDrain,gateFailure,hasRenderDevice;
            public string[] errors,loadedScenes;
        }
        private sealed class NativeOperation{public AsyncOperation Operation;public string Scene;public bool Completed;}
        private ContractController originalController;
        private NgoGameSession game;private MppmGameplayStartup startup;private SessionRoot original;
        private NetworkManager manager;private NetworkConnectionCoordinator connection;private NetworkSceneManager bound;
        private readonly List<NativeOperation> native=new List<NativeOperation>();private readonly List<string> errors=new List<string>();
        private string directory,nonce,menuScene,caseName="observing",targetScene,lastError,screenshotStatus="not requested";
        private int slot,pid,originalId,lastCommand,maxManagers,maxSessions,lossCount,stoppingCount,clientStopped,serverStopped,reentryCount,reentryWitness,resultCount,
            gateFrame,releaseFrame,firstOfflineFrame,firstStoppedPendingFrame;
        private long payout;private ConnectionPhase previousConnection;
        private bool initialized,disposed,armGate,autoFailPending,reenterOnResults,reentered,gateCaptured,originalAllow,gateFailure,lossPending,staleContinuation,newOwnerBeforeDrain,secondManager;
        private AsyncOperation gated;private double nextWrite,capturedAt,releaseAt,releasedAt,nativeDoneAt;
        public void Initialize(NgoGameSession owner,MppmGameplayStartup entry,string reportsRoot)
        {
            if(initialized||FindObjectsByType<LifecycleDiagnosticProbe>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length!=1)
                throw new InvalidOperationException("Only one lifecycle observer per process.");
            game=owner;startup=entry;original=owner.Session;manager=owner.Manager;connection=owner.Connection;
            nonce=entry.Profile.Nonce;slot=entry.Profile.Slot;pid=System.Diagnostics.Process.GetCurrentProcess().Id;originalId=original.GetInstanceID();
            var offlineRoot=owner.OfflineMenuBootstrapPrefab;
            var offlineBootstrap=offlineRoot!=null?offlineRoot.GetComponent<GameBootstrap>():null;
            if(offlineBootstrap==null||offlineBootstrap.SessionDriverProvider!=null||offlineBootstrap.Session==null||
                offlineBootstrap.Session.gameObject!=offlineRoot||string.IsNullOrWhiteSpace(offlineBootstrap.Session.MenuSceneName))
                throw new InvalidOperationException("Lifecycle observer requires the bound offline bootstrap's own SessionRoot and explicit MenuSceneName.");
            menuScene=Path.GetFileNameWithoutExtension(offlineBootstrap.Session.MenuSceneName);
            if(string.IsNullOrWhiteSpace(menuScene))throw new InvalidOperationException("Offline bootstrap MenuSceneName does not identify a scene.");
            directory=Path.Combine(reportsRoot,nonce,"player-"+slot+"-"+pid);Directory.CreateDirectory(directory);
            manager.OnClientStarted+=Bind;manager.OnServerStarted+=Bind;manager.OnClientStopped+=ClientStopped;manager.OnServerStopped+=ServerStopped;
            connection.SessionLost+=Lost;connection.Changed+=ConnectionChanged;original.Changed+=SessionChanged;
            originalController=original.Controller;if(originalController!=null)originalController.Finished+=Finished;
            SceneManager.sceneLoaded+=SceneLoaded;Application.logMessageReceived+=Log;
            previousConnection=connection.Phase;initialized=true;Bind();Record("observer-attached","Owns no game state; real manager/session IDs retained. Offline menu from bound bootstrap: "+menuScene);
        }
        private void Bind()
        {
            if(manager==null||manager.SceneManager==null||ReferenceEquals(bound,manager.SceneManager))return;
            if(bound!=null){bound.OnLoad-=Loaded;bound.OnUnload-=Unloaded;bound.OnLoadEventCompleted-=Completed;}
            bound=manager.SceneManager;bound.OnLoad+=Loaded;bound.OnUnload+=Unloaded;bound.OnLoadEventCompleted+=Completed;
        }
        private void Loaded(ulong client,string scene,LoadSceneMode mode,AsyncOperation operation)
        {
            if(operation==null)return;native.Add(new NativeOperation{Operation=operation,Scene=scene});Record("native-load-start",scene);
            if(!armGate||gateCaptured||Path.GetFileNameWithoutExtension(scene)!=targetScene||mode!=LoadSceneMode.Single)return;
            // Capture/gate only. Never start/stop a session or issue another scene event inside this callback.
            armGate=false;gated=operation;gateCaptured=true;gateFrame=Time.frameCount;capturedAt=Time.realtimeSinceStartupAsDouble;releaseAt=capturedAt+8;
            originalAllow=operation.allowSceneActivation;
            if(operation.isDone||!originalAllow){gateFailure=true;Record("gate-unavailable","Native operation already complete or owned by another activation gate.");return;}
            operation.allowSceneActivation=false;Record("engineering-native-activation-gate",scene+"; release bounded to eight seconds.");
        }
        private void Unloaded(ulong client,string scene,AsyncOperation operation)
        {if(operation!=null){native.Add(new NativeOperation{Operation=operation,Scene=scene});Record("native-unload-start",scene);}}
        private void Completed(string scene,LoadSceneMode mode,List<ulong> done,List<ulong> timedOut)
        {Record("ngo-load-event-completed",scene+" done="+string.Join(",",done)+" timedOut="+string.Join(",",timedOut));}
        private void Lost()
        {lossCount++;lossPending|=Pending()>0;Record("session-lost-notification","count="+lossCount+" nativePending="+Pending());}
        private void ConnectionChanged()
        {if(connection==null)return;if(connection.Phase==ConnectionPhase.Stopping&&previousConnection!=ConnectionPhase.Stopping)stoppingCount++;previousConnection=connection.Phase;Record("connection-phase",connection.Phase.ToString());}
        private void SessionChanged()
        {
            if(original==null)return;
            if(reenterOnResults&&!reentered&&original.Phase==SessionPhase.Results)
            {
                reentered=true;reentryCount++;
                var signal=typeof(NetworkConnectionCoordinator).GetField("unexpectedStop",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(connection) as UnexpectedStopSignal;
                if(signal!=null&&signal.IsNotifying)reentryWitness++;
                Record("engineering-reentrant-results-stop","Guard observed notifying="+(signal!=null&&signal.IsNotifying));
                connection.StopUnexpected("ENGINEERING diagnostic reentrant Results failure");
            }
        }
        private void Finished(ContractResult value){resultCount++;payout=value.Payout;Record("contract-result",value.Phase+" payout="+value.Payout+" run="+value.RunId);}
        private void ClientStopped(bool wasHost){clientStopped++;Record("native-client-stopped","wasHost="+wasHost);}
        private void ServerStopped(bool wasHost){serverStopped++;Record("native-server-stopped","wasHost="+wasHost);}
        private void SceneLoaded(Scene scene,LoadSceneMode mode){Record("unity-scene-loaded",scene.name+" mode="+mode);}
        private void Log(string condition,string stack,LogType type)
        {
            if(type!=LogType.Exception&&type!=LogType.Error&&condition.IndexOf("SceneEventInProgress",StringComparison.OrdinalIgnoreCase)<0)return;
            if(errors.Count<64)errors.Add(condition.Length>500?condition.Substring(0,500):condition);
        }
        private int Pending()=>native.Count(n=>n.Operation!=null&&!n.Operation.isDone);
        private void Update()
        {
            if(!initialized||disposed)return;
            try
            {
                Bind();ReadCommand();double now=Time.realtimeSinceStartupAsDouble;
                foreach(var operation in native)if(!operation.Completed&&(operation.Operation==null||operation.Operation.isDone))
                {operation.Completed=true;nativeDoneAt=now;Record("native-operation-done",operation.Scene);}
                if(gated!=null&&!gated.isDone&&!gated.allowSceneActivation&&!gateFailure)
                {
                    if(autoFailPending&&Time.frameCount>gateFrame+1&&gated.progress>=.89f)
                    {
                        autoFailPending=false;releaseAt=Math.Min(releaseAt,now+2);
                        Record("engineering-unexpected-stop-while-native-pending","Outside native callback; progress="+gated.progress);
                        connection.StopUnexpected("ENGINEERING diagnostic failure with pending native scene load");
                    }
                    if(now>=releaseAt)ReleaseGate();
                }
                ObserveInvariants();
                if(now>=nextWrite){nextWrite=now+.1;WriteState();}
            }
            catch(Exception error){lastError=error.GetType().Name+": "+error.Message;Record("harness-error",lastError);ReleaseGate();try{WriteState();}catch{}enabled=false;}
        }
        private void ReadCommand()
        {
            var path=Path.Combine(directory,"lifecycle-command.json");if(!File.Exists(path))return;
            if(!DiagnosticCommandFile.TryRead(path,4096,out var commandJson))return;
            var command=JsonUtility.FromJson<Command>(commandJson);if(command==null||command.sequence<=lastCommand)return;
            if(command.nonce!=nonce||command.slot!=slot)throw new InvalidOperationException("Lifecycle command routing mismatch.");
            lastCommand=command.sequence;string status="DONE",detail="";
            try
            {
                switch(command.kind)
                {
                    case "arm-load-delay":case "arm-load-failure":
                        if(game==null||!game.HasAuthority||original.Phase!=SessionPhase.Lobby||gateCaptured||armGate)throw new InvalidOperationException("Fresh authority lobby required to arm one next shared load.");
                        var contract=original.Catalog.Contracts.Single(c=>c!=null&&c.Quota==300&&Math.Abs(c.TimeLimitSeconds-120)<.001);
                        targetScene=Path.GetFileNameWithoutExtension(contract.SceneName);armGate=true;autoFailPending=command.kind=="arm-load-failure";caseName=command.kind;
                        detail="Engineering activation gate armed for actual authored "+targetScene;break;
                    case "unexpected-stop":
                        if(game==null||!game.HasAuthority||original.Phase!=SessionPhase.Playing||command.run!=original.RunId)throw new InvalidOperationException("Current playing authority RunId required.");
                        reenterOnResults=command.reenter;caseName="engineering-unexpected-stop";
                        connection.StopUnexpected("ENGINEERING diagnostic explicit unexpected stop");detail="Once-loss path invoked outside any scene callback.";break;
                    case "leave":
                        if(game==null||game.HasAuthority||original.Phase!=SessionPhase.Playing||command.run!=original.RunId)throw new InvalidOperationException("Current playing guest RunId required.");
                        caseName="normal-guest-leave";game.LeaveGuest();detail="Normal guest LeaveGuest endpoint.";break;
                    case "release-gate":ReleaseGate();detail="Owned native gate released.";break;
                    case "frame":CaptureOfflineFrame();detail=screenshotStatus;break;
                    default:throw new InvalidOperationException("Unknown lifecycle command.");
                }
            }
            catch(Exception error){status="REJECTED";detail=error.GetType().Name+": "+error.Message;}
            Record("command-"+command.kind,status+": "+detail);
            File.WriteAllText(Path.Combine(directory,"lifecycle-result-"+command.sequence.ToString("D5")+".json"),JsonUtility.ToJson(new CommandResult{sequence=command.sequence,status=status,detail=detail}));
        }
        [Serializable] private sealed class CommandResult{public int sequence;public string status,detail;}
        private void ReleaseGate()
        {
            if(gated==null||gateFailure||releasedAt>0)return;
            gated.allowSceneActivation=originalAllow;releasedAt=Time.realtimeSinceStartupAsDouble;releaseFrame=Time.frameCount;
            Record("engineering-native-gate-released","Restored captured allowSceneActivation="+originalAllow);
        }
        private SessionRoot FreshOffline()
        {
            if(SceneManager.GetActiveScene().name!=menuScene||Pending()!=0)return null;
            var sessions=FindObjectsByType<SessionRoot>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            if(sessions.Length!=1)return null;var candidate=sessions[0];var bootstrap=candidate.GetComponent<GameBootstrap>();
            return candidate.GetInstanceID()!=originalId&&candidate.IsInitialized&&candidate.Phase==SessionPhase.Lobby&&bootstrap!=null&&bootstrap.Session==candidate&&bootstrap.SessionDriverProvider==null&&
                Path.GetFileNameWithoutExtension(candidate.MenuSceneName)==menuScene&&
                FindObjectsByType<NetworkManager>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==0?candidate:null;
        }
        private bool MenuStructureActive()
        {
            return FindObjectsByType<SessionMenuView>(FindObjectsInactive.Include,FindObjectsSortMode.None).Any(view=>view.isActiveAndEnabled&&view.StartButton!=null&&
                view.StartButton.IsActive()&&view.StartButton.interactable&&view.GetComponentInParent<Canvas>()!=null&&view.GetComponentInParent<Canvas>().isActiveAndEnabled);
        }
        private void ObserveInvariants()
        {
            int managers=FindObjectsByType<NetworkManager>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length;
            var sessions=FindObjectsByType<SessionRoot>(FindObjectsInactive.Include,FindObjectsSortMode.None);maxManagers=Math.Max(maxManagers,managers);maxSessions=Math.Max(maxSessions,sessions.Length);
            secondManager|=managers>1;
            if(Pending()>0&&sessions.Any(s=>s.GetInstanceID()!=originalId))newOwnerBeforeDrain=true;
            if(lossCount>0&&original!=null&&original.Phase!=SessionPhase.ShuttingDown)staleContinuation=true;
            if(Pending()>0&&(manager==null||!manager.IsListening&&!manager.ShutdownInProgress)&&firstStoppedPendingFrame==0)firstStoppedPendingFrame=Time.frameCount;
            if(firstOfflineFrame==0&&FreshOffline()!=null)firstOfflineFrame=Time.frameCount;
        }
        private void CaptureOfflineFrame()
        {
            if(FreshOffline()==null||!MenuStructureActive())throw new InvalidOperationException("Actual fresh offline menu with active UI must exist first.");
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null){screenshotStatus="not captured: no render device; visible-menu acceptance missing";return;}
            ScreenCapture.CaptureScreenshot(Path.Combine(directory,"fresh-offline-menu.png"));screenshotStatus="requested actual screenshot; file and visible menu must be independently reviewed";
        }
        private void WriteState()
        {
            var fresh=FreshOffline();var managers=FindObjectsByType<NetworkManager>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            var value=new Observation{nonce=nonce,slot=slot,pid=pid,utc=DateTime.UtcNow.ToString("O"),caseName=caseName,frame=Time.frameCount,realtime=Time.realtimeSinceStartupAsDouble,
                originalSessionId=originalId,freshSessionId=fresh!=null?fresh.GetInstanceID():0,activeScene=SceneManager.GetActiveScene().name,menuScene=menuScene,
                oldPhase=original!=null?original.Phase.ToString():"destroyed",oldRootAlive=game!=null,oldWorldRunning=original!=null&&original.World.IsRunning,
                connectionPhase=connection!=null?connection.Phase.ToString():"destroyed",managerCount=managers.Length,maxManagerCount=maxManagers,
                sessionCount=FindObjectsByType<SessionRoot>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length,maxSessionCount=maxSessions,
                lossNotifications=lossCount,stoppingTransitions=stoppingCount,clientStopped=clientStopped,serverStopped=serverStopped,
                reentrantRequests=reentryCount,reentrantGuardWitnesses=reentryWitness,resultCount=resultCount,finalPayout=payout,
                pendingNative=Pending(),trackedNative=native.Count,completedNative=native.Count(n=>n.Completed),gateCaptured=gateCaptured,gateHeld=gated!=null&&!gated.isDone&&!gated.allowSceneActivation,
                gateProgress=gated!=null?gated.progress:0,gateFrame=gateFrame,releaseFrame=releaseFrame,gateCapturedAt=capturedAt,gateReleasedAt=releasedAt,nativeCompletedAt=nativeDoneAt,
                firstOfflineFrame=firstOfflineFrame,firstShutdownWhilePendingFrame=firstStoppedPendingFrame,lossWhileNativePending=lossPending,
                freshOfflineMenu=fresh!=null,menuStructureActive=MenuStructureActive(),staleContinuationObserved=staleContinuation,secondManagerObserved=secondManager,newOwnerBeforeDrain=newOwnerBeforeDrain,
                gateFailure=gateFailure,lastError=lastError,errors=errors.ToArray(),screenshotStatus=screenshotStatus,hasRenderDevice=SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,
                roster=manager!=null&&manager.IsServer?manager.ConnectedClientsIds.Count:0,
                loadedScenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i).name).ToArray()};
            string text=JsonUtility.ToJson(value);File.AppendAllText(Path.Combine(directory,"lifecycle-samples.jsonl"),text+Environment.NewLine);
            string target=Path.Combine(directory,"lifecycle-state.json"),temporary=target+".tmp";File.WriteAllText(temporary,text);
            try{if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);}catch(IOException){ /* Immutable sample remains; retry latest pointer next frame. */ }
        }
        private void Record(string kind,string detail)
        {if(directory!=null)File.AppendAllText(Path.Combine(directory,"lifecycle-events.jsonl"),JsonUtility.ToJson(new Event{utc=DateTime.UtcNow.ToString("O"),kind=kind,detail=detail,frame=Time.frameCount,realtime=Time.realtimeSinceStartupAsDouble})+Environment.NewLine);}
        private void OnDisable(){ReleaseGate();}
        private void OnDestroy()
        {
            if(disposed)return;disposed=true;ReleaseGate();
            if(manager!=null){manager.OnClientStarted-=Bind;manager.OnServerStarted-=Bind;manager.OnClientStopped-=ClientStopped;manager.OnServerStopped-=ServerStopped;}
            if(connection!=null){connection.SessionLost-=Lost;connection.Changed-=ConnectionChanged;}
            if(!ReferenceEquals(original,null))original.Changed-=SessionChanged;
            if(originalController!=null)originalController.Finished-=Finished;
            if(bound!=null){bound.OnLoad-=Loaded;bound.OnUnload-=Unloaded;bound.OnLoadEventCompleted-=Completed;}
            SceneManager.sceneLoaded-=SceneLoaded;Application.logMessageReceived-=Log;
        }
    }
}
#endif
