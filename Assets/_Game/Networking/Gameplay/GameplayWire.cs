using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
namespace HowToSuck.Networking
{
    public struct IntentWire : INetworkSerializable, IEquatable<IntentWire>
    {
        public FixedString64Bytes Run;
        public uint Sequence;
        public uint Jump;
        public uint Fire;
        public bool FireHeld;
        public Vector2 Move;
        public float Yaw;
        public float Pitch;
        public bool Sprint;
        public bool Vacuum;
        public bool Interact;
        public bool SuppressJump;
        public bool SuppressFire;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Run);
            s.SerializeValue(ref Sequence);
            s.SerializeValue(ref Jump);
            s.SerializeValue(ref Fire);
            s.SerializeValue(ref FireHeld);
            s.SerializeValue(ref Move);
            s.SerializeValue(ref Yaw);
            s.SerializeValue(ref Pitch);
            s.SerializeValue(ref Sprint);
            s.SerializeValue(ref Vacuum);
            s.SerializeValue(ref Interact);
            s.SerializeValue(ref SuppressJump);
            s.SerializeValue(ref SuppressFire);
        }
        public bool Equals(IntentWire x) =>
            Run.Equals(x.Run) &&
            Sequence.Equals(x.Sequence) &&
            Jump.Equals(x.Jump) &&
            Fire.Equals(x.Fire) &&
            FireHeld.Equals(x.FireHeld) &&
            Move.Equals(x.Move) &&
            Yaw.Equals(x.Yaw) &&
            Pitch.Equals(x.Pitch) &&
            Sprint.Equals(x.Sprint) &&
            Vacuum.Equals(x.Vacuum) &&
            Interact.Equals(x.Interact) &&
            SuppressJump.Equals(x.SuppressJump) &&
            SuppressFire.Equals(x.SuppressFire);
    }
    public struct PlayerWire : INetworkSerializable, IEquatable<PlayerWire>
    {
        public FixedString64Bytes Run;
        public FixedString64Bytes Tier;
        public uint Revision;
        public int PlayerId;
        public uint Sequence;
        public uint Jump;
        public Vector2 Move;
        public float Yaw;
        public float Pitch;
        public float Vertical;
        public float PlanarSpeed;
        public float FireCharge;
        public bool Grounded;
        public bool Sprint;
        public bool Vacuum;
        public bool Interact;
        public bool Frozen;
        public int StorageCount;
        public int StorageReserved;
        public int StorageCapacity;
        public FixedString64Bytes StorageNextType;
        public byte StorageNextRole;
        public int SuitSegments;
        public FixedString4096Bytes StorageTypes;
        public int RepairCharges;
        public float RepairProgress;
        public bool SuitRecoveryPending;
        public double SuitInvulnerableUntil;
        public double SuitObservedAt;
        public uint FireFeedbackSequence;
        public bool FireWasBlocked;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Run);
            s.SerializeValue(ref Tier);
            s.SerializeValue(ref Revision);
            s.SerializeValue(ref PlayerId);
            s.SerializeValue(ref Sequence);
            s.SerializeValue(ref Jump);
            s.SerializeValue(ref Move);
            s.SerializeValue(ref Yaw);
            s.SerializeValue(ref Pitch);
            s.SerializeValue(ref Vertical);
            s.SerializeValue(ref PlanarSpeed);
            s.SerializeValue(ref FireCharge);
            s.SerializeValue(ref Grounded);
            s.SerializeValue(ref Sprint);
            s.SerializeValue(ref Vacuum);
            s.SerializeValue(ref Interact);
            s.SerializeValue(ref Frozen);
            s.SerializeValue(ref StorageCount);
            s.SerializeValue(ref StorageReserved);
            s.SerializeValue(ref StorageCapacity);
            s.SerializeValue(ref StorageNextType);
            s.SerializeValue(ref StorageNextRole);
            s.SerializeValue(ref SuitSegments);
            s.SerializeValue(ref StorageTypes);s.SerializeValue(ref RepairCharges);s.SerializeValue(ref RepairProgress);
            s.SerializeValue(ref SuitRecoveryPending);
            s.SerializeValue(ref SuitInvulnerableUntil);
            s.SerializeValue(ref SuitObservedAt);
            s.SerializeValue(ref FireFeedbackSequence);
            s.SerializeValue(ref FireWasBlocked);
        }
        public bool Equals(PlayerWire x) =>
            Run.Equals(x.Run) &&
            Tier.Equals(x.Tier) &&
            Revision.Equals(x.Revision) &&
            PlayerId.Equals(x.PlayerId) &&
            Sequence.Equals(x.Sequence) &&
            Jump.Equals(x.Jump) &&
            Move.Equals(x.Move) &&
            Yaw.Equals(x.Yaw) &&
            Pitch.Equals(x.Pitch) &&
            Vertical.Equals(x.Vertical) &&
            PlanarSpeed.Equals(x.PlanarSpeed) &&
            FireCharge.Equals(x.FireCharge) &&
            Grounded.Equals(x.Grounded) &&
            Sprint.Equals(x.Sprint) &&
            Vacuum.Equals(x.Vacuum) &&
            Interact.Equals(x.Interact) &&
            Frozen.Equals(x.Frozen) &&
            StorageCount.Equals(x.StorageCount) &&
            StorageReserved.Equals(x.StorageReserved) &&
            StorageCapacity.Equals(x.StorageCapacity) &&
            StorageNextType.Equals(x.StorageNextType) &&
            StorageNextRole.Equals(x.StorageNextRole) &&
            SuitSegments.Equals(x.SuitSegments) && StorageTypes.Equals(x.StorageTypes) &&
            RepairCharges==x.RepairCharges && RepairProgress.Equals(x.RepairProgress) &&
            SuitRecoveryPending.Equals(x.SuitRecoveryPending) &&
            SuitInvulnerableUntil.Equals(x.SuitInvulnerableUntil) &&
            SuitObservedAt.Equals(x.SuitObservedAt) &&
            FireFeedbackSequence.Equals(x.FireFeedbackSequence) &&
            FireWasBlocked.Equals(x.FireWasBlocked);
    }
    public struct LootWire : INetworkSerializable, IEquatable<LootWire>
    {
        public FixedString64Bytes Run;
        public FixedString64Bytes Type;
        public uint Revision;
        public ulong InstanceId;
        public byte State;
        public bool Frozen;
        public bool Ingesting;
        public int IntakeId;
        public int PlayerId;
        public bool Truck;
        public double Started;
        public float Duration;
        public float Size;
        public Vector3 Start;
        public Quaternion StartRotation;
        public Vector3 Scale;
        public Vector3 Target;
        public Quaternion TargetRotation;
        public Vector3 End;
        public byte CargoRole;
        public FixedString64Bytes BossRun;
        public FixedString64Bytes BossId;
        public ulong BossInstance;
        public int StoredOwner;
        public int LastStorageOwner;
        public FixedString64Bytes LastStorageTier;
        public ulong ActiveShotId;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Run);
            s.SerializeValue(ref Type);
            s.SerializeValue(ref Revision);
            s.SerializeValue(ref InstanceId);
            s.SerializeValue(ref State);
            s.SerializeValue(ref Frozen);
            s.SerializeValue(ref Ingesting);
            s.SerializeValue(ref IntakeId);
            s.SerializeValue(ref PlayerId);
            s.SerializeValue(ref Truck);
            s.SerializeValue(ref Started);
            s.SerializeValue(ref Duration);
            s.SerializeValue(ref Size);
            s.SerializeValue(ref Start);
            s.SerializeValue(ref StartRotation);
            s.SerializeValue(ref Scale);
            s.SerializeValue(ref Target);
            s.SerializeValue(ref TargetRotation);
            s.SerializeValue(ref End);
            s.SerializeValue(ref CargoRole);
            s.SerializeValue(ref BossRun);
            s.SerializeValue(ref BossId);
            s.SerializeValue(ref BossInstance);
            s.SerializeValue(ref StoredOwner);
            s.SerializeValue(ref LastStorageOwner);
            s.SerializeValue(ref LastStorageTier);
            s.SerializeValue(ref ActiveShotId);
        }
        public bool Equals(LootWire x) =>
            Run.Equals(x.Run) &&
            Type.Equals(x.Type) &&
            Revision.Equals(x.Revision) &&
            InstanceId.Equals(x.InstanceId) &&
            State.Equals(x.State) &&
            Frozen.Equals(x.Frozen) &&
            Ingesting.Equals(x.Ingesting) &&
            IntakeId.Equals(x.IntakeId) &&
            PlayerId.Equals(x.PlayerId) &&
            Truck.Equals(x.Truck) &&
            Started.Equals(x.Started) &&
            Duration.Equals(x.Duration) &&
            Size.Equals(x.Size) &&
            Start.Equals(x.Start) &&
            StartRotation.Equals(x.StartRotation) &&
            Scale.Equals(x.Scale) &&
            Target.Equals(x.Target) &&
            TargetRotation.Equals(x.TargetRotation) &&
            End.Equals(x.End) &&
            CargoRole.Equals(x.CargoRole) &&
            BossRun.Equals(x.BossRun) &&
            BossId.Equals(x.BossId) &&
            BossInstance.Equals(x.BossInstance) &&
            StoredOwner.Equals(x.StoredOwner) &&
            LastStorageOwner.Equals(x.LastStorageOwner) &&
            LastStorageTier.Equals(x.LastStorageTier) &&
            ActiveShotId.Equals(x.ActiveShotId);
    }
    public struct SessionWire : INetworkSerializable, IEquatable<SessionWire>
    {
        public uint Revision;
        public FixedString64Bytes Run;
        public FixedString64Bytes Contract;
        public FixedString64Bytes Campaign;
        public FixedString64Bytes CampaignTier;
        public FixedString64Bytes SelectedContract;
        public byte Phase;
        public byte ContractPhase;
        public long Money;
        public long Quota;
        public long Balance;
        public int Count;
        public double Started;
        public double Deadline;
        public double Observed;
        public int Initiator;
        public double Hold;
        public bool HasResult;
        public int PayoutPercent;
        public long Payout;
        public double Finished;
        public bool PendingPayout;
        public byte ExtractionMask;
        public bool AllInExtraction;
        public int ExpectedItems;
        public int ExpectedPlayers;
        public bool CanStart;
        public bool TruckActive;
        public FixedString512Bytes Error;
        public bool HasCampaignProgression;
        public int PurchasedExtraSlots;
        public byte ClearedContractMask;
        public bool LegacyContractAccess;
        public FixedString64Bytes BossRun;
        public FixedString64Bytes BossId;
        public ulong BossInstance;
        public byte BossStatus;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Revision);
            s.SerializeValue(ref Run);
            s.SerializeValue(ref Contract);
            s.SerializeValue(ref Campaign);
            s.SerializeValue(ref CampaignTier);
            s.SerializeValue(ref SelectedContract);
            s.SerializeValue(ref Phase);
            s.SerializeValue(ref ContractPhase);
            s.SerializeValue(ref Money);
            s.SerializeValue(ref Quota);
            s.SerializeValue(ref Balance);
            s.SerializeValue(ref Count);
            s.SerializeValue(ref Started);
            s.SerializeValue(ref Deadline);
            s.SerializeValue(ref Observed);
            s.SerializeValue(ref Initiator);
            s.SerializeValue(ref Hold);
            s.SerializeValue(ref HasResult);
            s.SerializeValue(ref PayoutPercent);
            s.SerializeValue(ref Payout);
            s.SerializeValue(ref Finished);
            s.SerializeValue(ref PendingPayout);
            s.SerializeValue(ref ExtractionMask);
            s.SerializeValue(ref AllInExtraction);
            s.SerializeValue(ref ExpectedItems);
            s.SerializeValue(ref ExpectedPlayers);
            s.SerializeValue(ref CanStart);
            s.SerializeValue(ref TruckActive);
            s.SerializeValue(ref Error);
            s.SerializeValue(ref HasCampaignProgression);
            s.SerializeValue(ref PurchasedExtraSlots);
            s.SerializeValue(ref ClearedContractMask);
            s.SerializeValue(ref LegacyContractAccess);
            s.SerializeValue(ref BossRun);
            s.SerializeValue(ref BossId);
            s.SerializeValue(ref BossInstance);
            s.SerializeValue(ref BossStatus);
        }
        public bool Equals(SessionWire x) =>
            Revision.Equals(x.Revision) &&
            Run.Equals(x.Run) &&
            Contract.Equals(x.Contract) &&
            Campaign.Equals(x.Campaign) &&
            CampaignTier.Equals(x.CampaignTier) &&
            SelectedContract.Equals(x.SelectedContract) &&
            Phase.Equals(x.Phase) &&
            ContractPhase.Equals(x.ContractPhase) &&
            Money.Equals(x.Money) &&
            Quota.Equals(x.Quota) &&
            Balance.Equals(x.Balance) &&
            Count.Equals(x.Count) &&
            Started.Equals(x.Started) &&
            Deadline.Equals(x.Deadline) &&
            Observed.Equals(x.Observed) &&
            Initiator.Equals(x.Initiator) &&
            Hold.Equals(x.Hold) &&
            HasResult.Equals(x.HasResult) &&
            PayoutPercent.Equals(x.PayoutPercent) &&
            Payout.Equals(x.Payout) &&
            Finished.Equals(x.Finished) &&
            PendingPayout.Equals(x.PendingPayout) &&
            ExtractionMask.Equals(x.ExtractionMask) &&
            AllInExtraction.Equals(x.AllInExtraction) &&
            ExpectedItems.Equals(x.ExpectedItems) &&
            ExpectedPlayers.Equals(x.ExpectedPlayers) &&
            CanStart.Equals(x.CanStart) &&
            TruckActive.Equals(x.TruckActive) &&
            Error.Equals(x.Error) &&
            HasCampaignProgression.Equals(x.HasCampaignProgression) &&
            PurchasedExtraSlots.Equals(x.PurchasedExtraSlots) &&
            ClearedContractMask.Equals(x.ClearedContractMask) &&
            LegacyContractAccess.Equals(x.LegacyContractAccess) &&
            BossRun.Equals(x.BossRun) &&
            BossId.Equals(x.BossId) &&
            BossInstance.Equals(x.BossInstance) &&
            BossStatus.Equals(x.BossStatus);
    }
}
