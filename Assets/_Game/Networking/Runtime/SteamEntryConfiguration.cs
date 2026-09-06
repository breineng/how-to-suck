using System;
using UnityEngine;
namespace HowToSuck.Networking
{
    [CreateAssetMenu(menuName="How to Suck/Steam Entry Configuration",fileName="SteamEntryConfiguration")]
    public sealed class SteamEntryConfiguration : ScriptableObject
    {
        public SteamApplicationMode Mode = SteamApplicationMode.Unconfigured;
        // Public application identity, never a Web API key. No default/invented product AppID.
        public uint ProductionAppId;
        public bool TryConfiguration(SoloBuildIdentity identity,out NetworkConfiguration configuration,out string status)
        {
            configuration=null;
            if(Mode==SteamApplicationMode.Unconfigured)
            {status="Совместная игра ещё не настроена для этой сборки. Одиночная игра доступна.";return false;}
            try
            {
                if(identity==null)throw new InvalidOperationException("Missing authored build identity.");
                var real=identity.Configuration();
                bool development=false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                development=true;
#endif
                configuration=new NetworkConfiguration(Mode,ProductionAppId,real.BuildId,real.ContentHash,development);
                status=configuration.IsDevelopment480?"Совместная игра настроена для проверки. Для подключения нужен Steam.":"Для совместной игры нужен Steam.";
                return true;
            }
            catch(ArgumentException){status="Не удалось проверить настройки совместной игры. Одиночная игра доступна.";return false;}
            catch(InvalidOperationException){status="Совместная игра не настроена для этой сборки. Одиночная игра доступна.";return false;}
        }
    }
}
