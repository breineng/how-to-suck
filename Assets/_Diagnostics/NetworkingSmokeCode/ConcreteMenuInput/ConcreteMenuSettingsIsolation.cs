#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using UnityEngine;
namespace HowToSuck.Diagnostics
{
    // Imported diagnostic only. Never added to any authored prefab or scene.
    [DefaultExecutionOrder(-30000), DisallowMultipleComponent]
    public sealed class ConcreteMenuSettingsIsolation : MonoBehaviour
    {
        public string DirectoryPath;
        public string ScopeToken;
        public static string ExpectedDirectory, ExpectedToken;
        public static event Action<ConcreteMenuSettingsIsolation> Initialized;
        public bool Accepted { get; private set; }
        void Awake()
        {
            try
            {
                var settings = GetComponent<LocalSettingsController>();
                if (string.IsNullOrEmpty(ExpectedToken) || ScopeToken != ExpectedToken ||
                    string.IsNullOrEmpty(DirectoryPath) || Path.GetFullPath(DirectoryPath) != ExpectedDirectory ||
                    settings == null || settings.IsInitialized)
                    throw new InvalidOperationException("Menu diagnostic did not own settings before Awake.");
                settings.Initialize(DirectoryPath);
                if (settings.StoragePath != Path.Combine(ExpectedDirectory, LocalSettingsRepository.FileName))
                    throw new InvalidOperationException("Menu diagnostic settings owner mismatch.");
                Accepted = true;
                Initialized?.Invoke(this);
            }
            catch
            {
                // Never allow an invalid diagnostic composition to progress into Startup.
                gameObject.SetActive(false);
                throw;
            }
        }
    }
}
#endif