using System;
namespace HowToSuck
{
    public enum CargoRole { OrdinaryLoot = 0, BossBody = 1 }
    public readonly struct LootKey : IEquatable<LootKey>
    {
        public readonly string RunId;
        public readonly ulong InstanceId;
        public LootKey(string runId, ulong instanceId) { RunId = runId; InstanceId = instanceId; }
        public bool IsValid => !string.IsNullOrWhiteSpace(RunId) && RunId == RunId.Trim() && InstanceId != 0;
        public bool Equals(LootKey other) => RunId == other.RunId && InstanceId == other.InstanceId;
        public override bool Equals(object obj) => obj is LootKey other && Equals(other);
        public override int GetHashCode() => unchecked((RunId?.GetHashCode() ?? 0) * 397 ^ InstanceId.GetHashCode());
        public override string ToString() => RunId + ":" + InstanceId;
    }
    // InstanceId identifies the active boss, not the separate cargo GameObject.
    public readonly struct BossKey : IEquatable<BossKey>
    {
        public readonly string RunId, ContractBossId;
        public readonly ulong InstanceId;
        public BossKey(string runId, string contractBossId, ulong instanceId)
        { RunId = runId; ContractBossId = contractBossId; InstanceId = instanceId; }
        public bool IsValid => new LootKey(RunId, InstanceId).IsValid &&
            !string.IsNullOrWhiteSpace(ContractBossId) && ContractBossId == ContractBossId.Trim();
        public bool Equals(BossKey other) => RunId == other.RunId && ContractBossId == other.ContractBossId && InstanceId == other.InstanceId;
        public override bool Equals(object obj) => obj is BossKey other && Equals(other);
        public override int GetHashCode() => unchecked(new LootKey(RunId, InstanceId).GetHashCode() * 397 ^ (ContractBossId?.GetHashCode() ?? 0));
    }
}
