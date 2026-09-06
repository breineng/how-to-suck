using System;

namespace HowToSuck
{
    public enum ContractPhase { None, Preparing, Running, Succeeded, Failed, Aborted }

    // Copy these values from the authored SO before spawning the run.
    public sealed class ContractRules
    {
        public string ContractId { get; }
        public long Quota { get; }
        public double TimeLimitSeconds { get; }
        public int FailurePercent { get; }
        public string RequiredBossId { get; }

        public ContractRules(string contractId, long quota, double timeLimitSeconds, int failurePercent, string requiredBossId)
        {
            if (string.IsNullOrWhiteSpace(contractId) || contractId != contractId.Trim())
                throw new ArgumentException("A stable contract ID is required.", nameof(contractId));
            if (quota <= 0) throw new ArgumentOutOfRangeException(nameof(quota));
            if (double.IsNaN(timeLimitSeconds) || double.IsInfinity(timeLimitSeconds) || timeLimitSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeLimitSeconds));
            if (failurePercent < 0 || failurePercent > 100)
                throw new ArgumentOutOfRangeException(nameof(failurePercent));
            if (string.IsNullOrWhiteSpace(requiredBossId) || requiredBossId != requiredBossId.Trim())
                throw new ArgumentException("A main contract requires its explicit boss identity.", nameof(requiredBossId));
            RequiredBossId = requiredBossId;
            ContractId = contractId;
            Quota = quota;
            TimeLimitSeconds = timeLimitSeconds;
            FailurePercent = failurePercent;
        }
    }

    // Supply one entry per active authority player, using real current overlap.
    // Repeated collider-derived IDs are conservatively merged by the controller.
    public readonly struct ExtractionPlayerState
    {
        public int PlayerId { get; }
        public bool IsInside { get; }
        public bool ExtractionHeld { get; }
        public ExtractionPlayerState(int playerId, bool isInside, bool extractionHeld)
        { PlayerId = playerId; IsInside = isInside; ExtractionHeld = extractionHeld; }
    }

    public readonly struct ContractState
    {
        public string RunId { get; }
        public string ContractId { get; }
        public ContractPhase Phase { get; }
        public long CollectedMoney { get; }
        public long Quota { get; }
        public int CollectedInstanceCount { get; }
        public double StartedAt { get; }
        public double Deadline { get; }
        public double ObservedAt { get; }
        public int ExtractionInitiatorId { get; }
        public double ExtractHoldProgress { get; }
        public long DeliveredValue => CollectedMoney;
        public int DeliveredCargoCount => CollectedInstanceCount;
        public BossObjectiveSnapshot Boss { get; }
        public bool ObjectivesComplete => QuotaReached && Boss.IsDelivered;
        public bool QuotaReached => Quota > 0 && CollectedMoney >= Quota;
        public bool IsTerminal => Phase == ContractPhase.Succeeded ||
            Phase == ContractPhase.Failed || Phase == ContractPhase.Aborted;
        public double RemainingSeconds => Phase == ContractPhase.Running ?
            Math.Max(0, Math.Ceiling(Deadline - ObservedAt)) : 0;

        internal ContractState(string runId, string contractId, ContractPhase phase,
            long money, long quota, int count, double startedAt, double deadline,
            double observedAt, int initiatorId, double holdProgress, BossObjectiveSnapshot boss = default)
        {
            RunId = runId; ContractId = contractId; Phase = phase; Boss = boss;
            CollectedMoney = money; Quota = quota; CollectedInstanceCount = count;
            StartedAt = startedAt; Deadline = deadline; ObservedAt = observedAt;
            ExtractionInitiatorId = initiatorId; ExtractHoldProgress = holdProgress;
        }
    }
}
