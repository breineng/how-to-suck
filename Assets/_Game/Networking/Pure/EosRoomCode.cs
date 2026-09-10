using System;

namespace HowToSuck.Networking
{
    public static class EosRoomCode
    {
        public static string Create() => Guid.NewGuid().ToString("N");
        public static bool TryNormalize(string value, out string code)
        {
            code = value?.Trim().Replace("-", "").ToLowerInvariant();
            if (NetworkConfiguration.Hex(code, 32)) return true;
            code = null; return false;
        }
    }
}
