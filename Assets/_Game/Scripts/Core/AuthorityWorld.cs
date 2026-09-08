using System;
using System.Collections.Generic;
using UnityEngine;

namespace HowToSuck
{
    public sealed class AuthorityWorld : MonoBehaviour
    {
        public bool HasAuthority { get; private set; }
        public bool IsRunning { get; private set; }
        public ulong TickCount { get; private set; }
        public string RunId => HasAuthority ? Loot.RunId : replicaRun;
        private string replicaRun;
        private byte replicaExtractionMask;
        public int PlayerCount => players.Count;
        public IReadOnlyDictionary<int, PlayerMotor> Players => players;
        public LootRegistry Loot { get; } = new LootRegistry();
        public IngestionService Ingestion { get; private set; }
        public ItemFireService ItemFire { get; private set; }
        public EnemySimulationService Combat {get;private set;}
        public bool AllPlayersInExtraction { get; private set; }
        public event Action SnapshotChanged;
        public event Action RunStarted,WorldCleared;
        public event Action<int> PlayerRemoved;
        public event Action<DeliveryRecord> DeliveryCommitted;
        public string PreparedTierId => playerVacuum != null ? playerVacuum.TierId : null;
        private readonly Dictionary<int, PlayerStorage> storages = new Dictionary<int, PlayerStorage>();
        public IReadOnlyDictionary<int, PlayerStorage> Storages => new System.Collections.ObjectModel.ReadOnlyDictionary<int, PlayerStorage>(storages);
        private int preparedStorageCapacity = 1;

        private readonly Dictionary<int, PlayerMotor> players = new Dictionary<int, PlayerMotor>();
        private readonly Dictionary<int, PlayerIntentBuffer> inputs = new Dictionary<int, PlayerIntentBuffer>();
        private readonly Dictionary<int, VacuumEmitter> emitters = new Dictionary<int, VacuumEmitter>();
        private readonly List<IntakeReceiver> receivers = new List<IntakeReceiver>();
        private readonly List<ExtractionPlayerState> extractionPlayers = new List<ExtractionPlayerState>();
        private SuctionSystem suction;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public SuctionForceJournal DiagnosticAppliedForces => suction?.DiagnosticAppliedForces;
#endif
        private IWorldSpawner spawner;
        private TruckIntake truck;
        private VacuumDefinition playerVacuum;
        private ExtractionZone extraction;
        private WorldBoundsGuard boundsGuard;
        private ContractController contract;
        private Func<double> clock;
        private double stepNow;
        private bool inStep;

        public void Initialize(bool authority)
        {
            if (Ingestion != null) throw new InvalidOperationException("World is already initialized.");
            HasAuthority = authority;
            clock = () => Time.realtimeSinceStartupAsDouble;
            suction = new SuctionSystem(Loot);
            Ingestion = new IngestionService(Loot, suction, instance => spawner?.Despawn(instance));
            Ingestion.Delivered += OnDelivered;
            ItemFire = new ItemFireService(Loot, () => HasAuthority && IsRunning) { Ingestion=Ingestion };
            Combat = new EnemySimulationService(this);
        }

