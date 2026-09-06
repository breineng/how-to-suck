using Unity.Collections;
using Unity.Netcode;
namespace HowToSuck.Networking
{
    public struct AchievementFactWire : INetworkSerializable
    {
        public FixedString64Bytes Run,Event;
        public byte Recipients,Achievements;
        public AchievementFactWire(AchievementFact fact)
        {Run=new FixedString64Bytes(fact.RunId);Event=new FixedString64Bytes(fact.EventId);Recipients=fact.Recipients;Achievements=fact.Achievements;}
        public AchievementFact Fact=>new AchievementFact(Run.ToString(),Event.ToString(),Recipients,Achievements);
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T:IReaderWriter
        {serializer.SerializeValue(ref Run);serializer.SerializeValue(ref Event);serializer.SerializeValue(ref Recipients);serializer.SerializeValue(ref Achievements);}
    }
    // Separate server-only reliable message on the persistent SessionControl NetworkObject.
    // No client grant RPC exists. PlayerId masks are resolved against current owned spawned players.
    [UnityEngine.DisallowMultipleComponent]
    public sealed class NetworkAchievementRelay : NetworkBehaviour
    {
        private NgoGameSession game;
        private AchievementSessionBridge bridge;
        public override void OnNetworkSpawn()
        {
            game=NgoGameSession.RequireCurrent();bridge=game.Session.GetComponent<AchievementSessionBridge>();
            if(IsServer&&bridge!=null)bridge.GrantsProduced+=Publish;
        }
        private void Publish(AchievementFact fact)
        {
            if(IsServer&&IsSpawned&&!game.IsStopping&&fact.IsValid&&fact.RunId==game.Session.RunId)
                ReceiveFactRpc(new AchievementFactWire(fact));
        }
        [Rpc(SendTo.NotServer,InvokePermission=RpcInvokePermission.Server,Delivery=RpcDelivery.Reliable)]
        private void ReceiveFactRpc(AchievementFactWire value,RpcParams rpc=default)
        {
            if(IsServer||!IsSpawned||!isActiveAndEnabled||game==null||bridge==null||game.IsStopping||
                rpc.Receive.SenderClientId!=Unity.Netcode.NetworkManager.ServerClientId)return;
            var fact=value.Fact;
            if(!fact.IsValid||fact.RunId!=game.Session.RunId)return;
            foreach(var player in game.Players)
                if(player!=null&&player.IsSpawned&&player.IsOwner&&player.OwnerClientId==NetworkManager.LocalClientId&&
                    player.HasAcceptedCurrentSnapshot&&player.Snapshot.Value.Run.ToString()==fact.RunId&&player.Motor!=null)
                {bridge.ReceiveFromCurrentServer(fact,player.Motor.PlayerId);return;}
        }
        public override void OnNetworkDespawn()
        {if(bridge!=null)bridge.GrantsProduced-=Publish;bridge=null;game=null;}
    }
}