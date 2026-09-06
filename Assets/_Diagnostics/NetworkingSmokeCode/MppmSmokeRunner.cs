#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;
namespace HowToSuck.Networking
{
    // Main editor host + 1/3 additional editor processes, or the same fixture in separate development players.
    public sealed class MppmSmokeRunner : MonoBehaviour
    {
        public static MppmSmokeRunner Current { get; private set; }
        public NetworkConnectionCoordinator Connection;
        public GameObject ControlPrefab, ProbePrefab;
        public string SceneA = "NetworkSmokeA", SceneB = "NetworkSmokeB";
        public string ScenarioNonce, ReportsRoot, BuildId, ContentHash;
        private LoopbackSmokeProfile profile;
        private MppmSmokeControl control;
        private MppmSmokeProbe probe;
        private readonly Dictionary<ulong, int> pids = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, int> slots = new Dictionary<ulong, int>();
        private readonly HashSet<ulong> ready = new HashSet<ulong>(), sampled = new HashSet<ulong>(), gone = new HashSet<ulong>(), finished = new HashSet<ulong>();
        private readonly HashSet<int> pulses = new HashSet<int>();
        private int pid, round, cycle;
        private bool failed, expectedShutdown, terminal;
        private string reportDirectory;
        private Vector3 expectedPosition;
        [Serializable] private sealed class Record
        {
            public string status, detail, transport = "diagnostic UTP loopback", utc;
            public int processId, slot, expectedPlayers, cycle, round;
            public ulong clientId;
        }
        private void Start()
        {
            if (Current != null) { Fail("Duplicate smoke runner."); return; }
            Current = this; DontDestroyOnLoad(gameObject);
            using (var process = System.Diagnostics.Process.GetCurrentProcess()) pid = process.Id;
            try
            {
                if (Connection == null || ControlPrefab == null || ProbePrefab == null)
                    throw new InvalidOperationException("Bind diagnostic root, registered prefabs and explicit output directory.");
                profile = Application.isEditor ? LoopbackSmokeProfile.FromTags(MppmPlayerLabels.ReadTags(), MppmPlayerLabels.IsMainEditor, ScenarioNonce) : ReadStandalone();
                if (string.IsNullOrEmpty(ReportsRoot)) throw new InvalidOperationException("Bind an explicit report directory.");
                string candidateDirectory = Path.Combine(Path.GetFullPath(ReportsRoot), profile.Nonce, "slot-" + profile.Slot + "-pid-" + pid);
                if (File.Exists(Path.Combine(candidateDirectory, "events.jsonl"))) throw new InvalidOperationException("Use a fresh scenario nonce; existing evidence is never overwritten.");
                Directory.CreateDirectory(candidateDirectory); reportDirectory = candidateDirectory;
                Application.runInBackground = true; Application.targetFrameRate = 60; Time.timeScale = 1;
                Connection.Configure(new NetworkConfiguration(SteamApplicationMode.Development480, 0, BuildId, ContentHash, Application.isEditor || Debug.isDebugBuild));
                Connection.SessionLost += OnSessionLost;
                Log("START", "Two planned connection cycles; first run host+1, then run a fresh host+3 scenario.");
                StartCoroutine(Drive(Run()));
            }
            catch (Exception error) { Fail(error.Message); }
        }
        private LoopbackSmokeProfile ReadStandalone()
        {
            string[] args = Environment.GetCommandLineArgs();
            string Read(string key) { int i = Array.IndexOf(args, key); if (i < 0 || i + 1 >= args.Length) throw new ArgumentException("Missing " + key); return args[i + 1]; }
            ReportsRoot = Read("--hts-smoke-output");
            return LoopbackSmokeProfile.Create(int.Parse(Read("--hts-smoke-slot")), int.Parse(Read("--hts-smoke-count")), Read("--hts-smoke-nonce"), Debug.isDebugBuild);
        }
        private IEnumerator Drive(IEnumerator routine)
        {
            var stack = new Stack<IEnumerator>(); stack.Push(routine);
            try
            {
                while (stack.Count > 0 && !failed)
                {
                    bool next = false; object value = null;
                    try { next = stack.Peek().MoveNext(); if (next) value = stack.Peek().Current; }
                    catch (Exception error) { Fail(error.Message); }
                    if (failed) yield break;
                    if (!next) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (value is IEnumerator nested) stack.Push(nested); else yield return value;
                }
            }
            finally { while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); }
        }
        private IEnumerator Until(Func<bool> condition, double seconds, string failure)
        {
            double end = Time.realtimeSinceStartupAsDouble + seconds;
            while (!condition())
            { if (failed) yield break; if (Time.realtimeSinceStartupAsDouble >= end) throw new TimeoutException(failure); yield return null; }
        }
        private IEnumerator Delay(double seconds) { double end = Time.realtimeSinceStartupAsDouble + seconds; while (Time.realtimeSinceStartupAsDouble < end) yield return null; }
        private IEnumerator Run()
        {
            for (cycle = 1; cycle <= 2; cycle++)
            {
                pids.Clear(); slots.Clear(); expectedShutdown = false; finished.Clear();
                byte[] nonce = Guid.ParseExact(profile.Nonce, "N").ToByteArray(); nonce[15] ^= (byte)cycle;
                var runProfile = LoopbackSmokeProfile.Create(profile.Slot, profile.PlayerCount, new Guid(nonce).ToString("N"), true);
                if (!Connection.StartDiagnosticLoopback(runProfile)) throw new InvalidOperationException("Common coordinator did not start diagnostic transport: " + Connection.LastError);
                yield return Until(() => Connection.Phase == ConnectionPhase.Lobby, 25, "Connection did not reach Lobby.");
                Log("CONNECTED", "Steam was not initialized; actual NGO local client ID recorded.");
                if (profile.IsHost) yield return HostCycle();
                else
                {
                    yield return Until(() => expectedShutdown, 110, "Host never completed the test cycle.");
                    yield return Until(() => Connection.Phase == ConnectionPhase.Offline, 12, "Client did not finish common coordinator shutdown.");
                }
                yield return null;
                if (Connection.Manager.IsListening || Connection.Manager.ShutdownInProgress || control != null || probe != null)
                    throw new InvalidOperationException("Runtime network state survived shutdown.");
                Log("CYCLE_PASS", "Connection, two scene rounds, authoritative body and shutdown completed.");
                yield return Delay(1);
            }
            cycle = 2; terminal = true; Log("PASS_LOCAL_UTP", "MPPM/standalone local fixture only. Gameplay bridge and Steam/SDR are separate gates.");
            if (!Application.isEditor) Application.Quit(0);
        }
        private IEnumerator HostCycle()
        {
            yield return Until(() => Connection.Manager.ConnectedClientsIds.Count == profile.PlayerCount && Connection.ConnectedPlayerCount == profile.PlayerCount, cycle == 1 ? 120 : 25, "Expected MPPM processes did not connect.");
            control = Instantiate(ControlPrefab).GetComponent<MppmSmokeControl>();
            if (control == null) throw new InvalidOperationException("Control prefab is missing its smoke component.");
            DontDestroyOnLoad(control.gameObject); control.NetworkObject.Spawn(false);
            yield return Until(() => pids.Count == profile.PlayerCount && Connection.CanBeginContract, 15, "Distinct process identity/ready roster was not confirmed.");
            for (int index = 0; index < 2; index++)
            {
                round = (cycle - 1) * 2 + index + 1; ready.Clear(); sampled.Clear(); gone.Clear(); pulses.Clear();
                if (!Connection.EnterPreparing("network-smoke")) throw new InvalidOperationException("Common ready/admission gate rejected preparation.");
                yield return new NgoSceneBarrier(Connection.Manager).LoadOnHost(index == 0 ? SceneA : SceneB);
                probe = Instantiate(ProbePrefab, new Vector3(0, 3, 0), Quaternion.identity).GetComponent<MppmSmokeProbe>();
                if (probe == null) throw new InvalidOperationException("Probe prefab is incomplete.");
                probe.Prepare(round); probe.NetworkObject.Spawn(true);
                yield return Until(() => ready.Count == profile.PlayerCount, 15, "Not every peer received the initialized frozen probe.");
                yield return Delay(.25);
                if (!probe.Body.isKinematic || Mathf.Abs(probe.transform.position.y - 3) > .01f) throw new InvalidOperationException("Physics moved before all peers were ready.");
                if (!Connection.SetAuthoritativePhase(ConnectionPhase.Running, "network-smoke")) throw new InvalidOperationException("Common phase gate rejected Running.");
                probe.SetFrozen(false);
                for (int sequence = 1; sequence <= 10; sequence++) control.PulseRpc(round, sequence);
                yield return Delay(2);
                probe.SetFrozen(true); expectedPosition = probe.transform.position;
                if (probe.LocalAuthorityTicks < 20 || expectedPosition.y > .9f || expectedPosition.y < .4f) throw new InvalidOperationException("Host body did not fall and settle on the authored diagnostic floor.");
                Log("HOST_PHYSICS", "position=" + expectedPosition.ToString("F3") + "; authorityTicks=" + probe.LocalAuthorityTicks);
                sampled.Add(NetworkManager.ServerClientId);
                control.CommandRpc(1, round, expectedPosition);
                yield return Until(() => sampled.Count == profile.PlayerCount, 8, "Guest pose/kinematic/sample proof was incomplete.");
                probe.NetworkObject.Despawn(true);
                yield return Until(() => gone.Count == profile.PlayerCount, 8, "Probe despawn did not reach every peer.");
                if (!Connection.SetAuthoritativePhase(ConnectionPhase.Results, "network-smoke") || !Connection.SetAuthoritativePhase(ConnectionPhase.Lobby, "none"))
                    throw new InvalidOperationException("Common result/lobby transition failed.");
                control.CommandRpc(3, round, Vector3.zero);
                yield return Until(() => Connection.CanBeginContract, 8, "Ready state did not reset and reconfirm in Lobby.");
                Log("ROUND_PASS", "Scene load, initialized spawn, frozen readiness, host-only gravity, guest pose and despawn.");
            }
            expectedShutdown = true; finished.Add(NetworkManager.ServerClientId); control.CommandRpc(2, round, Vector3.zero);
            yield return Until(() => finished.Count == profile.PlayerCount, 8, "Guests did not acknowledge orderly shutdown.");
            Connection.StopSession();
            yield return Until(() => Connection.Phase == ConnectionPhase.Offline, 8, "Host common shutdown did not complete.");
        }
        public void ControlSpawned(MppmSmokeControl value)
        { control = value; Report(0, 0, 0, true, Vector3.zero); }
        public void ControlDespawned(MppmSmokeControl value) { if (control == value) control = null; }
        public void ProbeSpawned(MppmSmokeProbe value) { probe = value; round = value.Round.Value; pulses.Clear(); StartCoroutine(Drive(ReportProbeReady(value))); }
        private IEnumerator ReportProbeReady(MppmSmokeProbe value)
        { yield return null; if (value == null || !value.IsSpawned || !value.Frozen.Value || !value.Body.isKinematic) throw new InvalidOperationException("Probe was not frozen on initial observation."); Log("PROBE_READY", "NetworkObjectId=" + value.NetworkObjectId + "; frozen=true; authorityTicks=" + value.LocalAuthorityTicks); Report(1, round, value.LocalAuthorityTicks, true, value.transform.position); }
        public void ProbeDespawned(MppmSmokeProbe value, int valueRound)
        { if (probe == value) probe = null; if (control != null && !expectedShutdown) { Log("PROBE_DESPAWN", "round=" + valueRound); Report(3, valueRound, 0, true, Vector3.zero); } }
        public void ReceivePulse(int valueRound, int sequence) { if (valueRound == round && sequence >= 1 && sequence <= 10) pulses.Add(sequence); }
        public void ReceiveCommand(int command, int valueRound, Vector3 position)
        {
            if (profile.IsHost || valueRound != round) { Fail("Unexpected command role/round."); return; }
            if (command == 1) StartCoroutine(Drive(Sample(valueRound, position)));
            else if (command == 2) { expectedShutdown = true; Report(5, round, 0, true, Vector3.zero); }
            else if (command == 3) Report(4, round, 0, true, Vector3.zero);
            else Fail("Unknown smoke command.");
        }
        private IEnumerator Sample(int valueRound, Vector3 position)
        {
            yield return Until(() => probe != null && probe.Round.Value == valueRound && probe.Frozen.Value && Vector3.Distance(probe.transform.position, position) <= .2f, 5, "Guest did not converge to the frozen host pose.");
            Log("SAMPLE", "position=" + probe.transform.position.ToString("F3") + "; kinematic=" + probe.Body.isKinematic + "; authorityTicks=" + probe.LocalAuthorityTicks + "; unreliableSeen=" + pulses.Count);
            Report(2, round, probe.LocalAuthorityTicks, probe.Body.isKinematic, probe.transform.position);
        }
        private void Report(int kind, int valueRound, int ticks, bool kinematic, Vector3 position)
        { if (control != null) control.ReportRpc(kind, valueRound, profile.Slot, pid, ticks, kinematic, position, pulses.Count); }
        public void ReceiveReport(ulong sender, int kind, int valueRound, int slot, int processId, int ticks, bool kinematic, Vector3 position, int unreliableSeen)
        {
            if (!profile.IsHost || !Connection.Manager.ConnectedClients.ContainsKey(sender)) { Fail("Unadmitted report sender."); return; }
            if (kind == 0)
            {
                if (slots.ContainsKey(sender) || slots.ContainsValue(slot) || pids.ContainsValue(processId) || processId <= 0 || slot < 0 || slot >= profile.PlayerCount || ((sender == NetworkManager.ServerClientId) != (slot == 0)))
                { Fail("Duplicate process, slot or invalid actual ClientId mapping."); return; }
                slots.Add(sender, slot); pids.Add(sender, processId);
                if (!Connection.SetReadyFromServerRpc(sender, true)) Fail("Actual admitted peer could not set Ready.");
                Log("PEER", "ClientId=" + sender + "; slot=" + slot + "; pid=" + processId); return;
            }
            if (valueRound != round || !slots.TryGetValue(sender, out int knownSlot) || knownSlot != slot || pids[sender] != processId)
            { Fail("Stale report or mismatched admitted process identity."); return; }
            if (kind == 1) { if (!kinematic || ticks != 0 || !ready.Add(sender)) Fail("Duplicate/unfrozen initial probe report."); }
            else if (kind == 2)
            {
                if (sender == NetworkManager.ServerClientId || ticks != 0 || !kinematic || unreliableSeen < 1 || Vector3.Distance(position, expectedPosition) > .2f || !sampled.Add(sender))
                    Fail("Guest physics/pose/unreliable observation failed.");
            }
            else if (kind == 3) { if (!gone.Add(sender)) Fail("Duplicate despawn observation."); }
            else if (kind == 4) { if (!Connection.SetReadyFromServerRpc(sender, true)) Fail("Lobby ready reconfirmation failed."); }
            else if (kind == 5) { if (!expectedShutdown || !finished.Add(sender)) Fail("Invalid shutdown acknowledgment."); }
            else Fail("Unknown smoke report.");
        }
        private void OnSessionLost() { if (!expectedShutdown && !failed) Fail("Unexpected connection loss."); }
        public void Fail(string detail)
        {
            if (failed || terminal) return; failed = true;
            Log("FAIL", detail); Debug.LogError("HowToSuck MPPM smoke: " + detail);
            Connection?.StopSession();
            if (!Application.isEditor) Application.Quit(1);
        }
        private void Log(string status, string detail)
        {
            var value = new Record { status = status, detail = detail, utc = DateTime.UtcNow.ToString("O"), processId = pid,
                slot = profile?.Slot ?? -1, expectedPlayers = profile?.PlayerCount ?? 0, cycle = cycle, round = round,
                clientId = Connection != null && Connection.Manager != null ? Connection.Manager.LocalClientId : ulong.MaxValue };
            string json = JsonUtility.ToJson(value);
            if (reportDirectory != null)
            {
                File.AppendAllText(Path.Combine(reportDirectory, "events.jsonl"), json + Environment.NewLine);
                if (status == "FAIL" || status == "PASS_LOCAL_UTP") File.WriteAllText(Path.Combine(reportDirectory, "result.json"), json);
            }
            Debug.Log("HTS_MPPM " + json);
        }
        private void OnDestroy() { if (Connection != null) Connection.SessionLost -= OnSessionLost; if (Current == this) Current = null; }
    }
}
#endif
