using UnityEngine;
namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class ToolTierView : MonoBehaviour
    {
        public string TierId;
        public ToolTierMountProfile MountProfile;
        public Transform Mouth,EndPoint;
    }
}
