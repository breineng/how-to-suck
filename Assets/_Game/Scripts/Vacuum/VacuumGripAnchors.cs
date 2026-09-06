using UnityEngine;
namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class VacuumGripAnchors : MonoBehaviour
    {
        public Transform Left, Right;
        public Quaternion LeftHandRotation=Quaternion.identity, RightHandRotation=Quaternion.identity;
    }
}