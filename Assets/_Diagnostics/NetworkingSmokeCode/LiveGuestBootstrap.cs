#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using HowToSuck.Networking;
using UnityEngine;
namespace HowToSuck.Diagnostics
{
 // Only the dedicated diagnostic scene / offline proxy prefab references this component.
 // Own inactive clone allows role and explicit local settings path before any production Awake.
 [DefaultExecutionOrder(-32000)]
 public sealed class LiveGuestBootstrap:MonoBehaviour {
  public SoloBuildIdentity Identity;public GameObject OfflineProxy;
  private void Awake(){GameObject holder=null,owned=null;try{
   if(Identity==null||Identity.EntryRootPrefab==null||!Identity.Ready)throw new InvalidOperationException("Reviewed current product identity required");
   if(FindObjectsByType<SessionRoot>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length!=0)throw new InvalidOperationException("Diagnostic composition requires no existing SessionRoot");
   // Lazy parser is fail-closed on any malformed slot/count/nonce/reports intent. No user path.
   var args=Environment.GetCommandLineArgs();if(Array.IndexOf(args,"--hts-gameplay-nonce")<0)throw new InvalidOperationException("Explicit standalone diagnostic arguments required; never auto-start in Editor");
   string own=CampaignStoragePaths.ResolveOwnDirectory();
   holder=new GameObject("LiveGuest inactive composition");holder.SetActive(false);
   owned=Instantiate(Identity.EntryRootPrefab,holder.transform,false);owned.SetActive(false);
   var settings=owned.GetComponent<LocalSettingsController>();if(settings==null||settings.IsInitialized)throw new InvalidOperationException("Settings must be uninitialized on inactive clone");
   string settingsPath=Path.Combine(own,"DiagnosticSettings");var repo=new LocalSettingsRepository(settingsPath);var opened=repo.Open();
   if(opened.Kind==LocalSettingsOpenKind.Missing&&!repo.Save(new LocalSettingsData{AudioMigrationCompleted=true},out var e))throw new IOException(e);
   // A valid own JSON avoids consulting legacy real-player PlayerPrefs migration keys.
   settings.Initialize(settingsPath);
    var entry=owned.GetComponent<SoloSessionStartup>();if(entry==null||OfflineProxy==null)throw new InvalidOperationException("Product entry and own offline proxy required");DestroyImmediate(entry);
    var game=owned.GetComponent<NgoGameSession>();var boot=owned.GetComponent<GameBootstrap>();
    var startup=owned.AddComponent<MppmGameplayStartup>();startup.Game=game;startup.BuildId=Identity.BuildId;startup.ContentHash=Identity.ContentHash;
    game.RoleSource=startup;game.OfflineMenuBootstrapPrefab=OfflineProxy;boot.DeferInitialization=true;
    owned.AddComponent<LiveGuestProbe>();var runner=owned.AddComponent<GameplayDiagnosticRunner>();runner.Game=game;runner.Startup=startup;
    owned.transform.SetParent(null,false);owned.SetActive(true);boot.InitializeNow();
   Destroy(holder);holder=null;owned=null;Destroy(gameObject);
  }catch(Exception error){if(owned!=null)Destroy(owned);if(holder!=null)Destroy(holder);Debug.LogException(error);throw;}}
 }
}
#endif
