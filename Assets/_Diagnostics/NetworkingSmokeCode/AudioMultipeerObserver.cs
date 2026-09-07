#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HowToSuck.Audio;
using HowToSuck.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace HowToSuck.Diagnostics
{
    [Serializable] public sealed class AudioMixCommand
    {public string nonce,kind,segment,phase;public int slot,sequence,seconds=40;}
    [Serializable] public sealed class AudioMixSourceState
    {public int id,emitterId,player;public string name,clip;public bool active,playing,loop,localEmitter,truckEmitter;public float gain,spatial;public int sample;}
    [Serializable] public sealed class AudioMixState
    {
        public string nonce,utc,scene,run,phase,captureSegment,capturePhase;public int slot,pid,frame,players,items,rootCount,sessionCount,listeners,sources,managers,listeningManagers;
        public bool captureArmed,finished,authority,listenerPaused;public float listenerVolume,master,vacuum,truck,impacts,ui;
        public double realtime,dspTime;public long sampleFrame;public int rate,candidates,pending,voiceDrops,transportErrors,producerErrors,eventOverflow;
        public string[] pendingIds,errors;public AudioMixSourceState[] voices;
    }
    [Serializable] public sealed class AudioMixMark{public string phase;public long sampleFrame;public AudioMixState state;}
    [Serializable] public sealed class AudioMixPlayRow{public long sampleFrame;public string phase;public AudioPlaybackWitness play;}
    [Serializable] public sealed class AudioMixSegment
    {public string nonce,segment,run,scope;public int pid,slot,eventOverflow,producerErrors;public AudioCaptureReceipt capture;public AudioMixMark[] marks;public AudioMixPlayRow[] plays;}
    [Serializable] public sealed class AudioMixResult
    {public string nonce,kind,status,detail,utc,segment,receipt;public int slot,pid,sequence;public AudioMixState state;public AudioCaptureReceipt capture;}
    public sealed class AudioMultipeerObserver:MonoBehaviour
    {
        private string directory,nonce,segment="",phase="";private int slot,pid,lastSequence,oldRate,oldVsync,eventOverflow;private bool finished,subscribed;
        private double nextRead,nextState;private AudioDspCapture tap;private AudioListener captureListener;
        // Retain the managed buffer independently of Unity fake-null when its listener/tap is destroyed.
        private AudioCaptureBuffer captureBuffer;
        private readonly List<AudioMixMark> marks=new List<AudioMixMark>();private readonly List<AudioMixPlayRow> plays=new List<AudioMixPlayRow>();
        private readonly List<string> errors=new List<string>();private readonly HashSet<string> usedSegments=new HashSet<string>(StringComparer.Ordinal);
        private GameAudioRoot changedOwner;private VacuumAudioEmitter changedEmitter;private bool ownerWasEnabled,emitterWasEnabled;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var args=Environment.GetCommandLineArgs();if(!args.Contains("--hts-audio-capture"))return;
            string Value(string key){int index=Array.IndexOf(args,key);if(index<0||index+1>=args.Length)throw new ArgumentException("Missing explicit audio diagnostic argument "+key);return args[index+1];}
            string nonce=Value("--hts-gameplay-nonce");int slot=int.Parse(Value("--hts-gameplay-slot")),count=int.Parse(Value("--hts-gameplay-count"));
            if(!Regex.IsMatch(nonce,"^[a-f0-9]{32}$")||(count!=2&&count!=4)||slot<0||slot>=count)throw new ArgumentException("Explicit two/four-player audio identities required.");
            var go=new GameObject("Explicit multipeer audio diagnostic");DontDestroyOnLoad(go);
            go.AddComponent<AudioMultipeerObserver>().Initialize(Value("--hts-gameplay-reports"),nonce,slot);
        }
        private void Initialize(string reports,string value,int playerSlot)
        {
            nonce=value;slot=playerSlot;pid=System.Diagnostics.Process.GetCurrentProcess().Id;
            directory=Path.Combine(Path.GetFullPath(reports),nonce,"player-"+slot+"-"+pid);Directory.CreateDirectory(directory);
            oldRate=Application.targetFrameRate;oldVsync=QualitySettings.vSyncCount;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;
            Application.logMessageReceived+=OnLog;Atomic("audio-identity.json",JsonUtility.ToJson(new AudioMixResult{nonce=nonce,slot=slot,pid=pid,status="IDLE_EXPLICIT_OPT_IN",detail="No DSP capture until arm command; no OS/other-application audio recording."},true));
        }
        private void OnLog(string message,string stack,LogType type)
        {if((type==LogType.Error||type==LogType.Exception||type==LogType.Assert)&&errors.Count<32)errors.Add(message.Length>600?message.Substring(0,600):message);}
        private void Update()
        {
            if(directory==null)return;
            if(captureBuffer!=null&&(tap==null||!captureBuffer.Armed))
            {captureBuffer.Cancel("Listener/tap destroyed or capture stopped before explicit handshake");Error("Listener DSP capture stopped before explicit handshake");try{StopCapture();}catch(Exception e){Error(e.Message);}}
            if(!finished&&Time.realtimeSinceStartupAsDouble>=nextRead)
            {nextRead=Time.realtimeSinceStartupAsDouble+.05;ReadCommand();}
            if(Time.realtimeSinceStartupAsDouble>=nextState)
            {nextState=Time.realtimeSinceStartupAsDouble+.2;try{Atomic("audio-status.json",JsonUtility.ToJson(State(),true));}catch(Exception e){Error(e.Message);}}
        }
        private void ReadCommand()
        {
            string path=Path.Combine(directory,"audio-command.json");if(!DiagnosticCommandFile.TryRead(path,8192,out string text))return;
            AudioMixCommand command;try{command=JsonUtility.FromJson<AudioMixCommand>(text);}catch(ArgumentException){return;}
            if(command==null||command.sequence<=lastSequence)return;lastSequence=command.sequence;
            var result=new AudioMixResult{nonce=nonce,slot=slot,pid=pid,sequence=command.sequence,kind=command.kind,utc=DateTime.UtcNow.ToString("O"),status="DONE"};
            try
            {
                if(command.nonce!=nonce||command.slot!=slot)throw new InvalidOperationException("Audio command identity differs from this explicit process.");
                switch(command.kind)
                {
                    case "arm":Arm(command.segment,command.seconds);result.segment=segment;break;
                    case "mark":Mark(command.phase);break;
                    case "disable-emitter":
                        if(changedEmitter!=null)throw new InvalidOperationException("Emitter mutation already outstanding.");
                        var local=CurrentSession()?.LocalPlayer;changedEmitter=local!=null?local.GetComponent<VacuumAudioEmitter>():null;
                        if(changedEmitter==null)throw new InvalidOperationException("Actual owner audio emitter absent.");
                        emitterWasEnabled=changedEmitter.enabled;changedEmitter.enabled=false;break;
                    case "enable-emitter":RestoreEmitter();break;
                    case "disable-owner":
                        if(changedOwner!=null)throw new InvalidOperationException("Audio owner mutation already outstanding.");
                        changedOwner=GameAudioRoot.Current;if(changedOwner==null)throw new InvalidOperationException("Actual audio owner absent.");
                        ownerWasEnabled=changedOwner.enabled;changedOwner.enabled=false;break;
                    case "enable-owner":RestoreOwner();break;
                    case "stop":result.capture=StopCapture();result.segment=segment;result.receipt=Path.Combine(directory,"audio-"+segment+".json");break;
                    case "abandon":
                        if(captureBuffer!=null)throw new InvalidOperationException("Stop/drain game capture before changing listener scene.");
                        var session=CurrentSession();if(session==null||!session.HasAuthority||session.Phase!=SessionPhase.Playing||!session.AbandonToMenu())throw new InvalidOperationException("Normal authoritative AbandonToMenu endpoint rejected.");break;
                    case "finish":
                        if(captureBuffer!=null){result.capture=StopCapture();result.segment=segment;result.receipt=Path.Combine(directory,"audio-"+segment+".json");}
                        RestoreMutations();finished=true;Application.targetFrameRate=oldRate;QualitySettings.vSyncCount=oldVsync;break;
                    default:throw new InvalidOperationException("Unknown explicit audio command.");
                }
                result.state=State();
            }
            catch(Exception e){result.status="FAIL";result.detail=e.GetType().Name+": "+e.Message;Error(result.detail);RestoreMutations();}
            Atomic("audio-result-"+command.sequence.ToString("D5")+".json",JsonUtility.ToJson(result,true));
        }
        private SessionRoot CurrentSession()
        {var values=FindObjectsByType<SessionRoot>(FindObjectsSortMode.None);return values.Length==1?values[0]:null;}
        private void Arm(string name,int seconds)
        {
            if(captureBuffer!=null||errors.Count>0||!Regex.IsMatch(name??"","^[a-z0-9-]{1,40}$")||!usedSegments.Add(name))throw new InvalidOperationException("One new bounded segment at a time is required.");
            var listeners=FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Where(x=>x.isActiveAndEnabled).ToArray();
            if(listeners.Length!=1||AudioListener.pause||AudioListener.volume<=0||AudioSettings.outputSampleRate!=48000)throw new InvalidOperationException("Exactly one unpaused real 48k output listener required.");
            segment=name;phase="armed";marks.Clear();plays.Clear();eventOverflow=0;captureListener=listeners[0];
            tap=captureListener.gameObject.AddComponent<AudioDspCapture>();captureBuffer=tap.Buffer;captureBuffer.Begin(AudioSettings.outputSampleRate,seconds);
            AudioPlaybackDiagnostics.Played+=OnPlayed;subscribed=true;Mark("armed");
        }
        private void OnPlayed(AudioPlaybackWitness value)
        {
            if(captureBuffer==null||!captureBuffer.Armed)return;
            if(plays.Count>=4096){eventOverflow++;return;}
            plays.Add(new AudioMixPlayRow{sampleFrame=captureBuffer.Frames,phase=phase,play=value});
        }
        private void Mark(string value)
        {
            if(captureBuffer==null||!captureBuffer.Armed||!Regex.IsMatch(value??"","^[a-z0-9-]{1,40}$")||marks.Count>=64)throw new InvalidOperationException("Bounded marker requires an active DSP segment.");
            phase=value;var state=State();if(state.listeners!=1||state.sources>21||state.errors.Length>0)throw new InvalidOperationException("Actual listener/source/error witness failed.");
            marks.Add(new AudioMixMark{phase=phase,sampleFrame=captureBuffer.Frames,state=state});
        }
        private AudioCaptureReceipt StopCapture()
        {
            if(captureBuffer==null)throw new InvalidOperationException("No managed listener capture pending drain.");
            if(subscribed){AudioPlaybackDiagnostics.Played-=OnPlayed;subscribed=false;}
            var actual=tap;var data=captureBuffer.Drain();captureBuffer=null;tap=null;captureListener=null;if(actual!=null)Destroy(actual);
            var capture=AudioCaptureFile.Write(Path.Combine(directory,"audio-"+segment+".wav"),data);
            var report=new AudioMixSegment{nonce=nonce,pid=pid,slot=slot,segment=segment,run=marks.Count>0?marks[0].state.run:"",
                scope="Actual listener DSP copy, IEEE float32 stereo48k. Play records are returned source.Play calls, not isolated audible events or artistic acceptance.",
                capture=capture,marks=marks.ToArray(),plays=plays.ToArray(),eventOverflow=eventOverflow,producerErrors=AudioPlaybackDiagnostics.ObserverErrors};
            Atomic("audio-"+segment+".json",JsonUtility.ToJson(report,true));RestoreMutations();return capture;
        }
        private AudioMixState State()
        {
            var session=CurrentSession();var roots=FindObjectsByType<GameAudioRoot>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            var owner=roots.Length==1?roots[0]:null;var voices=FindObjectsByType<AudioSource>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            var listeners=FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(x=>x.isActiveAndEnabled);
            var managers=FindObjectsByType<NetworkManager>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            if(captureBuffer!=null&&voices.Length>21)Error("Source count exceeded21 during captured mix");
            if(captureBuffer!=null&&listeners!=1)Error("Enabled listener count changed during captured mix");
            var bindings=owner!=null?owner.Bindings:null;
            return new AudioMixState{nonce=nonce,slot=slot,pid=pid,utc=DateTime.UtcNow.ToString("O"),frame=Time.frameCount,realtime=Time.realtimeSinceStartupAsDouble,dspTime=AudioSettings.dspTime,
                scene=SceneManager.GetActiveScene().name,run=session!=null?session.RunId:"",phase=session!=null?session.Phase.ToString():"absent",authority=session!=null&&session.HasAuthority,
                sessionCount=FindObjectsByType<SessionRoot>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length,players=FindObjectsByType<PlayerMotor>(FindObjectsSortMode.None).Length,items=FindObjectsByType<SuckableObject>(FindObjectsSortMode.None).Length,
                rootCount=roots.Length,listeners=listeners,sources=voices.Length,managers=managers.Length,listeningManagers=managers.Count(x=>x.IsListening||x.ShutdownInProgress),
                rate=AudioSettings.outputSampleRate,listenerPaused=AudioListener.pause,listenerVolume=AudioListener.volume,captureArmed=captureBuffer!=null&&captureBuffer.Armed,finished=finished,
                captureSegment=segment,capturePhase=phase,sampleFrame=captureBuffer!=null?captureBuffer.Frames:0,master=owner!=null?owner.GetVolume(AudioBus.Master):0,
                vacuum=owner!=null?owner.GetVolume(AudioBus.Vacuum):0,truck=owner!=null?owner.GetVolume(AudioBus.Truck):0,impacts=owner!=null?owner.GetVolume(AudioBus.Impacts):0,ui=owner!=null?owner.GetVolume(AudioBus.UI):0,
                candidates=bindings!=null?bindings.Entries.Count(x=>x.Clip!=null):0,pending=bindings!=null?bindings.Entries.Count(x=>x.Status==ClipReviewStatus.PendingReplacement&&x.Clip==null):0,
                pendingIds=bindings!=null?bindings.Entries.Where(x=>x.Status==ClipReviewStatus.PendingReplacement).Select(x=>x.Id.ToString()).ToArray():Array.Empty<string>(),
                voiceDrops=owner!=null?owner.VoiceDrops:0,transportErrors=owner!=null?owner.TransportCueErrors:0,producerErrors=AudioPlaybackDiagnostics.ObserverErrors,eventOverflow=eventOverflow,errors=errors.ToArray(),
                voices=voices.Select(x=>{var emitter=x.GetComponentInParent<VacuumAudioEmitter>();var local=session!=null?session.LocalPlayer:null;
                    return new AudioMixSourceState{id=x.GetInstanceID(),name=x.name,clip=x.clip!=null?x.clip.name:"",active=x.isActiveAndEnabled,playing=x.isPlaying,
                    emitterId=emitter!=null?emitter.GetInstanceID():0,player=emitter!=null?emitter.PlayerId:0,truckEmitter=emitter!=null&&emitter.IsTruck,
                    localEmitter=emitter!=null&&local!=null&&emitter.gameObject==local.gameObject,
                    loop=x.loop,gain=x.volume,spatial=x.spatialBlend,sample=x.clip!=null&&x.isPlaying?x.timeSamples:0};}).ToArray()};
        }
        private void RestoreEmitter(){if(changedEmitter!=null)changedEmitter.enabled=emitterWasEnabled;changedEmitter=null;}
        private void RestoreOwner(){if(changedOwner!=null)changedOwner.enabled=ownerWasEnabled;changedOwner=null;}
        private void RestoreMutations(){RestoreOwner();RestoreEmitter();}
        private void Error(string value){if(errors.Count<32&&!errors.Contains(value))errors.Add(value);}
        private void Atomic(string name,string text)
        {string target=Path.Combine(directory,name),temporary=target+".tmp";File.WriteAllText(temporary,text);if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);}
        private void OnDisable()
        {
            if(subscribed){AudioPlaybackDiagnostics.Played-=OnPlayed;subscribed=false;}
            if(captureBuffer!=null){captureBuffer.Cancel("Diagnostic observer disabled before explicit finish");try{StopCapture();}catch(Exception e){Error(e.Message);}}
            RestoreMutations();Application.logMessageReceived-=OnLog;Application.targetFrameRate=oldRate;QualitySettings.vSyncCount=oldVsync;
        }
    }
}
#endif