        // The prepared controller supplies the only run ID; the world never invents another ID.
        public void PrepareWorld(LevelContext level, IWorldSpawner worldSpawner, VacuumDefinition vacuum,
            ContractController controller, Func<double> authorityClock, int storageCapacity = 1, int crewSize = 1)
        {
            if (!HasAuthority || Ingestion == null) throw new InvalidOperationException("Initialize the authority first.");
            if (controller == null || controller.State.Phase != ContractPhase.Preparing)
                throw new InvalidOperationException("Prepare the contract before preparing its world.");
            if (IsRunning || !string.IsNullOrEmpty(RunId) || players.Count != 0)
                throw new InvalidOperationException("Clear the previous world before preparing another run.");
            if (level == null || level.ExtractionZone == null) throw new ArgumentNullException(nameof(level));
            if (storageCapacity < 1) throw new ArgumentOutOfRangeException(nameof(storageCapacity));
            preparedStorageCapacity = storageCapacity;
            spawner = worldSpawner ?? throw new ArgumentNullException(nameof(worldSpawner));
            clock = authorityClock ?? throw new ArgumentNullException(nameof(authorityClock));
            contract = controller;
            playerVacuum = vacuum != null ? vacuum : throw new ArgumentNullException(nameof(vacuum));
            truck = level.Truck;
            ItemFire.Truck=truck;
            extraction = level.ExtractionZone;
            boundsGuard = level.BoundsGuard;
            extraction.Clear();
            receivers.RemoveAll(receiver => receiver == null || receiver.IsTruck);
            if (truck != null) { truck.Stop(); receivers.Add(truck.Receiver); }
            Loot.Begin(controller.State.RunId);
            Ingestion.Begin(RunId);
            ItemFire.Begin(RunId);
            boundsGuard?.BeginRun(RunId, level.PlayerSpawns);
            foreach (var spawn in level.LootSpawns)
            {
                var instance = spawner.Spawn(spawn.Prefab, spawn.transform.position, spawn.transform.rotation);
                if (instance == null) throw new InvalidOperationException("Loot spawn failed.");
                try
                {
                    Loot.Register(instance.GetComponent<SuckableObject>());
                    ItemFire.Track(instance.GetComponent<SuckableObject>());
                    instance.GetComponent<SuckableObject>().SetWorldFrozen(true);
                    (spawner as IWorldSpawnCommitter)?.CommitSpawn(instance);
                }
                catch { spawner.Despawn(instance); throw; }
            }
            Combat.Prepare(level,spawner,controller,crewSize);
        }

        public void SetRunning(bool running)
        {
            if (!HasAuthority) throw new InvalidOperationException("Only the authority can run simulation.");
            if (running && (contract == null || !contract.IsRunning || contract.State.RunId != RunId))
                throw new InvalidOperationException("Only the current running contract can unfreeze the world.");
            if (IsRunning == running) return;
            IsRunning = running;
            ItemFire?.SetRunning(running);
            Combat?.SetRunning(running);
            // Cancel reservations before freezing the preserved registry instances.
            Ingestion?.SetRunning(running);
            if (!running && truck != null) truck.Stop();
            foreach (var item in Loot.Items.Values) if (item != null) item.SetWorldFrozen(!running);
            if (!running)
            {
                foreach (var source in emitters.Values) if (source != null) source.Active = false;
                foreach (var player in players.Values)
                    if (player != null)
                    {
                        player.ResetContactResponse();
                        player.PresentFireCharge(0);
                        player.GetComponent<PlayerInputReader>()?.SetGameplayAvailable(false);
                    }
            }
            // Deadline/abort can stop outside the simulated step and bypass its finally notification.
            // Publish only after every physics/input owner has the final state; in-step changes publish in finally.
            if (running) RunStarted?.Invoke();
            if (!inStep) SnapshotChanged?.Invoke();
        }

        public void RegisterPlayer(PlayerMotor motor)
        {
            if (!HasAuthority || motor == null || motor.PlayerId <= 0 || string.IsNullOrWhiteSpace(RunId))
                throw new InvalidOperationException("A player needs the current prepared run before registration.");
            if (storages.ContainsKey(motor.PlayerId)) throw new InvalidOperationException("Previous owner storage must finish this run before player ID reuse.");
            var storage = new PlayerStorage(RunId, motor.PlayerId, preparedStorageCapacity);
            players.Add(motor.PlayerId, motor); storages.Add(motor.PlayerId, storage);
            ItemFire.RegisterPlayer(motor.PlayerId);
            Combat.RegisterPlayer(motor.PlayerId);
            motor.BindContactWorld(this);
            var buffer = new PlayerIntentBuffer();
            buffer.BindRun(RunId, motor.transform.eulerAngles.y, 0);
            inputs.Add(motor.PlayerId, buffer);
            var emitter = motor.GetComponent<VacuumEmitter>();
            if (emitter != null)
            {
                emitter.Source = motor.NozzleAnchor; emitter.OriginGuard = motor.AuthoritativeAim;
                emitter.EmitterId = motor.PlayerId; emitter.Definition = playerVacuum;
                emitters.Add(motor.PlayerId, emitter);
            }
            var receiver = motor.GetComponent<IntakeReceiver>();
            if (receiver != null)
            { receiver.IntakeId = motor.PlayerId; receiver.PlayerId = motor.PlayerId; receiver.BindStorage(storage); receivers.Add(receiver); }
        }

