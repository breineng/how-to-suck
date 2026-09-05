using UnityEngine;
namespace HowToSuck
{
    public sealed class IngestionSnapshot
    {
        public readonly string RunId,TypeId;
        public readonly ulong InstanceId;
        public readonly int IntakeId,PlayerId;
        public readonly long Value;
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
        public float Progress(double now) => Mathf.Clamp01((float)((now-StartedAt)/Duration));
        internal IngestionSnapshot(SuckableObject item,IntakeReceiver receiver,double now)
        {
            Item=item;Receiver=receiver;RunId=item.RunId;InstanceId=item.InstanceId;TypeId=item.Definition.TypeId;
            Value=item.Definition.Value;RequiredSize=item.Definition.RequiredIntakeSize;IntakeId=receiver.IntakeId;
            PlayerId=receiver.PlayerId;IsTruck=receiver.IsTruck;StartedAt=now;Duration=Mathf.Clamp(receiver.Duration,.35f,1.2f);
            StartPosition=item.VisualRoot.position;StartRotation=item.VisualRoot.rotation;StartScale=item.VisualRoot.localScale;
            TargetPosition=receiver.Position;TargetRotation=receiver.Rotation;EndPosition=receiver.EndPosition;
        }
    }
}