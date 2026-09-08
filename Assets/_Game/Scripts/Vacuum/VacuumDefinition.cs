using UnityEngine;

namespace HowToSuck
{
    [CreateAssetMenu(menuName="How to Suck/Vacuum")]
    public sealed class VacuumDefinition : ScriptableObject
    {
        public string TierId="mk1";
        public string DisplayName="MK1";
        public float Power=180f;
        public float IntakeSize=.45f;
        public long Price;
        public GameObject ViewPrefab;
        public Sprite Preview;
        public bool TryValidate(out string error)
        {
            error=null;
            if(string.IsNullOrWhiteSpace(TierId)) error="Vacuum tier ID is missing.";
            else if(float.IsNaN(Power)||float.IsInfinity(Power)||Power<=0) error="Vacuum power must be positive and finite.";
            else if(float.IsNaN(IntakeSize)||float.IsInfinity(IntakeSize)||IntakeSize<=0) error="Vacuum intake must be positive and finite.";
            else if(Price<0) error="Vacuum price cannot be negative.";
            return error==null;
        }
    }
}
