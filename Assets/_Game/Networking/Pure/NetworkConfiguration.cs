using System;
using System.Globalization;

namespace HowToSuck.Networking
{
    public enum SteamApplicationMode { Development480, Production, Unconfigured }
    public enum ConnectionMode { None, SoloLoopback, SteamHost, SteamClient, DiagnosticLoopbackHost, DiagnosticLoopbackClient }
    public enum ConnectionPhase { Offline, Starting, Lobby, Preparing, Running, Results, Stopping, Failed }

    public sealed class NetworkConfiguration
    {
        // ProjectSettings.productGUID is public project identity, not a credential or an authentication secret.
        public const string ProductKey = "hts.79bd2fdafe42bb445986bf55af7bf135";
        public const int MaxPlayers = 4;
        public const uint ProtocolVersion = 6; // Adds monotonic fire press + sticky cancellation to IntentWire; keeps protocol5 lobby selection.
        public const ushort SoloPort = 7777;
        public readonly SteamApplicationMode ApplicationMode;
        public readonly uint AppId;
        public readonly string BuildId;
        public readonly string ContentHash;
        public bool SteamConfigured => ApplicationMode != SteamApplicationMode.Unconfigured;
        public bool IsDevelopment480 => ApplicationMode == SteamApplicationMode.Development480;

        public NetworkConfiguration(SteamApplicationMode mode, uint productionAppId, string buildId, string contentHash,
            bool developmentExecution)
        {
            if (!ValidToken(buildId, 48)) throw new ArgumentException("An explicit ASCII build ID is required.", nameof(buildId));
            if (!Hex(contentHash, 64)) throw new ArgumentException("A canonical SHA256 content fingerprint is required.", nameof(contentHash));
            if (mode != SteamApplicationMode.Development480 && mode != SteamApplicationMode.Production)
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (mode == SteamApplicationMode.Development480 && !developmentExecution)
                throw new InvalidOperationException("Shared AppID480 is disabled in a production execution.");
            if (mode == SteamApplicationMode.Production && (productionAppId == 0 || productionAppId == 480))
                throw new ArgumentException("Production needs its own nonzero, non480 AppID.", nameof(productionAppId));
            ApplicationMode = mode; AppId = IsDevelopment480 ? 480u : productionAppId;
            BuildId = buildId; ContentHash = contentHash.ToLowerInvariant();
        }
        // Shipping solo has a real build/content identity but no invented Steam AppID.
        // This explicit factory never relaxes the configured Steam constructor's AppID/480 gates.
        public static NetworkConfiguration ForSolo(string buildId, string contentHash) => new NetworkConfiguration(buildId, contentHash);
        private NetworkConfiguration(string buildId, string contentHash)
        {
            if (!ValidToken(buildId, 48)) throw new ArgumentException("An explicit ASCII build ID is required.", nameof(buildId));
            if (!Hex(contentHash, 64)) throw new ArgumentException("A canonical SHA256 content fingerprint is required.", nameof(contentHash));
            ApplicationMode = SteamApplicationMode.Unconfigured; AppId = 0;
            BuildId = buildId; ContentHash = contentHash.ToLowerInvariant();
        }
        internal static bool ValidToken(string value, int maximum)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximum) return false;
            foreach (char c in value)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-')) return false;
            return true;
        }
        internal static bool Hex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (char c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            return true;
        }
        internal static string Number(ulong value) => value.ToString(CultureInfo.InvariantCulture);
    }
}