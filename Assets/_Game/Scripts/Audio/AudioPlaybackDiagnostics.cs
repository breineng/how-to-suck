#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;
namespace HowToSuck.Audio
{
    public enum AudioDiagnosticKind:byte { Generic,Ingestion,Impact,Loop }
    public struct AudioDiagnosticContext
    {
        public AudioDiagnosticKind Kind;public string Run;public ulong Instance,ImpactSequence;public int Player;
        public AudioDiagnosticContext(AudioDiagnosticKind kind,string run,ulong instance=0,ulong impactSequence=0,int player=0)
        {Kind=kind;Run=run;Instance=instance;ImpactSequence=impactSequence;Player=player;}
    }
    [Serializable] public struct AudioPlaybackWitness
    {
        public ulong serial,instance,impactSequence;public string run,clip;public int rootId,sourceId,frame,player,id,kind;
        public bool authority,loop;public double dspTime;public Vector3 position;
    }
    // Diagnostic value-copy notification after the existing real Play call returned.
    // No source/clip/physics references leave this producer; no logging, playback or game decisions here.
    public static class AudioPlaybackDiagnostics
    {
        public static event Action<AudioPlaybackWitness> Played;
        private static ulong serial;
        public static int ObserverErrors {get;private set;}
        public static void ReturnedPlay(AudioSource source,GameAudioRoot root,SfxId id,AudioDiagnosticContext context)
        {
            var observer=Played;if(observer==null)return;
            try
            {
                if(serial==ulong.MaxValue){ObserverErrors++;return;}
                observer(new AudioPlaybackWitness{serial=++serial,instance=context.Instance,impactSequence=context.ImpactSequence,
                    run=context.Run??root.Session.RunId,clip=source.clip!=null?source.clip.name:"",rootId=root.GetInstanceID(),sourceId=source.GetInstanceID(),
                    frame=Time.frameCount,player=context.Player,id=(int)id,kind=(int)context.Kind,authority=root.Session.HasAuthority,loop=source.loop,
                    dspTime=AudioSettings.dspTime,position=source.transform.position});
            }
            catch(Exception){ObserverErrors++;} // Observer failure cannot affect a real Play/physics caller.
        }
    }
}
#endif
