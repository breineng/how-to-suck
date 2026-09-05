using System;
using System.Collections.Generic;
using UnityEngine;
namespace HowToSuck
{
    public sealed class IngestionService
    {
        private readonly LootRegistry registry;
        private readonly SuctionSystem suction;
        private readonly Action<GameObject> despawn;
        private readonly List<IngestionSnapshot> active=new List<IngestionSnapshot>();
        private readonly List<Request> requests=new List<Request>();
        private readonly HashSet<ulong> completed=new HashSet<ulong>();
        private readonly List<CollectionRecord> records=new List<CollectionRecord>();
        private Collider[] nearby=new Collider[64];
        private readonly HashSet<Rigidbody> seen=new HashSet<Rigidbody>();
        private string runId;
        private bool running;
        private ulong generation;
        public int ActiveCount=>active.Count;
        public IReadOnlyList<CollectionRecord> Records=>records.AsReadOnly();
        public event Action<CollectionRecord> Collected;
        private readonly struct Request
        {
            public readonly IntakeReceiver Receiver;public readonly SuckableObject Item;public readonly float Distance;
            public Request(IntakeReceiver receiver,SuckableObject item,float distance){Receiver=receiver;Item=item;Distance=distance;}
        }
        public IngestionService(LootRegistry loot,SuctionSystem field,Action<GameObject> despawnObject)
        {registry=loot??throw new ArgumentNullException(nameof(loot));suction=field??throw new ArgumentNullException(nameof(field));despawn=despawnObject??throw new ArgumentNullException(nameof(despawnObject));}
        public void Begin(string id){generation++;CancelAll();runId=id;completed.Clear();records.Clear();running=false;}
        public void SetRunning(bool value){generation++;running=value;if(!value)CancelAll();}
        public void Clear(){SetRunning(false);runId=null;completed.Clear();records.Clear();}
        public void Step(double now,IReadOnlyList<IntakeReceiver> receivers)
        {
            if(!running || string.IsNullOrEmpty(runId) || registry.RunId!=runId || double.IsNaN(now) || double.IsInfinity(now))return;
            ulong currentGeneration=generation;
            // Finish accepted work even if its player/receiver has since disconnected.
            for(int i=active.Count-1;i>=0;i--)
            {
                var snapshot=active[i];
                if(snapshot.Receiver!=null && (snapshot.Receiver.Target!=null || snapshot.Receiver.Emitter!=null))
                {snapshot.TargetPosition=snapshot.Receiver.Position;snapshot.TargetRotation=snapshot.Receiver.Rotation;snapshot.EndPosition=snapshot.Receiver.EndPosition;}
                if(now>=snapshot.StartedAt+snapshot.Duration){active.RemoveAt(i);Finish(snapshot);if(!running || generation!=currentGeneration)return;}
            }
            requests.Clear();
            foreach(var receiver in receivers)
            {
                if(receiver==null || !receiver.CanAdmit)continue;
                int count;
                while(true){count=Physics.OverlapSphereNonAlloc(receiver.Position,receiver.AdmissionRadius,nearby,LayerMask.GetMask("Items"),QueryTriggerInteraction.Ignore);if(count<nearby.Length)break;Array.Resize(ref nearby,nearby.Length*2);if(nearby.Length>8192)throw new InvalidOperationException("Intake overlap exceeds scene budget.");}
                seen.Clear();
                for(int index=0;index<count;index++)
                {
                    var body=nearby[index].attachedRigidbody;if(body==null || !seen.Add(body))continue;
                    var item=body.GetComponent<SuckableObject>();
                    if(item==null || item.State!=SuckableState.Available || item.WorldFrozen || item.RunId!=runId || !receiver.Accepts(item.Definition))continue;
                    if(suction.TryFindIntakeSurface(receiver.Emitter,item,receiver.Position,receiver.AdmissionRadius,out var point))
                        requests.Add(new Request(receiver,item,Vector3.Distance(point,receiver.Position)));
                }
            }
            requests.Sort((a,b)=>{int result=a.Distance.CompareTo(b.Distance);if(result!=0)return result;result=a.Receiver.IntakeId.CompareTo(b.Receiver.IntakeId);return result!=0?result:a.Item.InstanceId.CompareTo(b.Item.InstanceId);});
            foreach(var request in requests)TryBegin(request.Item,request.Receiver,now);
        }
        private bool TryBegin(SuckableObject item,IntakeReceiver receiver,double now)
        {
            if(!running || item==null || receiver==null || !receiver.CanAdmit || !receiver.Accepts(item.Definition) ||
                item.State!=SuckableState.Available || item.WorldFrozen || item.RunId!=runId || item.InstanceId==0 ||
                !registry.Items.TryGetValue(item.InstanceId,out var registered) || registered!=item || completed.Contains(item.InstanceId))return false;
            if(!suction.TryFindIntakeSurface(receiver.Emitter,item,receiver.Position,receiver.AdmissionRadius,out _))return false;
            // State and slot are committed synchronously, before processing the next request.
            var snapshot=new IngestionSnapshot(item,receiver,now);
            if(!item.TryTransition(SuckableState.Available,SuckableState.Ingesting))return false;
            receiver.Current=snapshot;item.Ingestion=snapshot;active.Add(snapshot);return true;
        }
        private void Finish(IngestionSnapshot snapshot)
        {
            ReleaseSlot(snapshot);
            var item=snapshot.Item;
            if(item==null || item.RunId!=runId || !item.TryTransition(SuckableState.Ingesting,SuckableState.Collected) || !completed.Add(snapshot.InstanceId))return;
            var record=new CollectionRecord(snapshot);records.Add(record);
            registry.Unregister(item);
            try{Collected?.Invoke(record);}finally{despawn(item.gameObject);}
        }
        private static void ReleaseSlot(IngestionSnapshot snapshot)
        {
            if(snapshot.Receiver!=null && snapshot.Receiver.Current==snapshot)snapshot.Receiver.Current=null;
            if(snapshot.Item!=null)snapshot.Item.Ingestion=null;
        }
        private void CancelAll()
        {
            foreach(var snapshot in active)
            {
                ReleaseSlot(snapshot);
                var item=snapshot.Item;
                if(item==null)continue;
                item.TryTransition(SuckableState.Ingesting,SuckableState.Lost);
                registry.Unregister(item);despawn(item.gameObject);
            }
            active.Clear();requests.Clear();
        }
    }
}