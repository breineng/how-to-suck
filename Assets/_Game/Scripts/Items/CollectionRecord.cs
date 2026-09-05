namespace HowToSuck
{
    public readonly struct CollectionRecord
    {
        public readonly string RunId, TypeId;
        public readonly ulong InstanceId;
        public readonly int IntakeId, PlayerId;
        public readonly long Value;
        public readonly bool IsTruck;
        public CollectionRecord(IngestionSnapshot snapshot)
        {
            RunId=snapshot.RunId;TypeId=snapshot.TypeId;InstanceId=snapshot.InstanceId;
            IntakeId=snapshot.IntakeId;PlayerId=snapshot.PlayerId;Value=snapshot.Value;IsTruck=snapshot.IsTruck;
        }
    }
}