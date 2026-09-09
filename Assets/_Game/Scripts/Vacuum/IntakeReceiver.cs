using UnityEngine;
namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class IntakeReceiver : MonoBehaviour
    {
        internal static readonly System.Collections.Generic.List<IntakeReceiver> ActiveReceivers=new System.Collections.Generic.List<IntakeReceiver>();
        private void OnEnable(){if(!ActiveReceivers.Contains(this))ActiveReceivers.Add(this);}
        private void OnDisable()=>ActiveReceivers.Remove(this);
        public int IntakeId=1;
        public int PlayerId=1;
        public bool IsTruck;
        public VacuumEmitter Emitter;
        public Transform Target;
        public Transform VisualEndPoint;
        [System.NonSerialized] public Transform PresentationTarget;
        [Min(.01f)] public float AdmissionRadius=.24f;
        [Range(.35f,1.2f)] public float Duration=.7f;
        private readonly System.Collections.Generic.List<IngestionSnapshot> ingestions=new System.Collections.Generic.List<IngestionSnapshot>();
        public IngestionSnapshot Current { get; private set; }
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
        internal bool CanReceive => isActiveAndEnabled && Emitter!=null && Emitter.isActiveAndEnabled &&
            Emitter.Definition!=null && ValidTiming && Emitter.HasClearSourcePath() && (IsTruck || !Busy) &&
            (IsTruck || Storage != null && Storage.OwnerId == PlayerId && Storage.HasSpace);
        public bool CanAdmit => CanReceive && (IsTruck || Emitter.Active);
        internal bool Owns(IngestionSnapshot snapshot)=>ingestions.Contains(snapshot);
        internal void Attach(IngestionSnapshot snapshot)
        {
            if(ingestions.Contains(snapshot))return;
            // Authority admission enforces the player's single slot. Replica item
            // updates can briefly arrive in a different order within one frame.
            ingestions.Add(snapshot);
            ingestions.Sort((a,b)=>{int result=a.StartedAt.CompareTo(b.StartedAt);return result!=0?result:a.InstanceId.CompareTo(b.InstanceId);});
            Current=IsTruck?ingestions[0]:ingestions[ingestions.Count-1];
        }
        internal void Release(IngestionSnapshot snapshot)
        {ingestions.Remove(snapshot);Current=ingestions.Count>0?ingestions[IsTruck?0:ingestions.Count-1]:null;}
        private bool ValidTiming => !float.IsNaN(Duration) && !float.IsInfinity(Duration) && Duration>0 &&
            !float.IsNaN(AdmissionRadius) && !float.IsInfinity(AdmissionRadius) && AdmissionRadius>0;
        public bool Accepts(SuckableObject item) => item!=null &&
            (IsTruck ? item.CanBeSwallowedByTruck && CanAutomaticallyReceive(item) :
                item.CanBeSwallowedByPlayer && Emitter!=null && Emitter.FocusedItem==item) && Emitter!=null && Emitter.Definition!=null &&
            Emitter.Definition.IntakeSize>=item.RequiredIntakeSize;
        public bool CanAutomaticallyReceive(SuckableObject item) => item != null &&
            (item.State == SuckableState.Available && !item.AutomaticTruckAdmissionBlocked ||
             item.State == SuckableState.InFlight && item.DirectedIntakeId == IntakeId);
    }
}
