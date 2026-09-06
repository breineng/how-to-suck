using HowToSuck.Audio;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
namespace HowToSuck.Networking
{
    [DisallowMultipleComponent,RequireComponent(typeof(NetworkSessionAdapter))]
    public sealed class NetworkAudioRelay:NetworkBehaviour
    {
        private NgoGameSession game;private GameAudioRoot audioRoot;
        private double nextLoads;private string loadRun="",receivedRun="";private ulong loadSequence,receivedSequence;private bool subscribed;
        public override void OnNetworkSpawn()
        {
            game=NgoGameSession.RequireCurrent();audioRoot=game.Session.GetComponent<GameAudioRoot>();
            if(audioRoot==null){Debug.LogError("Network session needs its authored GameAudioRoot.",this);return;}
            Bind();
        }
        private void SendImpact(AuthorityImpactCue cue)
        {
            if(isActiveAndEnabled&&IsServer&&IsSpawned&&audioRoot!=null&&audioRoot.Playing&&!game.IsStopping)
                ImpactRpc(new FixedString64Bytes(cue.Run),cue.Sequence,(byte)cue.Id,cue.Position,cue.Gain);
        }
        private void SendCommitted(HowToSuck.Audio.CommittedAudioReceipt receipt)
        {
            if(!isActiveAndEnabled||!IsServer||!IsSpawned||audioRoot==null||!audioRoot.Playing||game.IsStopping)return;
            var f=receipt.Fact;if(!f.IsValid||f.Run!=game.Session.RunId||receipt.Sequence==0)return;
            CommittedRpc(new FixedString64Bytes(f.Run),receipt.Sequence,(byte)f.Kind,f.Occurrence,f.Item,f.Enemy,f.Owner,f.Intake,f.Truck,f.At,f.Size,f.Amount,f.Position,f.Duration);
        }
        [Rpc(SendTo.NotServer,InvokePermission=RpcInvokePermission.Server,Delivery=RpcDelivery.Reliable)]
        private void CommittedRpc(FixedString64Bytes run,ulong sequence,byte kind,ulong occurrence,ulong item,ulong enemy,int owner,int intake,bool truck,double at,float size,int amount,Vector3 position,float duration)
        {
            if(!isActiveAndEnabled||IsServer||!IsSpawned||audioRoot==null||game==null||game.IsStopping)return;
            var fact=new HowToSuck.Audio.CommittedAudioFact(run.ToString(),(HowToSuck.Audio.CommittedAudioKind)kind,occurrence,item,enemy,owner,intake,truck,at,size,amount,position,duration);
            if(fact.IsValid)audioRoot.ReceiveCommitted(new HowToSuck.Audio.CommittedAudioReceipt(sequence,fact));
        }
        // NotServer deliberately excludes the host's already played authoritative callback.
        [Rpc(SendTo.NotServer,InvokePermission=RpcInvokePermission.Server,Delivery=RpcDelivery.Unreliable)]
        private void ImpactRpc(FixedString64Bytes run,ulong sequence,byte id,Vector3 point,float gain)
        {if(isActiveAndEnabled&&!IsServer&&audioRoot!=null)audioRoot.ReceiveImpact(new AuthorityImpactCue(run.ToString(),sequence,(SfxId)id,point,gain));}
        private void Update()
        {
            if(!IsSpawned||!IsServer||audioRoot==null||!audioRoot.Playing||game.IsStopping||Time.realtimeSinceStartupAsDouble<nextLoads)return;
            nextLoads=Time.realtimeSinceStartupAsDouble+.2;string run=game.Session.RunId;
            if(run!=loadRun){loadRun=run;loadSequence=0;}
            float a=0,b=0,c=0,d=0;
            foreach(var player in game.Players)
            {
                if(player==null||!player.IsSpawned)continue;var emitter=player.GetComponent<VacuumEmitter>();
                float value=emitter!=null&&emitter.Active?Mathf.Clamp01(emitter.LastLoad/20f):0;
                switch(player.Motor.PlayerId){case 1:a=value;break;case 2:b=value;break;case 3:c=value;break;case 4:d=value;break;}
            }
            if(loadSequence!=ulong.MaxValue)LoadsRpc(new FixedString64Bytes(run),++loadSequence,a,b,c,d);
        }
        [Rpc(SendTo.NotServer,InvokePermission=RpcInvokePermission.Server,Delivery=RpcDelivery.Unreliable)]
        private void LoadsRpc(FixedString64Bytes run,ulong sequence,float a,float b,float c,float d)
        {
            if(!isActiveAndEnabled||IsServer||audioRoot==null||!audioRoot.Playing||run.ToString()!=game.Session.RunId||sequence==0)return;
            string id=run.ToString();if(receivedRun!=id){receivedRun=id;receivedSequence=0;}
            if(sequence<=receivedSequence)return;receivedSequence=sequence;
            audioRoot.SetReplicaLoad(id,1,a);audioRoot.SetReplicaLoad(id,2,b);audioRoot.SetReplicaLoad(id,3,c);audioRoot.SetReplicaLoad(id,4,d);
        }
        private void Bind(){if(isActiveAndEnabled&&IsSpawned&&IsServer&&audioRoot!=null&&!subscribed){audioRoot.AuthorityImpact+=SendImpact;audioRoot.AuthorityCommittedAudio+=SendCommitted;subscribed=true;}}
        private void Unbind(){if(subscribed&&audioRoot!=null){audioRoot.AuthorityImpact-=SendImpact;audioRoot.AuthorityCommittedAudio-=SendCommitted;}subscribed=false;}
        private void OnEnable()=>Bind();
        private void OnDisable()=>Unbind();
        public override void OnNetworkDespawn()
        {Unbind();game=null;audioRoot=null;receivedRun=loadRun="";receivedSequence=loadSequence=0;}
    }
}
