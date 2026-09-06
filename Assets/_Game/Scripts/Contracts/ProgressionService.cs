using System;
using System.Collections.Generic;

namespace HowToSuck
{
    public sealed class ProgressionService
    {
        public CampaignState Campaign { get; }
        public ContractResult PendingResult { get; private set; }
        public string CurrentRunId { get; private set; }
        public bool CanStartRun => CurrentRunId == null && PendingResult == null;

        private ContractController controller;
        private readonly HashSet<string> usedRunIds = new HashSet<string>(StringComparer.Ordinal);

        public ProgressionService(CampaignState campaign)
        { Campaign = campaign ?? throw new ArgumentNullException(nameof(campaign)); }

        internal void Attach(ContractController owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (controller != null && !ReferenceEquals(controller, owner))
                throw new InvalidOperationException("This campaign already has its authority controller.");
            controller = owner;
        }

        internal void ReserveRun(ContractController owner, string runId)
        {
            RequireOwner(owner);
            if (!CanStartRun)
                throw new InvalidOperationException("Settle the current result before starting another run.");
            if (runId == Campaign.LastSettledRunId || !usedRunIds.Add(runId))
                throw new InvalidOperationException("A run ID cannot be reused within a campaign.");
            CurrentRunId = runId;
        }

        internal void CancelPreparation(ContractController owner, string runId)
        {
            RequireOwner(owner);
            if (PendingResult != null || CurrentRunId != runId)
                throw new InvalidOperationException("This preparation is no longer current.");
            CurrentRunId = null;
        }

        internal void Publish(ContractController owner, ContractResult result)
        {
            RequireOwner(owner);
            if (result == null || !ReferenceEquals(owner.FinalResult, result) ||
                result.CampaignId != Campaign.CampaignId || result.RunId != CurrentRunId ||
                PendingResult != null)
                throw new InvalidOperationException("Only the current authority result may become pending.");
            PendingResult = result;
        }

        // Metadata alone never authenticates a result. A -> B -> delayed A fails
        // even though CampaignState intentionally keeps only the last settled ID.
        public bool TryApplyResult(ContractResult result)
        {
            if (result == null || !ReferenceEquals(result, PendingResult) ||
                result.CampaignId != Campaign.CampaignId || result.RunId != CurrentRunId ||
                result.RunId == Campaign.LastSettledRunId ||
                controller == null || !ReferenceEquals(controller.FinalResult, result))
                return false;
            if (result.Payout > long.MaxValue - Campaign.Balance)
                return false; // Keep pending intact; no partial settlement or wrapped balance.

            long balance = Campaign.Balance + result.Payout;
            Campaign.CommitSettlement(balance, result.RunId);
            PendingResult = null;
            CurrentRunId = null;
            return true;
        }

        private void RequireOwner(ContractController owner)
        {
            if (!ReferenceEquals(controller, owner))
                throw new InvalidOperationException("A different controller cannot change this campaign.");
        }
    }
}
