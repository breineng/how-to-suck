using System;
using UnityEngine;
namespace HowToSuck
{
    // Owns guest view references only. Never enters IngestionService, LootRegistry or ProgressionService.
    public sealed class ReplicaIngestionBinding : IDisposable
    {
        private readonly SuckableObject item;
        private IngestionSnapshot current;
        public ReplicaIngestionBinding(SuckableObject value)
        { item=value!=null?value:throw new ArgumentNullException(nameof(value)); if(item.HasPhysicsAuthority)throw new InvalidOperationException("Replica binding on authority."); }
        public void Apply(IntakeReceiver receiver,int intake,int player,bool truck,double started,float duration,float size,
            Vector3 start,Quaternion rotation,Vector3 scale,Vector3 target,Quaternion targetRotation,Vector3 end,Func<double> clock)
        {
            if(item.HasPhysicsAuthority||item.State!=SuckableState.Ingesting||duration<=0||float.IsNaN(duration)||float.IsInfinity(duration))
                throw new InvalidOperationException("Invalid guest ingestion snapshot.");
            if(current==null||current.StartedAt!=started||current.IntakeId!=intake||current.Receiver!=receiver)
            {
                Dispose();
                current=new IngestionSnapshot(item,receiver,intake,player,truck,started,duration,size,start,rotation,scale,target,targetRotation,end,clock);
                item.Ingestion=current;
                if(receiver!=null)receiver.Attach(current);
            }
            current.TargetPosition=target;current.TargetRotation=targetRotation;current.EndPosition=end;
        }
        public void Dispose()
        {
            if(current!=null&&current.Receiver!=null)current.Receiver.Release(current);
            if(item!=null&&ReferenceEquals(item.Ingestion,current))item.Ingestion=null;
            current=null;
        }
    }
}
