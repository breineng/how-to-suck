using System;

namespace HowToSuck
{
    // An immutable authority result. Only the bound controller publishes it.
    public sealed class ContractResult
    {
        public string CampaignId { get; }
        public string RunId { get; }
        public string ContractId { get; }
        public ContractPhase Phase { get; }
        public long CollectedMoney { get; }
        public long Quota { get; }
        public int PayoutPercent { get; }
        public long Payout { get; }
        public double StartedAt { get; }
        public double Deadline { get; }
        public double FinishedAt { get; }

        internal ContractResult(string campaignId, string runId, string contractId,
            ContractPhase phase, long collectedMoney, long quota, int failurePercent,
            double startedAt, double deadline, double finishedAt)
        {
            if (phase != ContractPhase.Succeeded && phase != ContractPhase.Failed &&
                phase != ContractPhase.Aborted)
                throw new ArgumentException("A result must be terminal.", nameof(phase));
            if (collectedMoney < 0) throw new ArgumentOutOfRangeException(nameof(collectedMoney));
            if (failurePercent < 0 || failurePercent > 100)
                throw new ArgumentOutOfRangeException(nameof(failurePercent));
            CampaignId = campaignId; RunId = runId; ContractId = contractId; Phase = phase;
            CollectedMoney = collectedMoney; Quota = quota;
            PayoutPercent = phase == ContractPhase.Succeeded ? 100 :
                phase == ContractPhase.Failed ? failurePercent : 0;
            // floor(money * percent / 100), without overflowing money * percent.
            Payout = checked((collectedMoney / 100) * PayoutPercent +
                ((collectedMoney % 100) * PayoutPercent) / 100);
            StartedAt = startedAt; Deadline = deadline; FinishedAt = finishedAt;
        }
    }
}
