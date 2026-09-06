using System;
using System.Collections.Generic;

namespace HowToSuck
{
    // Pure rules, stepped exclusively by the current authority.
    public sealed class ContractController
    {
        public const double ExtractionHoldSeconds = 2;
        public event Action<ContractResult> Finished;
        public ContractResult FinalResult { get; private set; }
        public ContractState State => new ContractState(runId, rules?.ContractId, phase,
            collectedMoney, rules?.Quota ?? 0, ledger.Count, startedAt, deadline,
            lastNow, initiatorId, HoldProgress);
        public bool IsRunning => phase == ContractPhase.Running;

        private readonly ProgressionService progression;
        private readonly Func<BossKey, bool> bossDeliveryValidator;
        private readonly HashSet<ulong> ledger = new HashSet<ulong>();
        private readonly HashSet<int> previousRoster = new HashSet<int>();
        private readonly Dictionary<int, ExtractionPlayerState> uniqueRoster =
            new Dictionary<int, ExtractionPlayerState>();
        private ContractRules rules;
        private string runId;
        private ContractPhase phase;
        private long collectedMoney;
        private double startedAt, deadline, lastNow, holdStartedAt;
        private int initiatorId;

        public ContractController(ProgressionService progression, Func<BossKey, bool> bossDeliveryValidator = null)
        {
            this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
            this.bossDeliveryValidator = bossDeliveryValidator;
            progression.Attach(this);
        }

        public void Prepare(string newRunId, ContractRules contract)
        {
            if (string.IsNullOrWhiteSpace(newRunId) || newRunId != newRunId.Trim())
                throw new ArgumentException("A fresh run ID is required.", nameof(newRunId));
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            if (phase == ContractPhase.Preparing || phase == ContractPhase.Running)
                throw new InvalidOperationException("The existing run is still active.");
            progression.ReserveRun(this, newRunId);
            runId = newRunId; rules = contract; phase = ContractPhase.Preparing;
            collectedMoney = 0; startedAt = 0; deadline = 0; lastNow = 0;
            FinalResult = null; ledger.Clear(); previousRoster.Clear(); uniqueRoster.Clear();
            ResetHold();
        }

        public void Start(double now)
        {
            if (phase != ContractPhase.Preparing)
                throw new InvalidOperationException("Prepare the run world before starting its timer.");
            double newDeadline = now + rules.TimeLimitSeconds;
            if (!Finite(now) || !Finite(newDeadline) || newDeadline <= now)
                throw new ArgumentOutOfRangeException(nameof(now), "The deadline must be finite and later than now.");
            startedAt = now; lastNow = now; deadline = newDeadline;
            phase = ContractPhase.Running;
        }

        public void Begin(string newRunId, ContractRules contract, double now)
        {
            // Validate time before reserving the run; failed convenience calls are atomic.
            if (contract == null) throw new ArgumentNullException(nameof(contract));
            double end = now + contract.TimeLimitSeconds;
            if (!Finite(now) || !Finite(end) || end <= now) throw new ArgumentOutOfRangeException(nameof(now));
            Prepare(newRunId, contract);
            Start(now);
        }

        // Failed loading can return to the lobby without a terminal Results screen.
        public bool CancelPreparation()
        {
            if (phase != ContractPhase.Preparing) return false;
            progression.CancelPreparation(this, runId);
            phase = ContractPhase.None; runId = null; rules = null;
            collectedMoney = 0; ledger.Clear(); ResetHold();
            previousRoster.Clear(); uniqueRoster.Clear();
            return true;
        }

        // True only when this call performs the timeout transition.
        public bool CheckDeadline(double now)
        {
            if (!ObserveRunning(now)) return false;
            return ExpireIfDue(now);
        }

        public bool TryRecordDelivery(DeliveryRecord record, double now)
        {
            // Time wins before even examining record validity or old run metadata.
            if (!ObserveRunning(now) || ExpireIfDue(now)) return false;
            if (!record.IsTruck || record.IntakeId <= 0 || record.RunId != runId || record.InstanceId == 0 ||
                string.IsNullOrWhiteSpace(record.TypeId) || !ValidDeliveryCargo(record) ||
                ledger.Contains(record.InstanceId) || record.Value > long.MaxValue - collectedMoney)
                return false;
            ledger.Add(record.InstanceId);
            collectedMoney += record.Value;
            return true;
        }

