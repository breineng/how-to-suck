#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Netcode;
using UnityEngine;
namespace HowToSuck.Networking
{
    // Persistent diagnostic control object, not a gameplay session or money controller.
    public sealed class MppmSmokeControl : NetworkBehaviour
    {
        public override void OnNetworkSpawn() => MppmSmokeRunner.Current?.ControlSpawned(this);
        public override void OnNetworkDespawn() => MppmSmokeRunner.Current?.ControlDespawned(this);
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReportRpc(int kind, int round, int slot, int pid, int authorityTicks, bool kinematic,
            Vector3 position, int unreliableSeen, RpcParams rpc = default)
        {
            if (!IsServer) return;
            MppmSmokeRunner.Current?.ReceiveReport(rpc.Receive.SenderClientId, kind, round, slot, pid,
                authorityTicks, kinematic, position, unreliableSeen);
        }
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server)]
        public void CommandRpc(int command, int round, Vector3 expectedPosition)
            => MppmSmokeRunner.Current?.ReceiveCommand(command, round, expectedPosition);
        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server, Delivery = RpcDelivery.Unreliable)]
        public void PulseRpc(int round, int sequence) => MppmSmokeRunner.Current?.ReceivePulse(round, sequence);
    }
}
#endif
