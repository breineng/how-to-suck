using System;
namespace HowToSuck
{
    // One process/account/AppID fence is retained across adapter detach. Steam's callback has no request ID.
    // An accepted but unacknowledged operation cannot be safely attributed to a later batch in this process.
    public sealed class AchievementStoreFence
    {
        public string Request {get;private set;}
        public bool Quarantined {get;private set;}
        public string Reason {get;private set;}
        private object owner;
        public bool Acquire(object next){if(next==null||owner!=null)return false;owner=next;return true;}
        public bool Begin(object caller,string request)
        {if(owner!=caller||Quarantined||Request!=null||!AchievementDefinitions.ValidRun(request))return false;Request=request;return true;}
        public bool IsCurrent(object caller,string request)=>owner==caller&&!Quarantined&&Request!=null&&Request==request;
        public void RejectedBeforeQueue(object caller,string request){if(IsCurrent(caller,request))Request=null;}
        public bool Settle(object caller,string request){if(!IsCurrent(caller,request))return false;Request=null;return true;}
        public void Quarantine(object caller,string reason){if(owner!=caller)return;Quarantined=true;Reason=reason;Request=null;}
        public void Release(object caller){if(owner!=caller)return;if(Request!=null)Quarantine(caller,"StoreStats owner detached before acknowledgement; restart to retry safely.");owner=null;}
    }
}
