using System;
using UnityEngine;
using UnityEngine.Audio;
namespace HowToSuck.Audio
{
    [CreateAssetMenu(menuName="How to Suck/Audio Bindings")]
    public sealed class AudioBindings:ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            public SfxId Id; public AudioClip Clip; public ClipReviewStatus Status;
            [Range(0,1)] public float Gain=.2f;
            public AudioBus Bus; [Range(0,256)] public int Priority=128;
            public float MinDistance=1,MaxDistance=12; public bool Loop;
        }
        public Entry[] Entries=Array.Empty<Entry>();
        public AudioMixer Mixer;
        public AudioMixerGroup Vacuum,Truck,Impacts,UI;
        public Entry Find(SfxId id)
        {foreach(var entry in Entries)if(entry!=null&&entry.Id==id)return entry;return null;}
        public bool Available(SfxId id)
        {var entry=Find(id);return entry!=null&&entry.Status!=ClipReviewStatus.PendingReplacement&&entry.Clip!=null;}
        public AudioMixerGroup Group(AudioBus bus)=>bus==AudioBus.Vacuum?Vacuum:bus==AudioBus.Truck?Truck:bus==AudioBus.Impacts?Impacts:UI;
        public bool Validate(out string error)
        {
            if(Mixer==null||Vacuum==null||Truck==null||Impacts==null||UI==null){error="Bind MainMixer and its four category groups.";return false;}
            var seen=new bool[19];
            foreach(var entry in Entries)
            {
                if(entry==null||(int)entry.Id>=19||!Enum.IsDefined(typeof(ClipReviewStatus),entry.Status)||!Enum.IsDefined(typeof(AudioBus),entry.Bus)||entry.Bus==AudioBus.Master||seen[(int)entry.Id]){error="Audio IDs must occur exactly once.";return false;}
                seen[(int)entry.Id]=true;
                if(entry.Status==ClipReviewStatus.PendingReplacement)
                {if(entry.Clip!=null){error="Pending sound must have no clip: "+entry.Id;return false;}continue;}
                if(entry.Clip==null||entry.Clip.channels!=1||entry.Clip.frequency!=48000||entry.Gain<0||entry.Gain>1||float.IsNaN(entry.Gain)||float.IsInfinity(entry.Gain)||float.IsNaN(entry.MinDistance)||float.IsInfinity(entry.MinDistance)||float.IsNaN(entry.MaxDistance)||float.IsInfinity(entry.MaxDistance)||entry.Priority<0||entry.Priority>256||entry.MinDistance<=0||entry.MaxDistance<=entry.MinDistance)
                {error="Require mono48k candidate and valid gain/distances: "+entry.Id;return false;}
            }
            foreach(bool present in seen)if(!present){error="All19 IDs including pending entries are required.";return false;}
            error=null;return true;
        }
    }
}
