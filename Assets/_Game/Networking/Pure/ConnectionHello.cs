using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HowToSuck.Networking
{
    public readonly struct ConnectionHello
    {
        public const int MaximumBytes = 320;
        public readonly string Product, Build, Content, Session;
        public readonly uint AppId, Protocol;
        public readonly ulong LobbyId;
        private ConnectionHello(string product, uint appId, uint protocol, string build, string content, ulong lobby, string session)
        { Product = product; AppId = appId; Protocol = protocol; Build = build; Content = content; LobbyId = lobby; Session = session; }
        public static ConnectionHello Create(NetworkConfiguration config, ulong lobby, string session)
        {
            if (config == null || !NetworkConfiguration.Hex(session, 32)) throw new ArgumentException("A current lobby session is required.");
            return new ConnectionHello(NetworkConfiguration.ProductKey, config.AppId, NetworkConfiguration.ProtocolVersion,
                config.BuildId, config.ContentHash, lobby, session.ToLowerInvariant());
        }
        public byte[] Encode() => Encoding.ASCII.GetBytes(string.Join("|", Product, NetworkConfiguration.Number(AppId),
            NetworkConfiguration.Number(Protocol), Build, Content, NetworkConfiguration.Number(LobbyId), Session));
        public static bool TryDecode(byte[] bytes, out ConnectionHello hello)
        {
            hello = default;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumBytes) return false;
            foreach (byte value in bytes) if (value < 32 || value > 126) return false;
            string[] parts = Encoding.ASCII.GetString(bytes).Split('|');
            if (parts.Length != 7 || !NetworkConfiguration.ValidToken(parts[0], 48) ||
                !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint app) ||
                !uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint protocol) ||
                !NetworkConfiguration.ValidToken(parts[3], 48) || !NetworkConfiguration.Hex(parts[4], 64) ||
                !ulong.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out ulong lobby) ||
                !NetworkConfiguration.Hex(parts[6], 32)) return false;
            hello = new ConnectionHello(parts[0], app, protocol, parts[3], parts[4].ToLowerInvariant(), lobby, parts[6].ToLowerInvariant());
            return true;
        }
        public bool Matches(NetworkConfiguration config, ulong lobby, string session) =>
            Product == NetworkConfiguration.ProductKey && AppId == config.AppId && Protocol == NetworkConfiguration.ProtocolVersion &&
            Build == config.BuildId && Content == config.ContentHash && LobbyId == lobby && Session == session;
    }

    public static class LobbyMetadata
    {
        public const string Product = "hts_game", App = "hts_app", Protocol = "hts_protocol", Build = "hts_build",
            Content = "hts_content", Session = "hts_session", Host = "hts_host", Phase = "hts_phase", Contract = "hts_contract";
        public static Dictionary<string, string> Create(NetworkConfiguration config, ulong host, string session)
        {
            if (host == 0 || !NetworkConfiguration.Hex(session, 32)) throw new ArgumentException("Host and session required.");
            return new Dictionary<string, string>
            {
                [Product] = NetworkConfiguration.ProductKey, [App] = NetworkConfiguration.Number(config.AppId),
                [Protocol] = NetworkConfiguration.Number(NetworkConfiguration.ProtocolVersion), [Build] = config.BuildId,
                [Content] = config.ContentHash, [Session] = session, [Host] = NetworkConfiguration.Number(host),
                [Phase] = ConnectionPhase.Lobby.ToString(), [Contract] = "none"
            };
        }
        // Before membership this is only an advertisement; it is never proof of the real lobby owner.
        public static bool TryReadAdvertisement(NetworkConfiguration config, IReadOnlyDictionary<string, string> values,
            bool requireLobby, out ulong host, out string session)
        {
            host = 0; session = null;
            if (config == null || values == null) return false;
            if (!Get(values, Product, NetworkConfiguration.ProductKey) || !Get(values, App, NetworkConfiguration.Number(config.AppId)) ||
                !Get(values, Protocol, NetworkConfiguration.Number(NetworkConfiguration.ProtocolVersion)) || !Get(values, Build, config.BuildId) ||
                !Get(values, Content, config.ContentHash) || !values.TryGetValue(Host, out var owner) ||
                !ulong.TryParse(owner, NumberStyles.None, CultureInfo.InvariantCulture, out host) || host == 0 ||
                !values.TryGetValue(Session, out session) || !NetworkConfiguration.Hex(session, 32) ||
                !values.TryGetValue(Phase, out var phase) || !Enum.TryParse(phase, false, out ConnectionPhase parsed) ||
                !Enum.IsDefined(typeof(ConnectionPhase), parsed) || (requireLobby && parsed != ConnectionPhase.Lobby)) return false;
            return true;
        }
        public static bool TryValidateMemberView(NetworkConfiguration config, IReadOnlyDictionary<string, string> values,
            ulong actualOwner, bool requireLobby, out ulong host, out string session) =>
            TryReadAdvertisement(config, values, requireLobby, out host, out session) && actualOwner != 0 && host == actualOwner;
        private static bool Get(IReadOnlyDictionary<string, string> values, string key, string expected) =>
            values.TryGetValue(key, out var value) && string.Equals(value, expected, StringComparison.Ordinal);
    }
}