// Derived from Unity-Technologies/multiplayer-community-contributions SteamNetworkingSocketsTransport1.0.1.
// Upstream f5d80002708c530ad5b95b66f4c20751e0925123. MIT notice is in LICENSE.md; see upstream.diff.
// This listen-server-only compatibility fork retains Steam P2P, peer SteamIDs, NGO serverID0 and the delivery trailer.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace HowToSuck.Networking
{
    public sealed class HowToSuckSteamTransport : NetworkTransport
    {
        private sealed class SteamConnectionData
        {
            public CSteamID Id;
            public HSteamNetConnection Connection;
            public bool ReportedConnected;
            public readonly SequencedReceiveGate Sequence = new SequencedReceiveGate();
        }
        public ulong ConnectToSteamID;
        public SteamNetworkingConfigValue_t[] Options = Array.Empty<SteamNetworkingConfigValue_t>();
        public Func<bool> SteamReady;
        public Func<ulong, bool> MayAcceptPeer;
        public override ulong ServerClientId => 0;
        public override bool IsSupported => SteamReady != null && SteamReady();
        private const int MaximumFrameBytes = 512 * 1024;
        private Callback<SteamNetConnectionStatusChangedCallback_t> callback;
        private HSteamListenSocket listenSocket;
        private SteamConnectionData serverUser;
        private readonly Dictionary<ulong, SteamConnectionData> connectionMapping = new Dictionary<ulong, SteamConnectionData>();
        private readonly Queue<SteamNetConnectionStatusChangedCallback_t> statusQueue = new Queue<SteamNetConnectionStatusChangedCallback_t>();
        private readonly Queue<ulong> localDisconnects = new Queue<ulong>();
        private readonly List<SteamConnectionData> receivePeers = new List<SteamConnectionData>(3);
        private int receiveCursor;
        private readonly IntPtr[] receivePointer = new IntPtr[1];
        private bool isServer, started, queueOverflow;
        private uint generation;

        public override void Initialize(NetworkManager networkManager = null)
        {
            if (!IsSupported) throw new InvalidOperationException("Explicitly initialize Steam before starting this transport.");
        }
        public override bool StartServer()
        {
            if (started || !IsSupported) return false;
            try
            {
                BeginCallbacks();
                isServer = true;
                listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(0, Options.Length, Options);
                if (listenSocket == HSteamListenSocket.Invalid) { Shutdown(); return false; }
                return true;
            }
            catch (Exception) { Shutdown(); return false; }
        }
        public override bool StartClient()
        {
            if (started || !IsSupported || !new CSteamID(ConnectToSteamID).IsValid()) return false;
            try
            {
                BeginCallbacks(); isServer = false;
                var identity = new SteamNetworkingIdentity(); identity.SetSteamID(new CSteamID(ConnectToSteamID));
                var handle = SteamNetworkingSockets.ConnectP2P(ref identity, 0, Options.Length, Options);
                if (handle == HSteamNetConnection.Invalid) { Shutdown(); return false; }
                serverUser = new SteamConnectionData { Id = new CSteamID(ConnectToSteamID), Connection = handle };
                connectionMapping.Add(ConnectToSteamID, serverUser);
                return true;
            }
            catch (Exception) { Shutdown(); return false; }
        }
        private void BeginCallbacks()
        {
            Options ??= Array.Empty<SteamNetworkingConfigValue_t>();
            statusQueue.Clear(); localDisconnects.Clear(); receiveCursor = 0; queueOverflow = false;
            uint epoch = ++generation;
            started = true;
            callback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(value =>
            {
                if (!started || generation != epoch) return;
                // Bound pending callbacks; a flood cannot allocate without limit before NGO polls.
                if (statusQueue.Count >= 256)
                {
                    queueOverflow = true;
                    if (value.m_info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
                        Close(value.m_hConn, "Connection queue full");
                    return;
                }
                statusQueue.Enqueue(value);
            });
        }
        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = 0; payload = default; receiveTime = Time.realtimeSinceStartup;
            if (!started || !IsSupported) return NetworkEvent.Nothing;
            if (queueOverflow) { Shutdown(); return NetworkEvent.TransportFailure; }
            if (localDisconnects.Count > 0) { clientId = localDisconnects.Dequeue(); return NetworkEvent.Disconnect; }
            while (statusQueue.Count > 0)
            {
                var value = statusQueue.Dequeue();
                ulong peer = value.m_info.m_identityRemote.GetSteamID64();
                var state = value.m_info.m_eState;
                if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
                {
                    // ConnectP2P also reports Connecting for our outgoing handle. It is not a server admission request.
                    if (!isServer)
                    {
                        if (serverUser == null || serverUser.Connection != value.m_hConn) Close(value.m_hConn, "Unexpected outgoing connection");
                        continue;
                    }
                    bool allowed = isServer && value.m_info.m_hListenSocket == listenSocket && listenSocket != HSteamListenSocket.Invalid &&
                        peer != 0 && !connectionMapping.ContainsKey(peer) && connectionMapping.Count < NetworkConfiguration.MaxPlayers - 1 &&
                        MayAcceptPeer != null && MayAcceptPeer(peer);
                    if (!allowed) { Close(value.m_hConn, "Lobby admission closed"); continue; }
                    if (SteamNetworkingSockets.AcceptConnection(value.m_hConn) != EResult.k_EResultOK)
                    { Close(value.m_hConn, "Connection not accepted"); continue; }
                    connectionMapping.Add(peer, new SteamConnectionData { Id = new CSteamID(peer), Connection = value.m_hConn });
                    continue;
                }
                // Old handles for a repeated SteamID may arrive after a fresh session has already started.
                if (!connectionMapping.TryGetValue(peer, out var connection) || connection.Connection != value.m_hConn) continue;
                if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
                {
                    if (connection.ReportedConnected) continue;
                    connection.ReportedConnected = true; clientId = peer;
                    return NetworkEvent.Connect;
                }
                if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer ||
                    state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
                {
                    Close(connection.Connection, "Peer disconnected"); connectionMapping.Remove(peer);
                    if (connection == serverUser) serverUser = null;
                    clientId = peer;
                    return NetworkEvent.Disconnect;
                }
            }
            receivePeers.Clear();
            foreach (var connection in connectionMapping.Values) if (connection.ReportedConnected) receivePeers.Add(connection);
            for (int peerIndex = 0; peerIndex < receivePeers.Count; peerIndex++)
            {
                // Resume with the next peer after returning data; one continuously busy peer cannot monopolize polling.
                if (receiveCursor >= receivePeers.Count) receiveCursor = 0;
                var connection = receivePeers[receiveCursor++];
                // A small bounded drain skips malformed/out-of-order frames without starving other peers.
                for (int i = 0; i < 16; i++)
                {
                    receivePointer[0] = IntPtr.Zero;
                    int count = SteamNetworkingSockets.ReceiveMessagesOnConnection(connection.Connection, receivePointer, 1);
                    if (count <= 0 || receivePointer[0] == IntPtr.Zero) break;
                    IntPtr pointer = receivePointer[0];
                    try
                    {
                        var data = Marshal.PtrToStructure<SteamNetworkingMessage_t>(pointer);
                        if (data.m_cbSize < 1 || data.m_cbSize > MaximumFrameBytes || data.m_pData == IntPtr.Zero || data.m_idxLane != 0) continue;
                        byte tag = Marshal.ReadByte(data.m_pData, data.m_cbSize - 1);
                        if (!ValidDelivery(tag)) continue;
                        if ((NetworkDelivery)tag == NetworkDelivery.UnreliableSequenced && !connection.Sequence.Accept(data.m_nMessageNumber)) continue;
                        var bytes = new byte[data.m_cbSize - 1];
                        if (bytes.Length != 0) Marshal.Copy(data.m_pData, bytes, 0, bytes.Length);
                        clientId = connection.Id.m_SteamID; payload = new ArraySegment<byte>(bytes);
                        return NetworkEvent.Data;
                    }
                    finally { SteamNetworkingMessage_t.Release(pointer); }
                }
            }
            return NetworkEvent.Nothing;
        }
        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery delivery)
        {
            if (!started || !IsSupported) return;
            if (clientId == ServerClientId)
            {
                if (serverUser == null) return;
                clientId = serverUser.Id.m_SteamID;
            }
            if (!connectionMapping.TryGetValue(clientId, out var connection)) return;
            if (payload.Count >= MaximumFrameBytes || (payload.Count != 0 && payload.Array == null) || (int)delivery < 0 || (int)delivery > byte.MaxValue || !ValidDelivery((byte)delivery))
                throw new ArgumentOutOfRangeException(nameof(payload), "Steam frame exceeds transport budget or has invalid delivery.");
            byte[] data = new byte[payload.Count + 1];
            if (payload.Count != 0) Array.Copy(payload.Array, payload.Offset, data, 0, payload.Count);
            data[payload.Count] = (byte)delivery;
            int flag = Constants.k_nSteamNetworkingSend_Unreliable;
            switch (delivery)
            {
                case NetworkDelivery.Reliable:
                case NetworkDelivery.ReliableFragmentedSequenced: flag = Constants.k_nSteamNetworkingSend_Reliable; break;
                case NetworkDelivery.ReliableSequenced: flag = Constants.k_nSteamNetworkingSend_ReliableNoNagle; break;
                case NetworkDelivery.UnreliableSequenced: flag = Constants.k_nSteamNetworkingSend_UnreliableNoNagle; break;
            }
            GCHandle pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
            EResult result;
            try { result = SteamNetworkingSockets.SendMessageToConnection(connection.Connection, pinned.AddrOfPinnedObject(), (uint)data.Length, flag, out _); }
            finally { pinned.Free(); }
            bool reliable = delivery == NetworkDelivery.Reliable || delivery == NetworkDelivery.ReliableSequenced || delivery == NetworkDelivery.ReliableFragmentedSequenced;
            if (result == EResult.k_EResultNoConnection || result == EResult.k_EResultInvalidParam || (reliable && result != EResult.k_EResultOK))
                DropAfterSendFailure(connection);
            else if (result != EResult.k_EResultOK && result != EResult.k_EResultIgnored)
                Debug.LogWarning("Steam transport could not queue a message: " + result);
        }
        private void DropAfterSendFailure(SteamConnectionData connection)
        {
            ulong peer = connection.Id.m_SteamID;
            if (!connectionMapping.Remove(peer)) return;
            if (connection == serverUser) serverUser = null;
            Close(connection.Connection, "Message could not be delivered");
            // Closing a native handle makes it unusable immediately. Notify NGO explicitly instead of waiting for a later callback.
            if (localDisconnects.Count >= NetworkConfiguration.MaxPlayers) queueOverflow = true;
            else localDisconnects.Enqueue(peer);
        }
        public override ulong GetCurrentRtt(ulong clientId)
        {
            if (!started || !IsSupported) return 0;
            if (clientId == ServerClientId) { if (serverUser == null) return 0; clientId = serverUser.Id.m_SteamID; }
            if (!connectionMapping.TryGetValue(clientId, out var connection)) return 0;
            var status = new SteamNetConnectionRealTimeStatus_t();
            var lane = new SteamNetConnectionRealTimeLaneStatus_t();
            var result = SteamNetworkingSockets.GetConnectionRealTimeStatus(connection.Connection, ref status, 0, ref lane);
            return result == EResult.k_EResultOK && status.m_nPing >= 0 ? (ulong)status.m_nPing : 0;
        }
        public bool TryGetVerifiedPeer(ulong transportId, out ulong steamId)
        {
            steamId = 0;
            if (!started || !connectionMapping.TryGetValue(transportId, out var connection) || !connection.ReportedConnected) return false;
            steamId = connection.Id.m_SteamID; return true;
        }
        public override void DisconnectLocalClient()
        {
            if (serverUser == null) return;
            Close(serverUser.Connection, "Disconnected"); connectionMapping.Remove(serverUser.Id.m_SteamID); serverUser = null;
        }
        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (!connectionMapping.TryGetValue(clientId, out var connection)) return;
            Close(connection.Connection, "Disconnected"); connectionMapping.Remove(clientId);
        }
        public override void Shutdown()
        {
            generation++; started = false;
            callback?.Dispose(); callback = null; statusQueue.Clear(); localDisconnects.Clear(); receivePeers.Clear(); receiveCursor = 0;
            try
            {
                if (IsSupported)
                {
                    if (listenSocket != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(listenSocket);
                    foreach (var connection in connectionMapping.Values) Close(connection.Connection, "Session ended");
                }
            }
            finally
            {
                listenSocket = HSteamListenSocket.Invalid; connectionMapping.Clear(); serverUser = null;
                isServer = false; queueOverflow = false;
            }
        }
        private void Close(HSteamNetConnection connection, string reason)
        {
            if (connection != HSteamNetConnection.Invalid && IsSupported)
                SteamNetworkingSockets.CloseConnection(connection, 0, reason, false);
        }
        private static bool ValidDelivery(byte value) => value == (byte)NetworkDelivery.Unreliable ||
            value == (byte)NetworkDelivery.UnreliableSequenced || value == (byte)NetworkDelivery.Reliable ||
            value == (byte)NetworkDelivery.ReliableSequenced || value == (byte)NetworkDelivery.ReliableFragmentedSequenced;
        private void OnDestroy() => Shutdown();
    }
}