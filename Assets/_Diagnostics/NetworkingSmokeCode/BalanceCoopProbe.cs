#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using HowToSuck.Networking;
using UnityEngine;
namespace HowToSuck.Diagnostics
{
 // Explicit diagnostic bootstrap only. Isolated profiles; damage is arranged by
 // the fixture, while recovery uses actual owner RPCs and replicated suit state.
 public sealed class BalanceCoopProbe:MonoBehaviour
 {
  const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
  NgoGameSession game;int slot,count;string directory;uint sequence=100000;bool inject,hold;
  readonly List<string> checks=new List<string>(),errors=new List<string>();
  static void Set(object o,string name,object value)=>o.GetType().GetField(name,Flags).SetValue(o,value);
  static object Call(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Flags).Invoke(o,args);
  void Need(bool ok,string why){if(!ok)throw new Exception(why);checks.Add(why);}
  void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
  async Task Until(Func<bool> ready,string label,int seconds=70){double end=Time.realtimeSinceStartupAsDouble+seconds;while(!ready()){if(errors.Count>0)throw new Exception(errors[0]);if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException(label);await Task.Delay(40);}}
  NetworkPlayerAdapter[] Players()=>FindObjectsByType<NetworkPlayerAdapter>(FindObjectsSortMode.None).Where(p=>p.IsSpawned).ToArray();
  bool Down(NetworkPlayerAdapter p)=>p.Snapshot.Value.SuitRecoveryPending;
  void Update(){if(!inject||game==null||game.Session.Phase!=SessionPhase.Playing)return;var own=Players().FirstOrDefault(p=>p.IsOwner);if(own==null)return;own.Input.enabled=false;own.SubmitIntent(own.Motor.PlayerId,new PlayerIntent{RunId=game.Session.RunId,Sequence=++sequence,InteractHeld=hold&&Down(own)});}
  async Task Damage(EnemyActor enemy,PlayerMotor player){
   var w=game.Session.World;
   for(int i=0;i<12&&!player.IsDowned;i++){
    double at=game.Driver.Now;Set(w,"inStep",true);Set(w,"stepNow",at);w.Combat.BeginStep(at);
    try{Call(w.Combat,"TryDamagePlayer",enemy,player,at,false);}finally{w.Combat.EndStep();Set(w,"inStep",false);}
    await Task.Delay(720);
   }
   Need(player.IsDowned,"Authority damage downs player "+player.PlayerId);
  }
  async void Start(){
   DontDestroyOnLoad(gameObject);Application.logMessageReceived+=Log;string failure=null;
   try{
    var args=Environment.GetCommandLineArgs();string Read(string key)=>args[Array.IndexOf(args,key)+1];
    slot=int.Parse(Read("--hts-gameplay-slot"));count=int.Parse(Read("--hts-gameplay-count"));directory=Path.Combine(Read("--hts-gameplay-reports"),Read("--hts-gameplay-nonce"));Directory.CreateDirectory(directory);
    await Until(()=>{game=FindFirstObjectByType<NgoGameSession>();return game!=null&&game.Control!=null&&game.Control.IsSpawned&&game.Session.Phase==SessionPhase.Lobby&&game.Control.Roster.Count==count;},"Lobby crew");
    await Until(()=>game.Session.DisplayedCrewSize==count,"Crew presentation");
    var contract=game.Session.SelectedLobbyContract;long quota=CampaignBalance.QuotaForCrew(contract.Quota,count);
    Need(game.Session.DisplayedQuota(contract)==quota,"Lobby quota matches connected crew "+count);
    game.SetLocalReady(true);await Until(()=>Enumerable.Range(0,game.Control.Roster.Count).All(n=>game.Control.Roster[n].Ready),"Ready roster");
    if(game.HasAuthority){await Task.Delay(500);Need(game.Session.StartSelectedContract(),"Host starts contract");}
    await Until(()=>game.Session.Phase==SessionPhase.Playing&&Players().Length==count&&Players().All(p=>p.HasAcceptedCurrentSnapshot),"Prepared players",100);inject=true;
    Need(game.Session.ContractState.Quota==quota,"Actual run quota agrees on peer "+slot);
    var level=FindFirstObjectByType<LevelContext>();var loot=FindObjectsByType<NetworkLootAdapter>(FindObjectsSortMode.None);
    await Until(()=>loot.Length==level.LootSpawns.Length&&loot.All(l=>l.HasAcceptedCurrentSnapshot),"All dynamic loot replicas");
    var decor=loot.Where(l=>l.Item.TypeId.StartsWith("decor_")).ToArray();Need(decor.Length==22,"All 22 house furniture objects spawned over NGO");
    Need(decor.All(l=>l.Item.HasPhysicsAuthority==game.HasAuthority&&l.Item.Body.isKinematic==(!game.HasAuthority||l.Item.IsMounted)),"Host simulates loose furniture; mountings and guest replicas remain kinematic");
    File.WriteAllText(Path.Combine(directory,"ready-"+slot),"ready");
    await Until(()=>Enumerable.Range(0,count).All(n=>File.Exists(Path.Combine(directory,"ready-"+n))),"Every peer inspected cargo");
    var mounted=decor.Single(l=>l.Item.TypeId.Contains("wallshelf"));var mountPosition=mounted.Item.Body.position;
    Need(mounted.Item.Body.isKinematic,"Wall shelf remains attached before release on peer "+slot);
    File.WriteAllText(Path.Combine(directory,"mount-ready-"+slot),"ready");
    await Until(()=>Enumerable.Range(0,count).All(n=>File.Exists(Path.Combine(directory,"mount-ready-"+n))),"Mount pose observed by every peer");
    if(game.HasAuthority){Call(mounted.Item,"AcceptSuction",true);mounted.Item.Body.AddForce(Vector3.down*.5f,ForceMode.VelocityChange);}
    await Until(()=>Vector3.Distance(mounted.Item.Body.position,mountPosition)>.12f,"Released mounting pose replicates",15);
    Need(mounted.Item.Body.isKinematic!=game.HasAuthority,"Released shelf is dynamic only on the host");
    File.WriteAllText(Path.Combine(directory,"mount-released-"+slot),"released");
    await Until(()=>Enumerable.Range(0,count).All(n=>File.Exists(Path.Combine(directory,"mount-released-"+n))),"Mount release observed by every peer");
    int reviveSlot=count>1?1:0;
    if(game.HasAuthority){
     // Wake an authored ordinary spawn early to arrange damage without waiting for the director.
     var combat=game.Session.World.Combat;var point=level.EnemyEncounters.First(e=>e.ContractId==contract.ContractId).Spawns.First(p=>!p.Prefab.GetComponent<EnemyActor>().Definition.IsBoss);
     bool spawned=(bool)Call(combat,"TrySpawn",point,point.transform.position,(ulong)10000,false,false);Need(spawned,"Authored enemy spawned for damage fixture");
     foreach(var a in combat.Actors.Values)a.SetFrozen(true);var enemy=combat.Actors[10000];
     Need(enemy.MaximumHealth==enemy.Definition.HealthForCrew(count),"Ordinary enemy health scales for crew");
     var fallen=game.Session.World.Players[reviveSlot+1];await Damage(enemy,fallen);
     File.WriteAllText(Path.Combine(directory,"one-down"),"down");
     await Task.Delay(4800);if(count>1)Need(fallen.IsDowned,"Owner RPC cannot self-revive while teammates live");
     File.WriteAllText(Path.Combine(directory,"release"),"release");await Task.Delay(700);
     foreach(var p in game.Session.World.Players.Values)if(!p.IsDowned)await Damage(enemy,p);
     File.WriteAllText(Path.Combine(directory,"all-down"),"down");
    }
    else await Until(()=>File.Exists(Path.Combine(directory,"one-down")),"First down arrangement");
    if(slot==reviveSlot&&count>1){hold=true;await Until(()=>File.Exists(Path.Combine(directory,"release")),"Release pre-wipe input");hold=false;}
    await Until(()=>Players().All(Down)&&File.Exists(Path.Combine(directory,"all-down")),"Full crew down replicated");
    double deadline=game.Session.ContractState.Deadline;checks.Add("Every peer observes all connected suits down");
    await Task.Delay(700);if(slot==reviveSlot)hold=true;
    await Until(()=>Players().Count(p=>!Down(p))==1,"Guest owner RPC self-rescue",20);hold=false;
    await Until(()=>game.Session.ContractState.Deadline==deadline-15,"Replicated rescue penalty");
    Need(Players().Single(p=>!Down(p)).Snapshot.Value.SuitSegments==CampaignBalance.ReviveHealth,"Recovered suit has 50 health on peer "+slot);
    Need(game.Session.ContractState.Quota==quota,"Wipe retains the original crew quota");
    File.WriteAllText(Path.Combine(directory,"recovered-"+slot),"recovered");
    await Until(()=>Enumerable.Range(0,count).All(n=>File.Exists(Path.Combine(directory,"recovered-"+n))),"All peers observed recovery");
    if(game.HasAuthority){await Task.Delay(500);game.Session.Controller.Abort(game.Driver.Now);}
    await Until(()=>game.Session.Phase==SessionPhase.Results,"Results replication");Need(errors.Count==0,"No runtime errors");
   }catch(Exception e){failure=e.ToString();}
   finally{inject=false;hold=false;Application.logMessageReceived-=Log;}
   if(directory!=null)File.WriteAllText(Path.Combine(directory,"result-"+slot+".json"),JsonUtility.ToJson(new Receipt{passed=failure==null,slot=slot,count=count,error=failure,checks=checks.ToArray(),runtimeErrors=errors.ToArray()},true));
   await Task.Delay(1000);Application.Quit(failure==null?0:2);
  }
  [Serializable]sealed class Receipt{public bool passed;public int slot,count;public string error;public string[] checks,runtimeErrors;}
 }
}
#endif
