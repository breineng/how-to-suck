using System;
using System.Collections.Generic;
using System.Linq;

namespace HowToSuck
{
    // A confirmed immutable snapshot. No public/internal balance mutation remains.
    public sealed class CampaignState
    {
        public string CampaignId { get; }
        public string CurrentTierId { get; }
        public long Balance { get; }
        public string LastSettledRunId { get; }
        public int PurchasedExtraSlots { get; }
        public IReadOnlyList<string> ClearedContractIds { get; }
        public bool LegacyContractAccess { get; }
        public CampaignState(string campaignId, string currentTierId="mk1", long balance=0, string lastSettledRunId=null,
            int purchasedExtraSlots=0,IEnumerable<string> clearedContractIds=null,bool legacyContractAccess=false)
        {
            SaveIdentity.RequireGuid(campaignId,nameof(campaignId));
            SaveIdentity.RequireTier(currentTierId);
            if(balance<0)throw new ArgumentOutOfRangeException(nameof(balance));
            if(lastSettledRunId!=null)SaveIdentity.RequireGuid(lastSettledRunId,nameof(lastSettledRunId));
            if(!CampaignCapacityRules.ValidBonus(purchasedExtraSlots))throw new ArgumentOutOfRangeException(nameof(purchasedExtraSlots));
            var history=(clearedContractIds??Array.Empty<string>()).ToArray();
            if(history.Length>64||history.Count(id=>!CampaignContractAccess.IsKnown(id))>58||history.Distinct(StringComparer.Ordinal).Count()!=history.Length)throw new ArgumentException("Bounded unique cleared contract IDs required.");
            foreach(string id in history)SaveIdentity.RequireTier(id); // Same canonical stable-ID syntax; preserve future valid IDs without granting route access.
            Array.Sort(history,StringComparer.Ordinal);
            PurchasedExtraSlots=purchasedExtraSlots;ClearedContractIds=Array.AsReadOnly(history);LegacyContractAccess=legacyContractAccess;
            CampaignId=campaignId;CurrentTierId=currentTierId;Balance=balance;LastSettledRunId=lastSettledRunId;
        }
        public bool SameValues(CampaignState other)=>other!=null&&CampaignId==other.CampaignId&&
            CurrentTierId==other.CurrentTierId&&Balance==other.Balance&&LastSettledRunId==other.LastSettledRunId&&
            PurchasedExtraSlots==other.PurchasedExtraSlots&&LegacyContractAccess==other.LegacyContractAccess&&ClearedContractIds.SequenceEqual(other.ClearedContractIds);
    }
    public static class SaveIdentity
    {
        public static void RequireGuid(string value,string name)
        {
            if(value==null||!Guid.TryParseExact(value,"N",out var id)||id==Guid.Empty||id.ToString("N")!=value)
                throw new ArgumentException("A canonical non-empty lower-case GUID in N format is required.",name);
        }
        public static void RequireTier(string value)
        {
            if(value==null||value.Length<1||value.Length>32||value[0]<'a'||value[0]>'z')
                throw new ArgumentException("Invalid stable tier ID.");
            foreach(char c in value)if(!(c>='a'&&c<='z'||c>='0'&&c<='9'||c=='_'||c=='-'))
                throw new ArgumentException("Invalid stable tier ID.");
        }
    }
    public sealed class CampaignTier
    {
        public string Id {get;}
        public long Price {get;}
        public CampaignTier(string id,long price)
        {SaveIdentity.RequireTier(id);if(price<0)throw new ArgumentOutOfRangeException(nameof(price));Id=id;Price=price;}
    }
    // One immutable copy from authored GameCatalog; prices never come from a save or purchase request.
    public sealed class CampaignTierCatalog
    {
        private readonly CampaignTier[] tiers;
        public CampaignTierCatalog(IEnumerable<CampaignTier> source)
        {
            tiers=source?.ToArray()??throw new ArgumentNullException(nameof(source));
            if(tiers.Length==0||tiers[0]==null||tiers[0].Id!="mk1"||tiers[0].Price!=0||
                tiers.Any(x=>x==null)||tiers.Select(x=>x.Id).Distinct(StringComparer.Ordinal).Count()!=tiers.Length||
                tiers.Skip(1).Any(x=>x.Price<=0))
                throw new ArgumentException("Ordered unique catalog must start with free mk1 and have positive next-tier prices.");
        }
        public bool Contains(string id)=>Array.Exists(tiers,x=>x.Id==id);
        public CampaignTier Next(string id)
        {int index=Array.FindIndex(tiers,x=>x.Id==id);return index<0||index+1>=tiers.Length?null:tiers[index+1];}
    }
}