using UnityEngine;
namespace HowToSuck
{
    // Optional extension of IWorldSpawner; LocalSessionDriver needs no changes.
    public interface IWorldSpawnCommitter
    {
        // Called after domain registration, RunId/InstanceId assignment and initial WorldFrozen=true.
        void CommitSpawn(GameObject initializedInstance);
    }
}