        public void RemovePlayer(int id)
        {
            bool wasRegistered = players.ContainsKey(id);
            if (storages.TryGetValue(id, out var storage)) storage.Detach();
            Ingestion?.CancelOwner(id);
            ItemFire?.RemovePlayer(id);
            Combat?.RemovePlayer(id);
            if (emitters.TryGetValue(id, out var emitter) && emitter != null) emitter.Active = false;
            if (players.TryGetValue(id, out var motor) && motor != null) motor.BindContactWorld(null);
            players.Remove(id); inputs.Remove(id); emitters.Remove(id);
            if (wasRegistered) PlayerRemoved?.Invoke(id);
            receivers.RemoveAll(receiver => receiver == null || (!receiver.IsTruck && receiver.PlayerId == id));
        }

        public bool SubmitIntent(int id, PlayerIntent intent) => HasAuthority && IsRunning &&
            !string.IsNullOrEmpty(RunId) && intent.RunId == RunId &&
            inputs.TryGetValue(id, out var buffer) && buffer.TrySubmit(intent, clock());

        public bool IsPlayerInExtraction(int id) => HasAuthority ? extraction != null && extraction.Contains(id) :
            id >= 1 && id <= 4 && (replicaExtractionMask & (1 << (id - 1))) != 0;
        public void ApplyReplicaWorld(string run, bool running, byte extractionMask, bool allInside)
        {
            if (HasAuthority) throw new InvalidOperationException("The authority cannot consume world replicas.");
            replicaRun = run; IsRunning = running; replicaExtractionMask = extractionMask;
            AllPlayersInExtraction = allInside; SnapshotChanged?.Invoke();
        }

        public void Clear()
        {
            WorldCleared?.Invoke();
            if (!HasAuthority)
            {
                // NGO owns replica lifetimes. Never despawn or run ingestion cancellation on a guest.
                replicaRun = null; replicaExtractionMask = 0; IsRunning = false; TickCount = 0;
                AllPlayersInExtraction = false; return;
            }
            SetRunning(false); TickCount = 0;
            Combat?.Clear();
            ItemFire?.Clear();
            foreach (var item in Loot.Items.Values) if (item != null) spawner?.Despawn(item.gameObject);
            Ingestion?.Clear();
            if (extraction != null) extraction.Clear();
            foreach (var motor in players.Values) if (motor != null) motor.BindContactWorld(null);
            if (boundsGuard != null) boundsGuard.Clear();
            boundsGuard = null;
            truck = null; extraction = null; contract = null; playerVacuum = null;
            foreach (var storage in storages.Values) storage.ClearForWorldEnd();
            storages.Clear();
            Loot.Clear(); players.Clear(); inputs.Clear(); emitters.Clear(); receivers.Clear();
            extractionPlayers.Clear(); AllPlayersInExtraction = false; inStep = false;
        }

