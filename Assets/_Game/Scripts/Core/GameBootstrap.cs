using UnityEngine;

namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public GameCatalog Catalog;
        public SessionRoot Session;
        private void Awake()
        {
            if (Session == null || Catalog == null)
            {
                Debug.LogError("Bootstrap needs its SessionRoot and GameCatalog references.", this);
                enabled = false;
                return;
            }
            // Discovery is limited to this composition boundary; gameplay has explicit references.
            foreach (var root in FindObjectsByType<SessionRoot>(FindObjectsSortMode.None))
                if (root != Session && root.IsInitialized)
                {
                    Destroy(gameObject);
                    return;
                }
            DontDestroyOnLoad(gameObject);
            Session.Initialize(Catalog, new LocalSessionDriver(Session.World));
        }
    }
}
