using System;
using Unity.Collections;
using UnityEngine;
namespace HowToSuck.Networking
{
    public static class GameplayStateValidation
    {
        public static BossKey Key(FixedString64Bytes run,FixedString64Bytes id,ulong instance)=>
            run.Length==0&&id.Length==0&&instance==0?default:new BossKey(run.ToString(),id.ToString(),instance);
        public static LootReplicaProvenance Provenance(LootWire s)=>new LootReplicaProvenance(s.Type.ToString(),(CargoRole)s.CargoRole,
            Key(s.BossRun,s.BossId,s.BossInstance),s.StoredOwner,s.LastStorageOwner,s.LastStorageTier.ToString(),s.ActiveShotId);
        public static PlayerStorageSnapshot Storage(PlayerWire s)=>new PlayerStorageSnapshot(s.Run.ToString(),s.PlayerId,s.StorageCount,
            s.StorageReserved,s.StorageCapacity,s.StorageNextType.ToString(),(CargoRole)s.StorageNextRole);
        public static PlayerSuitPresentation Suit(PlayerWire s)=>new PlayerSuitPresentation(
            new PlayerSuitSnapshot(s.Run.ToString(),s.PlayerId,s.SuitSegments,s.SuitInvulnerableUntil,s.SuitRecoveryPending),s.SuitObservedAt,s.Frozen);
        public static BossObjectiveSnapshot Boss(SessionWire s,string requiredBossId)=>GameplayReplicaPolicy.BossSnapshot(s.Run.ToString(),requiredBossId,
            Key(s.BossRun,s.BossId,s.BossInstance),(BossObjectiveStatus)s.BossStatus);
        public static void RequireLoot(LootWire s,string run,uint revision,ulong instance,string type,CargoRole role)
        {
            if(s.Run.ToString()!=run||s.Revision!=revision||revision==0||s.InstanceId!=instance||instance==0)
                throw new InvalidOperationException("Loot run/revision/instance changed.");
            GameplayReplicaPolicy.RequireLoot(run,(SuckableState)s.State,type,role,Provenance(s));
            if(s.Ingesting!=(s.State==(byte)SuckableState.Ingesting))throw new InvalidOperationException("Ingestion flag disagrees with cargo state.");
            if(!s.Ingesting)return;
            if(s.IntakeId<=0||(s.Truck?(s.IntakeId!=TruckIntake.DefaultIntakeId||s.PlayerId!=0):(!GameplayReplicaPolicy.Player(s.PlayerId)||s.IntakeId!=s.PlayerId)))
                throw new InvalidOperationException("Invalid ingestion receiver attribution.");
            if(!Finite(s.Started)||!Finite(s.Duration)||s.Duration<=0||!Finite(s.Size)||s.Size<=0||!Vector(s.Start)||!Vector(s.Target)||!Vector(s.End)||
                !Vector(s.Scale)||s.Scale.x<=0||s.Scale.y<=0||s.Scale.z<=0||!Rotation(s.StartRotation)||!Rotation(s.TargetRotation))
                throw new InvalidOperationException("Invalid ingestion presentation geometry/time.");
        }
        public static void RequirePlayer(PlayerWire s,string run,uint revision,int player,string tier)
        {
            if(s.Run.ToString()!=run||s.Revision!=revision||revision==0||s.PlayerId!=player||s.Tier.ToString()!=tier||
                !GameplayReplicaPolicy.Player(player)||!CampaignCapacityRules.TryModel(tier,out _,out _)||!Finite(s.Yaw)||!Finite(s.Pitch)||
                !Finite(s.Move.x)||!Finite(s.Move.y)||!Finite(s.Vertical)||!Finite(s.PlanarSpeed)||s.PlanarSpeed<0)
                throw new InvalidOperationException("Player identity or presentation changed inconsistently.");
            Storage(s); // Includes owner/run, count+reserved, capacity and coherent FIFO validation.
            Suit(s); // Same owner/run envelope; invalid suit data cannot satisfy prepared-snapshot readiness.
        }
        public static BossObjectiveSnapshot RequireSession(SessionWire s,string requiredBossId,int authoredFailurePercent)
        {
            if(!Enum.IsDefined(typeof(SessionPhase),(int)s.Phase)||!Enum.IsDefined(typeof(ContractPhase),(int)s.ContractPhase)||
                s.Money<0||s.Balance<0||s.Count<0||!Finite(s.Hold)||s.Hold<0||s.Hold>1||!Finite(s.Started)||!Finite(s.Deadline)||
                !Finite(s.Observed)||!Finite(s.Finished)||s.ExtractionMask>15)
                throw new InvalidOperationException("Invalid session replica.");
            if(s.HasCampaignProgression){
                SaveIdentity.RequireGuid(s.Campaign.ToString(),"replicatedCampaign");
                if(!CampaignCapacityRules.ValidBonus(s.PurchasedExtraSlots)||(s.ClearedContractMask&~63)!=0||
                    !CampaignCapacityRules.TryModel(s.CampaignTier.ToString(),out _,out _))throw new InvalidOperationException("Invalid campaign progression snapshot.");
            }else if(s.Campaign.Length!=0||s.CampaignTier.Length!=0||s.PurchasedExtraSlots!=0||s.ClearedContractMask!=0||s.LegacyContractAccess)
                throw new InvalidOperationException("Missing coherent campaign progression.");
            var phase=(ContractPhase)s.ContractPhase;
            if(phase==ContractPhase.None){
                if(s.Run.Length!=0||s.Contract.Length!=0||s.HasResult||s.Money!=0||s.Count!=0||s.Quota!=0||s.BossStatus!=0||!GameplayReplicaPolicy.EmptyBoss(Key(s.BossRun,s.BossId,s.BossInstance)))
                    throw new InvalidOperationException("Inactive contract carries gameplay facts.");
                return default;
            }
            GameplayReplicaPolicy.RequireRun(s.Run.ToString());
            if(!GameplayReplicaPolicy.StableId(s.Contract.ToString())||!GameplayReplicaPolicy.StableId(requiredBossId)||s.Quota<=0)
                throw new InvalidOperationException("Active contract is not in the authored boss catalog.");
            var boss=Boss(s,requiredBossId);
            if(phase==ContractPhase.Running&&(boss.Status==BossObjectiveStatus.Unassigned||s.Deadline<=s.Started||s.Observed<s.Started||s.Observed>=s.Deadline))
                throw new InvalidOperationException("Invalid running objective or clock.");
            if(phase==ContractPhase.Succeeded&&(s.Money<s.Quota||!boss.IsDelivered))throw new InvalidOperationException("Succeeded without both objectives.");
            if(s.HasResult){
                int percent=phase==ContractPhase.Succeeded?100:phase==ContractPhase.Failed?authoredFailurePercent:phase==ContractPhase.Aborted?0:-1;
                if(percent<0||percent>100||s.PayoutPercent!=percent||s.Finished!=s.Observed||
                    phase==ContractPhase.Succeeded&&(s.Finished<s.Started||s.Finished>=s.Deadline)||
                    phase==ContractPhase.Failed&&(s.Deadline<=s.Started||s.Finished<s.Deadline))
                    throw new InvalidOperationException("Inconsistent terminal result phase/time/percentage.");
                SaveIdentity.RequireGuid(s.Campaign.ToString(),"resultCampaign");
                long expected=checked((s.Money/100)*percent+((s.Money%100)*percent)/100);
                if(s.Payout!=expected)throw new InvalidOperationException("Inconsistent replicated payout.");
            }
            return boss;
        }
        public static bool SameTerminalResult(SessionWire a,SessionWire b)=>a.Run.Equals(b.Run)&&a.Contract.Equals(b.Contract)&&a.Campaign.Equals(b.Campaign)&&
            a.ContractPhase==b.ContractPhase&&a.Money==b.Money&&a.Quota==b.Quota&&a.Started==b.Started&&a.Deadline==b.Deadline&&a.Finished==b.Finished&&
            a.PayoutPercent==b.PayoutPercent&&a.Payout==b.Payout&&a.BossRun.Equals(b.BossRun)&&a.BossId.Equals(b.BossId)&&a.BossInstance==b.BossInstance&&a.BossStatus==b.BossStatus;
        private static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
        private static bool Vector(Vector3 x)=>Finite(x.x)&&Finite(x.y)&&Finite(x.z);
        private static bool Rotation(Quaternion q)=>Finite(q.x)&&Finite(q.y)&&Finite(q.z)&&Finite(q.w)&&Math.Abs((double)q.x*q.x+(double)q.y*q.y+(double)q.z*q.z+(double)q.w*q.w-1)<.001;
    }
}
