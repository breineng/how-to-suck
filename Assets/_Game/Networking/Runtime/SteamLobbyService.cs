using System;
using System.Collections.Generic;
using Steamworks;

namespace HowToSuck.Networking
{
    public readonly struct SteamLobbyCandidate
    {
        public readonly ulong LobbyId, HostSteamId;
        public readonly string Session;
        public SteamLobbyCandidate(ulong lobby, ulong host, string session) { LobbyId = lobby; HostSteamId = host; Session = session; }
    }

    public sealed class SteamLobbyService : IDisposable
    {
        public ulong LobbyId { get; private set; }
        public ulong HostSteamId { get; private set; }
        public string Session { get; private set; }
        public bool IsHost => LobbyId != 0 && HostSteamId == runtime.LocalSteamId;
        public bool Busy => pendingDataLobby != 0 || createCall != null || joinCall != null;
        public event Action<bool> Joined;
        public event Action MembersChanged;
        public event Action<string> Failed;
        public event Action<SteamLobbyCandidate> Discovered;
        public event Action<ulong> InviteRequested;
        public event Action HostLost;
        private readonly SteamRuntime runtime;
        private readonly NetworkConfiguration config;
        private readonly Func<double> clock;
        private readonly Callback<LobbyDataUpdate_t> dataUpdated;
        private readonly Callback<LobbyChatUpdate_t> chatUpdated;
        private readonly Callback<GameLobbyJoinRequested_t> invite;
        private CallResult<LobbyCreated_t> createCall;
        private CallResult<LobbyEnter_t> joinCall;
        private readonly Dictionary<ulong, double> discovery = new Dictionary<ulong, double>();
        private readonly List<ulong> expiredDiscovery = new List<ulong>();
        private ulong pendingDataLobby;
        private double deadline;
        private uint generation;
        private bool disposed;
        private static readonly string[] Keys = { LobbyMetadata.Product, LobbyMetadata.App, LobbyMetadata.Protocol,
            LobbyMetadata.Build, LobbyMetadata.Content, LobbyMetadata.Session, LobbyMetadata.Host, LobbyMetadata.Phase, LobbyMetadata.Contract };

