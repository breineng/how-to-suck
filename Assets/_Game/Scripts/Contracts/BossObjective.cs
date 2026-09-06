using System;

namespace HowToSuck
{
    public enum BossObjectiveStatus : byte { Unassigned, Active, Defeated, Delivered }

    public readonly struct BossObjectiveSnapshot
    {
        public BossKey Key { get; }
        public BossObjectiveStatus Status { get; }
        public bool IsDelivered => Status == BossObjectiveStatus.Delivered && Key.IsValid;

        internal BossObjectiveSnapshot(BossKey key, BossObjectiveStatus status)
        {
            Key = key;
            Status = status;
        }
    }

    // Owned by one ContractController. UI, collision views and clients only receive a snapshot.
    public sealed class BossObjective
    {
        public string RunId { get; }
        public string RequiredBossId { get; }
        public BossKey Key { get; private set; }
        public BossObjectiveStatus Status { get; private set; }
        public BossObjectiveSnapshot Snapshot => new BossObjectiveSnapshot(Key, Status);

        public BossObjective(string runId, string requiredBossId)
        {
            if (string.IsNullOrWhiteSpace(runId) || runId != runId.Trim())
                throw new ArgumentException("Current run identity is required.", nameof(runId));
            if (string.IsNullOrWhiteSpace(requiredBossId) || requiredBossId != requiredBossId.Trim())
                throw new ArgumentException("A main contract must require a boss.", nameof(requiredBossId));
            RunId = runId;
            RequiredBossId = requiredBossId;
        }

        public bool TryAssign(BossKey key)
        {
            if (Status != BossObjectiveStatus.Unassigned || !key.IsValid ||
                !string.Equals(key.RunId, RunId, StringComparison.Ordinal) ||
                !string.Equals(key.ContractBossId, RequiredBossId, StringComparison.Ordinal)) return false;
            Key = key;
            Status = BossObjectiveStatus.Active;
            return true;
        }

        public bool TryDefeat(BossKey key)
        {
            if (Status != BossObjectiveStatus.Active || !Matches(key)) return false;
            Status = BossObjectiveStatus.Defeated;
            return true;
        }

        public bool CanDeliver(BossKey key) => Status == BossObjectiveStatus.Defeated && Matches(key);

        public bool TryDeliver(BossKey key)
        {
            if (!CanDeliver(key)) return false;
            Status = BossObjectiveStatus.Delivered;
            return true;
        }

        private bool Matches(BossKey key) => key.IsValid && Key.IsValid && Key.Equals(key);
    }
}
