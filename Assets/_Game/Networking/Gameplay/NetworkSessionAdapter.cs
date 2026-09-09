using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
namespace HowToSuck.Networking
{
    [DisallowMultipleComponent,RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkSessionAdapter : NetworkBehaviour
    {
        public readonly NetworkVariable<SessionWire> Snapshot=new NetworkVariable<SessionWire>(default,
            NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Server);
        public NetworkList<LobbyMemberWire> Roster;
        private readonly System.Collections.Generic.List<LobbyMemberWire> rosterBuffer=new System.Collections.Generic.List<LobbyMemberWire>(4);
        private void Awake()=>Roster=new NetworkList<LobbyMemberWire>();
        private NgoGameSession game;
        private uint acknowledgedRevision;
        private string acknowledgedRun;
        private double nextPublish;
        private ContractResult previousResult;
        private SessionWire previousResultWire;
        private SessionWire acceptedState;private bool hasAcceptedState;
        public bool HasAcceptedCurrentSnapshot=>IsSpawned&&(IsServer||hasAcceptedState&&acceptedState.Equals(Snapshot.Value));
        private bool TryGetCurrentState(out SessionWire state)
        {
            state=default;
            if(!HasAcceptedCurrentSnapshot)return false;
            state=IsServer?Snapshot.Value:acceptedState;return true;
        }
        private string observedBossRun;private BossObjectiveSnapshot observedBoss;
        public bool LocalReady
        {
            get
            {
                if(!IsSpawned||Roster==null)return false;
                for(int i=0;i<Roster.Count;i++)if(Roster[i].ClientId==NetworkManager.LocalClientId)return Roster[i].Ready;
                return false;
            }
        }
        public override void OnNetworkSpawn()
        {
            game=NgoGameSession.RequireCurrent();DontDestroyOnLoad(gameObject);game.BindControl(this);
            Snapshot.OnValueChanged+=OnSnapshot;
            if(IsServer){game.Session.Changed+=Publish;game.Connection.Changed+=Publish;Publish();}
            else Apply(Snapshot.Value);
        }
        public void Publish()
        {
            if(!IsServer||!IsSpawned||game==null)return;
            game.Connection.CopyConnectedRoster(rosterBuffer);
            for(int i=0;i<rosterBuffer.Count;i++)
                if(i>=Roster.Count)Roster.Add(rosterBuffer[i]);
                else if(!Roster[i].Equals(rosterBuffer[i]))Roster[i]=rosterBuffer[i];
            while(Roster.Count>rosterBuffer.Count)Roster.RemoveAt(Roster.Count-1);
            var session=game.Session;var state=session.ContractState;var result=session.Result;var boss=state.Boss;
            byte mask=0;
            foreach(var pair in session.World.Players)if(pair.Key>=1&&pair.Key<=4&&session.World.IsPlayerInExtraction(pair.Key))mask|=(byte)(1<<(pair.Key-1));
            Snapshot.Value=new SessionWire{Revision=game.Driver.Revision,Run=new FixedString64Bytes(state.RunId??""),
                Contract=new FixedString64Bytes(state.ContractId??""),Campaign=new FixedString64Bytes(session.Campaign?.CampaignId??""),
                CampaignTier=new FixedString64Bytes(session.Campaign?.CurrentTierId??""),SelectedContract=new FixedString64Bytes(session.SelectedLobbyContractId),
                HasCampaignProgression=session.Campaign!=null,PurchasedExtraSlots=session.Campaign?.PurchasedExtraSlots??0,
                ClearedContractMask=CampaignContractAccess.ClearedMask(session.Campaign?.ClearedContractIds),LegacyContractAccess=session.Campaign?.LegacyContractAccess??false,
                BossRun=new FixedString64Bytes(boss.Key.RunId??""),BossId=new FixedString64Bytes(boss.Key.ContractBossId??""),BossInstance=boss.Key.InstanceId,BossStatus=(byte)boss.Status,
                Phase=(byte)session.Phase,ContractPhase=(byte)state.Phase,Money=state.CollectedMoney,Quota=state.Quota,
                Balance=session.Campaign?.Balance??0,Count=state.CollectedInstanceCount,Started=state.StartedAt,Deadline=state.Deadline,
                Observed=state.ObservedAt,Initiator=state.ExtractionInitiatorId,Hold=state.ExtractHoldProgress,
                HasResult=result!=null,PayoutPercent=result?.PayoutPercent??0,Payout=result?.Payout??0,Finished=result?.FinishedAt??0,
                PendingPayout=session.HasPendingSave,ExtractionMask=mask,AllInExtraction=session.World.AllPlayersInExtraction,
                ExpectedItems=game.Driver.ExpectedItems,ExpectedPlayers=game.Driver.ExpectedPlayers,CanStart=session.CanStartContract,
                TruckActive=session.CurrentLevel!=null&&session.CurrentLevel.Truck!=null&&session.CurrentLevel.Truck.FieldActive,
                Error=new FixedString512Bytes(ShortError(session.LastError))};
        }
        private static string ShortError(string text)
        {
            if(string.IsNullOrEmpty(text))return "";
            // UTF-8 worst-case four bytes per UTF-16 character stays inside FixedString512 payload.
            return text.Length>100?text.Substring(0,100):text;
        }
        private void Update()
        {
            if(!IsSpawned)return;
            if(IsServer&&Time.realtimeSinceStartupAsDouble>=nextPublish)
            {nextPublish=Time.realtimeSinceStartupAsDouble+.05;Publish();}
            if(!TryGetCurrentState(out var state))return;
            if(state.ContractPhase!=(byte)ContractPhase.Preparing||state.Revision==0||
                acknowledgedRevision==state.Revision&&acknowledgedRun==state.Run.ToString())return;
            if(!game.TryVerifyPrepared(state,out var ownObject))return;
            // Reliable, once per exact run/revision; server recomputes ownership and uses actual RPC sender.
            acknowledgedRevision=state.Revision;acknowledgedRun=state.Run.ToString();
            if(IsServer)game.Driver.Acknowledge(NetworkManager.LocalClientId,acknowledgedRun,state.Revision,ownObject,state.ExpectedItems,state.ExpectedPlayers);
            else PreparedRpc(state.Run,state.Revision,ownObject,state.ExpectedItems,state.ExpectedPlayers);
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        private void PreparedRpc(FixedString64Bytes run,uint revision,ulong ownedObject,int items,int players,RpcParams rpc=default)
        {
            if(IsServer)game.Driver.Acknowledge(rpc.Receive.SenderClientId,run.ToString(),revision,ownedObject,items,players);
        }
        public void SetLocalReady(bool ready)
        {
            if(!TryGetCurrentState(out var state)||state.Phase!=(byte)SessionPhase.Lobby)return;
            if(IsServer)
            {game.Connection.SetReadyFromServerRpc(NetworkManager.LocalClientId,ready);}
            else ReadyRpc(ready,state.Revision);
        }
        [Rpc(SendTo.Server,InvokePermission=RpcInvokePermission.Everyone)]
        private void ReadyRpc(bool ready,uint revision,RpcParams rpc=default)
        {
            if(!IsServer||revision!=game.Driver.Revision)return;
            game.Connection.SetReadyFromServerRpc(rpc.Receive.SenderClientId,ready);
        }
        private void OnSnapshot(SessionWire old,SessionWire state)
        {
            if(IsServer)return;
            Apply(state);
        }
        private void Apply(SessionWire value)
        {
            if(IsServer||game.IsStopping)return;
            if(!Enum.IsDefined(typeof(SessionPhase),(int)value.Phase)||!Enum.IsDefined(typeof(ContractPhase),(int)value.ContractPhase)||
                value.Money<0||value.Balance<0||value.Count<0||value.Hold<0||value.Hold>1)
                throw new InvalidOperationException("Invalid session replica.");
            string campaignTier=value.CampaignTier.ToString();
            bool knownTier=string.IsNullOrEmpty(campaignTier)&&value.Campaign.Length==0;
            if(game.Session.Catalog?.Vacuums!=null)foreach(var entry in game.Session.Catalog.Vacuums)
                if(entry!=null&&entry.TierId==campaignTier)knownTier=true;
            if(!knownTier)throw new InvalidOperationException("Confirmed host tier is absent from the local catalog.");
            string selectedContract=value.SelectedContract.ToString();
            bool knownSelection=string.IsNullOrEmpty(selectedContract)&&value.Phase!=(byte)SessionPhase.Lobby;
            if(game.Session.Catalog?.Contracts!=null)foreach(var entry in game.Session.Catalog.Contracts)
                if(entry!=null&&entry.ContractId==selectedContract)knownSelection=true;
            if(!knownSelection)throw new InvalidOperationException("Host-selected contract is absent from the local catalog.");
            ContractDefinition active=null;
            if(game.Session.Catalog?.Contracts!=null)foreach(var entry in game.Session.Catalog.Contracts)
                if(entry!=null&&entry.ContractId==value.Contract.ToString())active=entry;
            var boss=GameplayStateValidation.RequireSession(value,active?.RequiredBossId,active!=null?active.FailurePercent:0);
            if(observedBossRun==value.Run.ToString())GameplayReplicaPolicy.RequireBossAdvance(observedBoss,boss);
            var state=SessionReplica.StateCopy(value.Run.ToString(),value.Contract.ToString(),(ContractPhase)value.ContractPhase,
                value.Money,value.Quota,value.Count,value.Started,value.Deadline,value.Observed,value.Initiator,value.Hold,boss);
            // A missing result must not erase the immutable result already accepted for this run.
            if(previousResult!=null&&previousResult.RunId==state.RunId&&
                (!value.HasResult||!GameplayStateValidation.SameTerminalResult(previousResultWire,value)))
                throw new InvalidOperationException("A terminal result changed or disappeared in place.");
            if(value.HasResult)
            {
                if(previousResult==null||previousResult.RunId!=state.RunId){
                    previousResult=SessionReplica.ResultCopy(value.Campaign.ToString(),state.RunId,state.ContractId,state.Phase,
                        value.Money,value.Quota,value.PayoutPercent,value.Payout,value.Started,value.Deadline,value.Finished,boss);
                    previousResultWire=value;
                }
            }
            else {previousResult=null;previousResultWire=default;}
            observedBossRun=value.Run.ToString();observedBoss=boss;
            game.ApplyTruckPresentation(value.TruckActive);
            game.Session.ApplyReplica(new SessionReplica{Phase=(SessionPhase)value.Phase,State=state,Result=previousResult,
                Balance=value.Balance,CurrentTierId=campaignTier,SelectedContractId=selectedContract,
                HasCampaignProgression=value.HasCampaignProgression,PurchasedExtraSlots=value.PurchasedExtraSlots,
                ClearedContractMask=value.ClearedContractMask,LegacyContractAccess=value.LegacyContractAccess,
                PendingPayout=value.PendingPayout,ExtractionMask=value.ExtractionMask,
                AllInExtraction=value.AllInExtraction,Error=value.Error.ToString()});
            // Commit only after all validation and guest application. A rejected raw value never drives ACK/Ready.
            if(!IsSpawned||game.IsStopping)return;
            acceptedState=value;hasAcceptedState=true;
        }
        public override void OnNetworkDespawn()
        {
            hasAcceptedState=false;acceptedState=default;
            Snapshot.OnValueChanged-=OnSnapshot;previousResult=null;previousResultWire=default;observedBossRun=null;observedBoss=default;
            if(game!=null){game.Session.Changed-=Publish;game.Connection.Changed-=Publish;game.ReleaseControl(this);}
        }
    }
}
