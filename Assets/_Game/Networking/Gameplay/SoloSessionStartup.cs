using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace HowToSuck.Networking
{
    [DisallowMultipleComponent]
    public sealed class SoloSessionStartup : MonoBehaviour, IPreparedSessionRoleSource, ISessionMenuExit
    {
        public GameBootstrap Bootstrap;
        public NgoGameSession Game;
        public SoloBuildIdentity Identity;
        public string EntrySceneName="ProductEntry";
        private readonly SoloEntryAttempt attempt=new SoloEntryAttempt();
        private bool entryReady;
        private string notice="";
        public event Action Changed;
        public SoloEntryPhase Phase=>attempt.Phase;
        public string Status=>notice;
        public bool CanStartSolo=>entryReady&&attempt.Phase==SoloEntryPhase.Choosing&&!Bootstrap.Session.IsInitialized&&Game.Driver==null&&
            !Game.Manager.IsListening&&!Game.Manager.ShutdownInProgress;
        public bool CanQuit=>attempt.Phase==SoloEntryPhase.Choosing||attempt.Phase==SoloEntryPhase.Failed;
        public PreparedSessionRole ResolveRole()
        {
            if(!attempt.Selected||attempt.Phase!=SoloEntryPhase.Starting||Bootstrap==null||!Bootstrap.DeferInitialization)
                throw new InvalidOperationException("Select Solo before creating the session and campaign repository.");
            return PreparedSessionRole.Authority;
        }
        public static bool IsDeferredEntry(GameObject prefab)
        {
            if(prefab==null)return false;
            var entry=prefab.GetComponent<SoloSessionStartup>();var boot=prefab.GetComponent<GameBootstrap>();var game=prefab.GetComponent<NgoGameSession>();
            return entry!=null&&boot!=null&&game!=null&&boot.DeferInitialization&&boot.SessionDriverProvider==game&&entry.Bootstrap==boot&&entry.Game==game&&
                entry.Identity!=null&&game.RoleSource==entry&&game.Manager!=null&&game.Connection!=null&&game.Connection.SteamTransport==null&&
                game.GetComponent<HowToSuckSteamTransport>()==null&&game.SessionPrefab!=null&&boot.Session!=null&&boot.Catalog!=null&&
                !string.IsNullOrWhiteSpace(entry.EntrySceneName);
        }
        private IEnumerator Start()
        {
            try
            {
                // A prefab's direct self-reference remaps to its live instance.
                // Keep the next root in a separate asset so destruction cannot invalidate it.
                Game.OfflineMenuBootstrapPrefab=Identity.EntryRootPrefab;
                if(!IsDeferredEntry(gameObject)||Bootstrap.Session.World==null||Bootstrap.Session.gameObject!=gameObject||Bootstrap.Session.World.gameObject!=gameObject||Game.Manager.gameObject!=gameObject||Game.Connection.gameObject!=gameObject||
                    Game.Connection.Manager!=Game.Manager||Game.Connection.Loopback==null||Bootstrap.Session.IsInitialized||Game.Driver!=null||
                    !IsDeferredEntry(Game.OfflineMenuBootstrapPrefab))throw new InvalidOperationException("Incomplete deferred Solo composition.");
                Identity.Configuration(); // Real identity must validate before any mode, driver, repository or Steam is opened.
                if(!Application.CanStreamedLevelBeLoaded(EntrySceneName))throw new InvalidOperationException("Entry scene is not included in the build.");
                Game.Connection.Changed+=Refresh;
            }
            catch(Exception error){FailBeforeSession("Не удалось открыть главное меню.",error);yield break;}
            if(SceneManager.GetActiveScene().name!=EntrySceneName)
            {
                var load=SceneManager.LoadSceneAsync(EntrySceneName,LoadSceneMode.Single);
                if(load==null){FailBeforeSession("Не удалось открыть главное меню.",null);yield break;}
                yield return load;
            }
            if(SceneManager.GetActiveScene().name!=EntrySceneName){FailBeforeSession("Не удалось открыть главное меню.",null);yield break;}
            entryReady=true;Refresh();
        }
        public bool StartSolo()
        {
            if(!CanStartSolo||!attempt.TrySelect())return false;
            notice="Открываем кампанию…";Refresh();
            try
            {
                // Selection and configuration precede CreateDriver and SessionRoot.Initialize.
                Game.Connection.Configure(Identity.Configuration());
                Bootstrap.InitializeNow();
                if(!Bootstrap.Session.IsInitialized||!Game.HasAuthority||Game.Role!=PreparedSessionRole.Authority||Game.Driver==null)
                    throw new InvalidOperationException("Solo authority was not established before session initialization.");
                Bootstrap.Session.Changed+=Refresh;
            }
            catch(Exception error)
            {
                FailBeforeSession("Не удалось начать одиночную игру.",error);
                if(Game.Driver!=null)Game.Connection.StopUnexpected(notice);
                return false;
            }
            StartCoroutine(ConnectSolo());return true;
        }
        private IEnumerator ConnectSolo()
        {
            if(!Game.Connection.StartSolo())
            {notice="Не удалось начать одиночную игру.";attempt.Fail();Refresh();yield break;}
            double deadline=Time.realtimeSinceStartupAsDouble+10;
            while(Game.Connection.Phase!=ConnectionPhase.Lobby||!Game.Manager.IsConnectedClient||Game.Manager.ConnectedClientsIds.Count!=1||Game.Connection.ConnectedPlayerCount!=1)
            {
                if(Game.IsStopping)yield break;
                if(Time.realtimeSinceStartupAsDouble>=deadline)
                {notice="Не удалось завершить запуск одиночной игры.";attempt.Fail();Refresh();Game.Connection.StopUnexpected(notice);yield break;}
                yield return null;
            }
            try
            {
                Game.AttachConnectedGame();attempt.Connected();notice="";Refresh();
                // The existing shared scene barrier opens the lobby; existing SaveRecovery controls stay authoritative.
            }
            catch(Exception error)
            {FailBeforeSession("Не удалось открыть кампанию.",error);Game.Connection.StopUnexpected(notice);}
        }
        public bool CanExitSessionMenu(SessionRoot session)=>session==Bootstrap.Session&&attempt.Phase==SoloEntryPhase.Connected&&
            session.Phase==SessionPhase.Lobby&&!session.HasPendingSave&&!Game.IsStopping&&Game.Connection.Mode==ConnectionMode.SoloLoopback;
        public bool ExitSessionMenu(SessionRoot session)
        {
            if(!CanExitSessionMenu(session))return false;
            if(!Game.ReturnSoloToEntry())return false;
            attempt.Returning();notice="Возвращаемся в главное меню…";Refresh();return true;
        }
        public void AcceptReturnNotice(string value){notice=value??"";Refresh();}
        public void Quit(){if(CanQuit)Application.Quit();}
        private void FailBeforeSession(string message,Exception error)
        {attempt.Fail();notice=message;Refresh();if(error!=null)Debug.LogException(error,this);}
        private void Refresh()=>Changed?.Invoke();
        private void OnDestroy()
        {
            if(Game!=null&&Game.Connection!=null)Game.Connection.Changed-=Refresh;
            if(Bootstrap!=null&&Bootstrap.Session!=null)Bootstrap.Session.Changed-=Refresh;
            Changed=null;
        }
    }
}
