namespace HowToSuck.Networking
{
    public static class AdmissionPolicy
    {
        // Steam identity/membership and connected+reserved counts come from the host's runtime, never this payload.
        public static string Rejection(NetworkConfiguration config, ConnectionMode mode, ConnectionPhase phase,
            bool hostLocal, int admittedAndReserved, bool alreadyApproved, ulong verifiedSteamId,
            bool actualLobbyMember, bool steamIdentityAlreadyPresent, ulong lobbyId, string lobbySession, byte[] payload)
        {
            if (config == null) return "configuration";
            if (phase != ConnectionPhase.Lobby && !(hostLocal && phase == ConnectionPhase.Starting)) return "contract_in_progress";
            if (alreadyApproved) return "duplicate_connection";
            if (admittedAndReserved >= NetworkConfiguration.MaxPlayers) return "lobby_full";
            if (mode == ConnectionMode.SoloLoopback && !hostLocal) return "solo_only";
            if (mode != ConnectionMode.SoloLoopback && mode != ConnectionMode.SteamHost && mode != ConnectionMode.DiagnosticLoopbackHost) return "not_host";
            if (!ConnectionHello.TryDecode(payload, out var hello) || !hello.Matches(config, lobbyId, lobbySession)) return "incompatible_build";
            if (mode == ConnectionMode.SteamHost && !hostLocal &&
                (verifiedSteamId == 0 || verifiedSteamId == ulong.MaxValue || !actualLobbyMember || steamIdentityAlreadyPresent)) return "lobby_membership";
            return null;
        }
    }

    // Steam provides per-connection increasing message numbers. No custom wire sequence is introduced.
    public sealed class SequencedReceiveGate
    {
        private long last;
        public bool Accept(long messageNumber)
        {
            if (messageNumber <= 0 || messageNumber <= last) return false;
            last = messageNumber; return true;
        }
    }
}