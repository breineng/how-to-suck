using System;
using System.Collections.Generic;
namespace HowToSuck
{
    [Flags] public enum AchievementBits : byte
    { None=0,FirstSwallow=1,OldHouse=2,Supermarket=4,Warehouse=8,FullCrew=16,Mk4Run=32,PianoSwallow=64,DoubleQuota=128 }
    public static class AchievementDefinitions
    {
        private static readonly string[] ids={"first_swallow","old_house","supermarket","warehouse","full_crew","mk4_run","piano_swallow","double_quota"};
        private static readonly string[] apis={"ACH_FIRST_SWALLOW","ACH_OLD_HOUSE_CLEAR","ACH_SUPERMARKET_CLEAR","ACH_WAREHOUSE_CLEAR","ACH_FULL_CREW","ACH_MK4_CREW","ACH_PIANO_SNACK","ACH_GREED_PAID"};
        public const int Count=8;
        public static string Id(int index)=>ids[index];
        public static string Api(int index)=>apis[index];
        public static int IndexOfId(string id)=>Array.IndexOf(ids,id);
        public static int IndexOfApi(string api)=>Array.IndexOf(apis,api);
        public static byte MapAward(string id)=>id=="old_house"||id=="old_house_ii"?(byte)2:id=="supermarket"||id=="supermarket_ii"?(byte)4:id=="warehouse"||id=="warehouse_ii"?(byte)8:(byte)0;
        public static bool KnownContract(string id)=>MapAward(id)!=0;
        public static bool KnownTier(string id)=>id=="mk1"||id=="mk2"||id=="mk3"||id=="mk4";
        public static bool ValidRun(string run)=>Guid.TryParseExact(run,"N",out var g)&&g!=Guid.Empty&&g.ToString("N")==run;
        public static IEnumerable<string> Ids(byte mask){for(int i=0;i<Count;i++)if((mask&(1<<i))!=0)yield return ids[i];}
    }
    public readonly struct AchievementFact
    {
        public readonly string RunId,EventId;
        public readonly byte Recipients,Achievements;
        public AchievementFact(string run,string eventId,byte recipients,byte achievements){RunId=run;EventId=eventId;Recipients=recipients;Achievements=achievements;}
        public bool IsValid=>AchievementDefinitions.ValidRun(RunId)&&ValidEvent(EventId)&&Recipients>0&&(Recipients&0xf0)==0&&Achievements!=0;
        private static bool ValidEvent(string id)
        {
            if(id=="finish")return true;
            return id!=null&&id.StartsWith("delivery:",StringComparison.Ordinal)&&ulong.TryParse(id.Substring(9),out var n)&&n>0&&id=="delivery:"+n;
        }
    }
    // Only the authority bridge supplies ledger-accepted truck deliveries and its controller's exact FinalResult.
    public sealed class AchievementAuthorityEvaluator
    {
        private string run,contract,startTier;
        private BossKey boss;
        private byte startedRoster,continuousRoster;
        private bool terminal=true,mk4Continuous;
        private readonly HashSet<ulong> delivered=new HashSet<ulong>();
        private readonly HashSet<string> usedRuns=new HashSet<string>(StringComparer.Ordinal);
        public string RunId=>run;
        public void Begin(string runId,string contractId,string tier,BossKey currentBoss,string requiredBossId,IReadOnlyCollection<int> spawnedPlayers)
        {
            if(!AchievementDefinitions.ValidRun(runId)||!AchievementDefinitions.KnownContract(contractId)||!AchievementDefinitions.KnownTier(tier)||
                !currentBoss.IsValid||currentBoss.RunId!=runId||currentBoss.ContractBossId!=requiredBossId)
                throw new ArgumentException("Actual running contract, assigned boss and prepared campaign tier required.");
            byte roster=Roster(spawnedPlayers);
            if(roster==0||!terminal||usedRuns.Contains(runId))throw new InvalidOperationException("A fresh nonempty run may start only after the preceding evaluator run ended.");
            usedRuns.Add(runId);run=runId;contract=contractId;startTier=tier;boss=currentBoss;startedRoster=continuousRoster=roster;
            terminal=false;mk4Continuous=tier=="mk4";delivered.Clear();
        }
        public void ObserveRoster(IReadOnlyCollection<int> actualPlayers){if(!terminal)continuousRoster&=Roster(actualPlayers);}
        public void PlayerRemoved(int id){if(!terminal&&id>=1&&id<=4)continuousRoster&=(byte)~(1<<(id-1));}
        public void ObservePreparedTier(string actualTier){if(!terminal&&actualTier!="mk4")mk4Continuous=false;}
        public AchievementFact? AcceptCommittedDelivery(DeliveryRecord record,IReadOnlyCollection<int> actualPlayers)
        {
            if(terminal||run==null||record.RunId!=run||record.InstanceId==0||!record.IsTruck||record.PlayerId!=0||record.IntakeId<=0||
                !ValidDeliveredCargo(record)||string.IsNullOrWhiteSpace(record.TypeId)||!AchievementDefinitions.KnownTier(record.LastStorageTierId))return null;
            ObserveRoster(actualPlayers);
            if(!delivered.Add(record.InstanceId)||record.LastStorageOwner<1||record.LastStorageOwner>4||
                (continuousRoster&(1<<(record.LastStorageOwner-1)))==0)return null;
            byte mask=(byte)AchievementBits.FirstSwallow;
            if(record.CargoRole==CargoRole.OrdinaryLoot&&record.TypeId=="piano"&&record.LastStorageTierId=="mk4")mask|=(byte)AchievementBits.PianoSwallow;
            return new AchievementFact(run,"delivery:"+record.InstanceId,(byte)(1<<(record.LastStorageOwner-1)),mask);
        }
        private bool ValidDeliveredCargo(DeliveryRecord record)
        {
            if(record.CargoRole==CargoRole.BossBody)return record.Value==0&&record.BossKey.Equals(boss);
            return record.CargoRole==CargoRole.OrdinaryLoot&&record.Value>0&&string.IsNullOrEmpty(record.BossKey.RunId)&&
                string.IsNullOrEmpty(record.BossKey.ContractBossId)&&record.BossKey.InstanceId==0;
        }
        public AchievementFact? Finish(ContractResult result,string actualFinishTier,IReadOnlyCollection<int> actualPlayers)
        {
            if(terminal||result==null||result.RunId!=run||result.ContractId!=contract)return null;
            if(result.Phase!=ContractPhase.Succeeded&&result.Phase!=ContractPhase.Failed&&result.Phase!=ContractPhase.Aborted)return null;
            ObserveRoster(actualPlayers);ObservePreparedTier(actualFinishTier);terminal=true;
            if(result.Phase!=ContractPhase.Succeeded||result.Quota<=0||result.DeliveredValue<result.Quota||!result.Boss.IsDelivered||!result.Boss.Key.Equals(boss))return null;
            byte recipients=(byte)(continuousRoster&startedRoster);if(recipients==0)return null;
            byte mask=AchievementDefinitions.MapAward(contract);
            if(startedRoster==15&&recipients==15)mask|=(byte)AchievementBits.FullCrew;
            if(startTier=="mk4"&&mk4Continuous&&actualFinishTier=="mk4")mask|=(byte)AchievementBits.Mk4Run;
            if(result.DeliveredValue-result.Quota>=result.Quota)mask|=(byte)AchievementBits.DoubleQuota;
            return new AchievementFact(run,"finish",recipients,mask);
        }
        public void Cancel(){run=null;contract=null;startTier=null;boss=default;startedRoster=continuousRoster=0;terminal=true;mk4Continuous=false;delivered.Clear();}
        private static byte Roster(IReadOnlyCollection<int> players)
        {
            if(players==null||players.Count>4)throw new ArgumentException("At most four actual players.");byte mask=0;
            foreach(int id in players){if(id<1||id>4||(mask&(1<<(id-1)))!=0)throw new ArgumentException("Unique server-assigned PlayerId1..4 required.");mask|=(byte)(1<<(id-1));}return mask;
        }
    }
}
