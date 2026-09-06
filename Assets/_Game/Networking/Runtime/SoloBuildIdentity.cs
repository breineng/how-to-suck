using UnityEngine;
namespace HowToSuck.Networking
{
    [CreateAssetMenu(menuName="How to Suck/Solo Build Identity",fileName="SoloBuildIdentity")]
    public sealed class SoloBuildIdentity : ScriptableObject
    {
        public bool Ready;
        public GameObject EntryRootPrefab;
        public string BuildId;
        public string ContentHash;
        public NetworkConfiguration Configuration()
        { if(!Ready)throw new System.InvalidOperationException("Solo authored identity is not finalized."); return NetworkConfiguration.ForSolo(BuildId,ContentHash); }
    }
}
