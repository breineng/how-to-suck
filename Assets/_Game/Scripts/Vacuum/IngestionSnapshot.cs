using UnityEngine;
namespace HowToSuck
{
    public sealed class IngestionSnapshot
    {
        public readonly string RunId,TypeId;
        public readonly ulong InstanceId;
        public readonly int IntakeId,PlayerId;
        public readonly long Value;
        public readonly CargoRole CargoRole;
        public readonly BossKey BossKey;
        public readonly string TierId, LastStorageTierId;
        public readonly int LastStorageOwner;
        internal readonly PlayerStorage Storage;
        public readonly bool IsTruck;
        public readonly double StartedAt;
        public readonly float Duration,RequiredSize;
        public readonly Vector3 StartPosition,StartScale;
        public readonly Quaternion StartRotation;
        public Vector3 TargetPosition { get; internal set; }
        public Quaternion TargetRotation { get; internal set; }
        public Vector3 EndPosition { get; internal set; }
        internal readonly SuckableObject Item;
        internal readonly IntakeReceiver Receiver;
        public System.Func<double> PresentationClock;
        public double PresentationNow => PresentationClock != null ? PresentationClock() : Time.realtimeSinceStartupAsDouble;
        public float Progress(double now) => Mathf.Clamp01((float)((now-StartedAt)/Duration));
        internal IngestionSnapshot(SuckableObject item, IntakeReceiver receiver, int intake, int player, bool truck,
            double started, float duration, float size, Vector3 start, Quaternion rotation, Vector3 scale,
            Vector3 target, Quaternion targetRotation, Vector3 end, System.Func<double> clock)
        {
            Item=item; Receiver=receiver; RunId=item.RunId; InstanceId=item.InstanceId; TypeId=item.TypeId;
            Value=item.Value; IntakeId=intake; PlayerId=player; IsTruck=truck; StartedAt=started;
            Duration=duration; RequiredSize=size; StartPosition=start; StartRotation=rotation; StartScale=scale;
            TargetPosition=target; TargetRotation=targetRotation; EndPosition=end; PresentationClock=clock;
            CargoRole=item.CargoRole; BossKey=item.BossKey; LastStorageOwner=item.LastStorageOwner; LastStorageTierId=item.LastStorageTierId;
            TierId=receiver!=null && receiver.Emitter!=null && receiver.Emitter.Definition!=null ? receiver.Emitter.Definition.TierId : null;
            Storage=null;
        }
        internal IngestionSnapshot(SuckableObject item,IntakeReceiver receiver,double now)
        {
            Item=item;Receiver=receiver;RunId=item.RunId;InstanceId=item.InstanceId;TypeId=item.TypeId;
            Value=item.Value;RequiredSize=item.RequiredIntakeSize;IntakeId=receiver.IntakeId;
            PlayerId=receiver.PlayerId;IsTruck=receiver.IsTruck;StartedAt=now;Duration=Mathf.Clamp(receiver.Duration,.35f,1.2f);
            StartPosition=item.UnshiftedVisualPosition;StartRotation=item.VisualRoot.rotation;StartScale=item.VisualRoot.localScale;
            TargetPosition=receiver.Position;TargetRotation=receiver.Rotation;EndPosition=receiver.EndPosition;
            CargoRole=item.CargoRole; BossKey=item.BossKey; LastStorageOwner=item.LastStorageOwner; LastStorageTierId=item.LastStorageTierId;
            TierId=receiver.Emitter.Definition.TierId; Storage=receiver.IsTruck ? null : receiver.Storage;
        }
    }
}
