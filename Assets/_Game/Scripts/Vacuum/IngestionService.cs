using System;
using System.Collections.Generic;
using UnityEngine;
namespace HowToSuck
{
    public sealed class IngestionService
    {
        private readonly LootRegistry registry;
        private readonly SuctionSystem suction;
        private readonly List<IngestionSnapshot> active=new List<IngestionSnapshot>();
        private readonly List<Request> requests=new List<Request>();
        private readonly HashSet<ulong> completed=new HashSet<ulong>();
        private readonly List<DeliveryRecord> records=new List<DeliveryRecord>();
        private Collider[] nearby=new Collider[64];
        private readonly HashSet<Rigidbody> seen=new HashSet<Rigidbody>();
        private string runId;
        private bool running;
        private ulong generation;
        private ulong audioAdmissionOccurrence;
        public int ActiveCount=>active.Count;
        public IReadOnlyList<DeliveryRecord> Records=>records.AsReadOnly();
        public event Action<DeliveryRecord> Delivered;
        public event Action<StoredRecord> Stored;
        private readonly struct Request
        {
            public readonly IntakeReceiver Receiver;public readonly SuckableObject Item;public readonly float Distance;
            public Request(IntakeReceiver receiver,SuckableObject item,float distance){Receiver=receiver;Item=item;Distance=distance;}
        }
        public IngestionService(LootRegistry loot,SuctionSystem field,Action<GameObject> despawnObject)
        {registry=loot??throw new ArgumentNullException(nameof(loot));suction=field??throw new ArgumentNullException(nameof(field));if(despawnObject==null)throw new ArgumentNullException(nameof(despawnObject));}
        public void Begin(string id){if(!new LootKey(id,1).IsValid || registry.RunId!=id)throw new ArgumentException("Begin requires the current registry run.",nameof(id));generation++;CancelAll();runId=id;completed.Clear();records.Clear();running=false;audioAdmissionOccurrence=0;}
        public void SetRunning(bool value){generation++;running=value;if(!value)CancelAll();}
        public void Clear(){SetRunning(false);runId=null;completed.Clear();records.Clear();}
        // Legacy call retains complete-then-admit behavior for existing fixtures.
        public void Step(double now,IReadOnlyList<IntakeReceiver> receivers)
        {
            ulong before = generation;
            CompleteDue(now);
            if (generation == before) Admit(now, receivers);
        }
        public void CompleteDue(double now)
        {
            if(!running || string.IsNullOrEmpty(runId) || registry.RunId!=runId || double.IsNaN(now) || double.IsInfinity(now))return;
            ulong currentGeneration=generation;
            // Dead/detached receivers cancel even before the visual duration expires.
            for(int i=active.Count-1;i>=0;i--)
                if(!ReceiverStillOwns(active[i])) { var invalid=active[i]; active.RemoveAt(i); Cancel(invalid); }
            // Completion order defines FIFO; ties use stable authored receiver/item identities.
            active.Sort((a,b)=>{int c=(a.StartedAt+a.Duration).CompareTo(b.StartedAt+b.Duration); if(c!=0)return c;
                c=a.IntakeId.CompareTo(b.IntakeId);return c!=0?c:a.InstanceId.CompareTo(b.InstanceId);});
            while(active.Count>0)
            {
                var snapshot=active[0];
                if(snapshot.Receiver.Target!=null || snapshot.Receiver.Emitter!=null)
                {snapshot.TargetPosition=snapshot.Receiver.Position;snapshot.TargetRotation=snapshot.Receiver.Rotation;snapshot.EndPosition=snapshot.Receiver.EndPosition;}
                if(now<snapshot.StartedAt+snapshot.Duration)break;
                active.RemoveAt(0);Finish(snapshot);
                if(!running || generation!=currentGeneration)return;
            }
            // Continue following moving targets for every accepted item, including later completions.
            foreach(var snapshot in active)
            {snapshot.TargetPosition=snapshot.Receiver.Position;snapshot.TargetRotation=snapshot.Receiver.Rotation;snapshot.EndPosition=snapshot.Receiver.EndPosition;}
        }

