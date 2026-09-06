using UnityEngine;
namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class EnemySpawnPoint : MonoBehaviour
    {
        public GameObject Prefab;
        private void OnDrawGizmosSelected()
        { Gizmos.color=new Color(.85f,.45f,.2f);Gizmos.DrawWireSphere(transform.position,.4f);Gizmos.DrawLine(transform.position,transform.position+transform.forward); }
    }
}
