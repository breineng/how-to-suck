using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HowToSuck.Networking.Editor
{
    // Client config is injected directly into this build output, never into a tracked scene or prefab.
    public sealed class EosBuildConfiguration : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 500;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64) return;
            if (OnlineServicesSettings.SelectedBackend != OnlineBackend.EpicOnlineServices) return;
            if (!File.Exists(EosClientConfiguration.LocalPath))
            { Debug.LogWarning("EOS client settings are absent. This build supports Solo; online rooms remain unavailable until eos-client.json is supplied."); return; }
            if (!EosClientConfiguration.TryLoad(out _, out _))
                throw new BuildFailedException("Invalid .local/eos-client.json. Check the EOS setup guide; credential values are not logged.");
        }
        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64 ||
                OnlineServicesSettings.SelectedBackend != OnlineBackend.EpicOnlineServices || !File.Exists(EosClientConfiguration.LocalPath)) return;
            if (!EosClientConfiguration.TryLoad(out var config, out _)) throw new BuildFailedException("EOS client settings changed during the build.");
            string exe = report.summary.outputPath;
            string destination = Path.Combine(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + "_Data", "StreamingAssets");
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, EosClientConfiguration.FileName), JsonUtility.ToJson(config, true));
        }
        [MenuItem("How to Suck/Networking/Select Epic Online Services")]
        private static void SelectEpic() => Select(OnlineBackend.EpicOnlineServices);
        [MenuItem("How to Suck/Networking/Select Steam")]
        private static void SelectSteam() => Select(OnlineBackend.Steam);
        private static void Select(OnlineBackend backend)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode before changing the network provider.");
            var settings = Resources.Load<OnlineServicesSettings>("OnlineServicesSettings");
            if (settings == null) throw new InvalidOperationException("OnlineServicesSettings asset is missing.");
            Undo.RecordObject(settings, "Select network provider"); settings.Backend = backend;
            EditorUtility.SetDirty(settings); AssetDatabase.SaveAssetIfDirty(settings); Selection.activeObject = settings;
            Debug.Log("Network provider: " + backend + ". Rebuild both peers after changing the provider.");
        }
    }
}
