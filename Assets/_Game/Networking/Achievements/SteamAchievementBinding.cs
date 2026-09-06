using System;
using UnityEngine;
namespace HowToSuck.Networking
{
    [DisallowMultipleComponent]
    public sealed class SteamAchievementBinding : MonoBehaviour
    {
        private AchievementSessionBridge bridge;
        private NetworkConnectionCoordinator connection;
        private SteamRuntime attached;
        private double retryAt;
        private string lastError;
        private void Awake(){bridge=GetComponent<AchievementSessionBridge>();connection=GetComponent<NetworkConnectionCoordinator>();}
        private void Update()
        {
            if(bridge==null||bridge.Service==null||connection==null)return;
            var runtime=connection.ActiveSteamRuntime;
            bool explicitSteam=connection.Mode==ConnectionMode.SteamHost||connection.Mode==ConnectionMode.SteamClient;
            if(!explicitSteam||runtime==null||!runtime.Initialized||runtime.ActualAppId==480)
            {
                if(attached!=null){bridge.Service.DetachPlatform();attached=null;}
                return; // Solo does not initialize Steam or bind unbound offline history.
            }
            if(ReferenceEquals(attached,runtime)||Time.realtimeSinceStartupAsDouble<retryAt)return;
            SteamAchievementPlatform candidate=null;
            try
            {
                candidate=new SteamAchievementPlatform(runtime);
                bridge.Service.BindAuthenticatedAccount(candidate.AccountId,true,candidate.AccountLabel);
                bridge.Service.AttachPlatform(candidate);attached=runtime;candidate=null;lastError=null;
            }
            catch(Exception e)
            {
                candidate?.Dispose();retryAt=Time.realtimeSinceStartupAsDouble+15;
                if(lastError!=e.Message){lastError=e.Message;Debug.LogWarning("Achievement account binding: "+e.Message,this);}
            }
        }
        private void OnDisable(){if(attached!=null&&bridge!=null&&bridge.Service!=null)bridge.Service.DetachPlatform();attached=null;}
        private void OnDestroy(){if(attached!=null&&bridge!=null&&bridge.Service!=null)bridge.Service.DetachPlatform();attached=null;}
    }
}
