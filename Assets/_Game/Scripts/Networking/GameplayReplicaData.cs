using System;
using System.Text;
namespace HowToSuck
{
    // Read-only facts. Applying these on a guest never enters inventory, ingestion, combat or campaign services.
    public readonly struct LootReplicaProvenance
    {
        public readonly string TypeId,LastStorageTierId;
        public readonly CargoRole CargoRole;
        public readonly BossKey BossKey;
        public readonly int StoredOwner,LastStorageOwner;
        public readonly ulong ActiveShotId;
        public LootReplicaProvenance(string type,CargoRole role,BossKey boss,int stored,int last,string lastTier,ulong shot)
        {TypeId=type;CargoRole=role;BossKey=boss;StoredOwner=stored;LastStorageOwner=last;LastStorageTierId=lastTier??"";ActiveShotId=shot;}
    }
    public readonly struct PlayerStorageSnapshot
    {
        public readonly string RunId,NextTypeId;
        public readonly int OwnerId,Count,Reserved,Capacity;
        public readonly CargoRole NextCargoRole;
        public bool IsKnown=>!string.IsNullOrEmpty(RunId);
        public PlayerStorageSnapshot(string run,int owner,int count,int reserved,int capacity,string nextType,CargoRole nextRole)
        {
            GameplayReplicaPolicy.RequireRun(run);
            if(!GameplayReplicaPolicy.Player(owner)||capacity<1||capacity>16||count<0||reserved<0||count>capacity||reserved>capacity-count)
                throw new ArgumentException("Invalid coherent storage counts/owner/capacity.");
            if(count==0 ? !string.IsNullOrEmpty(nextType)||nextRole!=CargoRole.OrdinaryLoot : !GameplayReplicaPolicy.StableId(nextType)||!GameplayReplicaPolicy.Role(nextRole))
                throw new ArgumentException("FIFO description must correspond to committed stored count.");
            RunId=run;OwnerId=owner;Count=count;Reserved=reserved;Capacity=capacity;NextTypeId=nextType??"";NextCargoRole=nextRole;
        }
        public bool SameValues(PlayerStorageSnapshot other)=>RunId==other.RunId&&OwnerId==other.OwnerId&&Count==other.Count&&
            Reserved==other.Reserved&&Capacity==other.Capacity&&NextTypeId==other.NextTypeId&&NextCargoRole==other.NextCargoRole;
    }
    public static class GameplayReplicaPolicy
    {
        public static bool Player(int id)=>id>=1&&id<=4;
        public static bool Role(CargoRole role)=>role==CargoRole.OrdinaryLoot||role==CargoRole.BossBody;
        public static bool StableId(string text)=>!string.IsNullOrWhiteSpace(text)&&text==text.Trim()&&Encoding.UTF8.GetByteCount(text)<=61&&
            !ContainsControl(text);
        private static bool ContainsControl(string text){foreach(char c in text)if(char.IsControl(c))return true;return false;}
        public static bool CanonicalRun(string run)=>Guid.TryParseExact(run,"N",out var parsed)&&parsed!=Guid.Empty&&parsed.ToString("N")==run;
        public static void RequireRun(string run){if(!CanonicalRun(run))throw new ArgumentException("A canonical current run is required.");}
        public static bool EmptyBoss(BossKey key)=>string.IsNullOrEmpty(key.RunId)&&string.IsNullOrEmpty(key.ContractBossId)&&key.InstanceId==0;
        public static BossObjectiveSnapshot BossSnapshot(string run,string requiredBossId,BossKey key,BossObjectiveStatus status)
        {
            if(status<BossObjectiveStatus.Unassigned||status>BossObjectiveStatus.Delivered)throw new ArgumentException("Unknown boss objective status.");
            if(status==BossObjectiveStatus.Unassigned){if(!EmptyBoss(key))throw new ArgumentException("Unassigned objective cannot carry a key.");return default;}
            RequireRun(run);
            if(!StableId(requiredBossId)||!key.IsValid||key.RunId!=run||key.ContractBossId!=requiredBossId)
                throw new ArgumentException("Boss snapshot does not match the active run and authored required boss.");
            return new BossObjectiveSnapshot(key,status);
        }
        public static void RequireBossAdvance(BossObjectiveSnapshot previous,BossObjectiveSnapshot next)
        {
            if(previous.Status==BossObjectiveStatus.Unassigned)return;
            if(!previous.Key.Equals(next.Key)||next.Status<previous.Status)
                throw new ArgumentException("A current-run boss identity/status cannot regress or be replaced.");
        }
        public static void RequireLoot(string run,SuckableState state,string expectedType,CargoRole expectedRole,LootReplicaProvenance value)
        {
            RequireRun(run);
            if(state<SuckableState.Available||state>SuckableState.Spent||!StableId(value.TypeId)||value.TypeId!=expectedType||!Role(value.CargoRole)||value.CargoRole!=expectedRole)
                throw new ArgumentException("Loot state/type/role is not the frozen authored identity.");
            if(value.CargoRole==CargoRole.BossBody){
                if(!value.BossKey.IsValid||value.BossKey.RunId!=run||!StableId(value.BossKey.ContractBossId)||state==SuckableState.Spent)
                    throw new ArgumentException("Boss cargo requires its exact current-run key and cannot be spent ordinary ammunition.");
            }else if(!EmptyBoss(value.BossKey))throw new ArgumentException("Ordinary loot cannot carry any boss key fields.");
            if(state==SuckableState.Stored ? !Player(value.StoredOwner)||value.StoredOwner!=value.LastStorageOwner : value.StoredOwner!=0)
                throw new ArgumentException("Stored owner disagrees with cargo state.");
            if(value.LastStorageOwner==0){if(!string.IsNullOrEmpty(value.LastStorageTierId))throw new ArgumentException("Tier without a storage owner.");}
            else if(!Player(value.LastStorageOwner)||!CampaignCapacityRules.TryModel(value.LastStorageTierId,out _,out _))
                throw new ArgumentException("Invalid last storage attribution.");
            if(state==SuckableState.InFlight ? value.ActiveShotId==0||!Player(value.LastStorageOwner) : value.ActiveShotId!=0)
                throw new ArgumentException("Shot provenance must match in-flight state.");
        }
    }
}
