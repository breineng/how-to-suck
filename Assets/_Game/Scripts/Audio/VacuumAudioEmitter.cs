using UnityEngine;
namespace HowToSuck.Audio
{
    [DefaultExecutionOrder(11000),DisallowMultipleComponent,RequireComponent(typeof(VacuumEmitter))]
    public sealed class VacuumAudioEmitter:MonoBehaviour
    {
        public bool IsTruck;
        public int PlayerId=>motor!=null?motor.PlayerId:0;
        public bool IsLocal=>!IsTruck&&root!=null&&root.Session.LocalPlayer==motor;
        public Transform Anchor=>emitter!=null&&emitter.Source!=null?emitter.Source:transform;
        private VacuumEmitter emitter;private PlayerMotor motor;private GameAudioRoot root;
        private AudioSource idle,active;private AudioBindings.Entry idleEntry,activeEntry;
        private float blend,fade,load;private double nextRattle,replicaAt;
        private float replicaLoad;private string run;
        private void Awake(){emitter=GetComponent<VacuumEmitter>();motor=GetComponent<PlayerMotor>();}
        public void SetReplicaLoad(float value)
        {if(float.IsNaN(value)||float.IsInfinity(value))return;replicaLoad=Mathf.Clamp01(value);replicaAt=Time.realtimeSinceStartupAsDouble;}
        private void LateUpdate()
        {
            var owner=GameAudioRoot.Current;
            if(root!=owner||root!=null&&!root.Registered(this))
            {
                if(root!=null)root.Unregister(this);StopAudio();root=owner;
                if(root==null||!root.Register(this)){root=null;return;}
                idleEntry=root.Bindings.Find(IsTruck?SfxId.TruckIdle:SfxId.VacuumIdle);
                activeEntry=IsTruck?null:root.Bindings.Find(SfxId.VacuumActive);
                if(idle==null)idle=GameAudioRoot.CreateSource(transform,"Motor idle");
                if(!IsTruck&&active==null)active=GameAudioRoot.CreateSource(transform,"Motor suction");
            }
            if(root==null||!root.Playing||!emitter.isActiveAndEnabled){StopAudio();return;}
            if(run!=root.Session.RunId){StopAudio();run=root.Session.RunId;replicaAt=0;nextRattle=Time.realtimeSinceStartupAsDouble+1;}
            float dt=Mathf.Min(Time.unscaledDeltaTime,.1f);
            fade=Mathf.MoveTowards(fade,1,dt*5);blend=Mathf.MoveTowards(blend,emitter.Active?1:0,dt*7);
            float desired=root.Session.HasAuthority?Mathf.Clamp01(emitter.LastLoad/20f):Time.realtimeSinceStartupAsDouble-replicaAt<.45?replicaLoad:0;
            load=Mathf.Lerp(load,desired,1-Mathf.Exp(-dt*3));
            UpdateLoop(idle,idleEntry,IsTruck?fade:fade*Mathf.Sqrt(1-blend),1);
            if(!IsTruck)UpdateLoop(active,activeEntry,fade*Mathf.Sqrt(blend),Mathf.Lerp(1,.97f,load));
            if(!IsTruck&&emitter.Active&&load>.12f&&Time.realtimeSinceStartupAsDouble>=nextRattle)
            {nextRattle=Time.realtimeSinceStartupAsDouble+2.3;root.Rattle(this,load);}
        }
        private void UpdateLoop(AudioSource source,AudioBindings.Entry entry,float amount,float pitch)
        {
            if(source==null||entry==null||!root.Bindings.Available(entry.Id)||!entry.Loop){if(source!=null)source.Stop();return;}
            source.transform.position=Anchor.position;source.spatialBlend=IsLocal?0:1;source.pitch=pitch;
            source.priority=IsLocal?40:entry.Priority;source.minDistance=entry.MinDistance;source.maxDistance=entry.MaxDistance;
            source.outputAudioMixerGroup=root.Bindings.Group(entry.Bus);source.volume=entry.Gain*root.SourceBusGain(entry.Bus)*amount*(IsTruck||IsLocal?1f:.35f);
            if(source.clip!=entry.Clip){source.Stop();source.clip=entry.Clip;}
            if(!source.isPlaying){source.loop=true;source.Play();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                AudioPlaybackDiagnostics.ReturnedPlay(source,root,entry.Id,new AudioDiagnosticContext(AudioDiagnosticKind.Loop,root.Session.RunId,0,0,PlayerId));
#endif
            }
        }
        public void StopAudio(){if(idle!=null)idle.Stop();if(active!=null)active.Stop();blend=fade=load=replicaLoad=0;}
        private void OnDisable(){StopAudio();if(root!=null)root.Unregister(this);root=null;}
        private void OnDestroy(){if(idle!=null)Destroy(idle.gameObject);if(active!=null)Destroy(active.gameObject);}
    }
}
