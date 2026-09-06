using System;
using System.Collections.Generic;
using Steamworks;
namespace HowToSuck.Networking
{
    // Uses installed 2025.164.1 accessors. The existing SteamRuntime owns Init/Pump/Shutdown.
    public sealed class SteamAchievementPlatform:IAchievementPlatform
    {
        private static readonly Dictionary<string,AchievementStoreFence> processFences=new Dictionary<string,AchievementStoreFence>(StringComparer.Ordinal);
        private readonly SteamRuntime runtime;private readonly uint appId;private readonly AchievementStoreFence fence;
        private Callback<UserStatsStored_t> stored;private Callback<UserAchievementStored_t> achievementStored;
        private string request;private string[] requested;private double now,deadline;private bool disposed;
        private readonly HashSet<string> achievementCallbacks=new HashSet<string>(StringComparer.Ordinal);
        public ulong AccountId{get;}public string AccountLabel{get;}public string LastDiagnostic{get;private set;}
        public event Action<AchievementPlatformReceipt> Completed;
        public bool Available{get{
            if(disposed||!runtime.Initialized||runtime.ActualAppId!=appId||runtime.LocalSteamId!=AccountId)return false;
            try{return SteamUser.GetSteamID().m_SteamID==AccountId&&SteamUtils.GetAppID().m_AppId==appId&&SteamUser.BLoggedOn();}catch{return false;}
        }}
        public SteamAchievementPlatform(SteamRuntime runtime)
        {
            this.runtime=runtime??throw new ArgumentNullException(nameof(runtime));
            if(!runtime.Initialized||runtime.ActualAppId==0||runtime.ActualAppId==480||runtime.LocalSteamId==0)
                throw new InvalidOperationException("Product achievements require an explicitly initialized own non480 AppID/account.");
            appId=runtime.ActualAppId;AccountId=runtime.LocalSteamId;AccountLabel=SteamFriends.GetPersonaName();
            string key=appId+":"+AccountId;
            if(!processFences.TryGetValue(key,out fence)){fence=new AchievementStoreFence();processFences.Add(key,fence);}
            if(!fence.Acquire(this))throw new InvalidOperationException("One achievement platform owner per account/AppID.");
            try{stored=Callback<UserStatsStored_t>.Create(OnStored);achievementStored=Callback<UserAchievementStored_t>.Create(OnAchievementStored);}
            catch{stored?.Dispose();achievementStored?.Dispose();fence.Release(this);throw;}
        }
        public AchievementReadStatus Read(string apiName,out string error)
        {
            error=null;
            if(fence.Quarantined){error=fence.Reason;return AchievementReadStatus.InvalidConfiguration;}
            if(AchievementDefinitions.IndexOfApi(apiName)<0){error="Unknown achievement API name.";return AchievementReadStatus.InvalidConfiguration;}
            if(!Available){error="Steam offline or account changed.";return AchievementReadStatus.Unavailable;}
            try{
                // Current Steam client preloads local stats; there is no RequestCurrentStats wrapper.
                uint count=SteamUserStats.GetNumAchievements();
                if(count==0){error="No published achievement schema is visible for the configured product AppID; local queue retained.";return AchievementReadStatus.InvalidConfiguration;}
                bool found=false;for(uint i=0;i<count;i++)if(SteamUserStats.GetAchievementName(i)==apiName){found=true;break;}
                if(!found){error="Product API name missing: "+apiName;return AchievementReadStatus.InvalidConfiguration;}
                if(!SteamUserStats.GetAchievement(apiName,out bool unlocked)){error="Current achievement stats unavailable: "+apiName;return AchievementReadStatus.Unavailable;}
                return unlocked?AchievementReadStatus.Unlocked:AchievementReadStatus.Locked;
            }catch(Exception e){error=e.Message;return AchievementReadStatus.Unavailable;}
        }
        public AchievementSubmitStatus Submit(string requestId,IReadOnlyList<string> apiNames,out string error)
        {
            error=null;
            if(fence.Quarantined){error=fence.Reason;return AchievementSubmitStatus.InvalidConfiguration;}
            if(!Available||request!=null){error="Steam unavailable or StoreStats in flight.";return AchievementSubmitStatus.Unavailable;}
            if(!AchievementDefinitions.ValidRun(requestId)||apiNames==null||apiNames.Count<1||apiNames.Count>8){error="Invalid achievement batch.";return AchievementSubmitStatus.InvalidConfiguration;}
            var unique=new HashSet<string>(StringComparer.Ordinal);bool enteringStore=false;
            try{
                // Validate the complete batch before changing any Steam cache entry.
                foreach(string api in apiNames){if(!unique.Add(api)){error="Duplicate API name.";return AchievementSubmitStatus.InvalidConfiguration;}
                    var status=Read(api,out error);if(status==AchievementReadStatus.InvalidConfiguration)return AchievementSubmitStatus.InvalidConfiguration;if(status==AchievementReadStatus.Unavailable)return AchievementSubmitStatus.Unavailable;}
                foreach(string api in apiNames)if(!SteamUserStats.SetAchievement(api)){error="SetAchievement refused: "+api;return AchievementSubmitStatus.Unavailable;}
                if(!fence.Begin(this,requestId)){error="Achievement callback fence unavailable.";return AchievementSubmitStatus.InvalidConfiguration;}
                request=requestId;requested=new List<string>(apiNames).ToArray();achievementCallbacks.Clear();deadline=now+30;enteringStore=true;
                if(SteamUserStats.StoreStats())return AchievementSubmitStatus.Accepted;
                // Valve documents false as nothing sent. This definite refusal permits a bounded retry.
                fence.RejectedBeforeQueue(this,requestId);request=null;requested=null;error="StoreStats refused; local queue retained.";return AchievementSubmitStatus.Unavailable;
            }catch(Exception e){
                if(enteringStore)fence.Quarantine(this,"StoreStats outcome uncertain; restart the game to retry safely. Local queue retained.");
                request=null;requested=null;error=enteringStore?fence.Reason:e.Message;
                return enteringStore?AchievementSubmitStatus.InvalidConfiguration:AchievementSubmitStatus.Unavailable;
            }
        }
        public void Tick(double value)
        {
            if(disposed||double.IsNaN(value)||double.IsInfinity(value)||value<now)return;now=value;
            if(request!=null&&(!Available||now>=deadline)){
                string reason="StoreStats acknowledgement unavailable; restart the game to retry safely. Local queue retained.";
                fence.Quarantine(this,reason);Finish(false,false,Array.Empty<string>(),reason);
            }
        }
        private void OnAchievementStored(UserAchievementStored_t callback)
        {
            if(!Available||!fence.IsCurrent(this,request)||callback.m_nGameID!=appId||callback.m_nCurProgress!=0||callback.m_nMaxProgress!=0)return;
            if(Array.IndexOf(requested,callback.m_rgchAchievementName)>=0)achievementCallbacks.Add(callback.m_rgchAchievementName);
        }
        private void OnStored(UserStatsStored_t callback)
        {
            if(!Available||!fence.IsCurrent(this,request)||callback.m_nGameID!=appId)return;
            string id=request;
            if(callback.m_eResult!=EResult.k_EResultOK){fence.Settle(this,id);Finish(false,true,Array.Empty<string>(),"UserStatsStored: "+callback.m_eResult);return;}
            var confirmed=new List<string>();foreach(string api in requested)if(Read(api,out _)==AchievementReadStatus.Unlocked)confirmed.Add(api);
            LastDiagnostic="UserStatsStored OK + same-account readback "+confirmed.Count+"/"+requested.Length+"; achievement callbacks "+achievementCallbacks.Count+".";
            if(!fence.Settle(this,id))return;Finish(true,true,confirmed.ToArray(),null);
        }
        private void Finish(bool success,bool retryable,string[] confirmed,string error)
        {
            if(request==null)return;string id=request;request=null;requested=null;
            var receipt=new AchievementPlatformReceipt(AccountId,id,confirmed,success,retryable,error);
            var handlers=Completed;if(handlers!=null)foreach(Action<AchievementPlatformReceipt> handler in handlers.GetInvocationList())
                try{handler(receipt);}catch(Exception e){LastDiagnostic="Achievement receipt consumer failed: "+e.GetType().Name;}
        }
        public void Dispose(){if(disposed)return;disposed=true;fence.Release(this);stored?.Dispose();achievementStored?.Dispose();stored=null;achievementStored=null;request=null;requested=null;Completed=null;}
    }
}
