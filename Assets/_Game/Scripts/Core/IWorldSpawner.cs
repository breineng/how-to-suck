using UnityEngine;

namespace HowToSuck
{
    public interface IWorldSpawner
    {
        GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation);
        void Despawn(GameObject instance);
    }
}
