using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace HowToSuck.Networking
{
    // Connection foundation only. Contract06 gameplay is attached through the separate readiness/presentation bridge.
    [DisallowMultipleComponent]
    public sealed class NetworkConnectionCoordinator : MonoBehaviour
    {
        public NetworkManager Manager;
        public UnityTransport Loopback;
        public HowToSuckSteamTransport SteamTransport;
        public ConnectionMode Mode { get; private set; }
        public ConnectionPhase Phase { get; private set; } = ConnectionPhase.Offline;
        public string LastError { get; private set; }
        public SteamLobbyService Lobby => lobby;
        public bool IsHost => Manager != null && Manager.IsHost;
        public int ConnectedPlayerCount
        {
            get { int count = 0; foreach (var player in approved.Values) if (player.Connected) count++; return count; }
        }
        public event Action Changed;
        public void CopyConnectedRoster(List<LobbyMemberWire> destination)
        {
            destination.Clear();
            foreach(var pair in approved)if(pair.Value.Connected)
                destination.Add(new LobbyMemberWire{ClientId=pair.Key,SteamId=pair.Value.SteamId,Ready=pair.Value.Ready,Host=pair.Key==NetworkManager.ServerClientId});
            destination.Sort((a,b)=>a.Host!=b.Host?(a.Host?-1:1):a.ClientId.CompareTo(b.ClientId));
        }
        public event Action SessionLost;
        public event Action<SteamLobbyCandidate> FriendLobbyFound;
        public event Action<ulong> InviteAvailable;
        private sealed class Reservation
        {
            public ulong SteamId;
            public double At;
            public bool Connected, Ready;
        }
        private readonly Dictionary<ulong, Reservation> approved = new Dictionary<ulong, Reservation>();
        private readonly List<ulong> expired = new List<ulong>();
        private NetworkConfiguration config;
        private SteamRuntime steam;
        public SteamRuntime ActiveSteamRuntime => steam; // Existing lifetime owner; this accessor never initializes Steam.
        private SteamLobbyService lobby;
        private string sessionNonce;
        private bool initialized, stopping, hostApprovalRejected;
        private readonly UnexpectedStopSignal unexpectedStop = new UnexpectedStopSignal();
        private double connectDeadline;

        public void Configure(NetworkConfiguration configuration)
        {
            if (initialized) throw new InvalidOperationException("Configure one coordinator once.");
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (Manager == null || Loopback == null || (configuration.SteamConfigured && SteamTransport == null))
                throw new InvalidOperationException("Bind one persistent NetworkManager, loopback, and a Steam transport only for configured Steam sessions.");
            if (Manager.IsListening || Manager.ShutdownInProgress || Manager.ConnectionApprovalCallback != null)
                throw new InvalidOperationException("The manager already has an active connection owner.");
            config = configuration;
            Manager.ConnectionApprovalCallback = Approve;
            Manager.OnClientConnectedCallback += OnConnected;
            Manager.OnClientDisconnectCallback += OnDisconnected;
            Manager.OnClientStopped += OnClientStopped;
            Manager.OnServerStopped += OnServerStopped;
            if (SteamTransport != null)
            {
                SteamTransport.SteamReady = () => config.SteamConfigured && steam != null && steam.Initialized;
                SteamTransport.MayAcceptPeer = peer => Mode == ConnectionMode.SteamHost && Phase == ConnectionPhase.Lobby &&
                    lobby != null && lobby.IsHost && lobby.ContainsMember(peer);
            }
            initialized = true;
        }
        public bool StartSolo()
        {
            if (!CanStart(false)) return false;
            approved.Clear(); sessionNonce = Guid.NewGuid().ToString("N");
            Mode = ConnectionMode.SoloLoopback; SetPhase(ConnectionPhase.Starting);
            try
            {
                // This exact NGO2.13.2 overload ignores command-line IP/port overrides.
                Loopback.SetConnectionData(true, "127.0.0.1", NetworkConfiguration.SoloPort, "127.0.0.1");
                ConfigureManager(Loopback, 0, sessionNonce);
                hostApprovalRejected = false;
                if (!Manager.StartHost() || hostApprovalRejected) return StartFailed("Не удалось открыть одиночную сессию.");
                SetPhase(ConnectionPhase.Lobby);
                return true;
            }
            catch (Exception) { return StartFailed("Не удалось открыть одиночную сетевую сессию."); }
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private LoopbackSmokeProfile diagnostic;
        // Explicit diagnostic entry. Never called by product solo/Steam controls; the remaining NGO lifecycle is shared.
        public bool StartDiagnosticLoopback(LoopbackSmokeProfile profile)
        {
            if (profile == null || !CanStart(false)) return false;
            diagnostic = profile; approved.Clear(); sessionNonce = profile.Nonce;
            Mode = profile.IsHost ? ConnectionMode.DiagnosticLoopbackHost : ConnectionMode.DiagnosticLoopbackClient;
            SetPhase(ConnectionPhase.Starting);
            try
            {
                Loopback.SetConnectionData(true, "127.0.0.1", LoopbackSmokeProfile.Port, "127.0.0.1");
                ConfigureManager(Loopback, 0, sessionNonce);
                connectDeadline = profile.IsHost ? 0 : Time.realtimeSinceStartupAsDouble + 20;
                hostApprovalRejected = false;
                bool started = profile.IsHost ? Manager.StartHost() : Manager.StartClient();
                if (!started || hostApprovalRejected) return StartFailed("Diagnostic loopback did not start.");
                if (profile.IsHost) SetPhase(ConnectionPhase.Lobby);
                return true;
            }
            catch (Exception) { return StartFailed("Diagnostic loopback initialization failed."); }
        }
#endif
        public bool CreateSteamLobby()
        {
            if (!CanStart() || !EnsureSteam()) return false;
            Mode = ConnectionMode.SteamHost; SetPhase(ConnectionPhase.Starting);
            if (!lobby.Create()) { SetPhase(ConnectionPhase.Failed); return false; }
            return true;
        }
        public bool JoinSteamLobby(ulong lobbyId)
        {
            if (!CanStart() || !EnsureSteam()) return false;
            Mode = ConnectionMode.SteamClient; SetPhase(ConnectionPhase.Starting);
            if (!lobby.Join(lobbyId)) { SetPhase(ConnectionPhase.Failed); return false; }
            return true;
        }
        public bool DiscoverSteamFriends()
        {
            if (!CanStart() || !EnsureSteam()) return false;
            lobby.DiscoverFriends(); return true;
        }
        private bool EnsureSteam()
        {
            if (!config.SteamConfigured) { LastError = "Steam не настроен для этой сборки. Одиночная игра доступна."; Changed?.Invoke(); return false; }
            if (steam == null)
            {
                steam = new SteamRuntime();
                steam.Failed += OnSteamFailed;
            }
            if (!steam.TryInitialize(config)) { LastError = steam.LastError; Changed?.Invoke(); return false; }
            if (lobby == null)
            {
                lobby = new SteamLobbyService(steam, config, () => Time.realtimeSinceStartupAsDouble);
                lobby.Joined += OnLobbyJoined; lobby.Failed += OnLobbyFailed; lobby.HostLost += OnHostLost;
                lobby.MembersChanged += OnLobbyMembersChanged;
                lobby.Discovered += ForwardCandidate; lobby.InviteRequested += ForwardInvite;
            }
            return true;
        }
        private void OnLobbyJoined(bool hosting)
        {
            if (stopping || Phase != ConnectionPhase.Starting || hosting != (Mode == ConnectionMode.SteamHost))
            { lobby.Leave(); return; }
            try
            {
                approved.Clear(); sessionNonce = lobby.Session;
                ConfigureManager(SteamTransport, lobby.LobbyId, sessionNonce);
                connectDeadline = Time.realtimeSinceStartupAsDouble + 20;
                if (hosting)
                {
                    if (!lobby.IsHost) { StartFailed("Steam не подтвердил хозяина лобби."); return; }
                    hostApprovalRejected = false;
                    if (!Manager.StartHost() || hostApprovalRejected) { StartFailed("Не удалось запустить Steam-хост."); return; }
                    connectDeadline = 0;
                    SetPhase(ConnectionPhase.Lobby);
                }
                else
                {
                    SteamTransport.ConnectToSteamID = lobby.HostSteamId;
                    if (!Manager.StartClient()) StartFailed("Не удалось подключиться к Steam-хосту.");
                }
            }
            catch (Exception) { StartFailed("Не удалось запустить сетевой транспорт Steam."); }
        }
        private void ConfigureManager(NetworkTransport selected, ulong lobbyId, string nonce)
        {
            if (Manager.IsListening || Manager.ShutdownInProgress) throw new InvalidOperationException("Stop the old manager before selecting a transport.");
            Manager.NetworkConfig.NetworkTransport = selected;
            Manager.NetworkConfig.ProtocolVersion = checked((ushort)NetworkConfiguration.ProtocolVersion);
            Manager.NetworkConfig.NetworkTopology = NetworkTopologyTypes.ClientServer;
            Manager.NetworkConfig.ConnectionApproval = true;
            Manager.NetworkConfig.PlayerPrefab = null;
            Manager.NetworkConfig.AutoSpawnPlayerPrefabClientSide = false;
            Manager.NetworkConfig.EnableSceneManagement = true;
            Manager.NetworkConfig.TickRate = 30;
            Manager.NetworkConfig.ClientConnectionBufferTimeout = 10;
            Manager.NetworkConfig.LoadSceneTimeOut = 30;
            Manager.NetworkConfig.ConnectionData = ConnectionHello.Create(config, lobbyId, nonce).Encode();
        }
        private void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = false; response.CreatePlayerObject = false; response.Pending = false;
            bool hostLocal = request.ClientNetworkId == NetworkManager.ServerClientId;
            ExpireReservations();
            ulong steamId = 0;
            bool member = false, duplicate = false;
            if (Mode == ConnectionMode.SteamHost && !hostLocal)
            {
                ulong transportId = Manager.GetTransportIdFromClientId(request.ClientNetworkId);
                if (transportId != ulong.MaxValue && SteamTransport.TryGetVerifiedPeer(transportId, out steamId))
                {
                    member = lobby != null && lobby.ContainsMember(steamId);
                    foreach (var item in approved.Values) if (item.SteamId == steamId) duplicate = true;
                }
            }
            ulong expectedLobby = Mode == ConnectionMode.SoloLoopback ? 0 : lobby?.LobbyId ?? 0;
            string rejection = AdmissionPolicy.Rejection(config, Mode, Phase, hostLocal, approved.Count,
                approved.ContainsKey(request.ClientNetworkId), steamId, member, duplicate, expectedLobby, sessionNonce, request.Payload);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Mode == ConnectionMode.DiagnosticLoopbackHost && (diagnostic == null || approved.Count >= diagnostic.PlayerCount))
                rejection = "diagnostic_roster_full";
#endif
            if (rejection != null)
            {
                response.Reason = rejection;
                // NGO always approves its own host: flag a failed local precondition and stop after StartHost returns.
                if (hostLocal) hostApprovalRejected = true;
                return;
            }
            approved.Add(request.ClientNetworkId, new Reservation
            { SteamId = hostLocal && Mode == ConnectionMode.SteamHost ? steam.LocalSteamId : steamId, At = Time.realtimeSinceStartupAsDouble, Ready = hostLocal });
            response.Approved = true;
        }
        private void OnConnected(ulong id)
        {
            if (approved.TryGetValue(id, out var reservation)) reservation.Connected = true;
            if (IsRemoteClientMode() && id == Manager.LocalClientId)
            { connectDeadline = 0; SetPhase(ConnectionPhase.Lobby); }
            Changed?.Invoke();
        }
        private void OnDisconnected(ulong id)
        {
            approved.Remove(id);
            if (IsRemoteClientMode() && !stopping && id == Manager.LocalClientId)
            { LastError = "Соединение с хозяином завершено."; OnHostLost(); }
            Changed?.Invoke();
        }
        private void OnClientStopped(bool wasHost)
        { if (!stopping && IsRemoteClientMode() && Phase != ConnectionPhase.Offline) OnHostLost(); }
        private void OnServerStopped(bool wasHost)
        { if (!stopping && IsOnlineOrSoloActive()) { LastError = "Сетевая сессия завершена."; OnHostLost(); } }
        private void ExpireReservations()
        {
            expired.Clear(); double now = Time.realtimeSinceStartupAsDouble;
            foreach (var pair in approved)
                if (!pair.Value.Connected && now - pair.Value.At >= 11) expired.Add(pair.Key);
            foreach (ulong id in expired) approved.Remove(id);
        }
        // Call only with Receive.SenderClientId from the later server RPC, not a client-claimed ID.
        public bool SetReadyFromServerRpc(ulong senderClientId, bool ready)
        {
            if (!IsHost || Phase != ConnectionPhase.Lobby || !approved.TryGetValue(senderClientId, out var player) || !player.Connected) return false;
            player.Ready = ready; Changed?.Invoke(); return true;
        }
        public bool CanBeginContract
        {
            get
            {
                if (!IsHost || Phase != ConnectionPhase.Lobby || approved.Count == 0) return false;
                foreach (var player in approved.Values) if (!player.Connected || !player.Ready) return false;
                return true;
            }
        }
        public void ResetLobbyReadiness()
        {
            if(!IsHost||Phase!=ConnectionPhase.Lobby)return;
            foreach(var pair in approved)pair.Value.Ready=pair.Key==NetworkManager.ServerClientId;
            Changed?.Invoke();
        }
        public bool EnterPreparing(string contractId)
        {
            if (!CanBeginContract) return false;
            SetPhase(ConnectionPhase.Preparing); // Close approval before Steam metadata/network scene work.
            if (Mode == ConnectionMode.SteamHost && !lobby.SetHostPhase(ConnectionPhase.Preparing, contractId))
            { StartFailed("Не удалось закрыть лобби перед контрактом."); return false; }
            return true;
        }
        public bool SetAuthoritativePhase(ConnectionPhase phase, string contractId)
        {
            if (!IsHost || (phase != ConnectionPhase.Running && phase != ConnectionPhase.Results && phase != ConnectionPhase.Lobby)) return false;
            bool valid = phase == ConnectionPhase.Running ? Phase == ConnectionPhase.Preparing :
                phase == ConnectionPhase.Results ? Phase == ConnectionPhase.Running : Phase == ConnectionPhase.Results || Phase == ConnectionPhase.Preparing;
            if (!valid) return false;
            if (phase == ConnectionPhase.Lobby)
                foreach (var pair in approved) pair.Value.Ready = pair.Key == NetworkManager.ServerClientId;
            SetPhase(phase);
            if (Mode == ConnectionMode.SteamHost && !lobby.SetHostPhase(phase, contractId))
            { StartFailed("Не удалось обновить состояние лобби."); return false; }
            return true;
        }
        public void StopSession()
        {
            // A loss listener can request stop reentrantly. Notify all game owners first; the signal's finally schedules shutdown.
            if (stopping || !initialized || unexpectedStop.IsNotifying) return;
            // The old SessionRoot may request stop again during destruction after the first
            // shutdown already drained. Do not start a second coroutine on that dying root.
            if (Mode == ConnectionMode.None && Phase == ConnectionPhase.Offline &&
                (Manager == null || (!Manager.IsListening && !Manager.ShutdownInProgress))) return;
            stopping = true;
            StartCoroutine(StopRoutine());
        }
        private IEnumerator StopRoutine()
        {
            stopping = true; connectDeadline = 0; SetPhase(ConnectionPhase.Stopping);
            if (lobby != null && lobby.IsHost) lobby.SetHostPhase(ConnectionPhase.Stopping, null);
            if (Manager != null && (Manager.IsListening || Manager.ShutdownInProgress)) Manager.Shutdown();
            double until = Time.realtimeSinceStartupAsDouble + 5;
            while (Manager != null && (Manager.IsListening || Manager.ShutdownInProgress) && Time.realtimeSinceStartupAsDouble < until) yield return null;
            bool stopped = Manager == null || (!Manager.IsListening && !Manager.ShutdownInProgress);
            SteamTransport?.Shutdown(); lobby?.Leave(); approved.Clear(); sessionNonce = null;
            Mode = ConnectionMode.None; stopping = false;
            if (!stopped) LastError = "Сетевой менеджер не завершился вовремя. Новый запуск заблокирован.";
            SetPhase(stopped ? ConnectionPhase.Offline : ConnectionPhase.Failed);
        }
        private bool CanStart(bool requireSteamIdle = true)
        {
            if (!initialized) throw new InvalidOperationException("Configure the coordinator before choosing a mode.");
            if (stopping || unexpectedStop.IsNotifying || Manager.IsListening || Manager.ShutdownInProgress || (Phase != ConnectionPhase.Offline && Phase != ConnectionPhase.Failed) || (lobby != null && (lobby.LobbyId != 0 || (requireSteamIdle && lobby.Busy))))
                return false;
            unexpectedStop.Reset(); LastError = null; return true;
        }
        public void StopUnexpected(string reason) => FailAndStop(reason);
        private void FailAndStop(string reason)
        {
            if (!initialized || stopping || unexpectedStop.WasNotified) return;
            LastError = reason; connectDeadline = 0;
            var listeners = new List<Action>();
            if (SessionLost != null) foreach (Action listener in SessionLost.GetInvocationList()) listeners.Add(listener);
            // Separate guard is committed before callbacks. Do not set stopping here: callbacks still need
            // the live authority to freeze/settle/clear, and StopSession must schedule its coroutine afterward.
            unexpectedStop.Notify(listeners, error => Debug.LogException(error, this), StopSession);
        }
        private bool StartFailed(string reason) { FailAndStop(reason); return false; }
        private void OnSteamFailed(string reason)
        {
            // A previously initialized Steam pump must never terminate a later offline solo session.
            if (Mode == ConnectionMode.SteamHost || Mode == ConnectionMode.SteamClient) FailAndStop(reason);
            else { LastError = reason; Changed?.Invoke(); }
        }
        private void OnLobbyFailed(string reason)
        {
            if (Mode == ConnectionMode.SteamHost || Mode == ConnectionMode.SteamClient) FailAndStop(reason);
            else { LastError = reason; Changed?.Invoke(); }
        }
        private void OnHostLost() => FailAndStop("Хозяин покинул сессию. Переноса игрового мира к другому игроку нет.");
        private void OnLobbyMembersChanged()
        {
            if (Mode == ConnectionMode.SteamHost && Manager.IsServer)
            {
                expired.Clear();
                foreach (var pair in approved)
                    if (pair.Key != NetworkManager.ServerClientId && !lobby.ContainsMember(pair.Value.SteamId)) expired.Add(pair.Key);
                foreach (ulong id in expired) Manager.DisconnectClient(id, "lobby_membership");
            }
            Changed?.Invoke();
        }
        private void ForwardCandidate(SteamLobbyCandidate candidate) => FriendLobbyFound?.Invoke(candidate);
        private void ForwardInvite(ulong id) => InviteAvailable?.Invoke(id); // UI must reject switching away from an active contract.
        private bool IsRemoteClientMode() => Mode == ConnectionMode.SteamClient || Mode == ConnectionMode.DiagnosticLoopbackClient;
        private bool IsOnlineOrSoloActive() => Mode != ConnectionMode.None && Phase != ConnectionPhase.Offline && Phase != ConnectionPhase.Failed;
        private void SetPhase(ConnectionPhase phase) { Phase = phase; Changed?.Invoke(); }
        private void Update()
        {
            steam?.Pump(); lobby?.PollTimeouts();
            if (connectDeadline > 0 && Time.realtimeSinceStartupAsDouble >= connectDeadline)
                StartFailed("Хозяин не подтвердил соединение вовремя.");
        }
        private void OnDestroy()
        {
            if (!initialized) return;
            stopping = true;
            if (Manager != null)
            {
                Manager.ConnectionApprovalCallback = null;
                Manager.OnClientConnectedCallback -= OnConnected; Manager.OnClientDisconnectCallback -= OnDisconnected;
                Manager.OnClientStopped -= OnClientStopped; Manager.OnServerStopped -= OnServerStopped;
                if (Manager.IsListening || Manager.ShutdownInProgress) Manager.Shutdown(true);
            }
            SteamTransport?.Shutdown(); lobby?.Dispose(); steam?.Dispose();
            Changed = null; SessionLost = null; FriendLobbyFound = null; InviteAvailable = null;
        }
    }
}
