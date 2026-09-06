#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;
namespace HowToSuck
{
    public readonly struct AppliedSuctionForce
    {
        public readonly string RunId;
        public readonly ulong Tick,TargetId;
        public readonly int Sequence,EmitterId,SourceInstanceId,TargetBodyInstanceId;
        public readonly Vector3 Force,Point;
        public readonly bool BodyActive;
        internal AppliedSuctionForce(string run,ulong tick,int sequence,int emitter,int source,ulong target,int body,Vector3 force,Vector3 point,bool active)
        {RunId=run;Tick=tick;Sequence=sequence;EmitterId=emitter;SourceInstanceId=source;TargetId=target;TargetBodyInstanceId=body;Force=force;Point=point;BodyActive=active;}
    }
    // Public consumers can read only value copies. Writers are confined to the runtime assembly.
    // Fixed storage avoids allocation and external code inside the physical force loop.
    public sealed class SuctionForceJournal
    {
        public const int Capacity=4096;
        private readonly AppliedSuctionForce[] records=new AppliedSuctionForce[Capacity];
        public string RunId {get;private set;}
        public ulong Tick {get;private set;}
        public int Count {get;private set;}
        public int Dropped {get;private set;}
        internal void BeginStep(string run,ulong tick){RunId=run;Tick=tick;Count=0;Dropped=0;}
        internal void Record(int emitter,int source,ulong target,int body,Vector3 force,Vector3 point,bool active)
        {
            if(Count==Capacity){if(Dropped<int.MaxValue)Dropped++;return;}
            records[Count]=new AppliedSuctionForce(RunId,Tick,Count+1,emitter,source,target,body,force,point,active);Count++;
        }
        public AppliedSuctionForce Read(int index)
        {if(index<0||index>=Count)throw new ArgumentOutOfRangeException(nameof(index));return records[index];}
    }
}
#endif