        public void Admit(double now,IReadOnlyList<IntakeReceiver> receivers)
        {
            if(!running || string.IsNullOrEmpty(runId) || registry.RunId!=runId || double.IsNaN(now) || double.IsInfinity(now))return;
            if(receivers==null)throw new ArgumentNullException(nameof(receivers));
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
                    if(item==null || !Loose(item) || item.WorldFrozen || item.RunId!=runId || !receiver.Accepts(item))continue;
                    if(suction.TryFindIntakeSurface(receiver.Emitter,item,receiver.Position,receiver.AdmissionRadius,out var point))
                        requests.Add(new Request(receiver,item,Vector3.Distance(point,receiver.Position)));
                }
            }
            requests.Sort((a,b)=>{int result=a.Distance.CompareTo(b.Distance);if(result!=0)return result;result=a.Receiver.IntakeId.CompareTo(b.Receiver.IntakeId);return result!=0?result:a.Item.InstanceId.CompareTo(b.Item.InstanceId);});
            foreach(var request in requests)TryBegin(request.Item,request.Receiver,now);
        }
        private bool TryBegin(SuckableObject item,IntakeReceiver receiver,double now)
        {
            if(!running || item==null || receiver==null || !receiver.CanAdmit || !receiver.Accepts(item) ||
                !Loose(item) || item.WorldFrozen || item.RunId!=runId || item.InstanceId==0 ||
                !registry.Items.TryGetValue(item.InstanceId,out var registered) || registered!=item || completed.Contains(item.InstanceId))return false;
            if(item.CargoRole==CargoRole.OrdinaryLoot ? item.Value<=0 : item.CargoRole!=CargoRole.BossBody || item.Value!=0 || !item.BossKey.IsValid || item.BossKey.RunId!=runId)return false;
            if(!receiver.IsTruck && (receiver.Storage.RunId!=runId || string.IsNullOrWhiteSpace(receiver.Emitter.Definition.TierId)))return false;
            if(!suction.TryFindIntakeSurface(receiver.Emitter,item,receiver.Position,receiver.AdmissionRadius,out _))return false;
            // State and slot are committed synchronously, before processing the next request.
            var snapshot=new IngestionSnapshot(item,receiver,now);
            if(!snapshot.IsTruck && (snapshot.Storage==null || !snapshot.Storage.Reserve(snapshot)))return false;
            if(!item.TryTransition(item.State,SuckableState.Ingesting)) { snapshot.Storage?.Cancel(snapshot); return false; }
            receiver.Current=snapshot;item.Ingestion=snapshot;active.Add(snapshot);
            // Cosmetic fact only after the real slot/state/job commit; exhausted audio IDs never reject gameplay.
            if(audioAdmissionOccurrence<ulong.MaxValue)Audio.CommittedAudioEvents.Publish(new Audio.CommittedAudioFact(
                runId,Audio.CommittedAudioKind.Ingestion,++audioAdmissionOccurrence,item.InstanceId,0,snapshot.PlayerId,snapshot.IntakeId,
                snapshot.IsTruck,now,snapshot.RequiredSize,0,snapshot.TargetPosition,snapshot.Duration));
            return true;
        }
        private static bool Loose(SuckableObject item) => item.State==SuckableState.Available || item.State==SuckableState.InFlight;
        private bool Registered(SuckableObject item) => item!=null && item.RunId==runId && registry.Items.TryGetValue(item.InstanceId,out var found) && found==item;
        private static bool ReceiverStillOwns(IngestionSnapshot snapshot)
        {
            var receiver=snapshot.Receiver;
            return snapshot.Item!=null && ReferenceEquals(snapshot.Item.Ingestion,snapshot) && receiver!=null && receiver.isActiveAndEnabled && receiver.Current==snapshot && receiver.IsTruck==snapshot.IsTruck &&
                receiver.IntakeId==snapshot.IntakeId && receiver.PlayerId==snapshot.PlayerId && receiver.Emitter!=null &&
                receiver.Emitter.isActiveAndEnabled && receiver.Emitter.Definition!=null &&
                (snapshot.IsTruck || snapshot.Storage!=null && !snapshot.Storage.IsDetached && receiver.Storage==snapshot.Storage);
        }
        private void Finish(IngestionSnapshot snapshot)
        {
            var item=snapshot.Item;
            if(!Registered(item) || !ReceiverStillOwns(snapshot) || item.State!=SuckableState.Ingesting) { Cancel(snapshot); return; }
            if(!snapshot.IsTruck)
            {
                if(!snapshot.Storage.Commit(snapshot)) { Cancel(snapshot); return; }
                ReleaseSlot(snapshot); Stored?.Invoke(new StoredRecord(snapshot)); return;
            }
            if(completed.Contains(snapshot.InstanceId) || !item.TryTransition(SuckableState.Ingesting,SuckableState.Delivered)) { Cancel(snapshot); return; }
            completed.Add(snapshot.InstanceId);
            var record=new DeliveryRecord(snapshot); records.Add(record); ReleaseSlot(snapshot);
            // Keep the same registered terminal instance until world teardown. No private-intake money/despawn path.
            Delivered?.Invoke(record);
        }
        private static void ReleaseSlot(IngestionSnapshot snapshot)
        {
            if(snapshot.Receiver!=null && ReferenceEquals(snapshot.Receiver.Current,snapshot))snapshot.Receiver.Current=null;
            if(snapshot.Item!=null && ReferenceEquals(snapshot.Item.Ingestion,snapshot))snapshot.Item.Ingestion=null;
        }
        private void Cancel(IngestionSnapshot snapshot)
        {
            bool ownsItem=snapshot.Item!=null && ReferenceEquals(snapshot.Item.Ingestion,snapshot);
            snapshot.Storage?.Cancel(snapshot); ReleaseSlot(snapshot);
            if(ownsItem && Registered(snapshot.Item)) snapshot.Item.TryTransition(SuckableState.Ingesting,SuckableState.Available);
        }
        // Returned accepted items are back at their unchanged physical roots. The session may relocate them
        // only through a separately checked safe-drop operation; cancellation itself never invents geometry.
        public IReadOnlyList<SuckableObject> CancelOwner(int ownerId)
        {
            generation++;
            var returned=new List<SuckableObject>();
            for(int i=active.Count-1;i>=0;i--)
                if(!active[i].IsTruck && active[i].PlayerId==ownerId)
                {var snapshot=active[i];active.RemoveAt(i);Cancel(snapshot);if(Registered(snapshot.Item))returned.Add(snapshot.Item);}
            return returned.AsReadOnly();
        }
        public void CancelReceiver(IntakeReceiver receiver)
        {
            generation++;
            for(int i=active.Count-1;i>=0;i--)
                if(ReferenceEquals(active[i].Receiver,receiver)) {var snapshot=active[i];active.RemoveAt(i);Cancel(snapshot);}
        }
        private void CancelAll()
        {
            foreach(var snapshot in active)Cancel(snapshot);
            active.Clear();requests.Clear();
        }
    }
}
