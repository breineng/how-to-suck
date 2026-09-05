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
        [Min(.01f)] public float AdmissionRadius=.24f;
        [Range(.35f,1.2f)] public float Duration=.7f;
        public IngestionSnapshot Current { get; internal set; }
        public bool Busy => Current!=null;
        public Vector3 Position => Target!=null ? Target.position : Emitter.Position;
        public Quaternion Rotation => Target!=null ? Target.rotation : Quaternion.LookRotation(Emitter.Forward);
        public bool CanAdmit => isActiveAndEnabled && Emitter!=null && Emitter.Active &&
            Emitter.Definition!=null && Emitter.HasClearSourcePath() && !Busy;
        public bool Accepts(SuckableDefinition item) => item!=null &&
            (IsTruck ? item.CanBeSwallowedByTruck : item.CanBeSwallowedByPlayer) &&
            Emitter.Definition.IntakeSize>=item.RequiredIntakeSize;
    }
}