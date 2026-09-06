using UnityEngine;

namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public GameCatalog Catalog;
        public SessionRoot Session;
        public MonoBehaviour SessionDriverProvider;
        public bool DeferInitialization; // Default false preserves existing authored/session fixtures.
        private void Awake()
        {
            if (DeferInitialization)
            {
                if (Session == null || Catalog == null || !(SessionDriverProvider is ISessionDriverProvider))
                    throw new System.InvalidOperationException("Deferred entry requires an explicit complete session provider.");
                DontDestroyOnLoad(gameObject);
                return; // No driver, World authority, save repository or Steam initialization before mode selection.
            }
            InitializeNow();
        }
        public void InitializeNow()
        {
            if (Session == null || Catalog == null)
            {
                Debug.LogError("Bootstrap needs its SessionRoot and GameCatalog references.", this);
                enabled = false;
                return;
            }
            if (Session.IsInitialized) return;
            // Discovery is limited to this composition boundary; gameplay has explicit references.
            foreach (var root in FindObjectsByType<SessionRoot>(FindObjectsSortMode.None))
                if (root != Session && root.IsInitialized)
                {
                    Destroy(gameObject);
                    return;
                }
            DontDestroyOnLoad(gameObject);
            if (SessionDriverProvider != null && !(SessionDriverProvider is ISessionDriverProvider))
                throw new System.InvalidOperationException("Invalid session driver provider.");
            if (DeferInitialization && !(SessionDriverProvider is ISessionDriverProvider))
                throw new System.InvalidOperationException("Deferred entry cannot fall back to a local driver.");
            var selected = SessionDriverProvider is ISessionDriverProvider provider ?
                provider.CreateDriver(Session) : new LocalSessionDriver(Session.World);
            Session.Initialize(Catalog, selected);
        }
    }
}