        private void FixedUpdate()
        {
            if (!HasAuthority || !IsRunning || contract == null) return;
            double now = clock();
            if (double.IsNaN(now) || double.IsInfinity(now) || now < contract.State.ObservedAt) return;
            // Exact equality belongs to timeout, before movement, completion, forces or admissions.
            if (contract.CheckDeadline(now) || !contract.IsRunning || !IsRunning) return;
            TickCount++;
            stepNow = now; inStep = true;
            Combat.BeginStep(now);
            try
            {
                Combat.StepRecovery();
                if (boundsGuard != null)
                    boundsGuard.Step(this, now,
                        (motor, position) => !motor.IsDowned && motor.RecoverAt(position, inputs[motor.PlayerId].Read(now)),
                        instance => spawner.Despawn(instance));
                foreach (var pair in players)
                {
                    if (pair.Value == null || !pair.Value.isActiveAndEnabled)
                    {
                        if (emitters.TryGetValue(pair.Key, out var inactiveSource) && inactiveSource != null) inactiveSource.Active = false;
                        continue;
                    }
                    if (Combat.RequiresRecovery(pair.Key) || boundsGuard != null && boundsGuard.RequiresRecovery(pair.Key))
                    {
                        pair.Value.SuspendForRecovery(inputs[pair.Key].Read(now));
                        if (emitters.TryGetValue(pair.Key, out var recoveringSource) && recoveringSource != null)
                            recoveringSource.Active = false;
                        continue;
                    }
                    pair.Value.Step(inputs[pair.Key].Read(now), Time.fixedDeltaTime, now);
                    if (emitters.TryGetValue(pair.Key, out var emitter) && emitter != null)
                        emitter.Active = pair.Value.LastIntent.VacuumHeld && pair.Value.NozzlePoseValid;
                }
                // Collision callbacks from the previous native simulation are consumed only after the deadline gate.
                ItemFire.StepContacts(now);
                if (!IsRunning || !contract.IsRunning) return;
                foreach (var pair in players)
                {
                    emitters.TryGetValue(pair.Key, out var fireSource);
                    storages.TryGetValue(pair.Key, out var storage);
                    bool allowed = pair.Value != null && pair.Value.isActiveAndEnabled &&
                        !Combat.RequiresRecovery(pair.Key) && (boundsGuard == null || !boundsGuard.RequiresRecovery(pair.Key));
                    ItemFire.ProcessPlayer(pair.Key, inputs[pair.Key].Read(now), pair.Value, storage, fireSource, now, allowed);
                }
                Combat.StepActors(Time.fixedDeltaTime);
                if (!IsRunning || !contract.IsRunning) return;
                Ingestion.CompleteDue(now);
                if (!IsRunning || !contract.IsRunning) return;
                // One shared force budget for all handheld sources and the truck.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                suction.DiagnosticAppliedForces.BeginStep(RunId,TickCount);
#endif
                suction.BeginStep();
                foreach (var emitter in emitters.Values) if (emitter != null) suction.Apply(emitter);
                if (truck != null) truck.Step(suction);
                suction.Flush();
                Ingestion.Admit(now, receivers);
                if (!IsRunning || !contract.IsRunning) return;
                extraction.Refresh(players);
                Combat.StepRepairs();
                extractionPlayers.Clear();
                AllPlayersInExtraction = false;
                bool allInside = true;
                foreach (var pair in players)
                {
                    var motor = pair.Value;
                    if (motor == null || !motor.isActiveAndEnabled) continue;
                    bool inside = extraction.Contains(pair.Key);
                    allInside &= inside;
                    extractionPlayers.Add(new ExtractionPlayerState(pair.Key, inside, motor.LastIntent.InteractHeld));
                }
                AllPlayersInExtraction = extractionPlayers.Count > 0 && allInside;
                contract.StepExtraction(now, extractionPlayers);
            }
            finally { suction.CancelStep(); Combat.EndStep(); inStep = false; SnapshotChanged?.Invoke(); }
        }

        internal PlayerIntent RecoveryIntent(int id) => inStep && inputs.TryGetValue(id,out var buffer) ? buffer.Read(stepNow) : default;
        internal bool TryRecoverCombatPlayer(PlayerMotor motor,Vector3 position,double now)
        {
            if(!inStep||now!=stepNow||!IsRunning||motor==null||!players.TryGetValue(motor.PlayerId,out var current)||current!=motor||!inputs.TryGetValue(motor.PlayerId,out var buffer))return false;
            var intent=buffer.Read(now);storages.TryGetValue(motor.PlayerId,out var storage);emitters.TryGetValue(motor.PlayerId,out var emitter);
            ItemFire.ProcessPlayer(motor.PlayerId,intent,motor,storage,emitter,now,false);
            return motor.RecoverAt(position,intent);
        }
        private void OnDelivered(DeliveryRecord record)
        {
            if (inStep && IsRunning && contract != null && record.RunId == RunId && contract.TryRecordDelivery(record, stepNow))
                DeliveryCommitted?.Invoke(record);
        }

        private void OnDestroy()
        {
            Clear();
            if (Ingestion != null) Ingestion.Delivered -= OnDelivered;
            SnapshotChanged = null; RunStarted = null; WorldCleared = null; PlayerRemoved = null; DeliveryCommitted = null;
        }
    }
}
