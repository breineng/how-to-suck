using System;
using System.Collections.Generic;

namespace HowToSuck
{
    public enum CampaignChangeKind { Payout, Purchase, CapacityPurchase }
    public sealed class PendingCampaignChange
    {
        public string OperationId {get;}
        public CampaignChangeKind Kind {get;}
        public CampaignState Expected {get;}
        public CampaignState Candidate {get;}
        public ContractResult Result {get;}
        public string RequestedTier {get;}
        public long Price {get;}
        public bool IsPurchase=>Kind==CampaignChangeKind.Purchase||Kind==CampaignChangeKind.CapacityPurchase;
        internal PendingCampaignChange(CampaignChangeKind kind,CampaignState expected,CampaignState candidate,
            ContractResult result=null,string tier=null,long price=0)
        {OperationId=Guid.NewGuid().ToString("N");Kind=kind;Expected=expected;Candidate=candidate;Result=result;RequestedTier=tier;Price=price;}
    }
    public sealed class ProgressionService
    {
        public CampaignState Campaign {get;private set;}
        public ContractResult PendingResult {get;private set;}
        public PendingCampaignChange PendingChange {get;private set;}
        public string CurrentRunId {get;private set;}
        public string LastError {get;private set;}
        public SaveCommitKind? LastCommitKind {get;private set;}
        public bool IsSaving {get;private set;}
        public bool HasPending=>PendingResult!=null||PendingChange!=null||IsSaving;
        public bool IsContractUnlocked(string id)=>CampaignContractAccess.IsUnlocked(Campaign,id);
        public int EffectiveCapacity=>CampaignCapacityRules.Effective(Campaign.CurrentTierId,Campaign.PurchasedExtraSlots);
        public bool CanStartRun=>!closed&&repository.IsAuthority&&CurrentRunId==null&&!HasPending;
        private readonly SaveRepository repository;
        private readonly Func<SessionPhase> phase;
        private bool closed;
        private ContractController controller;
        private readonly HashSet<string> usedRunIds=new HashSet<string>(StringComparer.Ordinal);
        public ProgressionService(CampaignState confirmed,SaveRepository saves,Func<SessionPhase> currentPhase)
        {
            repository=saves??throw new ArgumentNullException(nameof(saves));phase=currentPhase??throw new ArgumentNullException(nameof(currentPhase));
            if(confirmed==null||!ReferenceEquals(repository.Confirmed,confirmed)||!repository.IsAuthority)
                throw new ArgumentException("A verified own-campaign repository snapshot is required.");
            Campaign=confirmed;
        }
        internal void Attach(ContractController owner)
        {
            if(owner==null)throw new ArgumentNullException(nameof(owner));
            if(controller!=null&&!ReferenceEquals(controller,owner))throw new InvalidOperationException("This campaign already has its controller.");
            controller=owner;
        }
        internal void ReserveRun(ContractController owner,string runId)
        {
            RequireOwner(owner);SaveIdentity.RequireGuid(runId,nameof(runId));
            if(!CanStartRun)throw new InvalidOperationException("Resolve campaign saving before starting a run.");
            if(runId==Campaign.LastSettledRunId||!usedRunIds.Add(runId))throw new InvalidOperationException("A run ID cannot be reused.");
            CurrentRunId=runId;
        }
        internal void CancelPreparation(ContractController owner,string runId)
        {
            RequireOwner(owner);
            if(HasPending||CurrentRunId!=runId)throw new InvalidOperationException("This preparation is no longer current.");
            CurrentRunId=null;
        }
        internal void Publish(ContractController owner,ContractResult result)
        {
            RequireOwner(owner);
            if(result==null||!ReferenceEquals(owner.FinalResult,result)||result.CampaignId!=Campaign.CampaignId||
                result.RunId!=CurrentRunId||HasPending)throw new InvalidOperationException("Only the current authority FinalResult may become pending.");
            PendingResult=result;
        }
        public bool TryApplyResult(ContractResult result)
        {
            if(!Allowed()||result==null||!ReferenceEquals(result,PendingResult)||controller==null||
                !ReferenceEquals(controller.FinalResult,result)||result.CampaignId!=Campaign.CampaignId||
                result.RunId!=CurrentRunId||result.RunId==Campaign.LastSettledRunId)
                return Reject("Only the current authentic pending result can be saved.");
            if(PendingChange!=null)
                return PendingChange.Kind==CampaignChangeKind.Payout&&ReferenceEquals(PendingChange.Result,result)&&RetryPending(PendingChange);
            if(result.Payout>long.MaxValue-Campaign.Balance)return Reject("Balance would exceed Int64; result remains pending.");
            var candidate=new CampaignState(Campaign.CampaignId,Campaign.CurrentTierId,
                checked(Campaign.Balance+result.Payout),result.RunId,Campaign.PurchasedExtraSlots,
                result.Phase==ContractPhase.Succeeded?CampaignContractAccess.WithSucceeded(Campaign,result.ContractId):Campaign.ClearedContractIds,Campaign.LegacyContractAccess);
            PendingChange=new PendingCampaignChange(CampaignChangeKind.Payout,Campaign,candidate,result);
            return RetryPending(PendingChange);
        }
        public bool TryPurchaseNext(string requestedTier)
        {
            if(!Allowed()||phase()!=SessionPhase.Lobby||!CanStartRun)return Reject("Only the host in Lobby with no unresolved result/save can buy.");
            var next=repository.Tiers.Next(Campaign.CurrentTierId);
            if(next==null||next.Id!=requestedTier)return Reject("Only the next authored tier may be purchased.");
            if(Campaign.Balance<next.Price)return Reject("Insufficient confirmed balance.");
            var candidate=new CampaignState(Campaign.CampaignId,next.Id,Campaign.Balance-next.Price,Campaign.LastSettledRunId,
                Campaign.PurchasedExtraSlots,Campaign.ClearedContractIds,Campaign.LegacyContractAccess);
            PendingChange=new PendingCampaignChange(CampaignChangeKind.Purchase,Campaign,candidate,tier:next.Id,price:next.Price);
            return RetryPending(PendingChange);
        }
        public bool TryPurchaseExtraSlot(int expectedPurchasedExtraSlots)
        {
            if(!Allowed()||phase()!=SessionPhase.Lobby||!CanStartRun)return Reject("Only the host in Lobby with no unresolved result/save can buy capacity.");
            if(expectedPurchasedExtraSlots!=Campaign.PurchasedExtraSlots)return Reject("The displayed capacity offer is stale.");
            if(!CampaignCapacityRules.TryNext(Campaign.CurrentTierId,Campaign.PurchasedExtraSlots,out _,out _,out long price))return Reject("This model cannot gain another slot.");
            if(Campaign.Balance<price)return Reject("Insufficient confirmed balance.");
            var candidate=new CampaignState(Campaign.CampaignId,Campaign.CurrentTierId,Campaign.Balance-price,Campaign.LastSettledRunId,
                Campaign.PurchasedExtraSlots+1,Campaign.ClearedContractIds,Campaign.LegacyContractAccess);
            PendingChange=new PendingCampaignChange(CampaignChangeKind.CapacityPurchase,Campaign,candidate,price:price);
            return RetryPending(PendingChange);
        }
        public bool RetryPending(PendingCampaignChange expectedOperation)
        {
            if(!Allowed()||expectedOperation==null||!ReferenceEquals(expectedOperation,PendingChange))
                return Reject("The exact current pending operation is required.");
            if(PendingChange.IsPurchase&&phase()!=SessionPhase.Lobby)
                return Reject("Purchase retry requires the same own Lobby.");
            if(PendingChange.Kind==CampaignChangeKind.Payout&&(controller==null||!ReferenceEquals(PendingChange.Result,PendingResult)||
                !ReferenceEquals(controller.FinalResult,PendingResult)||PendingResult.RunId!=CurrentRunId))
                return Reject("Payout pending identity is no longer current.");
            IsSaving=true;
            try {
                var operation=PendingChange;
                var outcome=repository.Commit(operation.Expected,operation.Candidate);
                LastCommitKind=outcome.Kind;
                if(!outcome.Success){LastError=outcome.Error;return false;}
                // Disk verification comes first. No added delta is applied to whatever memory happens to contain.
                Campaign=operation.Candidate;PendingChange=null;
                if(operation.Kind==CampaignChangeKind.Payout){PendingResult=null;CurrentRunId=null;}
                LastError=null;return true;
            }finally{IsSaving=false;}
        }
        // Lifecycle-only: call after an explicit exit decision, not as a Retry/Start bypass.
        // An ambiguous candidate may already exist on disk; this never rolls it back.
        public void CloseForShutdown()
        {if(IsSaving)throw new InvalidOperationException("Wait for the current synchronous save call.");closed=true;}
        private bool Allowed()=>!closed&&!IsSaving&&repository.IsAuthority;
        private bool Reject(string error){LastError=error;return false;}
        private void RequireOwner(ContractController owner)
        {if(!Allowed()||!ReferenceEquals(controller,owner))throw new InvalidOperationException("Only the bound active own-campaign controller can change progression.");}
    }
}