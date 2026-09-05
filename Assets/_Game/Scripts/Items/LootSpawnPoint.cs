using UnityEngine;

namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class LootSpawnPoint : MonoBehaviour
    {
        [Tooltip("Prefab with SuckableObject and a single Rigidbody on its root. This transform supplies its authored pose.")]
        public GameObject Prefab;
    }
}
