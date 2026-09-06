using System;
using System.IO;
using UnityEngine;

namespace HowToSuck
{
    // One composition path for this Play/process lifetime, including a guest's fresh offline menu.
    public static class CampaignStoragePaths
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly DiagnosticCampaignPathState diagnostic = new DiagnosticCampaignPathState();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDiagnosticDirectory() { diagnostic.Reset(); }

        public static void InitializeDiagnosticArguments()
        {
            // Also called lazily by Resolve: another BeforeSceneLoad callback cannot race us into default storage.
            diagnostic.EnsureArguments(Environment.GetCommandLineArgs());
            diagnostic.ThrowIfBlocked();
        }
        public static void ConfigureDiagnosticDirectory(string absoluteDirectory)
        {
            InitializeDiagnosticArguments();
            var sessions = UnityEngine.Object.FindObjectsByType<SessionRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            bool initialized = sessions != null && Array.Exists(sessions, session => session != null && session.IsInitialized);
            diagnostic.Configure(absoluteDirectory, initialized);
        }
#endif
        public static string ResolveOwnDirectory()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            InitializeDiagnosticArguments();
            if (diagnostic.Intent) return diagnostic.Directory; // ThrowIfBlocked already rejected unset/invalid state.
#endif
            return Path.Combine(Application.persistentDataPath, "HowToSuck");
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Pure state/path validation. No Unity calls, filesystem reads, directory creation, or default-user-path lookup.
    internal sealed class DiagnosticCampaignPathState
    {
        public string Directory { get; private set; }
        public string Failure { get; private set; }
        public bool Intent { get; private set; }
        private bool inspected;
        private static readonly string[] Keys = { "--hts-gameplay-slot", "--hts-gameplay-count", "--hts-gameplay-nonce", "--hts-gameplay-reports" };
        public void Reset() { Directory = null; Failure = null; Intent = false; inspected = false; }
        public void EnsureArguments(string[] args)
        {
            ThrowIfFailed();
            if (inspected) return;
            inspected = true;
            if (args == null)
            {
                Intent = true; Failure = "Diagnostic process arguments are unavailable.";
                ThrowIfFailed();
            }
            if (!Array.Exists(args, value => Array.IndexOf(Keys, value) >= 0)) return;
            Intent = true; // Latch BEFORE any parse/path operation; errors cannot fall back to user storage.
            try
            {
                string Read(string key)
                {
                    string value = null; bool found = false;
                    for (int i = 0; i < args.Length; i++) if (args[i] == key)
                    {
                        if (found || i + 1 >= args.Length) throw new ArgumentException("Missing/duplicate " + key);
                        found = true; value = args[++i];
                        if (string.IsNullOrWhiteSpace(value) || Array.IndexOf(Keys, value) >= 0)
                            throw new ArgumentException("Missing value for " + key);
                    }
                    return found ? value : throw new ArgumentException("Required diagnostic isolation argument " + key);
                }
                string nonce = Read(Keys[2]);
                if (!Guid.TryParseExact(nonce, "N", out var guid) || guid == Guid.Empty || guid.ToString("N") != nonce ||
                    !int.TryParse(Read(Keys[0]), out int slot) || !int.TryParse(Read(Keys[1]), out int count) ||
                    (count != 2 && count != 4) || slot < 0 || slot >= count)
                    throw new ArgumentException("Invalid diagnostic campaign identity.");
                string reports = FullyQualified(Read(Keys[3]));
                string requested = Path.Combine(reports, nonce, "campaign-slot-" + slot);
                SetDirectory(requested, false); // Exact process args are immutable; safe even on first lazy Resolve inside Initialize.
            }
            catch (Exception error) { Failure = Failure ?? error.Message; throw; }
        }
        public void Configure(string absoluteDirectory, bool hasInitializedSession)
        {
            Intent = true;
            ThrowIfFailed();
            try { SetDirectory(absoluteDirectory, hasInitializedSession); }
            catch (Exception error) { Failure = Failure ?? error.Message; throw; }
        }
        private void SetDirectory(string requested, bool hasInitializedSession)
        {
            string resolved = FullyQualified(requested);
            if (Directory != null)
            {
                if (!string.Equals(Directory, resolved, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("One diagnostic campaign directory per Play/process lifetime.");
                return; // Exact idempotent revalidation never changes an initialized session's composition.
            }
            if (hasInitializedSession) throw new InvalidOperationException("Configure storage before any session initialization.");
            Directory = resolved;
        }
        private static string FullyQualified(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                throw new ArgumentException("A fully qualified diagnostic campaign directory is required.");
            return Path.GetFullPath(path);
        }
        private void ThrowIfFailed()
        {
            if (Failure != null) throw new InvalidOperationException("Diagnostic campaign storage is blocked for this lifetime: " + Failure);
        }
        public void ThrowIfBlocked()
        {
            ThrowIfFailed();
            if (Intent && Directory == null) throw new InvalidOperationException("Diagnostic campaign directory has not been configured.");
        }
    }
#endif
}