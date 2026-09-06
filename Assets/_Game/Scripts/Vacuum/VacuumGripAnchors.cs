using UnityEngine;
namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class VacuumGripAnchors : MonoBehaviour
    {
        public Transform Left, Right;
        public bool RegripDReady;
        // Reference physical bar centres/axes in this semantic tool-root space; final Left marker already shifted.
        public Vector3 LeftBarCentre,RightBarCentre,LeftBarAxis,RightBarAxis;
        public Quaternion LeftHandRotation=Quaternion.identity, RightHandRotation=Quaternion.identity;
    }
}