using UnityEngine;

namespace HowToSuck
{
    [CreateAssetMenu(menuName = "How to Suck/Contract", fileName = "Contract")]
    public sealed class ContractDefinition : ScriptableObject
    {
        [Tooltip("Stable authored identifier. Do not change after the contract is published.")]
        public string ContractId;
        public string DisplayName;
        [Tooltip("Scene name or build-settings scene path, without the .unity extension.")]
        public string SceneName;
        public long Quota = 300;
        public float TimeLimitSeconds = 120f;
        [Range(0, 100)] public int FailurePercent = 25;

        public bool TryValidate(out string error)
        {
            if (string.IsNullOrWhiteSpace(ContractId) || ContractId != ContractId.Trim())
                return Fail("Contract ID must be non-empty and contain no leading or trailing whitespace.", out error);

            if (string.IsNullOrWhiteSpace(DisplayName))
                return Fail($"Contract '{ContractId}' needs a display name.", out error);

            if (Quota <= 0)
                return Fail($"Contract '{ContractId}' must have a positive quota.", out error);

            if (float.IsNaN(TimeLimitSeconds) || float.IsInfinity(TimeLimitSeconds) || TimeLimitSeconds <= 0f)
                return Fail($"Contract '{ContractId}' must have a finite positive time limit.", out error);

            if (FailurePercent < 0 || FailurePercent > 100)
                return Fail($"Contract '{ContractId}' failure payout must be between 0 and 100 percent.", out error);

            if (string.IsNullOrWhiteSpace(SceneName) || !Application.CanStreamedLevelBeLoaded(SceneName))
                return Fail($"Contract '{ContractId}' scene '{SceneName}' cannot be loaded. Add its scene to the enabled build scenes.", out error);

            error = null;
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