        public SteamLobbyService(SteamRuntime steamRuntime, NetworkConfiguration configuration, Func<double> monotonicClock)
        {
            runtime = steamRuntime ?? throw new ArgumentNullException(nameof(steamRuntime));
            config = configuration ?? throw new ArgumentNullException(nameof(configuration));
            clock = monotonicClock ?? throw new ArgumentNullException(nameof(monotonicClock));
            RequireSteam();
            dataUpdated = Callback<LobbyDataUpdate_t>.Create(OnDataUpdated);
            chatUpdated = Callback<LobbyChatUpdate_t>.Create(OnChatUpdated);
            invite = Callback<GameLobbyJoinRequested_t>.Create(value => { if (!disposed) InviteRequested?.Invoke(value.m_steamIDLobby.m_SteamID); });
        }
        public bool Create()
        {
            RequireSteam();
            if (LobbyId != 0 || Busy) return Fail("Предыдущее лобби или запрос ещё не завершены.");
            uint epoch = ++generation; deadline = clock() + 20;
            createCall = CallResult<LobbyCreated_t>.Create((value, ioFailure) => OnCreated(epoch, value, ioFailure));
            var call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, NetworkConfiguration.MaxPlayers);
            if (call == SteamAPICall_t.Invalid) { createCall.Dispose(); createCall = null; return Fail("Steam не принял создание лобби."); }
            createCall.Set(call);
            return true;
        }
        public bool Join(ulong lobby)
        {
            RequireSteam();
            if (lobby == 0 || LobbyId != 0 || Busy) return Fail("Невозможно начать вход в это лобби сейчас.");
            generation++; pendingDataLobby = lobby; deadline = clock() + 20;
            if (!SteamMatchmaking.RequestLobbyData(new CSteamID(lobby)))
            { pendingDataLobby = 0; return Fail("Не удалось запросить данные лобби."); }
            return true;
        }
        public void DiscoverFriends()
        {
            RequireSteam(); discovery.Clear();
            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < count && discovery.Count < 128; i++)
            {
                var friend = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                if (!SteamFriends.GetFriendGamePlayed(friend, out var game) || game.m_gameID.AppID().m_AppId != config.AppId ||
                    !game.m_steamIDLobby.IsValid() || discovery.ContainsKey(game.m_steamIDLobby.m_SteamID)) continue;
                ulong lobby = game.m_steamIDLobby.m_SteamID;
                discovery.Add(lobby, clock() + 10);
                if (!SteamMatchmaking.RequestLobbyData(game.m_steamIDLobby)) discovery.Remove(lobby);
            }
        }
        public void InviteFriendsOverlay()
        {
            RequireSteam();
            if (LobbyId != 0) SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(LobbyId));
        }
        public bool SetHostPhase(ConnectionPhase phase, string contractId)
        {
            RequireSteam();
            if (!IsHost || SteamMatchmaking.GetLobbyOwner(new CSteamID(LobbyId)).m_SteamID != HostSteamId) return false;
            bool joinable = phase == ConnectionPhase.Lobby;
            // Close before changing phase; approval separately closes synchronously in the coordinator.
            if (!SteamMatchmaking.SetLobbyJoinable(new CSteamID(LobbyId), false)) return false;
            if (!NetworkConfiguration.ValidToken(contractId ?? "none", 64)) return false;
            bool okay = SteamMatchmaking.SetLobbyData(new CSteamID(LobbyId), LobbyMetadata.Contract, contractId ?? "none") &&
                SteamMatchmaking.SetLobbyData(new CSteamID(LobbyId), LobbyMetadata.Phase, phase.ToString());
            return okay && (!joinable || SteamMatchmaking.SetLobbyJoinable(new CSteamID(LobbyId), true));
        }
        public bool ContainsMember(ulong steamId)
        {
            if (disposed || !runtime.Initialized || LobbyId == 0 || steamId == 0) return false;
            var lobby = new CSteamID(LobbyId);
            int count = SteamMatchmaking.GetNumLobbyMembers(lobby);
            for (int i = 0; i < count && i < NetworkConfiguration.MaxPlayers; i++)
                if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID == steamId) return true;
            return false;
        }
        public void PollTimeouts()
        {
            if (disposed) return;
            double now = clock();
            if (deadline > 0 && now >= deadline)
            {
                // Native async calls cannot be cancelled. Retain their call result to leave any late successful lobby.
                generation++; pendingDataLobby = 0; deadline = 0;
                Fail("Steam не завершил запрос вовремя. Одиночная игра остаётся доступной.");
            }
            expiredDiscovery.Clear();
            foreach (var pair in discovery) if (now >= pair.Value) expiredDiscovery.Add(pair.Key);
            foreach (ulong lobby in expiredDiscovery) discovery.Remove(lobby);
        }
        public void Leave()
        {
            if (disposed) return;
            generation++; deadline = 0; pendingDataLobby = 0;
            ulong leaving = LobbyId;
            LobbyId = HostSteamId = 0; Session = null;
            if (runtime.Initialized && leaving != 0) SteamMatchmaking.LeaveLobby(new CSteamID(leaving));
        }
        private void OnCreated(uint epoch, LobbyCreated_t value, bool ioFailure)
        {
            var finishedCall = createCall; createCall = null; finishedCall?.Dispose();
            bool okay = !ioFailure && value.m_eResult == EResult.k_EResultOK && value.m_ulSteamIDLobby != 0;
            if (disposed || epoch != generation)
            { if (okay && runtime.Initialized) SteamMatchmaking.LeaveLobby(new CSteamID(value.m_ulSteamIDLobby)); return; }
            deadline = 0;
            if (!okay) { Fail("Steam не создал лобби."); return; }
            LobbyId = value.m_ulSteamIDLobby; HostSteamId = runtime.LocalSteamId; Session = Guid.NewGuid().ToString("N");
            var lobby = new CSteamID(LobbyId);
            bool written = SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID == HostSteamId && SteamMatchmaking.SetLobbyJoinable(lobby, false);
            foreach (var pair in LobbyMetadata.Create(config, HostSteamId, Session))
                written &= SteamMatchmaking.SetLobbyData(lobby, pair.Key, pair.Value);
            written &= SteamMatchmaking.SetLobbyJoinable(lobby, true);
            if (!written) { Leave(); Fail("Steam не сохранил конфигурацию лобби."); return; }
            Joined?.Invoke(true);
        }
        private void OnDataUpdated(LobbyDataUpdate_t value)
        {
            if (disposed || !runtime.Initialized || value.m_ulSteamIDMember != value.m_ulSteamIDLobby) return;
            ulong id = value.m_ulSteamIDLobby;
            if (discovery.Remove(id) && value.m_bSuccess != 0 && TryCandidate(id, true, false, out var candidate)) Discovered?.Invoke(candidate);
            if (id == LobbyId && LobbyId != 0)
            {
                if (!TryCandidate(id, false, true, out var current) || current.HostSteamId != HostSteamId || current.Session != Session)
                    LoseHost();
            }
            if (id != pendingDataLobby) return;
            pendingDataLobby = 0;
            if (value.m_bSuccess == 0 || !TryCandidate(id, true, false, out var target))
            { deadline = 0; Fail("Лобби относится к другой игре/сборке либо контракт уже начался."); return; }
            uint epoch = generation;
            joinCall = CallResult<LobbyEnter_t>.Create((entered, failure) => OnEntered(epoch, target, entered, failure));
            var call = SteamMatchmaking.JoinLobby(new CSteamID(id));
            if (call == SteamAPICall_t.Invalid) { joinCall.Dispose(); joinCall = null; deadline = 0; Fail("Steam не принял вход в лобби."); return; }
            joinCall.Set(call);
        }
        private void OnEntered(uint epoch, SteamLobbyCandidate expected, LobbyEnter_t value, bool ioFailure)
        {
            var finishedCall = joinCall; joinCall = null; finishedCall?.Dispose();
            bool entered = !ioFailure && value.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;
            if (disposed || epoch != generation)
            { if (entered && runtime.Initialized) SteamMatchmaking.LeaveLobby(new CSteamID(value.m_ulSteamIDLobby)); return; }
            deadline = 0;
            if (!entered) { Fail("Не удалось войти в лобби Steam."); return; }
            if (value.m_ulSteamIDLobby != expected.LobbyId || !TryCandidate(expected.LobbyId, true, true, out var actual) ||
                actual.HostSteamId != expected.HostSteamId || actual.Session != expected.Session)
            { SteamMatchmaking.LeaveLobby(new CSteamID(value.m_ulSteamIDLobby)); Fail("Лобби изменилось во время подключения."); return; }
            LobbyId = actual.LobbyId; HostSteamId = actual.HostSteamId; Session = actual.Session;
            if (!ContainsMember(runtime.LocalSteamId)) { Leave(); Fail("Steam не подтвердил членство в лобби."); return; }
            Joined?.Invoke(false);
        }
        private bool TryCandidate(ulong id, bool requireLobby, bool verifyMemberOwner, out SteamLobbyCandidate candidate)
        {
            candidate = default;
            var lobby = new CSteamID(id);
            if (!lobby.IsValid() || SteamMatchmaking.GetLobbyMemberLimit(lobby) != NetworkConfiguration.MaxPlayers) return false;
            var values = new Dictionary<string, string>();
            foreach (string key in Keys)
            { string value = SteamMatchmaking.GetLobbyData(lobby, key); if (value == null || value.Length > 320) return false; values.Add(key, value); }
            if (!LobbyMetadata.TryReadAdvertisement(config, values, requireLobby, out var host, out var session)) return false;
            // Valve makes GetLobbyOwner available only to members; verify it after LobbyEnter.
            if (verifyMemberOwner && SteamMatchmaking.GetLobbyOwner(lobby).m_SteamID != host) return false;
            candidate = new SteamLobbyCandidate(id, host, session); return true;
        }
        private void OnChatUpdated(LobbyChatUpdate_t value)
        {
            if (disposed || LobbyId == 0 || value.m_ulSteamIDLobby != LobbyId) return;
            if (SteamMatchmaking.GetLobbyOwner(new CSteamID(LobbyId)).m_SteamID != HostSteamId || !ContainsMember(HostSteamId))
            { LoseHost(); return; }
            MembersChanged?.Invoke();
        }
        private void LoseHost() { Leave(); HostLost?.Invoke(); }
        private bool Fail(string reason) { Failed?.Invoke(reason); return false; }
        private void RequireSteam()
        { if (disposed) throw new ObjectDisposedException(nameof(SteamLobbyService)); if (!runtime.Initialized) throw new InvalidOperationException("Initialize Steam explicitly first."); }
        public void Dispose()
        {
            if (disposed) return;
            Leave(); disposed = true;
            createCall?.Dispose(); joinCall?.Dispose(); dataUpdated.Dispose(); chatUpdated.Dispose(); invite.Dispose();
            createCall = null; joinCall = null; discovery.Clear();
            Joined = null; MembersChanged = null; Failed = null; Discovered = null; InviteRequested = null; HostLost = null;
        }
    }
}