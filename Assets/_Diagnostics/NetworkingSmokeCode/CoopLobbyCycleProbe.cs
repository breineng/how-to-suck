#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;using System.IO;using System.Collections.Generic;using System.Threading.Tasks;
using HowToSuck.Networking;using UnityEngine;
namespace HowToSuck.Diagnostics
{
    // Explicit standalone diagnostic scene only. Uses isolated campaign arguments,
    // real NGO clients and the public session endpoints; never ships on a product root.
    public sealed class CoopLobbyCycleProbe : MonoBehaviour
    {
        [Serializable] private sealed class Receipt {public bool passed;public string error;public int slot;public string[] observations;}
        private readonly List<string> observations=new List<string>();private NgoGameSession game;private string output;private int slot;
        private async Task Until(Func<bool> predicate,string reason,int seconds=60)
        {
            double end=Time.realtimeSinceStartupAsDouble+seconds;
            while(!predicate()){if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException(reason);await Task.Delay(50);}
        }
        private void Need(bool value,string why){if(!value)throw new InvalidOperationException(why);}
        private void Record(string value)=>observations.Add(value);
        private async void Start()
        {
            DontDestroyOnLoad(gameObject);var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"--hts-gameplay-nonce")<0){enabled=false;return;}
            string Read(string key)=>args[Array.IndexOf(args,key)+1];slot=int.Parse(Read("--hts-gameplay-slot"));
            output=Path.Combine(Read("--hts-gameplay-reports"),Read("--hts-gameplay-nonce"),"lobby-cycle-"+slot+".json");Directory.CreateDirectory(Path.GetDirectoryName(output));
            string error=null;
            try
            {
                await Until(()=>{game=FindFirstObjectByType<NgoGameSession>();return game!=null&&game.Driver!=null;},"Game startup");
                ulong connection=0;
                for(int cycle=0;cycle<2;cycle++)
                {
                    await Until(()=>game.Control!=null&&game.Control.IsSpawned&&game.Session.Phase==SessionPhase.Lobby&&game.Control.Roster.Count==2,"Two-player lobby");
                    if(cycle==0)connection=game.Manager.LocalClientId;else Need(game.Manager.LocalClientId==connection,"Guest was disconnected/reconnected");
                    Need(game.Control.Roster[0].Host&&game.Control.Roster[0].Ready,"Host row missing readiness");
                    await Until(()=>!game.Control.Roster[1].Ready,"Guest ready reset");
                    Record("Lobby "+cycle+": same connection "+connection+", host ready, guest unready, two replicated members");
                    await Task.Delay(700);
                    if(!game.HasAuthority)game.SetLocalReady(true);
                    await Until(()=>game.Control.Roster[0].Ready&&game.Control.Roster[1].Ready,"Ready RPC and replicated roster");
                    Record("Both clients observe ready state "+cycle);
                    if(game.HasAuthority){await Task.Delay(700);Need(game.Session.StartSelectedContract(),"Start contract rejected");}
                    await Until(()=>game.Session.Phase==SessionPhase.Playing,"Contract load and prepared barrier",90);
                    Record("Playing "+cycle+": "+game.Session.RunId);
                    if(game.HasAuthority){await Task.Delay(1500);game.Session.Controller.Abort(game.Driver.Now);}
                    await Until(()=>game.Session.Phase==SessionPhase.Results,"Terminal result");
                    if(!game.HasAuthority){Need(!game.Session.CanReturnToLobby&&!game.Session.ReturnToLobby(),"Guest result action must wait without leaving");Need(game.Manager.IsConnectedClient,"Guest result action disconnected");}
                    Record("Results "+cycle+": still connected, result navigation guarded");
                    if(game.HasAuthority){await Task.Delay(1500);Need(game.Session.ReturnToLobby(),"Host return to lobby rejected");}
                    await Until(()=>game.Session.Phase==SessionPhase.Lobby,"Shared return scene barrier");
                    Need(game.Manager.IsConnectedClient&&game.Manager.LocalClientId==connection,"Connection lost across result return");
                    Record("Returned whole crew to lobby "+cycle);
                    if(cycle==0&&Array.IndexOf(args,"--hts-reset-campaign")>=0)await ResetCampaign(connection);
                }
            }
            catch(Exception failure){error=failure.ToString();Debug.LogException(failure);}
            File.WriteAllText(output,JsonUtility.ToJson(new Receipt{passed=error==null,error=error,slot=slot,observations=observations.ToArray()},true));
            await Task.Delay(2000);Application.Quit(error==null?0:1);
        }
        private async Task ResetCampaign(ulong connection)
        {
            await Until(()=>game.Control.HasAcceptedCurrentSnapshot&&game.Control.Snapshot.Value.Phase==(byte)SessionPhase.Lobby&&
                !game.Control.Roster[1].Ready,"Reset fixture lobby synchronized");
            await Task.Delay(700);
            if(!game.HasAuthority)game.SetLocalReady(true);
            await Until(()=>game.Control.Roster[1].Ready,"Ready before reset");
            string oldId=game.Control.Snapshot.Value.Campaign.ToString();uint revision=game.Control.Snapshot.Value.Revision;
            Need(game.Session.DisplayedBalance>0&&game.Session.DisplayedTierId=="mk2","Seeded progress visible before reset");
            string own=Path.Combine(CampaignStoragePaths.ResolveOwnDirectory(),SaveRepository.MainName);
            byte[] guestSave=game.HasAuthority?null:File.ReadAllBytes(own);
            var view=FindFirstObjectByType<NewCampaignView>();Need(view!=null,"Authored reset view");
            Need(view.NewGameButton.gameObject.activeInHierarchy==game.HasAuthority,"Only host sees new game button");
            if(game.HasAuthority)
            {
                await Task.Delay(900);
                Need(game.Session.SelectLobbyContract(game.Session.Catalog.Contracts[game.Session.Catalog.Contracts.Length-1].ContractId),"Select a later contract before reset");
                view.NewGameButton.onClick.Invoke();Need(view.IsOpen,"Host confirmation opens");
                view.ConfirmButton.onClick.Invoke();Need(!view.IsOpen,"Host confirmation completes");
            }
            else Need(!game.Session.CanStartNewCampaign&&!game.Session.StartNewCampaign(new CampaignState(Guid.NewGuid().ToString("N"))),"Guest cannot reset campaign");
            await Until(()=>game.Control.HasAcceptedCurrentSnapshot&&game.Control.Snapshot.Value.Campaign.ToString()!=oldId&&
                game.Control.Snapshot.Value.Revision>revision&&!game.Control.Roster[1].Ready,"Fresh campaign and unready guest replicated");
            Need(game.Session.Phase==SessionPhase.Lobby&&game.Manager.IsConnectedClient&&game.Manager.LocalClientId==connection&&game.Control.Roster.Count==2,"Reset preserves whole lobby and connection IDs");
            Need(game.Session.DisplayedBalance==0&&game.Session.DisplayedTierId=="mk1"&&game.Session.DisplayedExtraSlots==0&&
                game.Control.Snapshot.Value.ClearedContractMask==0&&!game.Control.Snapshot.Value.LegacyContractAccess&&game.Session.Result==null,"All progress and previous result cleared on both clients");
            Need(game.Session.SelectedLobbyContractId==game.Session.Catalog.Contracts[0].ContractId,"First contract selected again");
            Need(game.Control.Roster[0].Ready&&!game.Control.Roster[1].Ready,"Host ready and guest requires confirmation");
            if(guestSave!=null)Need(Convert.ToBase64String(File.ReadAllBytes(own))==Convert.ToBase64String(guestSave),"Guest personal campaign untouched");
            Record("Reset: same connections, fresh MK1/$0/no upgrades/history, new revision, old result cleared, guest unready, personal guest save unchanged");
        }
    }
}
#endif
