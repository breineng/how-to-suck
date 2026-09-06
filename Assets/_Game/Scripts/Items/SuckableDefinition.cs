using UnityEngine;

namespace HowToSuck
{
    [CreateAssetMenu(menuName = "How to Suck/Item", fileName = "Item")]
    public sealed class SuckableDefinition : ScriptableObject
    {
        [Tooltip("Stable authored type identifier shared by all instances of this item.")]
        public string TypeId;
        public string DisplayName;
        public long Value = 10;
        public CargoRole CargoRole = CargoRole.OrdinaryLoot;
        [Min(0.001f)] public float RequiredIntakeSize = 0.12f;
        public bool CanBeSwallowedByPlayer = true;
        public bool CanBeSwallowedByTruck = true;
        [Tooltip("Authoring category only; it never controls suction or admission.")]
        public string Category;

        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(TypeId) || TypeId != TypeId.Trim())
                error = "Item type ID must be non-empty and contain no leading or trailing whitespace.";
            else if (string.IsNullOrWhiteSpace(DisplayName))
                error = $"Item '{TypeId}' needs a display name.";
            else if (CargoRole != CargoRole.OrdinaryLoot && CargoRole != CargoRole.BossBody)
                error = "Unknown cargo role.";
            else if (CargoRole == CargoRole.OrdinaryLoot ? Value <= 0 : Value != 0)
                error = $"Item '{TypeId}' needs positive ordinary value or exactly zero boss-body value.";
            else if (float.IsNaN(RequiredIntakeSize) || float.IsInfinity(RequiredIntakeSize) || RequiredIntakeSize <= 0f)
                error = $"Item '{TypeId}' needs a finite positive intake size.";
            else
            {
                error = null;
                return true;
            }
            return false;
        }
    }
}
