#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;using System.IO;using System.Linq;using System.Collections.Generic;
using UnityEngine;using HowToSuck.Audio;
namespace HowToSuck.Diagnostics {
 [Serializable] public sealed class NativeAudioRow {
  public string channel,run,clip,phase;public int frame,kind,owner,intake,amount,id,diagnosticKind,sourceId,rootId;
  public ulong row,sequence,occurrence,item,enemy,shot,playSerial;public bool truck,authority,loop;
  public double wall,dsp,at;public float size,duration,x,y,z;public long sampleFrame;
 }
 [Serializable] public sealed class NativeAudioMark {
  public string name,run,phase;public double wall,dsp;public int frame,listeners,sources,playingSources,localOwner,voiceDrops,pendingClipSkips,transportCueErrors,queueDrops;
  public bool authority,listenerPaused;public float listenerVolume;public long sampleFrame;
 }
 [Serializable] public sealed class NativeAudioObservationReceipt {
  public string status,reason,directory,utc,driverOutcome,scope,listenerEnd;public int pid,sessionId,audioRootId,listenerId,sourceRows,receiptRows,playRows,ignoredForeign,eventOverflow,sourceObserverErrorsDelta,playObserverErrorsDelta;
  public bool finished,startedBeforeOwnedRun,everBound,everCaptured;public double began,ended;public string[] errors;
  public NativeAudioCaptureReceipt capture;public NativeAudioMark[] marks;
 }
 // Explicitly created only by an owned root fixture. Never installs itself or drives gameplay.
 public sealed class NativeContractAudioObserver:MonoBehaviour {
  public static NativeContractAudioObserver Current {get;private set;}
  public string DirectoryPath {get;private set;}public bool Finished {get;private set;}
  SessionRoot session;GameAudioRoot audioRoot;AudioListener listener;NativeAudioDspTap tap;
  NativeAudioCaptureBuffer retained;NativeAudioCaptureReceipt capture;bool subscribed,beforeRun,everBound,everCaptured;
  int sessionId,audioId,listenerId,ignored,eventOverflow,sourceCount,receiptCount,playCount,oldSourceErrors,oldPlayErrors;
  ulong row;double began,deadline,nextPoll,nextFlush;int seconds;string listenerEnd,finishReason,driverOutcome="unknown; inspect the independent driver receipt";
  readonly List<NativeAudioRow> pending=new List<NativeAudioRow>();readonly List<NativeAudioMark> marks=new List<NativeAudioMark>();readonly List<string> errors=new List<string>();
  const int MaxRows=16384;const int MaxMarks=512;
  public static NativeContractAudioObserver BeginBeforeOwnedRun(string directory,int maxSeconds) {
   if(FindObjectsByType<SessionRoot>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length!=0)throw new InvalidOperationException("Before-run mode requires the driver's fresh empty Play session boundary.");
   return Create(directory,maxSeconds,null,true);
  }
  public static NativeContractAudioObserver AttachOwnedSession(SessionRoot owner,string directory,int maxSeconds) {
   if(owner==null||!owner.IsInitialized||FindObjectsByType<SessionRoot>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length!=1)throw new InvalidOperationException("Pass the exact single existing initialized fixture-owned session.");
   return Create(directory,maxSeconds,owner,false);
  }
  static NativeContractAudioObserver Create(string directory,int maxSeconds,SessionRoot owner,bool early) {
   if(!Application.isPlaying||Current!=null||maxSeconds<1||maxSeconds>120)throw new InvalidOperationException("One explicit bounded observer in real Play is required.");
   var full=Path.GetFullPath(directory);var allowed=Path.GetFullPath(Path.Combine(Application.dataPath,"../docs/testing"))+Path.DirectorySeparatorChar;
   if(!full.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||Directory.Exists(full)||File.Exists(full))throw new InvalidOperationException("A new child directory under this project's docs/testing is required; no overwrite.");
   Directory.CreateDirectory(full);var go=new GameObject("Owned contract audio observation");DontDestroyOnLoad(go);
   var value=go.AddComponent<NativeContractAudioObserver>();value.DirectoryPath=full;value.seconds=maxSeconds;value.beforeRun=early;value.began=Time.realtimeSinceStartupAsDouble;value.deadline=value.began+maxSeconds;
   value.oldSourceErrors=CommittedAudioEvents.SubscriberFailures;value.oldPlayErrors=AudioPlaybackDiagnostics.ObserverErrors;Current=value;
   CommittedAudioEvents.Emitted+=value.OnSource;AudioPlaybackDiagnostics.Played+=value.OnPlayed;Application.logMessageReceived+=value.OnLog;value.subscribed=true;
   if(owner!=null)value.Bind(owner);value.Mark("explicit-observer-start");value.SaveReceipt(false);return value;
  }
  void Bind(SessionRoot value) {
   if(everBound&&value!=session)throw new InvalidOperationException("Observer cannot adopt a different session after its original owner.");
   session=value;sessionId=value.GetInstanceID();everBound=true;TryAudioBind();Mark("owned-session-bound");
  }
  void TryAudioBind() {
   if(session==null)return;var candidate=session.GetComponent<GameAudioRoot>();if(candidate==audioRoot)return;
   if(audioRoot!=null)audioRoot.AuthorityCommittedAudio-=OnReceipt;
   if(audioId!=0&&candidate!=audioRoot){Error("GameAudioRoot replaced within captured owner");return;}
   audioRoot=candidate;if(audioRoot!=null){audioId=audioRoot.GetInstanceID();audioRoot.AuthorityCommittedAudio+=OnReceipt;}
  }
  void DiscoverOwner() {
   if(everBound)return;var candidates=FindObjectsByType<SessionRoot>(FindObjectsInactive.Include,FindObjectsSortMode.None);
   if(candidates.Length>1){Error("Multiple sessions appeared at the owned fixture boundary");Finish("ambiguous-session",null);return;}
   if(candidates.Length==1&&candidates[0].IsInitialized)Bind(candidates[0]);
  }
  bool Matches(string run) {if(!everBound)DiscoverOwner();return !Finished&&session!=null&&session.IsInitialized&&session.RunId==run;}
  void OnSource(CommittedAudioFact f) {try{if(!Matches(f.Run)){ignored++;return;}Add(FactRow("source",f,0));}catch(Exception e){Error("source observer: "+e.Message);}}
  void OnReceipt(CommittedAudioReceipt receipt) {try{if(!Matches(receipt.Fact.Run)){ignored++;return;}Add(FactRow("authority-receipt",receipt.Fact,receipt.Sequence));}catch(Exception e){Error("receipt observer: "+e.Message);}}
  NativeAudioRow FactRow(string channel,CommittedAudioFact f,ulong sequence) {
   var r=Stamp(channel);r.run=f.Run;r.kind=(int)f.Kind;r.owner=f.Owner;r.intake=f.Intake;r.amount=f.Amount;r.sequence=sequence;r.occurrence=f.Occurrence;r.item=f.Item;r.enemy=f.Enemy;r.shot=f.Kind==CommittedAudioKind.ShotLaunch||f.Kind==CommittedAudioKind.EnemyHit||f.Kind==CommittedAudioKind.EnemyDefeat?f.Occurrence:0;r.truck=f.Truck;r.at=f.At;r.size=f.Size;r.duration=f.Duration;r.x=f.Position.x;r.y=f.Position.y;r.z=f.Position.z;return r;
  }
  void OnPlayed(AudioPlaybackWitness w) {try{
   if(Finished||audioRoot==null||w.rootId!=audioId){ignored++;return;}
   var r=Stamp("post-play");r.run=w.run;r.kind=w.committedKind;r.owner=w.player;r.sequence=w.committedSequence;r.occurrence=w.occurrence;r.item=w.instance;r.enemy=w.enemy;
   r.shot=w.committedKind>=2&&w.committedKind<=4?w.occurrence:0;r.playSerial=w.serial;r.id=w.id;r.clip=w.clip;r.diagnosticKind=w.kind;r.sourceId=w.sourceId;r.rootId=w.rootId;r.authority=w.authority;r.loop=w.loop;r.dsp=w.dspTime;r.frame=w.frame;r.x=w.position.x;r.y=w.position.y;r.z=w.position.z;Add(r);
  }catch(Exception e){Error("post-play observer: "+e.Message);}}
  NativeAudioRow Stamp(string channel)=>new NativeAudioRow{channel=channel,frame=Time.frameCount,wall=Time.realtimeSinceStartupAsDouble,dsp=AudioSettings.dspTime,sampleFrame=retained!=null?retained.Frames:-1,authority=session!=null&&session.HasAuthority,phase=session!=null?session.Phase.ToString():"unbound"};
  void Add(NativeAudioRow value) {
   if(Finished)return;if(row>=MaxRows){eventOverflow++;return;}value.row=++row;pending.Add(value);
   if(value.channel=="source")sourceCount++;else if(value.channel=="authority-receipt")receiptCount++;else playCount++;
  }
  void Update() {
   if(DirectoryPath==null||Finished)return;
   try {
    DiscoverOwner();if(Finished)return;
    if(everBound&&session==null){Finish("owned-session-destroyed",null);return;}TryAudioBind();
    if(retained!=null&&(tap==null||listener==null||!listener.isActiveAndEnabled||!retained.Armed))DrainCapture("owned-listener-ended");
    if(!everCaptured&&session!=null&&session.IsInitialized&&session.LocalPlayer!=null&&session.Phase==SessionPhase.Playing)TryCapture();
    var now=Time.realtimeSinceStartupAsDouble;
    if(now>=nextPoll){nextPoll=now+.25;Mark("sample");}
    if(now>=nextFlush){nextFlush=now+.5;FlushRows();}
    if(now>=deadline)Finish("bounded-observation-deadline",null);
   }catch(Exception e){Error("observer update: "+e.Message);Finish("observer-fault",null);}
  }
  void TryCapture() {
   var candidates=FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Where(x=>x.isActiveAndEnabled).ToArray();
   if(candidates.Length!=1)throw new InvalidOperationException("Exactly one real enabled listener is required for the captured mix.");
   listener=candidates[0];if(!listener.transform.IsChildOf(session.LocalPlayer.transform))throw new InvalidOperationException("The actual listener is outside the owned local player's hierarchy.");
   if(listener.GetComponent<NativeAudioDspTap>()!=null)throw new InvalidOperationException("Listener already has this diagnostic tap.");
   listenerId=listener.GetInstanceID();tap=listener.gameObject.AddComponent<NativeAudioDspTap>();retained=tap.Buffer;retained.Begin(AudioSettings.outputSampleRate,seconds);everCaptured=true;Mark("listener-dsp-begin");
  }
  void DrainCapture(string reason) {
   if(retained==null)return;listenerEnd=reason;var buffer=retained;retained=null;var ownedTap=tap;tap=null;listener=null;
   var data=buffer.Drain();try{capture=NativeAudioCaptureFile.Write(Path.Combine(DirectoryPath,"listener-mix.wav"),data);}catch(Exception e){Error("DSP drain/write: "+e.Message);}
   finally{if(ownedTap!=null)Destroy(ownedTap);}Mark("listener-dsp-drained");
  }
  void Mark(string name) {
   if(marks.Count>=MaxMarks)return;var sources=FindObjectsByType<AudioSource>(FindObjectsInactive.Include,FindObjectsSortMode.None);var listeners=FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(x=>x.isActiveAndEnabled);
   if(everCaptured&&retained!=null&&(listeners!=1||sources.Length>21))Error("During DSP capture listener/source bound changed: "+listeners+"/"+sources.Length);
   marks.Add(new NativeAudioMark{name=name,wall=Time.realtimeSinceStartupAsDouble,dsp=AudioSettings.dspTime,frame=Time.frameCount,run=session!=null?session.RunId:null,phase=session!=null?session.Phase.ToString():"absent",listeners=listeners,sources=sources.Length,playingSources=sources.Count(x=>x.isPlaying),localOwner=session!=null&&session.LocalPlayer!=null?session.LocalPlayer.PlayerId:0,authority=session!=null&&session.HasAuthority,listenerPaused=AudioListener.pause,listenerVolume=AudioListener.volume,sampleFrame=retained!=null?retained.Frames:-1,voiceDrops=audioRoot!=null?audioRoot.VoiceDrops:0,pendingClipSkips=audioRoot!=null?audioRoot.PendingClipSkips:0,transportCueErrors=audioRoot!=null?audioRoot.TransportCueErrors:0,queueDrops=audioRoot!=null?audioRoot.CommittedQueueDrops:0});
  }
  void OnLog(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)Error("runtime "+type+": "+message);}
  void Error(string message){if(errors.Count<64)errors.Add(message.Length>1200?message.Substring(0,1200):message);}
  void FlushRows() {
   if(pending.Count==0)return;using(var stream=new FileStream(Path.Combine(DirectoryPath,"events.jsonl"),FileMode.Append,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete))using(var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false)))foreach(var value in pending)writer.WriteLine(JsonUtility.ToJson(value));pending.Clear();
  }
  void SaveReceipt(bool finished) {
   var report=new NativeAudioObservationReceipt{status=finished?((errors.Count>0||eventOverflow>0||!everCaptured||(capture!=null&&capture.fault!=null)||driverOutcome.Contains("failed")||finishReason=="bounded-observation-deadline")?"PARTIAL_OBSERVATION_SAVED_REVIEW_REQUIRED":"OBSERVATION_SAVED_REVIEW_REQUIRED"):"OBSERVATION_ACTIVE",reason=finishReason,directory=DirectoryPath,utc=DateTime.UtcNow.ToString("O"),driverOutcome=driverOutcome,scope="Passive actual game event/AudioSource.Play/DSP observation only. Driver may use engineering arrangements and service intents: see its independent receipt. Not artistic acceptance, full peer transport, isolated perceived cue count, or player-input acceptance.",listenerEnd=listenerEnd,pid=System.Diagnostics.Process.GetCurrentProcess().Id,sessionId=sessionId,audioRootId=audioId,listenerId=listenerId,sourceRows=sourceCount,receiptRows=receiptCount,playRows=playCount,ignoredForeign=ignored,eventOverflow=eventOverflow,sourceObserverErrorsDelta=CommittedAudioEvents.SubscriberFailures-oldSourceErrors,playObserverErrorsDelta=AudioPlaybackDiagnostics.ObserverErrors-oldPlayErrors,finished=finished,startedBeforeOwnedRun=beforeRun,everBound=everBound,everCaptured=everCaptured,began=began,ended=Time.realtimeSinceStartupAsDouble,errors=errors.ToArray(),capture=capture,marks=marks.ToArray()};
   var target=Path.Combine(DirectoryPath,"receipt.json");var temp=target+".tmp";File.WriteAllText(temp,JsonUtility.ToJson(report,true));if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);
  }
  public string Finish(string reason,string independentDriverOutcome) {
   if(independentDriverOutcome!=null)driverOutcome=independentDriverOutcome;
   if(Finished){SaveReceipt(true);return Path.Combine(DirectoryPath,"receipt.json");}
   Finished=true;finishReason=reason;
   if(subscribed){CommittedAudioEvents.Emitted-=OnSource;AudioPlaybackDiagnostics.Played-=OnPlayed;Application.logMessageReceived-=OnLog;subscribed=false;}
   if(audioRoot!=null)audioRoot.AuthorityCommittedAudio-=OnReceipt;
   try{DrainCapture("finish: "+reason);FlushRows();if(!everCaptured)Error("No owned listener DSP was captured");Mark("finished");SaveReceipt(true);}catch(Exception e){Error("finish save: "+e.Message);try{SaveReceipt(true);}catch{}}
   return Path.Combine(DirectoryPath,"receipt.json");
  }
  void OnDisable(){if(DirectoryPath!=null&&!Finished)Finish("observer-disabled",null);}
  void OnDestroy(){if(DirectoryPath!=null&&!Finished)Finish("observer-destroyed",null);if(Current==this)Current=null;}
  void OnApplicationQuit(){if(DirectoryPath!=null&&!Finished)Finish("application-quitting",null);}
 }
}
#endif
