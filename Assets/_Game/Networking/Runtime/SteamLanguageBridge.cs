using System;
using Steamworks;
using UnityEngine;

namespace HowToSuck.Networking
{
    // Reading the launch language also works before choosing Solo. A short-lived API lease never
    // creates a lobby, opens a campaign, initializes relay networking or launches another Steam app.
    public sealed class SteamLanguageBridge : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var root=new GameObject("Steam language");DontDestroyOnLoad(root);root.AddComponent<SteamLanguageBridge>();
        }
        private void Awake()=>Refresh();
        private void OnApplicationFocus(bool focused){if(focused)Refresh();}
        private static void Refresh()
        {
            if(Application.isEditor)return; // The Editor's test AppID must not select the player's product language.
            bool initialized=false;
            try
            {
                if(SteamRuntime.TryReadOwnedLanguage(out var language)){GameLocalization.SetSteamLanguage(language);return;}
                if(SteamRuntime.HasOwner)return;
                initialized=SteamAPI.InitEx(out _)==ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
                if(initialized)GameLocalization.SetSteamLanguage(SteamApps.GetCurrentGameLanguage());
            }
            catch(DllNotFoundException){}catch(BadImageFormatException){}catch(EntryPointNotFoundException){}catch(InvalidOperationException){}
            finally{if(initialized)SteamAPI.Shutdown();}
        }
    }
}
