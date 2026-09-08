using UnityEngine;
namespace HowToSuck.Audio
{
    // Outlives the entry -> lobby -> entry root replacement so menu navigation
    // does not restart the song. It releases its sound on entering a contract.
    [DisallowMultipleComponent]
    public sealed class MenuMusicPlayer:MonoBehaviour
    {
        static MenuMusicPlayer instance;AudioSource source;float fade;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetLifetime(){instance=null;}
        public static void Ensure()
        {
            if(instance!=null)return;
            var go=new GameObject("Menu music");go.hideFlags=HideFlags.DontSave;
            DontDestroyOnLoad(go);instance=go.AddComponent<MenuMusicPlayer>();
            instance.source=GameAudioRoot.CreateSource(go.transform,"Menu theme");instance.source.loop=true;instance.source.spatialBlend=0;
        }
        void Update()
        {
            var root=GameAudioRoot.Current;var entry=root!=null?root.Bindings?.Find(SfxId.MenuMusic):null;
            bool menu=root!=null&&root.isActiveAndEnabled&&root.Session!=null&&
                (root.Session.Phase==SessionPhase.Booting||root.Session.Phase==SessionPhase.Lobby)&&entry?.Clip!=null;
            fade=Mathf.MoveTowards(fade,menu?1:0,Time.unscaledDeltaTime/(menu?.8f:.3f));
            if(entry?.Clip!=null){source.outputAudioMixerGroup=root.Bindings.Group(entry.Bus);if(source.clip!=entry.Clip){source.clip=entry.Clip;source.Stop();}}
            source.volume=fade*(entry!=null?entry.Gain:0)*(root!=null?root.SourceBusGain(AudioBus.UI):0);
            if(menu&&!source.isPlaying)source.Play();else if(!menu&&fade<=0&&source.isPlaying)source.Pause();
        }
        void OnDestroy(){if(instance==this)instance=null;}
    }
}
