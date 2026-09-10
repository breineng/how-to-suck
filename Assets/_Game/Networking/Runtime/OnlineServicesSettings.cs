using UnityEngine;

namespace HowToSuck.Networking
{
    [CreateAssetMenu(menuName = "How to Suck/Online Services", fileName = "OnlineServicesSettings")]
    public sealed class OnlineServicesSettings : ScriptableObject
    {
        public OnlineBackend Backend = OnlineBackend.EpicOnlineServices;
        public SteamEntryConfiguration SteamConfiguration;
        public static SteamEntryConfiguration SteamFallback => Resources.Load<OnlineServicesSettings>("OnlineServicesSettings")?.SteamConfiguration;
        public static OnlineBackend SelectedBackend
        {
            get
            {
                var settings = Resources.Load<OnlineServicesSettings>("OnlineServicesSettings");
                return settings != null ? settings.Backend : OnlineBackend.EpicOnlineServices;
            }
        }
    }
}
