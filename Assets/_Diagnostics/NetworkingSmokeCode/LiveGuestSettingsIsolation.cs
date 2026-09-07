#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using UnityEngine;
namespace HowToSuck.Diagnostics {
 // Authored solely on the own diagnostic offline copy. Runs before LocalSettingsController.Awake.
 [DefaultExecutionOrder(-32000)] public sealed class LiveGuestSettingsIsolation:MonoBehaviour {
  private void Awake(){
   if(Array.IndexOf(Environment.GetCommandLineArgs(),"--hts-gameplay-nonce")<0)throw new InvalidOperationException("Explicit diagnostic arguments required");
   var settings=GetComponent<LocalSettingsController>();if(settings==null||settings.IsInitialized)throw new InvalidOperationException("Isolated settings must initialize first");
   string path=Path.Combine(CampaignStoragePaths.ResolveOwnDirectory(),"DiagnosticSettings");
   var repo=new LocalSettingsRepository(path);if(repo.Open().Kind==LocalSettingsOpenKind.Missing&&!repo.Save(new LocalSettingsData{AudioMigrationCompleted=true},out var e))throw new IOException(e);
   settings.Initialize(path);
  }
 }
}
#endif
