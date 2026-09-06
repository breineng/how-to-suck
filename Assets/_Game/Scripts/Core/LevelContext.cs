using UnityEngine;

namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class LevelContext : MonoBehaviour
    {
        public ContractDefinition Contract;
        public TruckIntake Truck;
        public ExtractionZone ExtractionZone;
        public WorldBoundsGuard BoundsGuard;

        // Derived from the authored list; never a second editable loot total.
        public long AvailableLootValue
        {
            get
            {
                long total = 0;
                foreach (var spawn in LootSpawns)
                    total = checked(total + spawn.Prefab.GetComponent<SuckableObject>().Definition.Value);
                return total;
            }
        }
        public GameObject PlayerPrefab;
        public LootSpawnPoint[] LootSpawns = System.Array.Empty<LootSpawnPoint>();
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

            var motor = PlayerPrefab != null ? PlayerPrefab.GetComponent<PlayerMotor>() : null;
            var reader = PlayerPrefab != null ? PlayerPrefab.GetComponent<PlayerInputReader>() : null;
            if (motor == null || reader == null || reader.Actions == null ||
                motor.AuthoritativeAim == null || motor.NozzleAnchor == null || motor.CameraPivot == null ||
                PlayerPrefab.GetComponent<VacuumEmitter>() == null)
            {
                error = "Location needs a complete player prefab with input, aim and vacuum references.";
                return false;
            }
            if (LootSpawns == null)
            {
                error = "Location loot spawn list is missing.";
                return false;
            }
            var authored = new System.Collections.Generic.HashSet<LootSpawnPoint>();
            foreach (var spawn in LootSpawns)
            {
                if (spawn == null || !authored.Add(spawn) || spawn.Prefab == null)
                {
                    error = "Location has a missing or duplicate loot spawn or prefab.";
                    return false;
                }
                var item = spawn.Prefab.GetComponent<SuckableObject>();
                if (item == null)
                {
                    error = "Loot prefab '" + spawn.Prefab.name + "' has no SuckableObject.";
                    return false;
                }
                if (!item.TryValidate(out error)) return false;
            }
            if (BoundsGuard == null) { error = "Location needs its world bounds guard."; return false; }
            if (!BoundsGuard.TryValidate(out error)) return false;
            if (Truck == null) { error = "Location needs its Suck Truck."; return false; }
            if (!Truck.TryValidate(out error)) return false;
            if (ExtractionZone == null || ExtractionZone.Area != Truck.ExtractionArea)
            { error = "Location must bind the truck's authored extraction area."; return false; }
            if (!ExtractionZone.TryValidate(out error)) return false;
            try
            {
                if (AvailableLootValue < Contract.Quota)
                { error = "Authored loot cannot meet this contract's quota."; return false; }
            }
            catch (System.OverflowException)
            { error = "Authored loot total exceeds the campaign money limit."; return false; }
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
