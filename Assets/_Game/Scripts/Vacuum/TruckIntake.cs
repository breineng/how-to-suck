using System;
using UnityEngine;

namespace HowToSuck
{
    /// <summary>Stepped only by AuthorityWorld, alongside the player emitters.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VacuumEmitter), typeof(IntakeReceiver))]
    public sealed class TruckIntake : MonoBehaviour
    {
        public const int DefaultIntakeId = 1000;
        public VacuumEmitter Emitter;
        public IntakeReceiver Receiver;
        public BoxCollider ExtractionArea;
        public bool HasNearbyLoot { get; private set; }
        public bool FieldActive => Emitter != null && Emitter.Active;

        // Collect and Apply use the same registry, Items layer, cone and visibility checks.
        // There is deliberately no FixedUpdate or private ingestion service here.
        public void Step(SuctionSystem suction)
        {
            if (suction == null) throw new ArgumentNullException(nameof(suction));
            if (!isActiveAndEnabled || Emitter == null || !Emitter.isActiveAndEnabled ||
                Emitter.Definition == null || Receiver == null || !Receiver.isActiveAndEnabled)
            {
                Stop();
                return;
            }

            // Collect requires an enabled emitter. This temporary probe is synchronous;
            // only the final nearby/busy state remains visible after the authority step.
            Emitter.Active = true;
            HasNearbyLoot = suction.Collect(Emitter).Count > 0;
            Emitter.Active = HasNearbyLoot || Receiver.Busy;
            suction.Apply(Emitter);
        }

        public void Stop()
        {
            HasNearbyLoot = false;
            if (Emitter == null) return;
            Emitter.Active = false;
            Emitter.LastAffectedCount = 0;
            Emitter.LastLoad = 0;
            // Receiver.Current belongs to IngestionService, including cancellation.
        }

        public bool TryValidate(out string error)
        {
            error = null;
            if (Emitter == null || Receiver == null) error = "Truck needs its shared emitter and receiver.";
            else if (Emitter.Definition == null) error = "Truck vacuum definition is missing.";
            else if (!Emitter.Definition.TryValidate(out error)) return false;
            else if (Emitter.Source == null || Receiver.Target != Emitter.Source)
                error = "Truck admission target must be the same rear gate as the suction source.";
            else if (Receiver.VisualEndPoint == null) error = "Truck visual tube endpoint is missing.";
            else if (GetComponentsInChildren<Rigidbody>(true).Length != 0 || GetComponentsInChildren<SuckableObject>(true).Length != 0) error = "Truck must stay static and non-collectible.";
            else if (Receiver.Emitter != Emitter) error = "Truck receiver is bound to a different emitter.";
            else if (!Receiver.IsTruck || Receiver.PlayerId != 0 || Receiver.IntakeId != DefaultIntakeId || Emitter.EmitterId != DefaultIntakeId)
                error = "Truck must use intake/emitter ID 1000, player ID 0 and IsTruck.";
            else if (float.IsNaN(Emitter.Range) || float.IsInfinity(Emitter.Range) || Emitter.Range <= 0 || Emitter.Range > 3.01f)
                error = "Truck suction must remain a local field of at most three metres.";
            else if (Emitter.HalfAngle < 45 || Emitter.HalfAngle > 55 || float.IsNaN(Emitter.HalfAngle))
                error = "Truck cone half-angle must be 45 to 55 degrees.";
            else if (ExtractionArea == null || !ExtractionArea.isTrigger)
                error = "Truck needs a separate extraction marker trigger.";
            return error == null;
        }

        private void Awake() => Stop();
        private void OnDisable() => Stop();
    }
}