        private bool ValidDeliveryCargo(DeliveryRecord record)
        {
            if (record.CargoRole == CargoRole.OrdinaryLoot) return record.Value > 0 && !record.BossKey.IsValid;
            if (record.CargoRole != CargoRole.BossBody || record.Value != 0 || !record.BossKey.IsValid || record.BossKey.RunId != runId || bossDeliveryValidator == null) return false;
            return bossDeliveryValidator(record.BossKey);
        }

        // True when this call completes the run. Empty roster resets the hold;
        // SessionRoot/network driver owns any decision to abort after disconnect.
        public bool StepExtraction(double now, IReadOnlyList<ExtractionPlayerState> roster)
        {
            if (!ObserveRunning(now)) return false;
            if (ExpireIfDue(now)) return true;
            uniqueRoster.Clear();
            if (roster != null)
            {
                foreach (var player in roster)
                {
                    if (player.PlayerId <= 0)
                    { previousRoster.Clear(); ResetHold(); return false; }
                    if (uniqueRoster.TryGetValue(player.PlayerId, out var prior))
                        uniqueRoster[player.PlayerId] = new ExtractionPlayerState(player.PlayerId,
                            prior.IsInside && player.IsInside, prior.ExtractionHeld && player.ExtractionHeld);
                    else uniqueRoster.Add(player.PlayerId, player);
                }
            }
            bool sameRoster = previousRoster.SetEquals(uniqueRoster.Keys);
            if (!sameRoster)
            {
                ResetHold();
                previousRoster.Clear();
                foreach (int id in uniqueRoster.Keys) previousRoster.Add(id);
            }
            if (uniqueRoster.Count == 0 || collectedMoney < rules.Quota)
            { ResetHold(); return false; }
            foreach (var player in uniqueRoster.Values)
                if (!player.IsInside) { ResetHold(); return false; }

            if (initiatorId != 0 &&
                (!uniqueRoster.TryGetValue(initiatorId, out var initiator) || !initiator.ExtractionHeld))
                ResetHold();
            if (initiatorId == 0)
            {
                int selected = 0;
                foreach (var player in uniqueRoster.Values)
                    if (player.ExtractionHeld && (selected == 0 || player.PlayerId < selected))
                        selected = player.PlayerId;
                if (selected == 0) return false;
                initiatorId = selected; holdStartedAt = now;
            }
            if (now - holdStartedAt < ExtractionHoldSeconds) return false;
            Finish(ContractPhase.Succeeded, now);
            return true;
        }

        public bool Abort(double now)
        {
            if (phase == ContractPhase.Running)
            {
                if (!ObserveRunning(now)) return false;
                if (ExpireIfDue(now)) return true;
            }
            else if (phase == ContractPhase.Preparing)
            {
                if (!Finite(now)) return false;
                lastNow = now;
            }
            else return false;
            Finish(ContractPhase.Aborted, now);
            return true;
        }

        private bool ObserveRunning(double now)
        {
            if (phase != ContractPhase.Running || !Finite(now) || now < lastNow) return false;
            lastNow = now;
            return true;
        }

        private bool ExpireIfDue(double now)
        {
            if (now < deadline) return false;
            Finish(ContractPhase.Failed, now);
            return true;
        }

        private void Finish(ContractPhase terminal, double now)
        {
            if (phase != ContractPhase.Running && phase != ContractPhase.Preparing) return;
            phase = terminal; ResetHold();
            var result = new ContractResult(progression.Campaign.CampaignId, runId,
                rules.ContractId, terminal, collectedMoney, rules.Quota, rules.FailurePercent,
                startedAt, deadline, now);
            FinalResult = result;
            progression.Publish(this, result);
            // State/pending commit precedes callbacks. A callback may settle/start
            // another run; this call performs no old-run writes after notification.
            Finished?.Invoke(result);
        }

        private void ResetHold() { initiatorId = 0; holdStartedAt = 0; }
        private double HoldProgress => initiatorId == 0 ? 0 :
            Math.Max(0, Math.Min(1, (lastNow - holdStartedAt) / ExtractionHoldSeconds));
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

