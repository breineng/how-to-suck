using System;
using System.Collections.Generic;
using UnityEngine;
namespace HowToSuck.Audio
{
    public readonly struct AuthorityImpactCue
    {
        public readonly string Run;public readonly ulong Sequence;public readonly SfxId Id;
        public readonly Vector3 Position;public readonly float Gain;
        public AuthorityImpactCue(string run,ulong sequence,SfxId id,Vector3 position,float gain)
        {Run=run;Sequence=sequence;Id=id;Position=position;Gain=gain;}
    }
    [DefaultExecutionOrder(10500),DisallowMultipleComponent,RequireComponent(typeof(SessionRoot))]
    public sealed class GameAudioRoot:MonoBehaviour
    {
        public AudioBindings Bindings;
        // Explicit product authoring switch; old isolated fixtures keep their original source-gain route.
        public bool UseSettingsMixer;
        public string SettingsMixerError {get;private set;}="";
        public bool SettingsMixerApplied {get;private set;}
        private LocalSettingsController settingsOwner;
        public SessionRoot Session {get;private set;}
        public static GameAudioRoot Current {get;private set;}
        public event Action<AuthorityImpactCue> AuthorityImpact;
        public bool Playing=>isActiveAndEnabled&&Session!=null&&Session.IsInitialized&&Session.Phase==SessionPhase.Playing&&Session.ContractState.Phase==ContractPhase.Running;
        public int VoiceDrops {get;private set;}
        public int PendingClipSkips {get;private set;}
        public int TransportCueErrors {get;private set;}
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private AudioDiagnosticContext nextDiagnosticCue;
#endif
        private readonly AudioEventLedger ledger=new AudioEventLedger();
        private readonly HashSet<VacuumAudioEmitter> loops=new HashSet<VacuumAudioEmitter>();
        private readonly float[] volumes={.7f,1,1,1,1};
        private sealed class Voice{public AudioSource Source;public AudioBindings.Entry Entry;public float Gain;public Transform Follow;public bool Spatial;}
        private readonly Voice[] voices=new Voice[12];
        private bool ready,wasPlaying;
        private ulong impactSequence;
        private double impactAt,impactTokens=8;
        private Vector3 listener;
        private void Awake(){Session=GetComponent<SessionRoot>();}
        private void OnEnable()
        {
            if(Current!=null&&Current!=this)throw new InvalidOperationException("One audio owner per active session root is required.");
            Current=this;if(Session==null)Session=GetComponent<SessionRoot>();Session.Changed+=RefreshState;
        }
        private void Start()
        {
            string error=null;
            if(Bindings==null||!Bindings.Validate(out error))
            {Debug.LogError("Audio bindings are missing or invalid: "+(Bindings==null?"missing":error),this);enabled=false;return;}
            for(int i=0;i<voices.Length;i++)voices[i]=new Voice{Source=CreateSource(transform,"SFX voice "+i)};
            ready=true;ApplyMixerSettings();RefreshState();
        }
        public static AudioSource CreateSource(Transform parent,string name)
        {
            var child=new GameObject(name);child.transform.SetParent(parent,false);
            var source=child.AddComponent<AudioSource>();source.playOnAwake=false;source.dopplerLevel=0;
            source.rolloffMode=AudioRolloffMode.Logarithmic;source.reverbZoneMix=0;source.volume=0;return source;
        }
        private void LateUpdate()
        {
            if(!ready)return;RefreshState();
            var local=Session.LocalPlayer;var view=local!=null?local.GetComponent<PlayerView>():null;
            var camera=view!=null?view.Camera:null;if(camera==null)camera=Camera.main;
            listener=camera!=null?camera.transform.position:transform.position;
            foreach(var voice in voices)
            {
                if(voice.Source==null||voice.Entry==null)continue;
                if(!voice.Source.isPlaying){voice.Entry=null;voice.Follow=null;continue;}
                if(voice.Follow!=null)voice.Source.transform.position=voice.Follow.position;
            }
            ApplyVoiceVolumes();
        }
        private void ApplyVoiceVolumes()
        {
            float sum=0;foreach(var voice in voices)if(voice!=null&&voice.Entry!=null&&voice.Source.isPlaying)
                sum+=voice.Entry.Gain*voice.Gain*volumes[(int)voice.Entry.Bus];
            float budget=sum>.40f?.40f/sum:1;
            foreach(var voice in voices)if(voice!=null&&voice.Entry!=null)
                voice.Source.volume=voice.Entry.Gain*voice.Gain*SourceBusGain(voice.Entry.Bus)*budget;
        }
        private void RefreshState()
        {
            if(!ready||Session==null)return;
            string run=Session.RunId??"";
            if(ledger.SetRun(run)){StopAll();impactSequence=0;impactTokens=8;impactAt=Time.realtimeSinceStartupAsDouble;}
            bool playing=Playing;
            if(wasPlaying&&!playing)StopWorld();wasPlaying=playing;
            var state=Session.ContractState;
            if(state.Phase==ContractPhase.Running&&state.QuotaReached)QuotaReached(run);
            if(state.Phase==ContractPhase.Succeeded)ContractSucceeded(run);
            else if(state.Phase==ContractPhase.Failed)ContractTimedOut(run);
        }
        public float BusGain(AudioBus bus)=>bus==AudioBus.Master?volumes[0]:volumes[0]*volumes[(int)bus];
        public float SourceBusGain(AudioBus bus)=>UseSettingsMixer?(SettingsMixerApplied&&BusGain(bus)>0?1:0):BusGain(bus);
        public float GetVolume(AudioBus bus)=>(int)bus<volumes.Length?volumes[(int)bus]:0;
        public void BindLocalSettings(LocalSettingsController owner)
        {
            if(owner==null||owner.gameObject!=gameObject||owner.Audio!=this)
                throw new InvalidOperationException("Bind the explicit local settings owner on this audio session root.");
            if(settingsOwner!=null&&settingsOwner!=owner)throw new InvalidOperationException("Audio already has a local settings owner.");
            settingsOwner=owner;
        }
        public void UnbindLocalSettings(LocalSettingsController owner){if(settingsOwner==owner)settingsOwner=null;}
        public void ApplyLocalSettings(LocalSettingsController owner,LocalSettingsData value)
        {
            if(settingsOwner!=owner||owner==null||value==null||!value.Valid)throw new InvalidOperationException("Invalid local audio settings binding or values.");
            volumes[0]=value.Master;volumes[1]=value.Vacuum;volumes[2]=value.Truck;volumes[3]=value.Impacts;volumes[4]=value.UI;
            if(ready){ApplyMixerSettings();ApplyVoiceVolumes();}
        }
        private void ApplyMixerSettings()
        {
            if(!ready)return;SettingsMixerApplied=false;SettingsMixerError="";
            if(!UseSettingsMixer){
                // Another session may have used this shared mixer before an isolated legacy fixture.
                // Neutralize only our exposed controls before the source-gain path; do not double-attenuate.
                if(Bindings!=null&&Bindings.Mixer!=null)for(int i=0;i<volumes.Length;i++)
                    if(Bindings.Mixer.GetFloat(LocalSettingsMath.MixerParameter(i),out _))Bindings.Mixer.SetFloat(LocalSettingsMath.MixerParameter(i),0);
                return;
            }
            if(Bindings==null||Bindings.Mixer==null){SettingsMixerError="Не удалось применить громкость: микшер недоступен.";return;}
            for(int i=0;i<volumes.Length;i++)
                if(!Bindings.Mixer.SetFloat(LocalSettingsMath.MixerParameter(i),LocalSettingsMath.Decibels(volumes[i])))
                {SettingsMixerError="Не удалось применить громкость: группа микшера недоступна.";return;}
            SettingsMixerApplied=true;
        }
        // Settings UI may preview locally, then explicitly save on Apply. No campaign/profile ownership is changed.
        public void SetVolume(AudioBus bus,float value)
        {if((int)bus>=volumes.Length||float.IsNaN(value)||float.IsInfinity(value))return;
            if(settingsOwner!=null){settingsOwner.PreviewVolume(bus,Mathf.Clamp01(value));return;}
            volumes[(int)bus]=Mathf.Clamp01(value);if(ready)ApplyMixerSettings();}
        public void SaveVolumeSettings()
        {if(settingsOwner!=null)settingsOwner.Apply();}
        public bool Register(VacuumAudioEmitter value)
        {
            if(!ready||value==null)return false;if(loops.Contains(value))return true;
            int same=0;foreach(var loop in loops)if(loop!=null&&loop.IsTruck==value.IsTruck)same++;
            if(same>=(value.IsTruck?1:4)){VoiceDrops++;return false;}return loops.Add(value);
        }
        public bool Registered(VacuumAudioEmitter value)=>loops.Contains(value);
        public void Unregister(VacuumAudioEmitter value){loops.Remove(value);}
        public void SetReplicaLoad(string run,int player,float load)
        {if(!Playing||Session.HasAuthority||run!=Session.RunId)return;foreach(var loop in loops)if(loop!=null&&!loop.IsTruck&&loop.PlayerId==player)loop.SetReplicaLoad(load);}
        public void Ingestion(IngestionSnapshot value)
        {
            if(!Playing||value==null||value.RunId!=Session.RunId||!ledger.Ingestion(value.RunId,value.InstanceId))return;
            // A late replica retains its identity but does not replay an already finished accent.
            if(value.PresentationNow>value.StartedAt+value.Duration+.1)return;
            SfxId id=value.IsTruck?Variant(SfxId.TruckSwallowA,SfxId.TruckSwallowB,value.InstanceId):
                value.RequiredSize<=.2f?SfxId.SwallowTiny:value.RequiredSize<=.6f?SfxId.SwallowMedium:SfxId.SwallowHeavy;
            Transform follow=null;foreach(var loop in loops)if(loop!=null&&(value.IsTruck?loop.IsTruck:!loop.IsTruck&&loop.PlayerId==value.PlayerId)){follow=loop.Anchor;break;}
            bool local=!value.IsTruck&&Session.LocalPlayer!=null&&Session.LocalPlayer.PlayerId==value.PlayerId;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            nextDiagnosticCue=new AudioDiagnosticContext(AudioDiagnosticKind.Ingestion,value.RunId,value.InstanceId,0,value.PlayerId);
#endif
            Play(id,value.TargetPosition,!local,1,follow);
        }
        public void Rattle(VacuumAudioEmitter owner,float load)
        {if(Playing&&Registered(owner))Play(SfxId.VacuumRattle,owner.Anchor.position,!owner.IsLocal,Mathf.Lerp(.3f,.7f,load),owner.Anchor);}
        public void QuotaReached(string run){if(Playing&&Session.ContractState.QuotaReached&&ledger.Signal(run,SfxId.QuotaReady))Play(SfxId.QuotaReady,Vector3.zero,false,1);}
        public void ContractSucceeded(string run){if(Session!=null&&Session.ContractState.Phase==ContractPhase.Succeeded&&ledger.Signal(run,SfxId.ExtractSuccess))Play(SfxId.ExtractSuccess,Vector3.zero,false,1);}
        public void ContractTimedOut(string run){if(Session!=null&&Session.ContractState.Phase==ContractPhase.Failed&&ledger.Signal(run,SfxId.ContractFail))Play(SfxId.ContractFail,Vector3.zero,false,1);}
        // Call only after a successful real purchase transaction, never from a button's raw click.
        public void PurchaseConfirmed(string transactionId){if(ledger.Purchase(transactionId))Play(SfxId.UiPurchase,Vector3.zero,false,1);}
        public void Collision(SuckableObject item,Collision collision)
        {
            if(!Playing||!Session.HasAuthority||item==null||!item.HasPhysicsAuthority||item.RunId!=Session.RunId||item.WorldFrozen||item.State!=SuckableState.Available||item.Body.isKinematic||collision==null||collision.contactCount==0)return;
            var other=collision.rigidbody!=null?collision.rigidbody.GetComponent<SuckableObject>():null;
            if(other!=null&&(other.RunId!=item.RunId||other.InstanceId==0||item.InstanceId>other.InstanceId))return;
            float speed=collision.relativeVelocity.magnitude,impulse=collision.impulse.magnitude;
            if(!Finite(speed)||!Finite(impulse)||speed<.65f||impulse<.18f)return;
            ulong otherId=other!=null?other.InstanceId:(1UL<<63)|(uint)collision.collider.GetInstanceID();
            double now=Time.realtimeSinceStartupAsDouble;
            if(!ledger.CollisionPair(item.RunId,item.InstanceId,otherId,now))return;
            impactTokens=Math.Min(8,impactTokens+Math.Max(0,now-impactAt)*16);impactAt=now;
            if(impactTokens<1||impactSequence==ulong.MaxValue)return;impactTokens--;
            float mass=Mathf.Max(item.Body.mass,other!=null?other.Body.mass:0);
            var a=mass<2?SfxId.ImpactSmallA:mass<30?SfxId.ImpactWoodA:SfxId.ImpactHeavyA;
            var b=(SfxId)((int)a+1);ulong sequence=++impactSequence;var id=Variant(a,b,sequence);
            var point=collision.GetContact(0).point;float gain=Mathf.Clamp(Mathf.Log10(1+impulse)*.65f,.15f,1);
            if(!Finite(point))return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            nextDiagnosticCue=new AudioDiagnosticContext(AudioDiagnosticKind.Impact,item.RunId,item.InstanceId,sequence);
#endif
            Play(id,point,true,gain);
            try{AuthorityImpact?.Invoke(new AuthorityImpactCue(item.RunId,sequence,id,point,gain));}
            catch(Exception){TransportCueErrors++;} // Cosmetic transport failure cannot alter a physics callback or award money.
        }
        public void ReceiveImpact(AuthorityImpactCue cue)
        {
            if(!Playing||Session.HasAuthority||!ImpactId(cue.Id)||!Finite(cue.Position)||!Finite(cue.Gain)||cue.Gain<0||cue.Gain>1||!ledger.ImpactSequence(cue.Run,cue.Sequence))return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            nextDiagnosticCue=new AudioDiagnosticContext(AudioDiagnosticKind.Impact,cue.Run,0,cue.Sequence);
#endif
            Play(cue.Id,cue.Position,true,cue.Gain);
        }
        private static bool ImpactId(SfxId id)=>id>=SfxId.ImpactSmallA&&id<=SfxId.ImpactHeavyB;
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        private static bool Finite(Vector3 v)=>Finite(v.x)&&Finite(v.y)&&Finite(v.z);
        private SfxId Variant(SfxId a,SfxId b,ulong identity)
        {if(!Bindings.Available(b))return a;if(!Bindings.Available(a))return b;return (identity&1)==0?a:b;}
        private float Score(AudioBindings.Entry entry,Vector3 point,bool spatial)
        {float distance=spatial?Vector3.Distance(listener,point):0;return (257-entry.Priority)/(1+distance);}
        private void Play(SfxId id,Vector3 position,bool spatial,float gain,Transform follow=null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var diagnosticCue=nextDiagnosticCue;nextDiagnosticCue=default;
#endif
            if(!ready||!isActiveAndEnabled)return;var entry=Bindings.Find(id);
            if(entry==null||!Bindings.Available(id)){PendingClipSkips++;return;}
            if(entry.Loop||spatial&&Vector3.Distance(listener,position)>entry.MaxDistance)return;
            Voice chosen=null;float weakest=float.PositiveInfinity;
            foreach(var voice in voices)
            {
                if(!voice.Source.isPlaying){chosen=voice;break;}
                float score=Score(voice.Entry,voice.Source.transform.position,voice.Spatial);
                if(score<weakest){weakest=score;chosen=voice;}
            }
            if(chosen==null||chosen.Source.isPlaying&&Score(entry,position,spatial)<=weakest){VoiceDrops++;return;}
            var source=chosen.Source;source.Stop();chosen.Entry=entry;chosen.Gain=Mathf.Clamp01(gain);chosen.Follow=follow;chosen.Spatial=spatial;
            source.transform.position=position;source.clip=entry.Clip;source.loop=false;source.pitch=1;source.priority=entry.Priority;
            source.spatialBlend=spatial?1:0;source.minDistance=entry.MinDistance;source.maxDistance=entry.MaxDistance;
            source.outputAudioMixerGroup=Bindings.Group(entry.Bus);source.volume=0;source.Play();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AudioPlaybackDiagnostics.ReturnedPlay(source,this,id,diagnosticCue);
#endif
            ApplyVoiceVolumes();
        }
        private void StopWorld()
        {
            foreach(var voice in voices)if(voice!=null&&voice.Entry!=null&&voice.Entry.Bus!=AudioBus.UI){voice.Source.Stop();voice.Entry=null;voice.Follow=null;}
            foreach(var loop in loops)if(loop!=null)loop.StopAudio();
        }
        private void StopAll()
        {foreach(var voice in voices)if(voice!=null){voice.Source.Stop();voice.Entry=null;voice.Follow=null;}foreach(var loop in loops)if(loop!=null)loop.StopAudio();}
        private void OnDisable()
        {if(Session!=null)Session.Changed-=RefreshState;StopAll();loops.Clear();if(Current==this)Current=null;}
        private void OnDestroy(){AuthorityImpact=null;foreach(var voice in voices)if(voice!=null&&voice.Source!=null)Destroy(voice.Source.gameObject);}
    }
}
