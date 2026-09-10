#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using HowToSuck.Networking;
using UnityEngine;
namespace HowToSuck.Diagnostics
{
    // Explicit diagnostic bootstrap. Checks real production collider replicas,
    // kitchen settling and support removal on independent Windows peers.
    public sealed class ColliderCoopProbe : MonoBehaviour
    {
        NgoGameSession game;int slot,count;string directory;
        readonly List<string> checks=new List<string>(),errors=new List<string>();
        static string Argument(string key){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i<0?null:a[i+1];}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Seed()
        {
            if(Argument("--hts-collider-probe")!="enabled"||Argument("--hts-gameplay-slot")!="0")return;
            string own=CampaignStoragePaths.ResolveOwnDirectory();
            if(!own.Contains(Argument("--hts-gameplay-nonce")))throw new Exception("Isolated profile required");
            var tiers=new CampaignTierCatalog(Enumerable.Range(0,4).Select(n=>new CampaignTier("mk"+(n+1),CampaignBalance.ModelPrices[n])));
            using(var repo=new SaveRepository(own,tiers,()=>true))
            {
                var opened=repo.Open();
                if(!opened.Ready||!repo.Commit(opened.State,new CampaignState(opened.State.CampaignId,"mk1",clearedContractIds:CampaignBalance.Contracts)).Success)
                    throw new Exception("Cannot seed isolated collider campaign");
            }
        }
        void Need(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
        static string PreviewText(ContractSelectionView view)
        {
            // Keep this diagnostic assembly independent of the TMP package.
            object text=typeof(ContractSelectionView).GetField("TimeValue").GetValue(view);
            return text==null?null:text.GetType().GetProperty("text").GetValue(text) as string;
        }
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message);}
        async Task Until(Func<bool> ready,string label,int seconds=75)
        {
            double end=Time.realtimeSinceStartupAsDouble+seconds;
            while(!ready()){if(errors.Count>0)throw new Exception(errors[0]);if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException(label);await Task.Delay(40);}
        }
        async Task Barrier(string label)
        {
            File.WriteAllText(Path.Combine(directory,label+"-"+slot),"ready");
            await Until(()=>Enumerable.Range(0,count).All(n=>File.Exists(Path.Combine(directory,label+"-"+n))),label);
        }
        async void Start()
        {
            DontDestroyOnLoad(gameObject);Application.logMessageReceived+=Log;string failure=null;
            try
            {
                if(Argument("--hts-collider-probe")!="enabled")throw new Exception("Explicit collider diagnostic required");
                slot=int.Parse(Argument("--hts-gameplay-slot"));count=int.Parse(Argument("--hts-gameplay-count"));
                directory=Path.Combine(Argument("--hts-gameplay-reports"),Argument("--hts-gameplay-nonce"));Directory.CreateDirectory(directory);
                foreach(string id in new[]{"old_house","supermarket","warehouse"})
                {
                    await Until(()=>{game=FindFirstObjectByType<NgoGameSession>();return game!=null&&game.Control!=null&&game.Control.IsSpawned&&game.Session.Phase==SessionPhase.Lobby&&game.Control.Roster.Count==count;},"Connected lobby");
                    if(game.HasAuthority)Need(game.Session.SelectLobbyContract(id),"Select "+id);
                    await Until(()=>game.Session.SelectedLobbyContractId==id&&game.Session.DisplayedContractUnlocked(id),"Selection replicates");
                    var selected=game.Session.SelectedLobbyContract;double duration=CampaignBalance.TimeLimitForCrew(id,count);
                    Need(game.Session.DisplayedTimeLimit(selected)==duration,"Preview "+id+" "+duration+" seconds");
                    await Until(()=>FindObjectsByType<ContractSelectionView>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                        .Any(v=>PreviewText(v)==$"{(int)duration/60}:{(int)duration%60:00}"),"Production lobby text matches duration");
                    game.SetLocalReady(true);await Until(()=>Enumerable.Range(0,count).All(n=>game.Control.Roster[n].Ready),"Ready roster");
                    if(game.HasAuthority){await Task.Delay(250);Need(game.Session.StartSelectedContract(),"Start "+id);}
                    await Until(()=>game.Session.Phase==SessionPhase.Playing,"Start real world",100);
                    var state=game.Session.ContractState;
                    Need(state.ContractId==id&&Math.Abs(state.Deadline-state.StartedAt-duration)<.001,"Server start and replica deadline agree: "+id);
                    Need(state.Quota==CampaignBalance.QuotaForCrew(selected.Quota,count),"Quota uses same crew: "+id);
                    var loot=FindObjectsByType<NetworkLootAdapter>(FindObjectsSortMode.None);
                    int expected=id=="old_house"?90:id=="supermarket"?207:158;
                    await Until(()=>{loot=FindObjectsByType<NetworkLootAdapter>(FindObjectsSortMode.None).Where(l=>l.IsSpawned).ToArray();return loot.Length==expected&&loot.All(l=>l.HasAcceptedCurrentSnapshot);},"All cargo replicas");
                    Need(loot.All(l=>l.Item.HasPhysicsAuthority==game.HasAuthority),"Correct collider authority: "+id);
                    string shapes=string.Join("|",loot.OrderBy(l=>l.Item.InstanceId).Select(l=>l.Item.TypeId+":"+l.Item.GameplayColliders.Length));
                    File.WriteAllText(Path.Combine(directory,id+"-shapes-"+slot),shapes);
                    await Barrier(id+"-started");
                    Need(Enumerable.Range(0,count).All(n=>File.ReadAllText(Path.Combine(directory,id+"-shapes-"+n))==shapes),"Every peer has identical compound colliders: "+id);
                    await Task.Delay(8000);
                    if(id=="old_house")
                    {
                        var sink=loot.Single(l=>l.Item.TypeId=="decor_oldhouse_fixture_box_kitchencounter").Item;
                        var microwave=loot.Single(l=>l.Item.TypeId=="microwave").Item;
                        Need(sink.IsMounted&&sink.GameplayColliders.Length==15,"Corrected kitchen stays mounted");
                        Need(Mathf.Abs(microwave.Body.position.y-.94f)<.025f,"Microwave rests on visible counter on every peer");
                        float y=microwave.Body.position.y;await Barrier(id+"-settled");
                        if(game.HasAuthority)Need(sink.TryTransition(SuckableState.Available,SuckableState.Lost),"Remove support on host");
                        await Until(()=>microwave.Body.position.y<y-.3f,"Unsupported microwave fall replicates");
                        Need(sink.State==SuckableState.Lost&&sink.GameplayColliders.All(c=>!c.enabled),"Removed support collision disabled on every peer");
                    }
                    await Barrier(id+"-physics");
                    if(game.HasAuthority)
                    {
                        double now=game.Driver.Now;
                        // Preserve the actual clock. Arrange only a shortened deadline
                        // through its normal penalty API and let the world time out.
                        Need(game.Session.Controller.ApplyDeadlinePenalty(game.Session.ContractState.Deadline-now-2,now),"Arrange two seconds remaining: "+id);
                    }
                    await Until(()=>game.Session.Phase==SessionPhase.Results,"Authoritative timeout replicates");
                    Need(game.Session.ContractState.Phase==ContractPhase.Failed&&game.Session.Result!=null&&game.Session.Result.Phase==ContractPhase.Failed,"Failed result on every peer: "+id);
                    Need(game.Session.Result.Payout==game.Session.Result.DeliveredValue*selected.FailurePercent/100,"Payout agrees with actual delivered cargo: "+id);
                    await Barrier(id+"-results");
                    if(game.HasAuthority){await Task.Delay(250);Need(game.Session.ReturnToLobby(),"Return after timeout: "+id);}
                    await Until(()=>game.Session.Phase==SessionPhase.Lobby,"Peers return to lobby");
                    await Barrier(id+"-lobby");
                }
                Need(errors.Count==0,"No runtime errors");
            }
            catch(Exception e){failure=e.ToString();}
            finally{Application.logMessageReceived-=Log;}
            if(directory!=null)File.WriteAllText(Path.Combine(directory,"result-"+slot+".json"),JsonUtility.ToJson(new Receipt{passed=failure==null,slot=slot,count=count,error=failure,checks=checks.ToArray(),runtimeErrors=errors.ToArray()},true));
            await Task.Delay(1000);Application.Quit(failure==null?0:2);
        }
        [Serializable]sealed class Receipt{public bool passed;public int slot,count;public string error;public string[] checks,runtimeErrors;}
    }
}
#endif
