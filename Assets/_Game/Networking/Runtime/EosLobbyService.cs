using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using UnityEngine;

namespace HowToSuck.Networking
{
    public sealed class EosLobbyService : IDisposable
    {
        private readonly EosRuntime runtime;
        private readonly NetworkConfiguration configuration;
        private readonly LobbyInterface api;
        public string LobbyId { get; private set; }
        public string Session { get; private set; }
        public ProductUserId HostUserId { get; private set; }
        public bool IsHost => LobbyId != null && HostUserId == runtime.LocalUserId;
        public bool Busy => operations != 0;
        public event Action<bool> Joined;
        public event Action MembersChanged, HostLost;
        public event Action<string> Failed;
        private readonly HashSet<string> members = new HashSet<string>(StringComparer.Ordinal);
        private readonly ulong memberNotify, lobbyNotify;
        private bool disposed, leaving, joined, updating;
        private uint generation;
        private int operations;
        private double deadline;
        private ConnectionPhase desiredPhase = ConnectionPhase.Lobby;
        private string desiredContract = "none";
        private uint metadataRevision;

        public EosLobbyService(EosRuntime runtime, NetworkConfiguration configuration)
        {
            this.runtime = runtime; this.configuration = configuration;
            api = runtime.Platform.GetLobbyInterface();
            var memberOptions = new AddNotifyLobbyMemberStatusReceivedOptions();
            memberNotify = api.AddNotifyLobbyMemberStatusReceived(ref memberOptions, null, OnMemberStatus);
            var lobbyOptions = new AddNotifyLobbyUpdateReceivedOptions();
            lobbyNotify = api.AddNotifyLobbyUpdateReceived(ref lobbyOptions, null, (ref LobbyUpdateReceivedCallbackInfo info) => {
                if (!disposed && !leaving && joined && info.LobbyId.ToString() == LobbyId) RefreshMembership(false);
            });
        }
        public bool Create()
        {
            if (disposed || Busy || LobbyId != null) return false;
            leaving = false; uint epoch = ++generation;
            string id = EosRoomCode.Create(); Session = Guid.NewGuid().ToString("N");
            deadline = Time.realtimeSinceStartupAsDouble + 30; operations++;
            var options = new CreateLobbyOptions {
                LocalUserId = runtime.LocalUserId, LobbyId = id, MaxLobbyMembers = NetworkConfiguration.MaxPlayers,
                PermissionLevel = LobbyPermissionLevel.Inviteonly, BucketId = NetworkConfiguration.ProductKey,
                DisableHostMigration = true, PresenceEnabled = false, EnableRTCRoom = false,
                EnableJoinById = false, AllowInvites = false
            };
            api.CreateLobby(ref options, null, (ref CreateLobbyCallbackInfo info) => {
                operations--;
                if (disposed) return;
                if (epoch != generation || leaving)
                { if (info.ResultCode == Result.Success) ReleaseLobby(info.LobbyId.ToString(), true); return; }
                if (info.ResultCode != Result.Success) { Fail("Не удалось создать комнату Epic", info.ResultCode); return; }
                LobbyId = info.LobbyId.ToString(); HostUserId = runtime.LocalUserId;
                desiredPhase = ConnectionPhase.Lobby; desiredContract = "none";
                Publish(true);
            });
            return true;
        }
        public bool Join(string code)
        {
            if (disposed || Busy || LobbyId != null || !EosRoomCode.TryNormalize(code, out var id)) return false;
            leaving = false; uint epoch = ++generation;
            deadline = Time.realtimeSinceStartupAsDouble + 30;
            var create = new CreateLobbySearchOptions { MaxResults = 1 };
            if (api.CreateLobbySearch(ref create, out var search) != Result.Success) return false;
            var filter = new LobbySearchSetLobbyIdOptions { LobbyId = id };
            if (search.SetLobbyId(ref filter) != Result.Success) { search.Release(); return false; }
            var find = new LobbySearchFindOptions { LocalUserId = runtime.LocalUserId }; operations++;
            search.Find(ref find, null, (ref LobbySearchFindCallbackInfo found) => {
                operations--;
                try
                {
                    if (disposed || leaving || epoch != generation) return;
                    if (found.ResultCode != Result.Success) { Fail("Не удалось найти комнату Epic", found.ResultCode); return; }
                    var copy = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = 0 };
                    if (search.CopySearchResultByIndex(ref copy, out var details) != Result.Success)
                    { Fail("Комната не найдена, заполнена или уже начала контракт"); return; }
                    try
                    {
                        if (!Validate(details, true, out var host, out var session))
                        { Fail("Комната недоступна или использует другую версию игры"); return; }
                        HostUserId = host; Session = session;
                        var join = new JoinLobbyOptions { LocalUserId = runtime.LocalUserId, LobbyDetailsHandle = details, PresenceEnabled = false };
                        operations++;
                        api.JoinLobby(ref join, null, (ref JoinLobbyCallbackInfo info) => {
                            operations--;
                            if (disposed) return;
                            if (leaving || epoch != generation)
                            { if (info.ResultCode == Result.Success) ReleaseLobby(info.LobbyId.ToString(), false); return; }
                            if (info.ResultCode != Result.Success) { Fail("Не удалось войти в комнату Epic", info.ResultCode); return; }
                            LobbyId = info.LobbyId.ToString();
                            if (LobbyId != id || !RefreshMembership(true)) { Fail("Состояние комнаты изменилось. Подключитесь заново"); return; }
                            joined = true; deadline = 0; Joined?.Invoke(false);
                        });
                    }
                    finally { details.Release(); }
                }
                finally { search.Release(); }
            });
            return true;
        }
        private bool Validate(LobbyDetails details, bool requireOpen, out ProductUserId owner, out string session)
        {
            owner = null; session = null;
            var infoOptions = new LobbyDetailsCopyInfoOptions();
            if (details.CopyInfo(ref infoOptions, out var info) != Result.Success || !info.HasValue) return false;
            owner = info.Value.LobbyOwnerUserId;
            if (owner == null || !owner.IsValid() || info.Value.MaxMembers != NetworkConfiguration.MaxPlayers ||
                info.Value.AllowHostMigration || info.Value.BucketId.ToString() != NetworkConfiguration.ProductKey) return false;
            session = Read(details, LobbyMetadata.Session);
            return Read(details, LobbyMetadata.Product) == NetworkConfiguration.ProductKey &&
                Read(details, LobbyMetadata.Build) == configuration.BuildId && Read(details, LobbyMetadata.Content) == configuration.ContentHash &&
                Read(details, LobbyMetadata.Protocol) == NetworkConfiguration.Number(NetworkConfiguration.ProtocolVersion) &&
                Read(details, LobbyMetadata.Host) == owner.ToString() && NetworkConfiguration.Hex(session, 32) &&
                (!requireOpen || Read(details, LobbyMetadata.Phase) == ConnectionPhase.Lobby.ToString());
        }
        private static string Read(LobbyDetails details, string key)
        {
            var options = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = key };
            return details.CopyAttributeByKey(ref options, out var attribute) == Result.Success && attribute?.Data != null
                ? attribute.Value.Data.Value.Value.AsUtf8?.ToString() : null;
        }
        public bool ContainsMember(string user) => !disposed && !leaving && user != null && members.Contains(user);
        public bool ContainsMember(ProductUserId user) => user != null && ContainsMember(user.ToString());
        private bool RefreshMembership(bool requireOpen)
        {
            var copy = new CopyLobbyDetailsHandleOptions { LocalUserId = runtime.LocalUserId, LobbyId = LobbyId };
            if (api.CopyLobbyDetailsHandle(ref copy, out var details) != Result.Success) return false;
            try
            {
                if (!Validate(details, requireOpen, out var owner, out var session) || owner != HostUserId || session != Session)
                { if (joined) HostLost?.Invoke(); return false; }
                var countOptions = new LobbyDetailsGetMemberCountOptions();
                uint count = details.GetMemberCount(ref countOptions);
                if (count > NetworkConfiguration.MaxPlayers) return false;
                members.Clear();
                for (uint i = 0; i < count; i++)
                {
                    var member = new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
                    var id = details.GetMemberByIndex(ref member);
                    if (id != null && id.IsValid()) members.Add(id.ToString());
                }
                if (!members.Contains(runtime.LocalUserId.ToString()) || !members.Contains(HostUserId.ToString()))
                { if (joined) HostLost?.Invoke(); return false; }
                MembersChanged?.Invoke(); return true;
            }
            finally { details.Release(); }
        }
        private void OnMemberStatus(ref LobbyMemberStatusReceivedCallbackInfo info)
        {
            if (disposed || leaving || !joined || info.LobbyId.ToString() != LobbyId) return;
            bool departed = info.CurrentStatus == LobbyMemberStatus.Left || info.CurrentStatus == LobbyMemberStatus.Disconnected ||
                info.CurrentStatus == LobbyMemberStatus.Kicked || info.CurrentStatus == LobbyMemberStatus.Closed;
            if ((departed && (info.TargetUserId == HostUserId || info.TargetUserId == runtime.LocalUserId)) ||
                (info.CurrentStatus == LobbyMemberStatus.Promoted && info.TargetUserId != HostUserId))
            { HostLost?.Invoke(); return; }
            // Remove synchronously; a delayed cache refresh must never re-admit a known departed peer.
            if (departed) { members.Remove(info.TargetUserId.ToString()); MembersChanged?.Invoke(); }
            else if (!RefreshMembership(false)) Fail("Не удалось проверить участников комнаты Epic");
        }
        public bool SetHostPhase(ConnectionPhase phase, string contract)
        {
            if (!IsHost || disposed || leaving || !NetworkConfiguration.ValidToken(contract ?? "none", 64)) return false;
            desiredPhase = phase; desiredContract = contract ?? "none"; metadataRevision++;
            if (!updating) Publish(false);
            return true;
        }
        private void Publish(bool initial)
        {
            var options = new UpdateLobbyModificationOptions { LocalUserId = runtime.LocalUserId, LobbyId = LobbyId };
            var result = api.UpdateLobbyModification(ref options, out var modification);
            if (result != Result.Success) { Fail("Не удалось обновить комнату Epic", result); return; }
            try
            {
                var values = new Dictionary<string, string> {
                    [LobbyMetadata.Product] = NetworkConfiguration.ProductKey, [LobbyMetadata.Build] = configuration.BuildId,
                    [LobbyMetadata.Content] = configuration.ContentHash, [LobbyMetadata.Protocol] = NetworkConfiguration.Number(NetworkConfiguration.ProtocolVersion),
                    [LobbyMetadata.Host] = HostUserId.ToString(), [LobbyMetadata.Session] = Session,
                    [LobbyMetadata.Phase] = desiredPhase.ToString(), [LobbyMetadata.Contract] = desiredContract
                };
                foreach (var value in values)
                {
                    var attribute = new LobbyModificationAddAttributeOptions {
                        Attribute = new AttributeData { Key = value.Key, Value = value.Value }, Visibility = LobbyAttributeVisibility.Public
                    };
                    result = modification.AddAttribute(ref attribute);
                    if (result != Result.Success) { Fail("Не удалось записать параметры комнаты Epic", result); return; }
                }
                var permission = new LobbyModificationSetPermissionLevelOptions {
                    PermissionLevel = desiredPhase == ConnectionPhase.Lobby ? LobbyPermissionLevel.Publicadvertised : LobbyPermissionLevel.Inviteonly
                };
                result = modification.SetPermissionLevel(ref permission);
                if (result != Result.Success) { Fail("Не удалось закрыть вход в комнату Epic", result); return; }
                var update = new UpdateLobbyOptions { LobbyModificationHandle = modification };
                uint epoch = generation, revision = metadataRevision;
                updating = true; operations++; deadline = Time.realtimeSinceStartupAsDouble + 30;
                api.UpdateLobby(ref update, null, (ref UpdateLobbyCallbackInfo info) => {
                    operations--; updating = false;
                    if (disposed || leaving || epoch != generation) return;
                    if (info.ResultCode != Result.Success) { Fail("Не удалось обновить комнату Epic", info.ResultCode); return; }
                    deadline = 0;
                    if (initial)
                    {
                        if (!RefreshMembership(true)) { Fail("Epic не подтвердил созданную комнату"); return; }
                        joined = true; Joined?.Invoke(true);
                    }
                    if (revision != metadataRevision && !updating && !leaving) Publish(false);
                });
            }
            finally { modification.Release(); }
        }
        public void PollTimeouts()
        {
            if (!disposed && !leaving && deadline > 0 && Time.realtimeSinceStartupAsDouble >= deadline)
            { deadline = 0; Fail("Epic не ответил на запрос комнаты вовремя"); }
        }
        private void Fail(string message, Result? result = null)
        { deadline = 0; Failed?.Invoke(message + (result.HasValue ? " (" + result.Value + ")." : ".")); }
        public void Leave()
        {
            if (disposed || leaving) return;
            leaving = true; generation++; deadline = 0;
            string id = LobbyId; bool host = IsHost;
            LobbyId = null; joined = false; members.Clear();
            if (id != null) ReleaseLobby(id, host);
        }
        private void ReleaseLobby(string id, bool host)
        {
            operations++;
            if (host)
            {
                var options = new DestroyLobbyOptions { LocalUserId = runtime.LocalUserId, LobbyId = id };
                api.DestroyLobby(ref options, null, (ref DestroyLobbyCallbackInfo _) => operations--);
            }
            else
            {
                var options = new LeaveLobbyOptions { LocalUserId = runtime.LocalUserId, LobbyId = id };
                api.LeaveLobby(ref options, null, (ref LeaveLobbyCallbackInfo _) => operations--);
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            Leave(); disposed = true;
            api.RemoveNotifyLobbyMemberStatusReceived(memberNotify); api.RemoveNotifyLobbyUpdateReceived(lobbyNotify);
            Joined = null; MembersChanged = null; HostLost = null; Failed = null;
        }
    }
}
