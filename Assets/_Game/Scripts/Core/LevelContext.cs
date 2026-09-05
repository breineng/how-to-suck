using UnityEngine;

namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class LevelContext : MonoBehaviour
    {
        public ContractDefinition Contract;
        public GameObject PlayerPrefab;
        public Transform[] PlayerSpawns = new Transform[4];

        private bool showBootstrapHint;

        public bool TryValidate(out string error)
        {
            if (Contract == null)
            {
                error = $"Level '{gameObject.scene.name}' is missing its contract definition.";
                return false;
            }

            if (!Contract.TryValidate(out error))
                return false;

            string expected = System.IO.Path.GetFileNameWithoutExtension(Contract.SceneName);
            if (!string.Equals(gameObject.scene.name, expected, System.StringComparison.Ordinal))
            {
                error = $"Level '{gameObject.scene.name}' references contract scene '{Contract.SceneName}'.";
                return false;
            }

            if (PlayerSpawns == null || PlayerSpawns.Length != 4)
            {
                error = $"Level '{gameObject.scene.name}' needs exactly four player spawn transforms.";
                return false;
            }

            for (int i = 0; i < PlayerSpawns.Length; i++)
            {
                if (PlayerSpawns[i] == null)
                {
                    error = $"Level '{gameObject.scene.name}' player spawn {i + 1} is missing.";
                    return false;
                }

                for (int j = 0; j < i; j++)
                {
                    if (PlayerSpawns[j] == PlayerSpawns[i])
                    {
                        error = $"Level '{gameObject.scene.name}' player spawns {j + 1} and {i + 1} reference the same transform.";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        private void Start()
        {
            // The only scene lookup is a launch-boundary diagnostic, never service resolution.
            showBootstrapHint = FindFirstObjectByType<SessionRoot>() == null;
            if (showBootstrapHint)
                Debug.LogWarning("How to Suck: open Assets/_Game/Scenes/Bootstrap.unity and start Play there. Gameplay scenes require the existing session root.", this);
        }

        private void OnGUI()
        {
            if (!showBootstrapHint)
                return;

            float width = Mathf.Min(640f, Screen.width - 32f);
            GUI.Box(new Rect((Screen.width - width) * 0.5f, 24f, width, 90f),
                "How to Suck\nОткройте сцену Bootstrap и запустите игру оттуда.\nЭта комната запускается через главное меню.");
        }
    }
}
