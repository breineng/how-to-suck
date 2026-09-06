#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Netcode;
using UnityEngine;
namespace HowToSuck.Networking
{
    // Diagnostic physics item. Final gameplay still uses SuckableObject/AuthorityWorld, not this fixture.
    [RequireComponent(typeof(Rigidbody), typeof(NetworkObject))]
    public sealed class MppmSmokeProbe : NetworkBehaviour
    {
        public readonly NetworkVariable<int> Round = new NetworkVariable<int>(0);
        public readonly NetworkVariable<bool> Frozen = new NetworkVariable<bool>(true);
        public Rigidbody Body { get; private set; }
        public int LocalAuthorityTicks { get; private set; }
        private int observedRound;
        public void Prepare(int round)
        {
            if (IsSpawned || round <= 0) throw new System.InvalidOperationException("Initialize the fixture before network spawn.");
            Body = GetComponent<Rigidbody>(); Body.isKinematic = true;
            Round.Value = round; Frozen.Value = true;
        }
        public override void OnNetworkSpawn()
        {
            Body = GetComponent<Rigidbody>(); observedRound = Round.Value;
            Frozen.OnValueChanged += OnFrozen; ApplyMode();
            if (!IsServer && (Round.CanClientWrite(NetworkManager.LocalClientId) || Frozen.CanClientWrite(NetworkManager.LocalClientId)))
            { MppmSmokeRunner.Current?.Fail("Guest has write permission over host-owned fixture state."); return; }
            if (observedRound <= 0 || !Frozen.Value) { MppmSmokeRunner.Current?.Fail("Probe published before its initial state was ready."); return; }
            MppmSmokeRunner.Current?.ProbeSpawned(this);
        }
        private void OnFrozen(bool oldValue, bool newValue) => ApplyMode();
        private void ApplyMode() => Body.isKinematic = !IsServer || Frozen.Value;
        private void FixedUpdate()
        {
            if (!IsSpawned) return;
            if (!IsServer)
            { if (!Body.isKinematic) MppmSmokeRunner.Current?.Fail("Guest physics became dynamic."); return; }
            if (!Frozen.Value) LocalAuthorityTicks++;
        }
        public void SetFrozen(bool frozen)
        { if (!IsServer) throw new System.InvalidOperationException("Only the host changes fixture physics."); Frozen.Value = frozen; ApplyMode(); }
        public override void OnNetworkDespawn()
        {
            Frozen.OnValueChanged -= OnFrozen;
            MppmSmokeRunner.Current?.ProbeDespawned(this, observedRound);
        }
    }
}
#endif
