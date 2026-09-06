using UnityEngine;
namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class IntakeReceiver : MonoBehaviour
    {
        public int IntakeId=1;
        public int PlayerId=1;
        public bool IsTruck;
        public VacuumEmitter Emitter;
        public Transform Target;
        public Transform VisualEndPoint;
        [System.NonSerialized] public Transform PresentationTarget;
        [Min(.01f)] public float AdmissionRadius=.24f;
        [Range(.35f,1.2f)] public float Duration=.7f;
        public IngestionSnapshot Current { get; internal set; }
        public bool Busy => Current!=null;
        public PlayerStorage Storage { get; private set; }
        public void BindStorage(PlayerStorage storage)
        {
            if (Busy) throw new System.InvalidOperationException("Cancel intake before rebinding storage.");
            if (storage != null && (IsTruck || storage.OwnerId != PlayerId)) throw new System.InvalidOperationException("Receiver/storage owner mismatch.");
            Storage = storage;
        }
        public Vector3 Position => Target!=null ? Target.position : Emitter.Position;
        public Quaternion Rotation => Target!=null ? Target.rotation : Quaternion.LookRotation(Emitter.Forward);
        public Vector3 EndPosition => VisualEndPoint!=null ? VisualEndPoint.position : Position-Rotation*Vector3.forward*.14f;
        public bool CanAdmit => isActiveAndEnabled && Emitter!=null && Emitter.Active &&
            Emitter.Definition!=null && ValidTiming && Emitter.HasClearSourcePath() && !Busy &&
            (IsTruck || Storage != null && Storage.OwnerId == PlayerId && Storage.HasSpace);
        private bool ValidTiming => !float.IsNaN(Duration) && !float.IsInfinity(Duration) && Duration>0 &&
            !float.IsNaN(AdmissionRadius) && !float.IsInfinity(AdmissionRadius) && AdmissionRadius>0;
        public bool Accepts(SuckableObject item) => item!=null &&
            (IsTruck ? item.CanBeSwallowedByTruck : item.CanBeSwallowedByPlayer) && Emitter!=null && Emitter.Definition!=null &&
            Emitter.Definition.IntakeSize>=item.RequiredIntakeSize;
    }
}