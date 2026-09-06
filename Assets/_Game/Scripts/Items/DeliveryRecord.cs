namespace HowToSuck
{
    // Created only by the authority service after a truck transition to Delivered.
    public readonly struct DeliveryRecord
    {
        public readonly string RunId, TypeId, LastStorageTierId;
        public readonly ulong InstanceId;
        public readonly int IntakeId, PlayerId, LastStorageOwner;
        public readonly long Value;
        public readonly bool IsTruck;
        public readonly CargoRole CargoRole;
        public readonly BossKey BossKey;
        public LootKey Key => new LootKey(RunId, InstanceId);
        internal DeliveryRecord(IngestionSnapshot snapshot)
        {
            RunId = snapshot.RunId; TypeId = snapshot.TypeId; InstanceId = snapshot.InstanceId;
            IntakeId = snapshot.IntakeId; PlayerId = snapshot.PlayerId; Value = snapshot.Value; IsTruck = snapshot.IsTruck;
            CargoRole = snapshot.CargoRole; BossKey = snapshot.BossKey;
            LastStorageOwner = snapshot.LastStorageOwner; LastStorageTierId = snapshot.LastStorageTierId;
        }
    }
    public readonly struct StoredRecord
    {
        public readonly LootKey Key;
        public readonly string TypeId, TierId;
        public readonly int OwnerId;
        internal StoredRecord(IngestionSnapshot snapshot)
        { Key = new LootKey(snapshot.RunId, snapshot.InstanceId); TypeId = snapshot.TypeId; TierId = snapshot.TierId; OwnerId = snapshot.PlayerId; }
    }
}
