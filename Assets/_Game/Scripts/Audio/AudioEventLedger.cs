using System;
using System.Collections.Generic;
namespace HowToSuck.Audio
{
    // Run identity is established by SessionRoot, never adopted from an incoming cue.
    public sealed class AudioEventLedger
    {
        private readonly Dictionary<CommittedAudioSourceKey,CommittedAudioFact> committed=new Dictionary<CommittedAudioSourceKey,CommittedAudioFact>();
        private ulong issuedCommitted,receivedCommitted;
        public int ConflictingFacts {get;private set;}
        private readonly HashSet<string> purchases=new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<Pair,double> pairs=new Dictionary<Pair,double>();
        private readonly List<Pair> expired=new List<Pair>();
        private ulong newestImpact,impactBits,signals;
        public string Run {get;private set;}="";
        public int CapacityDrops {get;private set;}
        public bool SetRun(string run)
        {
            run=run??"";if(run==Run)return false;
            Run=run;committed.Clear();issuedCommitted=receivedCommitted=0;pairs.Clear();expired.Clear();newestImpact=impactBits=signals=0;return true;
        }
        public bool Matches(string run)=>!string.IsNullOrEmpty(Run)&&string.Equals(run,Run,StringComparison.Ordinal);
        public bool TryIssueCommitted(CommittedAudioFact fact,bool localAuthority,out CommittedAudioReceipt receipt)
        {
            receipt=default;if(!localAuthority||issuedCommitted==ulong.MaxValue||!StoreCommitted(fact))return false;
            receipt=new CommittedAudioReceipt(++issuedCommitted,fact);return true;
        }
        public bool TryReceiveCommitted(CommittedAudioReceipt receipt,bool fromCurrentServer,bool localAuthority)
        {
            // Reliable ordered channel, with independent source occurrence protection against a repackaged duplicate.
            if(!fromCurrentServer||localAuthority||receipt.Sequence==0||receipt.Sequence<=receivedCommitted||!StoreCommitted(receipt.Fact))return false;
            receivedCommitted=receipt.Sequence;return true;
        }
        private bool StoreCommitted(CommittedAudioFact fact)
        {
            if(!fact.IsValid||!Matches(fact.Run))return false;var key=new CommittedAudioSourceKey(fact);
            if(committed.TryGetValue(key,out var old)){if(!old.Equals(fact))ConflictingFacts++;return false;}
            if(committed.Count>=8192){CapacityDrops++;return false;}committed.Add(key,fact);return true;
        }
        public bool Signal(string run,SfxId id)
        {
            if(!Matches(run)||(id!=SfxId.QuotaReady&&id!=SfxId.ExtractSuccess&&id!=SfxId.ContractFail))return false;
            ulong terminal=(1UL<<(int)SfxId.ExtractSuccess)|(1UL<<(int)SfxId.ContractFail);
            if((signals&terminal)!=0)return false;
            ulong bit=1UL<<(int)id;if((signals&bit)!=0)return false;signals|=bit;return true;
        }
        // Preserved across run changes: purchase confirmations are local transaction IDs, not run IDs.
        public bool Purchase(string transaction)
        {
            if(string.IsNullOrWhiteSpace(transaction)||transaction.Length>128||purchases.Contains(transaction))return false;
            if(purchases.Count>=512){CapacityDrops++;return false;}return purchases.Add(transaction);
        }
        public bool ImpactSequence(string run,ulong sequence)
        {
            if(!Matches(run)||sequence==0)return false;
            if(sequence>newestImpact)
            {ulong shift=sequence-newestImpact;impactBits=shift>=64?1:(impactBits<<(int)shift)|1;newestImpact=sequence;return true;}
            ulong behind=newestImpact-sequence;if(behind>=64)return false;ulong bit=1UL<<(int)behind;
            if((impactBits&bit)!=0)return false;impactBits|=bit;return true;
        }
        public bool CollisionPair(string run,ulong item,ulong other,double now)
        {
            if(!Matches(run)||item==0||other==0||double.IsNaN(now)||double.IsInfinity(now))return false;
            var pair=new Pair(item,other);
            if(pairs.TryGetValue(pair,out var until)&&now<until)return false;
            if(pairs.Count>=512)
            {expired.Clear();foreach(var entry in pairs)if(entry.Value<=now)expired.Add(entry.Key);foreach(var key in expired)pairs.Remove(key);}
            if(!pairs.ContainsKey(pair)&&pairs.Count>=512){CapacityDrops++;return false;}
            pairs[pair]=now+.18;return true;
        }
        private readonly struct Pair:IEquatable<Pair>
        {
            private readonly ulong a,b;
            public Pair(ulong x,ulong y){a=Math.Min(x,y);b=Math.Max(x,y);}
            public bool Equals(Pair other)=>a==other.a&&b==other.b;
            public override bool Equals(object other)=>other is Pair pair&&Equals(pair);
            public override int GetHashCode()=>unchecked(a.GetHashCode()*397^b.GetHashCode());
        }
    }
}
