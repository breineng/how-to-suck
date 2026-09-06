using System;
using System.Collections.Generic;
namespace HowToSuck
{
    public static class ContractSelectionPolicy
    {
        public static bool TrySelect(bool initialized,bool authority,SessionPhase phase,IReadOnlyList<string> catalog,string requested,out string selected)
        {
            selected=null;
            if(!initialized||!authority||phase!=SessionPhase.Lobby||catalog==null||string.IsNullOrWhiteSpace(requested))return false;
            for(int i=0;i<catalog.Count;i++)if(string.Equals(catalog[i],requested,StringComparison.Ordinal)){selected=requested;return true;}
            return false;
        }
    }
}
