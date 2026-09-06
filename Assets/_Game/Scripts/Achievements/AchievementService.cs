using System;
using System.Collections.Generic;
namespace HowToSuck
{
    public enum AchievementReadStatus { Unavailable,Locked,Unlocked,InvalidConfiguration }
    public enum AchievementSubmitStatus { Accepted,Unavailable,InvalidConfiguration }
    public readonly struct AchievementPlatformReceipt
    {
        public readonly ulong AccountId;
        public readonly string RequestId;
        public readonly string[] ConfirmedApiNames;
        public readonly bool Success,Retryable;
        public readonly string Error;
        public AchievementPlatformReceipt(ulong account,string request,string[] confirmed,bool success,bool retryable,string error)
        {AccountId=account;RequestId=request;ConfirmedApiNames=confirmed??Array.Empty<string>();Success=success;Retryable=retryable;Error=error;}
    }
    public interface IAchievementPlatform : IDisposable
    {
        ulong AccountId{get;}
        bool Available{get;}
        string AccountLabel{get;}
        event Action<AchievementPlatformReceipt> Completed;
        AchievementReadStatus Read(string apiName,out string error);
        AchievementSubmitStatus Submit(string requestId,IReadOnlyList<string> apiNames,out string error);
        void Tick(double now);
    }
    // Local recipient/profile owner. No campaign, Unity or Steam dependency.
    public sealed class AchievementService : IDisposable
    {
        private readonly IAchievementProfileRepository repository;
        private AchievementProfileStore store;
        private IAchievementPlatform platform;
        private Action<AchievementPlatformReceipt> receiptHandler;
        private long platformEpoch;
        private string localRun,requestId;
        private int localPlayerId;
        private readonly HashSet<string> seenEvents=new HashSet<string>(StringComparer.Ordinal);
        private HashSet<string> inFlightIds;
        private bool dirty,disposed,platformBlocked;
        private double nextSync,nextSaveRetry,lastNow;
        private int failures;
        public string LastError{get;private set;}
        public string AccountNotice{get;private set;}="";
        public event Action Changed;
        public bool HasPendingSave=>dirty;
        public bool PlatformBlocked=>platformBlocked;
        public AchievementProfile ActiveProfile=>store.Active.Copy();
        public AchievementService(IAchievementProfileRepository repository)
        {this.repository=repository??throw new ArgumentNullException(nameof(repository));store=repository.Open();store.Validate();}
        public void BindLocalRun(string run,int playerId)
        {
            if(!AchievementDefinitions.ValidRun(run)||playerId<1||playerId>4)throw new ArgumentException("Actual local spawned run/player required.");
            if(localRun==run&&localPlayerId==playerId)return;
            localRun=run;localPlayerId=playerId;seenEvents.Clear();
        }
        public void UnbindLocalRun(){localRun=null;localPlayerId=0;seenEvents.Clear();}
        public bool Accept(AchievementFact fact,bool fromCurrentServer)
        {
            if(disposed||localPlayerId<1||!fromCurrentServer||!fact.IsValid||fact.RunId!=localRun||
                (fact.Recipients&(1<<(localPlayerId-1)))==0||!seenEvents.Add(fact.EventId))return false;
            var active=store.Active;var unlocked=new HashSet<string>(active.UnlockedLocal,StringComparer.Ordinal);var pending=new HashSet<string>(active.PendingSteam,StringComparer.Ordinal);bool changed=false;
            foreach(string id in AchievementDefinitions.Ids(fact.Achievements))if(unlocked.Add(id)){pending.Add(id);changed=true;}
            if(!changed)return false;
            active.UnlockedLocal=Ordered(unlocked);active.PendingSteam=Ordered(pending);dirty=true;SaveNow();nextSync=Math.Max(nextSync,lastNow+1);Changed?.Invoke();return true;
        }
        public void BindAuthenticatedAccount(ulong accountId,bool explicitSteamSelection,string label)
        {
            if(!explicitSteamSelection||accountId==0)throw new InvalidOperationException("Only an explicit authenticated Steam mode can bind achievement history.");
            string id=accountId.ToString(System.Globalization.CultureInfo.InvariantCulture);var active=store.Active;
            if(active.SteamId==id)return;
            // Flush A first; a failed durable write cannot silently switch to B and lose A's progress.
            if(dirty&&!SaveNow())throw new InvalidOperationException("Save current achievement history before changing accounts.");
            DetachPlatform();AchievementProfile target=null;
            foreach(var p in store.Profiles)if(p.SteamId==id)target=p;
            if(active.SteamId.Length==0)
            {
                if(target!=null)throw new InvalidOperationException("An unexpected pre-existing account cannot silently consume unbound history.");
                active.SteamId=id;target=active;
                AccountNotice="Локальная история достижений привязана к "+label+" (SteamID "+id+").";
            }
            else
            {
                if(target==null)
                {
                    target=new AchievementProfile{ProfileId=Guid.NewGuid().ToString("N"),SteamId=id};
                    var list=new List<AchievementProfile>(store.Profiles);list.Add(target);store.Profiles=list.ToArray();
                }
                AccountNotice="Достижения аккаунта "+label+" (SteamID "+id+"). История другого аккаунта сохранена отдельно.";
            }
            store.ActiveProfileId=target.ProfileId;dirty=true;
            if(!SaveNow())throw new InvalidOperationException("Achievement account binding is not yet saved; platform remains detached.");
            Changed?.Invoke();
        }
        public void AttachPlatform(IAchievementPlatform value)
        {
            if(value==null||value.AccountId==0||store.Active.SteamId!=value.AccountId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Platform must match the explicitly bound active account.");
            if(dirty&&!SaveNow())throw new InvalidOperationException("Persist binding before sending achievements.");
            DetachPlatform();platform=value;long epoch=++platformEpoch;
            receiptHandler=r=>OnReceipt(value,epoch,r);platform.Completed+=receiptHandler;platformBlocked=false;failures=0;nextSync=lastNow;Changed?.Invoke();
        }
        public void DetachPlatform()
        {
            ++platformEpoch;
            if(platform!=null){platform.Completed-=receiptHandler;platform.Dispose();}
            platform=null;receiptHandler=null;requestId=null;inFlightIds=null;platformBlocked=false;
        }
        public void Tick(double now)
        {
            if(disposed||double.IsNaN(now)||double.IsInfinity(now)||now<lastNow)return;lastNow=now;
            if(dirty&&now>=nextSaveRetry){SaveNow();nextSaveRetry=now+5;}
            if(platform==null||platformBlocked)return;
            platform.Tick(now);
            if(requestId!=null||!platform.Available||now<nextSync)return;
            if(store.Active.SteamId!=platform.AccountId.ToString(System.Globalization.CultureInfo.InvariantCulture)){DetachPlatform();return;}
            var active=store.Active;var unlocked=new HashSet<string>(active.UnlockedLocal,StringComparer.Ordinal);bool changed=false;
            for(int i=0;i<AchievementDefinitions.Count;i++)
            {
                var status=platform.Read(AchievementDefinitions.Api(i),out var error);
                if(status==AchievementReadStatus.InvalidConfiguration){Block(error);return;}
                if(status==AchievementReadStatus.Unavailable){Retry(error);return;}
                if(status==AchievementReadStatus.Unlocked)changed|=unlocked.Add(AchievementDefinitions.Id(i));
            }
            if(changed){active.UnlockedLocal=Ordered(unlocked);dirty=true;SaveNow();Changed?.Invoke();}
            // Reading Steam cache never clears locally pending facts. Store callback + readback does.
            if(active.PendingSteam.Length==0){nextSync=now+60;return;}
            if(dirty&&!SaveNow()){nextSync=now+5;return;}
            var apis=new List<string>();inFlightIds=new HashSet<string>(active.PendingSteam,StringComparer.Ordinal);
            foreach(string id in active.PendingSteam)apis.Add(AchievementDefinitions.Api(AchievementDefinitions.IndexOfId(id)));
            requestId=Guid.NewGuid().ToString("N");
            var submit=platform.Submit(requestId,apis,out var failure);
            if(submit!=AchievementSubmitStatus.Accepted)
            {
                requestId=null;inFlightIds=null;
                if(submit==AchievementSubmitStatus.InvalidConfiguration)Block(failure);else Retry(failure);
            }
        }
        private void OnReceipt(IAchievementPlatform sender,long epoch,AchievementPlatformReceipt receipt)
        {
            if(disposed||sender!=platform||epoch!=platformEpoch||receipt.RequestId!=requestId||requestId==null||
                receipt.AccountId!=platform.AccountId||store.Active.SteamId!=receipt.AccountId.ToString(System.Globalization.CultureInfo.InvariantCulture))return;
            var pending=new HashSet<string>(store.Active.PendingSteam,StringComparer.Ordinal);
            if(receipt.Success)
            {
                foreach(string api in receipt.ConfirmedApiNames)
                {int i=AchievementDefinitions.IndexOfApi(api);if(i>=0&&inFlightIds.Contains(AchievementDefinitions.Id(i)))pending.Remove(AchievementDefinitions.Id(i));}
                store.Active.PendingSteam=Ordered(pending);dirty=true;SaveNow();failures=0;nextSync=lastNow+60;if(!dirty)LastError=null;
            }
            requestId=null;inFlightIds=null;
            if(!receipt.Success){if(receipt.Retryable)Retry(receipt.Error);else Block(receipt.Error);}
            Changed?.Invoke();
        }
        public bool RetryLocalSave()=>SaveNow();
        public bool IsUnlocked(string id)=>Array.IndexOf(store.Active.UnlockedLocal,id)>=0;
        private bool SaveNow()
        {
            if(!dirty)return true;
            try{store.Validate();repository.Save(store.Copy());dirty=false;LastError=null;return true;}
            catch(Exception e) when(e is System.IO.IOException||e is System.IO.InvalidDataException||e is UnauthorizedAccessException||e is InvalidOperationException)
            {LastError="Достижения пока не сохранены: "+e.Message;nextSaveRetry=lastNow+5;return false;}
        }
        private void Retry(string error){LastError=error??"Steam временно недоступен.";failures=Math.Min(failures+1,6);nextSync=lastNow+Math.Min(300,15*Math.Pow(2,failures-1));}
        private void Block(string error){platformBlocked=true;LastError=error??"Проверьте AppID и зарегистрированные API names.";Changed?.Invoke();}
        private static string[] Ordered(HashSet<string> values){var list=new List<string>();for(int i=0;i<8;i++)if(values.Contains(AchievementDefinitions.Id(i)))list.Add(AchievementDefinitions.Id(i));return list.ToArray();}
        public void Dispose(){if(disposed)return;SaveNow();DetachPlatform();disposed=true;repository.Dispose();Changed=null;}
    }
}