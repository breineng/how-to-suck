using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class AchievementSessionBridge:MonoBehaviour
    {
        public AchievementCatalog Catalog;
        public AchievementService Service{get;private set;}
        public string LastError{get;private set;}="";
        public string AccountNotice=>Service?.AccountNotice??"";
        public event Action Changed;
        public event Action<AchievementFact> GrantsProduced;
        private SessionRoot session;
        private readonly AchievementAuthorityEvaluator evaluator=new AchievementAuthorityEvaluator();
        public void Bind(SessionRoot value,string ownStorageDirectory=null)
        {
            if(session!=null)return;session=value;
            try{
                if(session==null||session.World==null||Catalog==null)throw new InvalidOperationException("Achievement references missing.");
                if(!Catalog.TryValidate(out var error))throw new InvalidOperationException(error);
                // Guest owns only this separate local profile. No campaign repository or host path is opened here.
                string directory=Path.Combine(ownStorageDirectory??CampaignStoragePaths.ResolveOwnDirectory(),"Achievements");
                var repository=new AchievementProfileRepository(directory,new UnityAchievementProfileCodec());
                try{Service=new AchievementService(repository);}catch{repository.Dispose();throw;}
                Service.Changed+=Notify;
            }catch(Exception e){Report(e);}
            if(session!=null&&session.World!=null){
                session.World.RunStarted+=OnRunStarted;session.World.DeliveryCommitted+=OnDelivery;
                session.World.PlayerRemoved+=OnPlayerRemoved;session.World.WorldCleared+=OnClear;session.Changed+=OnSessionChanged;
            }
        }
        private int[] ActiveRoster()
        {
            var result=new List<int>();foreach(var pair in session.World.Players)
                if(pair.Value!=null&&pair.Value.isActiveAndEnabled)result.Add(pair.Key);return result.ToArray();
        }
        private void OnRunStarted()
        {
            if(!isActiveAndEnabled||!session.HasAuthority)return;
            try{
                if(!session.World.IsRunning||!session.Controller.IsRunning||session.CurrentContract==null||session.Campaign==null||
                    session.World.PreparedTierId!=session.Campaign.CurrentTierId||session.Controller.State.Boss.Status!=BossObjectiveStatus.Active)
                    throw new InvalidOperationException("Use the running world, purchased campaign tier and assigned active boss.");
                evaluator.Begin(session.World.RunId,session.CurrentContract.ContractId,session.World.PreparedTierId,
                    session.Controller.State.Boss.Key,session.CurrentContract.RequiredBossId,ActiveRoster());
            }catch(Exception e){evaluator.Cancel();Report(e);}
        }
        private void OnDelivery(DeliveryRecord record)
        {
            if(!isActiveAndEnabled||!session.HasAuthority||!session.World.IsRunning)return;
            try{evaluator.ObservePreparedTier(session.World.PreparedTierId);Publish(evaluator.AcceptCommittedDelivery(record,ActiveRoster()));}catch(Exception e){Report(e);}
        }
        private void OnPlayerRemoved(int id){if(session.HasAuthority)evaluator.PlayerRemoved(id);if(session.LocalPlayer!=null&&session.LocalPlayer.PlayerId==id)Service?.UnbindLocalRun();}
        private void OnSessionChanged()
        {
            if(session.Phase==SessionPhase.ShuttingDown||session.Phase==SessionPhase.Lobby||session.Phase==SessionPhase.Loading)Service?.UnbindLocalRun();
            if(isActiveAndEnabled&&session.HasAuthority&&session.World.IsRunning)
                try{evaluator.ObserveRoster(ActiveRoster());evaluator.ObservePreparedTier(session.World.PreparedTierId);}catch(Exception e){evaluator.Cancel();Report(e);}
        }
        // Exact bound controller reference check precedes evaluation; replicas cannot invoke this path.
        public void OnAuthorityFinished(ContractResult result)
        {
            if(!isActiveAndEnabled||session==null||!session.HasAuthority||session.Phase==SessionPhase.ShuttingDown||!ReferenceEquals(result,session.Controller?.FinalResult))return;
            try{Publish(evaluator.Finish(result,session.World.PreparedTierId,ActiveRoster()));}catch(Exception e){Report(e);}
        }
        private void Publish(AchievementFact? optional)
        {
            if(!optional.HasValue)return;var fact=optional.Value;
            try{if(session.LocalPlayer!=null)Receive(fact,session.LocalPlayer.PlayerId);}catch(Exception e){Report(e);}
            var handlers=GrantsProduced;if(handlers!=null)foreach(Action<AchievementFact> handler in handlers.GetInvocationList())try{handler(fact);}catch(Exception e){Report(e);}
        }
        public void ReceiveFromCurrentServer(AchievementFact fact,int ownedPlayerId)
        {if(session==null||session.HasAuthority)return;try{Receive(fact,ownedPlayerId);}catch(Exception e){Report(e);}}
        private void Receive(AchievementFact fact,int ownedPlayerId)
        {
            if(!isActiveAndEnabled||Service==null||!fact.IsValid||fact.RunId!=session.RunId||session.LocalPlayer==null||session.LocalPlayer.PlayerId!=ownedPlayerId||
                session.Phase!=SessionPhase.Playing&&session.Phase!=SessionPhase.Results)return;
            Service.BindLocalRun(fact.RunId,ownedPlayerId);Service.Accept(fact,true);
        }
        private void OnClear(){evaluator.Cancel();Service?.UnbindLocalRun();}
        private void Update(){if(Service!=null)try{Service.Tick(Time.realtimeSinceStartupAsDouble);}catch(Exception e){Report(e);}}
        private void Report(Exception e){string message=e.GetType().Name+": "+e.Message;if(message==LastError)return;LastError=message;Debug.LogWarning("Achievements: "+message,this);Notify();}
        private void Notify(){var handlers=Changed;if(handlers!=null)foreach(Action handler in handlers.GetInvocationList())try{handler();}catch(Exception e){Debug.LogWarning("Achievement view failed: "+e.GetType().Name,this);}}
        private void OnDisable()=>OnClear();
        private void OnDestroy()
        {
            if(session!=null&&session.World!=null){session.World.RunStarted-=OnRunStarted;session.World.DeliveryCommitted-=OnDelivery;
                session.World.PlayerRemoved-=OnPlayerRemoved;session.World.WorldCleared-=OnClear;session.Changed-=OnSessionChanged;}
            if(Service!=null){Service.Changed-=Notify;Service.Dispose();Service=null;}Changed=null;GrantsProduced=null;
        }
    }
}
