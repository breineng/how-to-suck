using System;
using System.Collections;
using UnityEngine;
namespace HowToSuck
{
    // Optional composition seam. The role is selected before World.Initialize and cannot change in-place.
    public interface ISessionDriverProvider { ISessionDriver CreateDriver(SessionRoot session); }
    public interface INetworkSessionDriver : ISessionDriver
    {
        bool HasAuthority { get; }
        bool CanBeginContract { get; }
        void Bind(SessionRoot session);
        bool EnterPreparing(string contractId);
        IEnumerator PrepareGameplay(LevelContext level, VacuumDefinition vacuum);
        void ClearPlayers();
        void PhaseChanged(SessionPhase phase);
        void LeaveGuest();
    }
    // Presentation data only. Creating this copy never prepares a controller or applies campaign money.
    public sealed class SessionReplica
    {
        public SessionPhase Phase;
        public ContractState State;
        public ContractResult Result;
        public long Balance;
        public string CurrentTierId; // Confirmed host tier for readonly Lobby/shop presentation.
        public string SelectedContractId; // Host Lobby choice, separate from active contract/result.
        public bool HasCampaignProgression;
        public int PurchasedExtraSlots;
        public byte ClearedContractMask;
        public bool LegacyContractAccess;
        public bool PendingPayout, AllInExtraction;
        public byte ExtractionMask;
        public string Error;
        public static ContractState StateCopy(string run, string contract, ContractPhase phase, long money,
            long quota, int count, double started, double deadline, double observed, int initiator, double hold, BossObjectiveSnapshot boss) =>
            new ContractState(run, contract, phase, money, quota, count, started, deadline, observed, initiator, hold,boss);
        public static ContractResult ResultCopy(string campaign, string run, string contract, ContractPhase phase,
            long money, long quota, int percent, long payout, double started, double deadline, double finished, BossObjectiveSnapshot boss)
        {
            SaveIdentity.RequireGuid(campaign,nameof(campaign));GameplayReplicaPolicy.RequireRun(run);
            GameplayReplicaPolicy.BossSnapshot(run,boss.Key.ContractBossId,boss.Key,boss.Status);
            var copy = new ContractResult(campaign, run, contract, phase, money, quota, percent, started, deadline, finished,boss);
            if (copy.Payout != payout || copy.PayoutPercent != percent) throw new InvalidOperationException("Inconsistent replicated result.");
            return copy;
        }
    }
}
