using System;
using Unity.Netcode;

namespace HowToSuck.Networking
{
    public struct LobbyMemberWire : INetworkSerializable, IEquatable<LobbyMemberWire>
    {
        public ulong ClientId, SteamId;
        public bool Ready, Host;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);serializer.SerializeValue(ref SteamId);
            serializer.SerializeValue(ref Ready);serializer.SerializeValue(ref Host);
        }
        public bool Equals(LobbyMemberWire other)=>ClientId==other.ClientId&&SteamId==other.SteamId&&Ready==other.Ready&&Host==other.Host;
    }
}
