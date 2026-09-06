using System;
using System.Collections.Generic;
using System.Linq;
namespace HowToSuck
{
    public static class CampaignContractAccess
    {
        private static readonly string[] Route={"old_house","supermarket","old_house_ii","warehouse","supermarket_ii","warehouse_ii"};
        public static IReadOnlyList<string> OrderedIds {get;}=Array.AsReadOnly(Route);
        public static bool IsKnown(string id)=>Array.IndexOf(Route,id)>=0;
        public static bool IsIntro(string id)=>id=="old_house"||id=="supermarket"||id=="warehouse";
        public static string Prerequisite(string id){int index=Array.IndexOf(Route,id);return index>0?Route[index-1]:null;}
        public static bool IsUnlocked(string id,IEnumerable<string> cleared,bool legacyContractAccess)
        {
            int index=Array.IndexOf(Route,id);if(index<0)return false;
            var history=new HashSet<string>(cleared??Array.Empty<string>(),StringComparer.Ordinal);
            return index==0||history.Contains(id)||(legacyContractAccess&&IsIntro(id))||history.Contains(Route[index-1]);
        }
        public static bool IsUnlocked(CampaignState state,string id)=>state!=null&&IsUnlocked(id,state.ClearedContractIds,state.LegacyContractAccess);
        public static string[] WithSucceeded(CampaignState state,string id)
        {
            if(state==null)throw new ArgumentNullException(nameof(state));
            // Engineering contracts outside the authored campaign can settle money but do not invent chapter progress.
            if(!IsKnown(id)||state.ClearedContractIds.Contains(id))return state.ClearedContractIds.ToArray();
            return state.ClearedContractIds.Concat(new[]{id}).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        }
        public static byte ClearedMask(IEnumerable<string> ids)
        {byte mask=0;if(ids!=null)foreach(string id in ids){int index=Array.IndexOf(Route,id);if(index>=0)mask|=(byte)(1<<index);}return mask;}
        public static string[] FromMask(byte mask)
        {if((mask&~63)!=0)throw new ArgumentOutOfRangeException(nameof(mask));return Route.Where((id,index)=>(mask&(1<<index))!=0).ToArray();}
    }
}
