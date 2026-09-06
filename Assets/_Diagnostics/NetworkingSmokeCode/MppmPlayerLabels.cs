using System.Collections.Generic;
using Unity.Multiplayer.PlayMode;
namespace HowToSuck.Networking
{
    // Verified against Unity6000.3.6f1 UnityEngine.MultiplayerModule, not the obsolete Playmode namespace.
    public static class MppmPlayerLabels
    {
        public static string[] ReadTags() => new List<string>(CurrentPlayer.Tags).ToArray();
        public static bool IsMainEditor => CurrentPlayer.IsMainEditor;
    }
}
