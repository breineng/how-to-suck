using System;
using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using Unity.Netcode;
using UnityEngine;

namespace HowToSuck.Networking
{
    public sealed class HowToSuckEosTransport : NetworkTransport
    {
        public EosRuntime Runtime;
        public ProductUserId HostUserId;
        public string Session;
        public Func<ProductUserId, bool> MayAcceptPeer;
        public override ulong ServerClientId => 0;
        public override bool IsSupported => Runtime != null && Runtime.Ready;
        private sealed class Peer
        {
            public ProductUserId User;
            public ulong Id;
            public bool Connected;
            public double LastReceived, LastProbe;
            public ulong Rtt;
            public readonly uint[] Sequence = new uint[3];
            public readonly EosFrameCodec Frames = new EosFrameCodec();
        }
        private struct Event { public NetworkEvent Kind; public ulong Id; }
        private readonly Dictionary<string, Peer> peers = new Dictionary<string, Peer>(StringComparer.Ordinal);
        private readonly Dictionary<ulong, Peer> ids = new Dictionary<ulong, Peer>();
        private readonly Queue<Event> events = new Queue<Event>();
        private readonly List<Peer> disconnected = new List<Peer>();
        private readonly byte[] receiveBuffer = new byte[EosFrameCodec.PacketBytes], sendBuffer = new byte[EosFrameCodec.PacketBytes];
        private P2PInterface api;
        private SocketId socket;
        private ulong requests, established, closed, queueFull, nextId;
        private bool started, server, failed;
        private uint generation;
        public override void Initialize(NetworkManager networkManager = null)
        { if (!IsSupported) throw new InvalidOperationException("Initialize EOS before selecting its transport"); }
        public override bool StartServer() => StartTransport(true);
        public override bool StartClient() => StartTransport(false);
        private bool StartTransport(bool hosting)
        {
            if (started || !IsSupported || !NetworkConfiguration.Hex(Session, 32) || (!hosting && (HostUserId == null || !HostUserId.IsValid()))) return false;
            api = Runtime.Platform.GetP2PInterface(); server = hosting; failed = false; nextId = 1;
            // Unique socket per lobby session rejects packets delayed from a previous session.
            socket = new SocketId { SocketName = Session };
            uint epoch = ++generation; started = true;
            var relay = new SetRelayControlOptions { RelayControl = Runtime.ForceRelay ? RelayControl.ForceRelays : RelayControl.AllowRelays };
            if (api.SetRelayControl(ref relay) != Result.Success) { Shutdown(); return false; }
            var request = new AddNotifyPeerConnectionRequestOptions { LocalUserId = Runtime.LocalUserId, SocketId = socket };
            requests = api.AddNotifyPeerConnectionRequest(ref request, null, (ref OnIncomingConnectionRequestInfo info) => {
                if (!started || epoch != generation || info.SocketId?.SocketName != socket.SocketName) return;
                bool existing = peers.TryGetValue(info.RemoteUserId.ToString(), out var peer);
                bool allowed = existing || (server && peers.Count < NetworkConfiguration.MaxPlayers - 1 && MayAcceptPeer?.Invoke(info.RemoteUserId) == true);
                if (!allowed) { CloseNative(info.RemoteUserId); return; }
                if (!existing) peer = AddPeer(info.RemoteUserId, nextId++);
                var accept = new AcceptConnectionOptions { LocalUserId = Runtime.LocalUserId, RemoteUserId = peer.User, SocketId = socket };
                if (api.AcceptConnection(ref accept) != Result.Success) Drop(peer);
            });
            var establishedOptions = new AddNotifyPeerConnectionEstablishedOptions { LocalUserId = Runtime.LocalUserId, SocketId = socket };
            established = api.AddNotifyPeerConnectionEstablished(ref establishedOptions, null, (ref OnPeerConnectionEstablishedInfo info) => {
                if (!started || epoch != generation || !peers.TryGetValue(info.RemoteUserId.ToString(), out var peer)) return;
                peer.LastReceived = Time.realtimeSinceStartupAsDouble;
                if (!peer.Connected) { peer.Connected = true; Enqueue(NetworkEvent.Connect, peer.Id); }
            });
            var closedOptions = new AddNotifyPeerConnectionClosedOptions { LocalUserId = Runtime.LocalUserId, SocketId = socket };
            closed = api.AddNotifyPeerConnectionClosed(ref closedOptions, null, (ref OnRemoteConnectionClosedInfo info) => {
                if (started && epoch == generation && peers.TryGetValue(info.RemoteUserId.ToString(), out var peer)) Drop(peer);
            });
            var fullOptions = new AddNotifyIncomingPacketQueueFullOptions();
            queueFull = api.AddNotifyIncomingPacketQueueFull(ref fullOptions, null, (ref OnIncomingPacketQueueFullInfo _) => { if (started && epoch == generation) failed = true; });
            if (requests == 0 || established == 0 || closed == 0 || queueFull == 0) { Shutdown(); return false; }
            if (!hosting)
            {
                var peer = AddPeer(HostUserId, 0);
                var accept = new AcceptConnectionOptions { LocalUserId = Runtime.LocalUserId, RemoteUserId = HostUserId, SocketId = socket };
                if (api.AcceptConnection(ref accept) != Result.Success || !SendProbe(peer)) { Shutdown(); return false; }
            }
            return true;
        }
        private Peer AddPeer(ProductUserId user, ulong id)
        {
            var peer = new Peer { User = user, Id = id, LastReceived = Time.realtimeSinceStartupAsDouble };
            peers.Add(user.ToString(), peer); ids.Add(id, peer); return peer;
        }
        public bool TryGetVerifiedPeer(ulong transportId, out string user)
        {
            user = null;
            if (!started || !ids.TryGetValue(transportId, out var peer) || !peer.Connected) return false;
            user = peer.User.ToString(); return true;
        }
        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery delivery)
        {
            if (!started || !ids.TryGetValue(clientId, out var peer)) return;
            if (payload.Array == null || payload.Count > EosFrameCodec.MaximumFrameBytes) { Drop(peer); return; }
            byte kind = delivery == NetworkDelivery.Unreliable ? (byte)0 : delivery == NetworkDelivery.UnreliableSequenced ? (byte)1 : (byte)2;
            uint message = ++peer.Sequence[kind]; if (message == 0) message = ++peer.Sequence[kind];
            int parts = EosFrameCodec.FragmentCount(payload.Count);
            for (int i = 0; i < parts; i++)
            {
                int bytes = EosFrameCodec.WriteFragment(payload, kind, message, i, sendBuffer);
                var result = SendPacket(peer, new ArraySegment<byte>(sendBuffer, 0, bytes), (byte)(kind + 1), kind == 2);
                if (result == Result.Success) continue;
                if (kind == 2) Drop(peer); // Losing a reliable fragment must end this connection.
                break;
            }
        }
        private Result SendPacket(Peer peer, ArraySegment<byte> payload, byte channel, bool reliable)
        {
            var options = new SendPacketOptions {
                LocalUserId = Runtime.LocalUserId, RemoteUserId = peer.User, SocketId = socket, Channel = channel, Data = payload,
                Reliability = reliable ? PacketReliability.ReliableOrdered : PacketReliability.UnreliableUnordered,
                AllowDelayedDelivery = true, DisableAutoAcceptConnection = true
            };
            return api.SendPacket(ref options);
        }
        private bool SendProbe(Peer peer)
        {
            peer.LastProbe = Time.realtimeSinceStartupAsDouble;
            var packet = new byte[9]; packet[0] = 1;
            Buffer.BlockCopy(BitConverter.GetBytes(peer.LastProbe), 0, packet, 1, 8);
            return SendPacket(peer, new ArraySegment<byte>(packet), 0, false) == Result.Success;
        }
        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            clientId = 0; payload = default; receiveTime = Time.realtimeSinceStartup;
            if (!started) return NetworkEvent.Nothing;
            if (failed || !IsSupported) return NetworkEvent.TransportFailure;
            if (events.Count > 0) { var next = events.Dequeue(); clientId = next.Id; return next.Kind; }
            for (int i = 0; i < 128; i++)
            {
                var options = new ReceivePacketOptions { LocalUserId = Runtime.LocalUserId, MaxDataSizeBytes = EosFrameCodec.PacketBytes };
                ProductUserId user = null; var receivedSocket = new SocketId();
                var result = api.ReceivePacket(ref options, ref user, ref receivedSocket, out var channel, new ArraySegment<byte>(receiveBuffer), out var length);
                if (result == Result.NotFound) return NetworkEvent.Nothing;
                if (result != Result.Success) { failed = true; return NetworkEvent.TransportFailure; }
                if (receivedSocket.SocketName != socket.SocketName || user == null || !peers.TryGetValue(user.ToString(), out var peer) || !peer.Connected) continue;
                double now = Time.realtimeSinceStartupAsDouble;
                if (channel == 0)
                {
                    if (length != 9 || (receiveBuffer[0] != 1 && receiveBuffer[0] != 2)) { Drop(peer); continue; }
                    peer.LastReceived = now;
                    if (receiveBuffer[0] == 1)
                    { receiveBuffer[0] = 2; SendPacket(peer, new ArraySegment<byte>(receiveBuffer, 0, 9), 0, false); }
                    else
                    {
                        double stamp = BitConverter.ToDouble(receiveBuffer, 1);
                        if (!double.IsNaN(stamp) && stamp <= now && stamp >= now - 30) peer.Rtt = (ulong)((now - stamp) * 1000);
                    }
                    continue;
                }
                var decoded = peer.Frames.Receive(receiveBuffer, (int)length, channel, now, out var frame);
                if (decoded == EosFrameResult.Invalid) { Drop(peer); continue; }
                peer.LastReceived = now;
                if (decoded != EosFrameResult.Complete) continue;
                clientId = peer.Id; payload = new ArraySegment<byte>(frame); return NetworkEvent.Data;
            }
            return NetworkEvent.Nothing;
        }
        private void Update()
        {
            if (!started || failed || !IsSupported) return;
            double now = Time.realtimeSinceStartupAsDouble; disconnected.Clear();
            foreach (var peer in peers.Values)
            {
                if (now - peer.LastReceived >= 30 || peer.Frames.Expire(now)) { disconnected.Add(peer); continue; }
                if (now - peer.LastProbe >= 2) SendProbe(peer);
            }
            foreach (var peer in disconnected) Drop(peer);
        }
        private void Enqueue(NetworkEvent kind, ulong id)
        { if (events.Count >= 64) failed = true; else events.Enqueue(new Event { Kind = kind, Id = id }); }
        private void CloseNative(ProductUserId user)
        {
            var close = new CloseConnectionOptions { LocalUserId = Runtime.LocalUserId, RemoteUserId = user, SocketId = socket };
            api.CloseConnection(ref close);
            var clear = new ClearPacketQueueOptions { LocalUserId = Runtime.LocalUserId, RemoteUserId = user, SocketId = socket };
            api.ClearPacketQueue(ref clear);
        }
        private void Drop(Peer peer)
        {
            if (!peers.Remove(peer.User.ToString())) return;
            ids.Remove(peer.Id); CloseNative(peer.User); Enqueue(NetworkEvent.Disconnect, peer.Id);
        }
        public override ulong GetCurrentRtt(ulong clientId) => ids.TryGetValue(clientId, out var peer) ? peer.Rtt : 0;
        public override void DisconnectRemoteClient(ulong clientId) { if (ids.TryGetValue(clientId, out var peer)) Drop(peer); }
        public override void DisconnectLocalClient() => DisconnectRemoteClient(0);
        public override void Shutdown()
        {
            if (!started) return;
            started = false; generation++;
            if (api != null)
            {
                if (requests != 0) api.RemoveNotifyPeerConnectionRequest(requests);
                if (established != 0) api.RemoveNotifyPeerConnectionEstablished(established);
                if (closed != 0) api.RemoveNotifyPeerConnectionClosed(closed);
                if (queueFull != 0) api.RemoveNotifyIncomingPacketQueueFull(queueFull);
                foreach (var peer in peers.Values) CloseNative(peer.User);
            }
            peers.Clear(); ids.Clear(); events.Clear(); disconnected.Clear(); api = null;
            requests = established = closed = queueFull = 0;
        }
        private void OnDestroy() => Shutdown();
    }
}
