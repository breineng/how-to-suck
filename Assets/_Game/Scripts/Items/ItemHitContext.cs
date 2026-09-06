using UnityEngine;
namespace HowToSuck
{
    public interface IItemDamageReceiver
    {
        // Authority implementation must validate exact active item/shot/run + its active enemy state,
        // then commit InFlight -> Spent BEFORE changing HP, with no callback between those writes.
        bool TryApplyItemHit(in ItemHitContext hit);
    }
    public readonly struct ItemHitContext
    {
        public SuckableObject Item { get; }
        public LootKey Key { get; }
        public ulong ShotId { get; }
        public string RunId { get; }
        public int OwnerId { get; }
        public float RelativeSpeed { get; }
        public int Damage { get; }
        public Vector3 Point { get; }
        public double AuthorityTime { get; }
        internal ItemHitContext(SuckableObject item, ulong shot, int owner, float speed, int damage, Vector3 point, double now)
        { Item = item; Key = item.Key; ShotId = shot; RunId = item.RunId; OwnerId = owner; RelativeSpeed = speed; Damage = damage; Point = point; AuthorityTime = now; }
    }
}
