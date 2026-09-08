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
        private readonly Queue<Contact> sweptContacts = new Queue<Contact>();
        private readonly List<ulong> ended = new List<ulong>();
        private readonly List<Flight> captureCandidates = new List<Flight>();
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
        public TruckIntake Truck { get; set; }
        public IngestionService Ingestion { get; set; }
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
            internal Vector3 PreviousPosition,PreviousCenter;
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
                flights.Clear(); contacts.Clear(); sweptContacts.Clear(); overflow=false;
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
            bool canCharge = gameplayAllowed && intent.IsFinite && motor != null && !motor.IsDowned &&
                storage != null && storage.TryPeek(out _, out _);
            bool trigger = gate.TryTrigger(intent.RunId,intent.FirePressSequence,intent.FireHeld,now,
                intent.SuppressFire || !canCharge,out float charge);
            if (motor != null) motor.PresentFireCharge(gate.ChargeAmount(now));
            if (!trigger) return false;
            if (!gameplayAllowed || !intent.IsFinite || motor==null || !motor.isActiveAndEnabled || !motor.HasMovementAuthority ||
                motor.PlayerId!=id || !motor.NozzlePoseValid || motor.NozzleAnchor==null || source==null ||
                !source.isActiveAndEnabled || source.Definition==null || source.Source!=motor.NozzleAnchor ||
                storage==null || storage.RunId!=RunId || storage.OwnerId!=id || storage.IsDetached ||
                !storage.TryPeek(out var key,out var first) || !Registered(first) || first.WorldFrozen || first.Body==null ||
                !tracked.TryGetValue(first.InstanceId,out var t) || t.Item!=first) return false;
            var receiver=motor.GetComponent<IntakeReceiver>();
            if (receiver==null || receiver.IsTruck || receiver.Storage!=storage || !ItemFireRules.Finite(receiver.AdmissionRadius) || receiver.AdmissionRadius<=0 || !Finite(receiver.Position)) return false;
            if(!source.HasClearSourcePath())
            { LastFailure="Physical nozzle path blocked.";motor.ApplyFireFeedback(intent.FirePressSequence,true);return false; }
            float frontDistance=Mathf.Max(.025f,Vector3.Dot(receiver.Position-motor.NozzleAnchor.position,motor.NozzleAnchor.forward)+receiver.AdmissionRadius+.025f);
            if (!t.Geometry.TryLaunchPose(motor.NozzleAnchor,motor.transform,frontDistance,out var pose,out string error))
            {
                LastFailure=error;
                if(error!=null&&(error.StartsWith("Endpoint blocked by ",StringComparison.Ordinal)||
                    error.StartsWith("Launch origin envelope blocked by ",StringComparison.Ordinal)||
                    error.StartsWith("Launch path blocked by ",StringComparison.Ordinal)))
                    motor.ApplyFireFeedback(intent.FirePressSequence,true);
                return false;
            }
            if (nextShot==ulong.MaxValue) { LastFailure="Shot identity exhausted."; return false; }
            float mass=first.Body.mass;
            if (!ItemFireRules.Finite(mass) || mass<=0) { LastFailure="Invalid physical item mass."; return false; }
            var aim=motor.AimRay;
            Vector3 aimPoint=aim.GetPoint(35);
            if(Physics.Raycast(aim,out var aimHit,35,LayerMask.GetMask("World","Items","Enemies"),QueryTriggerInteraction.Ignore))aimPoint=aimHit.point;
            Vector3 direction=aimPoint-pose.position;
            direction=Vector3.Dot(direction,aim.direction)>.1f?direction.normalized:aim.direction;
            // Decide while the stored cargo is still non-solid, before its own
            // released collider can occlude the crosshair ray.
            int directedIntake=DirectedTruck(aim.origin,aim.direction)?Truck.Receiver.IntakeId:0;
            var flight=new Flight {Item=first,Key=key,Shot=nextShot+1,Owner=id,Damage=ItemFireRules.Damage(mass,charge),
                Expires=now+ItemFireRules.FlightLifetime,OriginalCcd=first.Body.collisionDetectionMode,PreviousPosition=pose.position};
            if (!storage.TryReleaseFirst(key,pose.position,pose.rotation,flight.Shot,out var released))
                return false;
            // Primitive cargo uses a swept time-of-impact test. Speculative CCD
            // expands the broad phase in every direction and can deflect a fast
            // centre-doorway shot against distant jambs. Convex meshes retain the
            // supported fallback; the contact relay filters unrealized predictions.
            released.Body.collisionDetectionMode=t.Geometry.SupportsSweptCcd?
                CollisionDetectionMode.ContinuousDynamic:CollisionDetectionMode.ContinuousSpeculative;
            // No callbacks intervene between checked FIFO release and this launch of the exact same Rigidbody.
            released.Body.maxLinearVelocity=Mathf.Max(released.Body.maxLinearVelocity,ItemFireRules.MaximumLaunchSpeed);
            released.Body.linearVelocity=direction*ItemFireRules.Speed(charge);
            flight.PreviousCenter=released.Body.worldCenterOfMass;
            // Intent comes from the crosshair ray; delivery assists only this deliberately aimed shot.
            released.DirectedIntakeId=directedIntake;
            nextShot=flight.Shot; flights.Add(first.InstanceId,flight); gate.CommitLaunch(now); LaunchCount++;
            motor.ApplyFireFeedback(intent.FirePressSequence,false);
            Audio.CommittedAudioEvents.Publish(new Audio.CommittedAudioFact(RunId,Audio.CommittedAudioKind.ShotLaunch,
                flight.Shot,first.InstanceId,0,id,0,false,now,0,flight.Damage,motor.NozzleAnchor.position));
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
            // Sweep the actual travelled centre segment before consuming contact callbacks. This catches thin enemies
            // crossed by fast compound cargo without letting a cosmetic/ground contact erase an already physical hit.
            foreach(var f in flights.Values)
            {
                if(!Live(f))continue;
                var position=f.Item.Body.position;var delta=position-f.PreviousPosition;
                if(delta.sqrMagnitude>.00001f)
                {
                    float radius=Mathf.Clamp(f.Item.RequiredIntakeSize*.18f,.035f,.18f);
                    var hits=Physics.SphereCastAll(f.PreviousPosition,radius,delta.normalized,delta.magnitude,
                        LayerMask.GetMask("World","Enemies"),QueryTriggerInteraction.Ignore);
                    Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
                    foreach(var hit in hits)
                    {
                        if(hit.collider==null||hit.collider.transform.IsChildOf(f.Item.transform))continue;
                        if(FindReceiver(hit.collider)==null)
                        {
                            if(hit.normal.y>=.6f)continue;
                            // A thin world obstruction crossed between callbacks must
                            // also cancel delivery intent before the next intake check.
                            sweptContacts.Enqueue(new Contact(f.Item,f.Shot,f.Owner,hit.collider,
                                Mathf.Max(f.Item.Body.linearVelocity.magnitude,delta.magnitude/Time.fixedDeltaTime),hit.point));break;
                        }
                        sweptContacts.Enqueue(new Contact(f.Item,f.Shot,f.Owner,hit.collider,Mathf.Max(f.Item.Body.linearVelocity.magnitude,delta.magnitude/Time.fixedDeltaTime),hit.point));break;
                    }
                }
            }
            // Expiry wins at equality; queued contacts cannot resurrect old damaging provenance.
            ended.Clear();
            foreach (var pair in flights)
                if (!Live(pair.Value) || now>=pair.Value.Expires || overflow)
                { EndFlight(pair.Value); ended.Add(pair.Key); }
            foreach (ulong id in ended) flights.Remove(id);
            if (overflow)
            {
                QueueOverflowCount++; LastFailure="Collision queue overflow: all active shots ended without damage.";
                overflow=false; contacts.Clear(); sweptContacts.Clear(); return;
            }
            ulong batchRevision=lifecycleRevision;
            while ((sweptContacts.Count>0||contacts.Count>0) && IsRunning && authorityRunning() && lifecycleRevision==batchRevision)
            {
                var c=sweptContacts.Count>0?sweptContacts.Dequeue():contacts.Dequeue();
                if (!flights.TryGetValue(c.Key.InstanceId,out var f) || !Live(f) || c.Item!=f.Item || !c.Key.Equals(f.Key) ||
                    c.Shot!=f.Shot || c.Owner!=f.Owner) continue;
                // A verified travelled enemy intersection takes precedence over a same-step resting contact.
                try
                {
                    var receiver=FindReceiver(c.Other);
                    if (receiver!=null && ItemFireRules.CanDamage(f.Item.CargoRole==CargoRole.OrdinaryLoot,now,f.Expires,c.Speed) && Finite(c.Point))
                    {
                        var hit=new ItemHitContext(f.Item,f.Shot,f.Owner,c.Speed,f.Damage,c.Point,now);
                        bool applied=receiver.TryApplyItemHit(in hit);
                        if(applied&&f.Item!=null&&f.Item.State==SuckableState.InFlight)
                            throw new InvalidOperationException("Applied damage must atomically release this shot for reuse.");
                    }
                }
                finally
                {
                    // A physical contact after crossing the opening must not erase
                    // delivery because native collision response already stopped it.
                    bool caught=c.Other!=null&&Truck!=null&&c.Other.transform.IsChildOf(Truck.transform)&&TryCaptureTruck(f,now);
                    // World/prop bounces retain their short damage window. Enemy contact consumes it once.
                    // Any bounce cancels delivery intent unless the cargo already reached the intake opening.
                    if(!caught&&f.Item!=null&&c.Other!=null&&FindReceiver(c.Other)==null&&
                        (Truck==null||Vector3.Distance(c.Point,Truck.Receiver.Position)>Truck.Receiver.AdmissionRadius+.35f))f.Item.DirectedIntakeId=0;
                    if(caught||c.Other==null||FindReceiver(c.Other)!=null||f.Item==null||f.Item.Body.linearVelocity.sqrMagnitude<.25f)
                    {EndFlight(f);if(flights.TryGetValue(c.Key.InstanceId,out var current)&&ReferenceEquals(current,f))flights.Remove(c.Key.InstanceId);}
                }
            }
            // Check the travelled segment even if this tick ended behind the gate.
            // Enemy and unrelated world contacts have already consumed/cancelled intent.
            ended.Clear();
            captureCandidates.Clear();captureCandidates.AddRange(flights.Values);
            foreach(var f in captureCandidates)
            {
                // Admission publishes a cosmetic event; a listener may stop/clear
                // the world. Never enumerate the mutable flight table across it.
                if(!IsRunning||!authorityRunning()||lifecycleRevision!=batchRevision)break;
                if(TryCaptureTruck(f,now)){EndFlight(f);ended.Add(f.Key.InstanceId);}
                else if(f.Item!=null){f.PreviousPosition=f.Item.Body.position;f.PreviousCenter=f.Item.Body.worldCenterOfMass;}
            }
            if(lifecycleRevision==batchRevision)foreach(ulong id in ended)flights.Remove(id);
        }
        private bool TryCaptureTruck(Flight flight,double now)=>Ingestion!=null&&Truck!=null&&Live(flight)&&now<flight.Expires&&
            Ingestion.TryCaptureTruckShot(flight.Item,Truck,flight.PreviousCenter,flight.Item.Body.worldCenterOfMass,now);
        private bool Ready(double now)
        {
            if (!IsRunning || !authorityRunning() || registry.RunId!=RunId || !ItemFireRules.Finite(now) || now<observedAt) return false;
            observedAt=now; return true;
        }
        private bool DirectedTruck(Vector3 origin,Vector3 direction)
        {
            if(Truck==null||Truck.Receiver==null)return false;
            var receiver=Truck.Receiver;Vector3 normal=receiver.Rotation*Vector3.forward;
            float facing=Vector3.Dot(direction,normal);if(facing>=-.01f)return false;
            float distance=Vector3.Dot(receiver.Position-origin,normal)/facing;
            if(distance<0||distance>ItemFireRules.LaunchSpeed*ItemFireRules.FlightLifetime)return false;
            Vector3 aim=origin+direction*distance;
            if(Vector3.Distance(aim,receiver.Position)>receiver.AdmissionRadius)return false;
            // A targeted enemy before the gate wins over the truck even when both line up.
            if(Physics.Raycast(origin,direction,out var hit,distance,LayerMask.GetMask("Enemies","World","Items"),QueryTriggerInteraction.Ignore)&&
                !hit.collider.transform.IsChildOf(Truck.transform))return false;
            return true;
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
