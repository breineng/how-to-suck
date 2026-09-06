using System;using UnityEngine;
namespace HowToSuck.Audio
{
 public enum CommittedAudioKind:byte { Ingestion=1,ShotLaunch=2,EnemyHit=3,EnemyDefeat=4,SuitHit=5,SuitRecovered=6 }
 // Immutable copied presentation facts. No GameObject, Rigidbody or authority mutation handle reaches subscribers.
 public readonly struct CommittedAudioFact:IEquatable<CommittedAudioFact>
 {
  public readonly string Run;public readonly CommittedAudioKind Kind;public readonly ulong Occurrence,Item,Enemy;
  public readonly int Owner,Intake,Amount;public readonly bool Truck;public readonly double At;public readonly float Size,Duration;public readonly Vector3 Position;
  public CommittedAudioFact(string run,CommittedAudioKind kind,ulong occurrence,ulong item,ulong enemy,int owner,int intake,bool truck,double at,float size,int amount,Vector3 position,float duration=0)
  {Run=run;Kind=kind;Occurrence=occurrence;Item=item;Enemy=enemy;Owner=owner;Intake=intake;Truck=truck;At=at;Size=size;Amount=amount;Position=position;Duration=duration;}
  public bool IsValid {
   get {
    if(!Guid.TryParseExact(Run,"N",out _)||Occurrence==0||!Finite(At)||At<0||!Finite(Size)||!Finite(Duration)||!Finite(Position.x)||!Finite(Position.y)||!Finite(Position.z))return false;
    switch(Kind){
     case CommittedAudioKind.Ingestion:return Item!=0&&Enemy==0&&Size>0&&Duration>0&&Amount==0&&(Truck?Owner==0&&Intake==1000:Player(Owner)&&Intake==Owner);
     case CommittedAudioKind.ShotLaunch:return Item!=0&&Enemy==0&&Player(Owner)&&Intake==0&&!Truck&&Size==0&&Duration==0&&Amount>=1&&Amount<=100;
     case CommittedAudioKind.EnemyHit:return Item!=0&&Enemy!=0&&Player(Owner)&&Intake==0&&!Truck&&Size==0&&Duration==0&&Amount>=1&&Amount<=100;
     case CommittedAudioKind.EnemyDefeat:return Item!=0&&Enemy!=0&&Player(Owner)&&Intake==0&&!Truck&&Size==0&&Duration==0&&Amount==0;
     case CommittedAudioKind.SuitHit:return Item==0&&Enemy!=0&&Player(Owner)&&Intake==0&&!Truck&&Size==0&&Duration==0&&Amount>=0&&Amount<=2;
     case CommittedAudioKind.SuitRecovered:return Item==0&&Enemy==0&&Player(Owner)&&Intake==0&&!Truck&&Size==0&&Duration==0&&Amount==3;
     default:return false;
    }
   }
  }
  public bool CanPlayForLocal(int localOwner)=>IsValid&&Player(localOwner)&&(!IsPersonal||Owner==localOwner);
  public bool IsPersonal=>Kind==CommittedAudioKind.SuitHit||Kind==CommittedAudioKind.SuitRecovered;
  public static bool Player(int id)=>id>=1&&id<=4;
  private static bool Finite(double x)=>!double.IsNaN(x)&&!double.IsInfinity(x);
  public bool Equals(CommittedAudioFact b)=>Run==b.Run&&Kind==b.Kind&&Occurrence==b.Occurrence&&Item==b.Item&&Enemy==b.Enemy&&Owner==b.Owner&&Intake==b.Intake&&Amount==b.Amount&&Truck==b.Truck&&At.Equals(b.At)&&Size.Equals(b.Size)&&Duration.Equals(b.Duration)&&Position.x.Equals(b.Position.x)&&Position.y.Equals(b.Position.y)&&Position.z.Equals(b.Position.z);
  public override bool Equals(object b)=>b is CommittedAudioFact f&&Equals(f);
  public override int GetHashCode()=>unchecked(((int)Kind*397^Occurrence.GetHashCode())*397^Owner);
 }
 public readonly struct CommittedAudioReceipt
 {public readonly ulong Sequence;public readonly CommittedAudioFact Fact;public CommittedAudioReceipt(ulong sequence,CommittedAudioFact fact){Sequence=sequence;Fact=fact;}}
 public readonly struct CommittedAudioSourceKey:IEquatable<CommittedAudioSourceKey>
 {
  private readonly CommittedAudioKind kind;private readonly ulong occurrence;private readonly int owner;
  // Admission and shot counters are run-global; suit counters are per owner. Receiver/item are payload, never a way to evade occurrence dedupe.
  public CommittedAudioSourceKey(CommittedAudioFact f){kind=f.Kind;occurrence=f.Occurrence;owner=f.IsPersonal?f.Owner:0;}
  public bool Equals(CommittedAudioSourceKey b)=>kind==b.kind&&occurrence==b.occurrence&&owner==b.owner;
  public override bool Equals(object b)=>b is CommittedAudioSourceKey k&&Equals(k);
  public override int GetHashCode()=>unchecked(((int)kind*397^occurrence.GetHashCode())*397^owner);
 }
 public static class CommittedAudioEvents
 {
  public static event Action<CommittedAudioFact> Emitted;
  public static int SubscriberFailures{get;private set;}
  // Only checked domain commits call this; exceptions from cosmetic consumers never unwind authority transitions.
  public static void Publish(CommittedAudioFact fact)
  {if(!fact.IsValid)return;var handlers=Emitted;if(handlers==null)return;foreach(Action<CommittedAudioFact> handler in handlers.GetInvocationList())try{handler(fact);}catch(Exception){if(SubscriberFailures<int.MaxValue)SubscriberFailures++;}}
 }
}
