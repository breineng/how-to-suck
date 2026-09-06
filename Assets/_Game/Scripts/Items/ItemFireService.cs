using System;
using System.Collections.Generic;
using UnityEngine;
namespace HowToSuck
{
    // AuthorityWorld is the only command/contact consumer. No Update/FixedUpdate or damage runs in this class independently.
    public sealed class ItemFireService
    {
        private const int MaximumQueuedContacts = 2048;
        private readonly LootRegistry registry;
        private readonly Func<bool> authorityRunning;
        private readonly Dictionary<int, ItemFireCommandGate> players = new Dictionary<int, ItemFireCommandGate>();
        private readonly Dictionary<ulong, Tracked> tracked = new Dictionary<ulong, Tracked>();
        private readonly Dictionary<ulong, Flight> flights = new Dictionary<ulong, Flight>();
        private readonly Queue<Contact> contacts = new Queue<Contact>();
        private readonly List<ulong> ended = new List<ulong>();
        private ulong nextShot;
        private ulong lifecycleRevision;
        private bool overflow;
        private double observedAt = double.NegativeInfinity;
        public string RunId { get; private set; }
        public bool IsRunning { get; private set; }
        public string LastFailure { get; private set; }
        public int QueueOverflowCount { get; private set; }
        public ulong LaunchCount { get; private set; }
        public int ActiveFlightCount => flights.Count;
        public int QueuedContactCount => contacts.Count;
        private sealed class Tracked
        {
            internal SuckableObject Item;
            internal ItemLaunchGeometry Geometry;
            internal ItemFireContactRelay Relay;
            internal bool CreatedRelay;
        }
        private sealed class Flight
        {
            internal SuckableObject Item;
            internal LootKey Key;
            internal ulong Shot;
            internal int Owner, Damage;
            internal double Expires;
            internal CollisionDetectionMode OriginalCcd;
        }
        private readonly struct Contact
        {
            internal readonly SuckableObject Item;
            internal readonly LootKey Key;
            internal readonly ulong Shot;
            internal readonly int Owner;
            internal readonly Collider Other;
            internal readonly float Speed;
            internal readonly Vector3 Point;
            internal Contact(SuckableObject item, ulong shot, int owner, Collider other, float speed, Vector3 point)
            { Item=item; Key=item.Key; Shot=shot; Owner=owner; Other=other; Speed=speed; Point=point; }
        }
        public ItemFireService(LootRegistry registry, Func<bool> authorityRunning)
        { this.registry = registry ?? throw new ArgumentNullException(nameof(registry)); this.authorityRunning = authorityRunning ?? throw new ArgumentNullException(nameof(authorityRunning)); }
        public void Begin(string runId)
        {
            if (!string.IsNullOrEmpty(RunId) || registry.RunId != runId || string.IsNullOrWhiteSpace(runId))
                throw new InvalidOperationException("Clear fire service, then begin the already registered run.");
            unchecked { lifecycleRevision++; }
            RunId=runId; nextShot=0; observedAt=double.NegativeInfinity; LastFailure=null; QueueOverflowCount=0; LaunchCount=0;
        }
        public void Track(SuckableObject item)
        {
            if (!Registered(item)) throw new InvalidOperationException("Track the original registered authority loot instance.");
            if (tracked.TryGetValue(item.InstanceId, out var existing))
            {
                if (existing.Item != item) throw new InvalidOperationException("Item ID was reused.");
                return;
            }
            if (!ItemLaunchGeometry.TryCapture(item, out var geometry, out string error)) throw new InvalidOperationException(error);
            var relay=item.GetComponent<ItemFireContactRelay>(); bool created=relay==null;
            if (created) relay=item.gameObject.AddComponent<ItemFireContactRelay>();
            if (!relay.enabled) throw new InvalidOperationException("The authority contact relay must be enabled.");
            relay.Bind(this,item);
            tracked.Add(item.InstanceId,new Tracked {Item=item,Geometry=geometry,Relay=relay,CreatedRelay=created});
        }
        public void RegisterPlayer(int id)
        {
            if (id<=0 || string.IsNullOrEmpty(RunId)) throw new InvalidOperationException("Register a real player in this run.");
            players.Add(id,new ItemFireCommandGate(RunId));
        }
        // Launched loot remains host owned after its originating player leaves.
        public void RemovePlayer(int id) => players.Remove(id);
        public void SetRunning(bool running)
        {
            if (running && (string.IsNullOrEmpty(RunId) || !authorityRunning())) throw new InvalidOperationException("Fire cannot start outside the active authority world.");
            if (IsRunning!=running) unchecked { lifecycleRevision++; }
            IsRunning=running;
            if (!running)
            {
                foreach (var f in flights.Values) EndFlight(f);
                flights.Clear(); contacts.Clear(); overflow=false;
            }
        }
        public bool TryFindSafePose(SuckableObject item, IReadOnlyList<Pose> callerCandidates, out Pose pose, out string error)
        {
            pose=default; error="Item was not captured in this authority run.";
            return Registered(item) && tracked.TryGetValue(item.InstanceId,out var t) && t.Item==item &&
                t.Geometry.TryFindSafePose(callerCandidates,out pose,out error);
        }
        public bool ProcessPlayer(int id, PlayerIntent intent, PlayerMotor motor, PlayerStorage storage,
            VacuumEmitter source, double now, bool gameplayAllowed)
        {
            if (!Ready(now) || !players.TryGetValue(id,out var gate)) return false;
            // A newer counter is consumed before any remaining rejection, including an unavailable/disabled player.
            if (!gate.Consume(intent.RunId,intent.FirePressSequence,now,intent.SuppressFire)) return false;
            if (!gameplayAllowed || !intent.IsFinite || motor==null || !motor.isActiveAndEnabled || !motor.HasMovementAuthority ||
                motor.PlayerId!=id || !motor.NozzlePoseValid || motor.NozzleAnchor==null || source==null ||
                !source.isActiveAndEnabled || source.Definition==null || source.Source!=motor.NozzleAnchor || !source.HasClearSourcePath() ||
                storage==null || storage.RunId!=RunId || storage.OwnerId!=id || storage.IsDetached ||
                !storage.TryPeek(out var key,out var first) || !Registered(first) || first.WorldFrozen || first.Body==null ||
                !tracked.TryGetValue(first.InstanceId,out var t) || t.Item!=first) return false;
            var receiver=motor.GetComponent<IntakeReceiver>();
            if (receiver==null || receiver.IsTruck || receiver.Storage!=storage || !ItemFireRules.Finite(receiver.AdmissionRadius) || receiver.AdmissionRadius<=0 || !Finite(receiver.Position)) return false;
            float frontDistance=Mathf.Max(.025f,Vector3.Dot(receiver.Position-motor.NozzleAnchor.position,motor.NozzleAnchor.forward)+receiver.AdmissionRadius+.025f);
            if (!t.Geometry.TryLaunchPose(motor.NozzleAnchor,motor.transform,frontDistance,out var pose,out string error))
            { LastFailure=error; return false; }
            if (nextShot==ulong.MaxValue) { LastFailure="Shot identity exhausted."; return false; }
            float mass=first.Body.mass;
            if (!ItemFireRules.Finite(mass) || mass<=0) { LastFailure="Invalid physical item mass."; return false; }
            var flight=new Flight {Item=first,Key=key,Shot=nextShot+1,Owner=id,Damage=ItemFireRules.Damage(mass),
                Expires=now+ItemFireRules.FlightLifetime,OriginalCcd=first.Body.collisionDetectionMode};
            // Speculative CCD supports the captured convex/primitive compound and the Stored kinematic state.
            first.Body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
            if (!storage.TryReleaseFirst(key,pose.position,pose.rotation,flight.Shot,out var released))
            { first.Body.collisionDetectionMode=flight.OriginalCcd; return false; }
            // No callbacks intervene between checked FIFO release and this launch of the exact same Rigidbody.
            released.Body.linearVelocity=motor.NozzleAnchor.forward*ItemFireRules.LaunchSpeed;
            nextShot=flight.Shot; flights.Add(first.InstanceId,flight); gate.CommitLaunch(now); LaunchCount++;
            return true;
        }
        internal void EnqueueContact(SuckableObject item, ulong shot, int owner, Collider other, float speed, Vector3 point)
        {
            if (!IsRunning || !authorityRunning() || !Registered(item) || item.State!=SuckableState.InFlight ||
                item.ActiveShotId!=shot || item.ShotOwner!=owner || other==null) return;
            if (contacts.Count>=MaximumQueuedContacts) { overflow=true; return; }
            contacts.Enqueue(new Contact(item,shot,owner,other,speed,point));
        }
        public void StepContacts(double now)
        {
            if (!Ready(now)) return;
            // Expiry wins at equality; queued contacts cannot resurrect old damaging provenance.
            ended.Clear();
            foreach (var pair in flights)
                if (!Live(pair.Value) || now>=pair.Value.Expires || overflow)
                { EndFlight(pair.Value); ended.Add(pair.Key); }
            foreach (ulong id in ended) flights.Remove(id);
            if (overflow)
            {
                QueueOverflowCount++; LastFailure="Collision queue overflow: all active shots ended without damage.";
                overflow=false; contacts.Clear(); return;
            }
            ulong batchRevision=lifecycleRevision;
            while (contacts.Count>0 && IsRunning && authorityRunning() && lifecycleRevision==batchRevision)
            {
                var c=contacts.Dequeue();
                if (!flights.TryGetValue(c.Key.InstanceId,out var f) || !Live(f) || c.Item!=f.Item || !c.Key.Equals(f.Key) ||
                    c.Shot!=f.Shot || c.Owner!=f.Owner) continue;
                // Process callback arrival order. A world contact before an enemy contact ends this shot.
                // A destroyed target still represents a physical contact; it ends flight without damage.
                try
                {
                    var receiver=FindReceiver(c.Other);
                    if (receiver!=null && ItemFireRules.CanDamage(f.Item.CargoRole==CargoRole.OrdinaryLoot,now,f.Expires,c.Speed) && Finite(c.Point))
                    {
                        var hit=new ItemHitContext(f.Item,f.Shot,f.Owner,c.Speed,f.Damage,c.Point,now);
                        bool applied=receiver.TryApplyItemHit(in hit);
                        if (lifecycleRevision==batchRevision && applied != (f.Item!=null && f.Item.State==SuckableState.Spent))
                            throw new InvalidOperationException("Item damage receiver result disagrees with its atomic Spent commit.");
                    }
                }
                finally
                {
                    // Includes ineffective hits, walls, ordinary props, players and boss cargo. Preserve physical bounce.
                    EndFlight(f);
                    if (flights.TryGetValue(c.Key.InstanceId,out var current) && ReferenceEquals(current,f)) flights.Remove(c.Key.InstanceId);
                }
            }
        }
        private bool Ready(double now)
        {
            if (!IsRunning || !authorityRunning() || registry.RunId!=RunId || !ItemFireRules.Finite(now) || now<observedAt) return false;
            observedAt=now; return true;
        }
        private bool Registered(SuckableObject item) => item!=null && item.HasPhysicsAuthority && item.RunId==RunId &&
            registry.RunId==RunId && registry.Items.TryGetValue(item.InstanceId,out var actual) && actual==item;
        private bool Live(Flight f) => Registered(f.Item) && f.Item.isActiveAndEnabled &&
            tracked.TryGetValue(f.Key.InstanceId,out var t) && t.Item==f.Item && t.Relay!=null && t.Relay.isActiveAndEnabled &&
            f.Item.Key.Equals(f.Key) && !f.Item.WorldFrozen &&
            f.Item.State==SuckableState.InFlight && f.Item.ActiveShotId==f.Shot && f.Item.ShotOwner==f.Owner;
        private static IItemDamageReceiver FindReceiver(Collider collider)
        {
            if (collider==null || collider.GetComponentInParent<PlayerMotor>()!=null) return null;
            foreach (var component in collider.GetComponentsInParent<MonoBehaviour>(true))
                if (component!=null && component.isActiveAndEnabled && component is IItemDamageReceiver receiver) return receiver;
            return null;
        }
        private static bool Finite(Vector3 v) => ItemFireRules.Finite(v.x)&&ItemFireRules.Finite(v.y)&&ItemFireRules.Finite(v.z);
        private static void EndFlight(Flight f)
        {
            var item=f.Item;
            if (item==null) return;
            // A callback may have ended this lifecycle; never restore an old CCD lease over a newer shot.
            if (item.State==SuckableState.InFlight && item.ActiveShotId!=0 && item.ActiveShotId!=f.Shot) return;
            if (item.State==SuckableState.InFlight && item.ActiveShotId==f.Shot)
            {
                if (!item.TryTransition(SuckableState.InFlight,SuckableState.Available)) item.CancelFlightProvenance();
            }
            if (item.Body!=null) item.Body.collisionDetectionMode=f.OriginalCcd;
        }
        public void Clear()
        {
            SetRunning(false);
            unchecked { lifecycleRevision++; }
            foreach (var t in tracked.Values)
                if (t.Relay!=null) { t.Relay.Unbind(this); if (t.CreatedRelay) UnityEngine.Object.Destroy(t.Relay); }
            tracked.Clear(); players.Clear(); RunId=null; nextShot=0; observedAt=double.NegativeInfinity;
        }
    }
}
