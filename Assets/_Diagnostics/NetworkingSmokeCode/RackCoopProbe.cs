#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using HowToSuck.Networking;
using UnityEngine;
namespace HowToSuck.Diagnostics
{
    // Explicit rack diagnostic bootstrap; separate profiles and Windows peers.
    public sealed class RackCoopProbe : MonoBehaviour
    {
        NgoGameSession game; int slot, count; string directory, contract;
        SuctionSystem arrangedSuction; VacuumEmitter arrangedEmitter; SuckableObject selectedStock;
        readonly List<string> motion = new List<string>(); float nextObservation;
        void FixedUpdate()
        {
            if (arrangedEmitter == null || !arrangedEmitter.Active) return;
            arrangedSuction.BeginStep(Time.fixedDeltaTime); arrangedSuction.Apply(arrangedEmitter); arrangedSuction.Flush();
        }
        void Update()
        {
            if (selectedStock == null || Time.realtimeSinceStartup < nextObservation || motion.Count >= 40) return;
            nextObservation = Time.realtimeSinceStartup + .5f;
            motion.Add(selectedStock.InstanceId + " " + selectedStock.Body.position.ToString("F3") + " " + selectedStock.State + " frozen=" + selectedStock.WorldFrozen + " kinematic=" + selectedStock.Body.isKinematic + " sleeping=" + selectedStock.Body.IsSleeping());
        }
        readonly List<string> checks = new List<string>(), errors = new List<string>();
        static string Argument(string key) { var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, key); return at < 0 ? null : args[at + 1]; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void SeedIsolatedCampaign()
        {
            string contract = Argument("--hts-racks-contract");
            if (contract == null) return;
            if (contract != "supermarket" && contract != "warehouse") throw new ArgumentException("Unknown rack map");
            if (Argument("--hts-gameplay-slot") != "0") return;
            var root = CampaignStoragePaths.ResolveOwnDirectory();
            if (!root.Contains(Argument("--hts-gameplay-nonce"))) throw new Exception("Isolated profile required");
            var tiers = new CampaignTierCatalog(Enumerable.Range(0, 4).Select(n => new CampaignTier("mk" + (n + 1), CampaignBalance.ModelPrices[n])));
            using (var repo = new SaveRepository(root, tiers, () => true))
            {
                var opened = repo.Open();
                if (!opened.Ready || !repo.Commit(opened.State, new CampaignState(opened.State.CampaignId, "mk3", clearedContractIds: CampaignBalance.Contracts)).Success)
                    throw new Exception("Cannot seed diagnostic progression");
            }
        }
        void Need(bool ok, string label) { if (!ok) throw new Exception(label); checks.Add(label); }
        void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
        async Task Until(Func<bool> condition, string label, int seconds = 70)
        {
            double end = Time.realtimeSinceStartupAsDouble + seconds;
            while (!condition()) { if (errors.Count > 0) throw new Exception(errors[0]); if (Time.realtimeSinceStartupAsDouble > end) throw new TimeoutException(label); await Task.Delay(40); }
        }
        async Task Barrier(string label)
        {
            File.WriteAllText(Path.Combine(directory, label + "-" + slot), label == "stock-ready" ? selectedStock.InstanceId.ToString() : "ready");
            await Until(() => Enumerable.Range(0, count).All(n => File.Exists(Path.Combine(directory, label + "-" + n))), label);
        }
        NetworkLootAdapter[] Loot() => FindObjectsByType<NetworkLootAdapter>(FindObjectsSortMode.None).Where(l => l.IsSpawned).ToArray();
        async void Start()
        {
            DontDestroyOnLoad(gameObject); Application.logMessageReceived += Log; string failure = null;
            try
            {
                slot = int.Parse(Argument("--hts-gameplay-slot")); count = int.Parse(Argument("--hts-gameplay-count")); contract = Argument("--hts-racks-contract");
                directory = Path.Combine(Argument("--hts-gameplay-reports"), Argument("--hts-gameplay-nonce")); Directory.CreateDirectory(directory);
                await Until(() => { game = FindFirstObjectByType<NgoGameSession>(); return game != null && game.Control != null && game.Control.IsSpawned && game.Session.Phase == SessionPhase.Lobby && game.Control.Roster.Count == count; }, "Connected lobby");
                if (game.HasAuthority) Need(game.Session.SelectLobbyContract(contract), "Select authored rack map");
                await Until(() => game.Session.SelectedLobbyContractId == contract && game.Session.DisplayedContractUnlocked(contract), "Unlocked selection replicates");
                game.SetLocalReady(true);
                await Until(() => Enumerable.Range(0, count).All(n => game.Control.Roster[n].Ready), "Ready roster");
                if (game.HasAuthority) { await Task.Delay(300); Need(game.Session.StartSelectedContract(), "Start real contract"); }
                await Until(() => game.Session.Phase == SessionPhase.Playing, "Load contract", 100);
                int expected = contract == "supermarket" ? 207 : 158;
                await Until(() => Loot().Length == expected && Loot().All(l => l.HasAcceptedCurrentSnapshot), "All independent loot replicas");
                var loot = Loot(); var modules = loot.Where(l => l.Item.TypeId.StartsWith("modular_")).ToArray();
                var frames = modules.Where(l => l.Item.StartMounted).OrderBy(l => l.Item.InstanceId).ToArray();
                var goods = modules.Where(l => !l.Item.StartMounted).ToArray();
                Need(frames.Length == (contract == "supermarket" ? 8 : 12) && goods.Length == (contract == "supermarket" ? 100 : 36), "Every section and stock item has its own network identity");
                Need(modules.All(l => l.Item.HasPhysicsAuthority == game.HasAuthority && l.Item.Body.isKinematic == (!game.HasAuthority || l.Item.IsMounted)), "Only host simulates loose stock; frames remain mounted");
                await Task.Delay(8000);
                var frame = frames[0].Item; var initial = frame.Body.position;
                var supported = goods.Select(l => l.Item).Where(i => Mathf.Abs(i.Body.position.x - initial.x) < .7f && Mathf.Abs(i.Body.position.z - initial.z) < .8f).OrderBy(i => i.InstanceId).ToArray();
                Need(supported.Length >= 3, "Stock remains on the settled section");
                // Interpolated Y positions differ slightly between peers. Pick by
                // stable network identity, never by floating-point settling order.
                var target = supported[0]; selectedStock = target; var start = target.Body.position;
                var upper = supported.Where(i => i != target && i.Body.position.y > 1.1f).ToDictionary(i => i, i => i.Body.position.y);
                Need(upper.Count > 0, "Elevated stock observed before removing support");
                await Barrier("stock-ready");
                Need(Enumerable.Range(0, count).All(n => File.ReadAllText(Path.Combine(directory, "stock-ready-" + n)) == target.InstanceId.ToString()), "Every peer observes the same selected network item");
                if (game.HasAuthority)
                {
                    var outward = contract == "supermarket" ? (start.x < initial.x ? Vector3.left : Vector3.right) : (initial.x < 0 ? Vector3.right : Vector3.left);
                    arrangedEmitter = new GameObject("Diagnostic stock vacuum").AddComponent<VacuumEmitter>();
                    arrangedEmitter.Definition = game.Session.Catalog.Vacuums.Single(v => v.TierId == (contract == "supermarket" ? "mk1" : "mk2"));
                    arrangedEmitter.Range = 1.3f; arrangedEmitter.HalfAngle = 7; arrangedEmitter.Active = true;
                    var receiver = arrangedEmitter.gameObject.AddComponent<IntakeReceiver>(); receiver.Emitter = arrangedEmitter;
                    receiver.BindStorage(new PlayerStorage(game.Session.RunId, 1, 2));
                    arrangedEmitter.transform.position = target.GameplayColliders[0].bounds.center + outward * 1.2f;
                    arrangedEmitter.transform.rotation = Quaternion.LookRotation(-outward);
                    arrangedSuction = new SuctionSystem(game.Session.World.Loot);
                    Need(arrangedSuction.Collect(arrangedEmitter).Any(h => h.Item == target), "Vacuum targets one product through the open shelf bay");
                }
                await Until(() => Vector3.Distance(target.Body.position, start) > .7f, "Stock clears the posts and movement replicates", 15);
                if (arrangedEmitter != null) { arrangedEmitter.Active = false; Destroy(arrangedEmitter.gameObject); }
                Need(Vector3.Distance(frame.Body.position, initial) < .005f, "Moving a product leaves its section fixed");
                await Barrier("stock-moved");
                // Arrange the same collision-removal transition as completed intake.
                // Individual vacuum targeting and MK3 detachment are verified by the native PhysX fixture.
                if (game.HasAuthority) Need(frame.TryTransition(SuckableState.Available, SuckableState.Lost), "Remove just one support on authority");
                await Until(() => frame.State == SuckableState.Lost && upper.All(p => p.Key.Body.position.y < p.Value - .35f), "Sleeping stock falls and the new poses replicate", 15);
                Need(frames.Skip(1).All(l => l.Item.State == SuckableState.Available && l.Item.IsMounted), "Other rack sections remain independent");
                await Barrier("support-removed");
                if (game.HasAuthority) { await Task.Delay(300); game.Session.Controller.Abort(game.Driver.Now); }
                await Until(() => game.Session.Phase == SessionPhase.Results, "Results replicate"); Need(errors.Count == 0, "No runtime errors");
            }
            catch (Exception e) { failure = e.ToString(); }
            finally { if (arrangedEmitter != null) Destroy(arrangedEmitter.gameObject); Application.logMessageReceived -= Log; }
            if (directory != null) File.WriteAllText(Path.Combine(directory, "result-" + slot + ".json"), JsonUtility.ToJson(new Receipt { passed = failure == null, slot = slot, count = count, contract = contract, error = failure, checks = checks.ToArray(), runtimeErrors = errors.ToArray(), motion = motion.ToArray() }, true));
            await Task.Delay(1000); Application.Quit(failure == null ? 0 : 2);
        }
        [Serializable] sealed class Receipt { public bool passed; public int slot, count; public string contract, error; public string[] checks, runtimeErrors, motion; }
    }
}
#endif
