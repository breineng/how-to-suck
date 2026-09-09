using System;
using System.Collections;
using System.Collections.Generic;
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
        public SteamEntryConfiguration SteamConfiguration; // Optional. Missing means Solo-only, with no Steam initialization.
        public string EntrySceneName="ProductEntry";
        private readonly ProductEntryChoice attempt=new ProductEntryChoice();
        private bool connectionConfigured;
        private readonly List<SteamLobbyCandidate> friends=new List<SteamLobbyCandidate>();
        private ulong pendingInvitation;
        public IReadOnlyList<SteamLobbyCandidate> FriendLobbies=>friends.AsReadOnly();
        public bool HasPendingInvitation=>pendingInvitation!=0;
        public bool IsBrowsingCoop=>attempt.CanChooseSteam;
        public bool CanBeginCoop=>FreshChoice&&(attempt.CanChooseSolo||attempt.CanChooseSteam)&&TrySteamConfiguration(out _,out _);
        public string CoopStatus {get {TrySteamConfiguration(out _,out var status);return status;}}
        public bool CanCancelEntry=>entryReady&&!Game.IsStopping&&
            (attempt.Browsing&&!attempt.Selected||attempt.Phase==SoloEntryPhase.Starting||attempt.Phase==SoloEntryPhase.Failed);
        private bool FreshChoice=>entryReady&&isActiveAndEnabled&&Bootstrap!=null&&Bootstrap.Session!=null&&!Bootstrap.Session.IsInitialized&&
            Game!=null&&Game.Driver==null&&!Game.Manager.IsListening&&!Game.Manager.ShutdownInProgress;
        private bool entryReady;
        private string notice="";
        public event Action Changed;
        public SoloEntryPhase Phase=>attempt.Phase;
        public string Status=>notice;
        public bool CanStartSolo=>FreshChoice&&attempt.CanChooseSolo&&!connectionConfigured;
        public bool CanQuit=>attempt.Phase==SoloEntryPhase.Choosing||attempt.Phase==SoloEntryPhase.Failed;
        public PreparedSessionRole ResolveRole()
        {
            if(!attempt.Selected||attempt.Phase!=SoloEntryPhase.Starting||Bootstrap==null||!Bootstrap.DeferInitialization)
                throw new InvalidOperationException("Select the session role before creating the driver and campaign repository.");
            return attempt.ResolveGuest()?PreparedSessionRole.Guest:PreparedSessionRole.Authority;
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
                Game.Connection.FriendLobbyFound+=FoundFriend;
                Game.Connection.InviteAvailable+=Invitation;
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
            if(!CanStartSolo||!attempt.TrySelect(ProductEntryMode.Solo))return false;
            notice="Открываем кампанию…";Refresh();
            try
            {
                // Selection and configuration precede CreateDriver and SessionRoot.Initialize.
                Game.Connection.Configure(Identity.Configuration());connectionConfigured=true;
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
            {notice="Не удалось начать одиночную игру.";attempt.Fail();Refresh();Game.Connection.StopUnexpected(notice);yield break;}
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
            session.Phase==SessionPhase.Lobby&&!session.HasPendingSave&&!Game.IsStopping&&
            (Game.Connection.Mode==ConnectionMode.SoloLoopback||Game.Connection.Mode==ConnectionMode.SteamHost||Game.Connection.Mode==ConnectionMode.SteamClient);
        public bool ExitSessionMenu(SessionRoot session)
        {
            if(!CanExitSessionMenu(session))return false;
            if(!Game.ReturnProductLobbyToEntry())return false;
            attempt.TryReturn();notice="Возвращаемся в главное меню…";Refresh();return true;
        }

        private bool TrySteamConfiguration(out NetworkConfiguration configuration,out string status)
        {
            configuration=null;
            if(SteamConfiguration==null)
            {status="Совместная игра ещё не настроена для этой сборки. Одиночная игра доступна.";return false;}
            return SteamConfiguration.TryConfiguration(Identity,out configuration,out status);
        }
        // No API initialization, repository, world or scene work merely by opening this choice.
        public bool BeginCoop()
        {
            if(!FreshChoice)return false;
            if(attempt.CanChooseSteam)return connectionConfigured;
            if(!attempt.CanChooseSolo||connectionConfigured)return false;
            if(!TrySteamConfiguration(out var config,out notice)){Refresh();return false;}
            if(!attempt.TryBrowse())return false;
            try
            {
                if(Game.Connection.SteamTransport!=null||GetComponent<HowToSuckSteamTransport>()!=null)
                    throw new InvalidOperationException("A fresh entry must not own an earlier Steam transport.");
                Game.Connection.SteamTransport=gameObject.AddComponent<HowToSuckSteamTransport>();
                Game.Connection.Configure(config);connectionConfigured=true;
                notice="Создайте игру или выберите игру друга.";Refresh();return true;
            }
            catch(Exception error){FailBeforeSession("Не удалось подготовить совместную игру. Вернитесь в главное меню.",error);return false;}
        }
        public bool RefreshFriendLobbies()
        {
            if(!FreshChoice||!attempt.CanChooseSteam||!connectionConfigured)return false;
            friends.Clear();pendingInvitation=0;Refresh();
            try
            {
                bool started=Game.Connection.DiscoverSteamFriends();
                notice=started?"Ищем доступные игры друзей…":Game.Connection.LastError??"Не удалось найти игры друзей. Проверьте Steam.";
                Refresh();return started;
            }
            catch(Exception error){FailBeforeSession("Не удалось найти игры друзей. Вернитесь в главное меню.",error);return false;}
        }
        private void FoundFriend(SteamLobbyCandidate value)
        {
            if(!FreshChoice||!attempt.CanChooseSteam||value.LobbyId==0||value.HostSteamId==0)return;
            for(int i=0;i<friends.Count;i++)if(friends[i].LobbyId==value.LobbyId){friends[i]=value;Refresh();return;}
            if(friends.Count<128)friends.Add(value);
            notice="Выберите игру друга или создайте свою.";Refresh();
        }
        private void Invitation(ulong lobby)
        {
            // A received invitation never switches a running session or changes the prepared role.
            if(!FreshChoice||!attempt.CanChooseSteam||lobby==0)return;
            pendingInvitation=lobby;notice="Пришло приглашение в игру. Подключиться можно после подтверждения.";Refresh();
        }
        public bool AcceptPendingInvitation()=>pendingInvitation!=0&&JoinSteamLobby(pendingInvitation);
        public bool StartSteamHost()=>StartSteam(ProductEntryMode.SteamHost,0);
        public bool JoinSteamLobby(ulong lobby)
        {
            if(lobby==0)return false;
            bool offered=lobby==pendingInvitation;
            foreach(var value in friends)if(value.LobbyId==lobby){offered=true;break;}
            // Discovery is only a UI hint: SteamLobbyService requests and revalidates exact current metadata again.
            return offered&&StartSteam(ProductEntryMode.SteamGuest,lobby);
        }
        private bool StartSteam(ProductEntryMode mode,ulong lobby)
        {
            if(!BeginCoop()||!attempt.TrySelect(mode))return false;
            friends.Clear();pendingInvitation=0;
            notice=mode==ProductEntryMode.SteamHost?"Открываем вашу кампанию…":"Подключаемся к игре…";Refresh();
            try
            {
                Bootstrap.InitializeNow(); // ResolveRole has already committed Host/Guest; guest never opens a campaign.
                bool guest=mode==ProductEntryMode.SteamGuest;
                if(!Bootstrap.Session.IsInitialized||Game.Driver==null||Game.HasAuthority==guest||
                    Game.Role!=(guest?PreparedSessionRole.Guest:PreparedSessionRole.Authority)||
                    guest&&(Bootstrap.Session.Campaign!=null||Bootstrap.Session.Progression!=null))
                    throw new InvalidOperationException("Prepared role/repository ownership mismatch.");
                Bootstrap.Session.Changed+=Refresh;
                bool started=guest?Game.Connection.JoinSteamLobby(lobby):Game.Connection.CreateSteamLobby();
                if(!started)
                {
                    notice=Game.Connection.LastError??"Не удалось подключиться к совместной игре.";
                    attempt.Fail();Refresh();Game.Connection.StopUnexpected(notice);return false;
                }
                StartCoroutine(ConnectSteam());return true;
            }
            catch(Exception error)
            {
                FailBeforeSession("Не удалось открыть совместную игру.",error);
                if(Game.Driver!=null)Game.Connection.StopUnexpected(notice);
                return false;
            }
        }
        private IEnumerator ConnectSteam()
        {
            // Existing Steam lobby timeout and connection deadline remain independently active.
            double deadline=Time.realtimeSinceStartupAsDouble+45;
            while(Game.Connection.Phase!=ConnectionPhase.Lobby||!Game.Manager.IsConnectedClient)
            {
                if(Game.IsStopping||attempt.Phase!=SoloEntryPhase.Starting)yield break;
                if(Time.realtimeSinceStartupAsDouble>=deadline)
                {notice="Не удалось завершить подключение. Возвращаемся в главное меню.";attempt.Fail();Refresh();Game.Connection.StopUnexpected(notice);yield break;}
                yield return null;
            }
            if(Game.IsStopping||attempt.Phase!=SoloEntryPhase.Starting)yield break;
            try
            {
                Game.AttachConnectedGame();attempt.Connected();notice="";Refresh();
                // Existing NGO scene synchronization/control snapshot opens the lobby for each role.
            }
            catch(Exception error){FailBeforeSession("Не удалось открыть лобби.",error);Game.Connection.StopUnexpected(notice);}
        }
        public bool SetLobbyReady(bool ready)
        {
            if(attempt.Phase!=SoloEntryPhase.Connected||Game.IsStopping||Game.Session.Phase!=SessionPhase.Lobby||
                Game.Control==null||!Game.Control.IsSpawned||!Game.Control.HasAcceptedCurrentSnapshot)return false;
            Game.SetLocalReady(ready);return true; // Request accepted for sending; LocalReady updates only after the authority accepts it.
        }
        public bool CanInviteToLobby=>attempt.Phase==SoloEntryPhase.Connected&&!Game.IsStopping&&
            (Game.Connection.Mode==ConnectionMode.SteamHost||Game.Connection.Mode==ConnectionMode.SteamClient)&&
            Game.Session.Phase==SessionPhase.Lobby&&Game.Connection.Phase==ConnectionPhase.Lobby&&
            Game.Connection.ActiveSteamRuntime?.Initialized==true&&Game.Connection.Lobby!=null&&Game.Connection.Lobby.LobbyId!=0&&
            Game.Control!=null&&Game.Control.IsSpawned&&Game.Control.HasAcceptedCurrentSnapshot&&Game.Control.Roster.Count<4;
        public bool OpenInviteOverlay()
        {
            if(!CanInviteToLobby)return false;
            return Game.Connection.Lobby.InviteFriendsOverlay();
        }
        public bool InviteFriend(ulong steamId)=>CanInviteToLobby&&Game.Connection.Lobby.InviteFriend(steamId);
        public bool CancelEntry()
        {
            if(!CanCancelEntry)return false;
            if(Game.Driver!=null)
            {
                if(!Game.CancelProductEntryStart())return false;
                attempt.TryReturn();notice="Возвращаемся в главное меню…";Refresh();return true;
            }
            if(Bootstrap.Session.IsInitialized||!attempt.TryReturn())return false;
            entryReady=false;notice="Возвращаемся в главное меню…";friends.Clear();pendingInvitation=0;Refresh();
            if(connectionConfigured)Game.Connection.StopSession();
            StartCoroutine(ReturnUninitializedEntry());return true;
        }
        private IEnumerator ReturnUninitializedEntry()
        {
            // Browsing has no NGO/native shared scene operation. Only this owned manager may be drained/destroyed.
            double deadline=Time.realtimeSinceStartupAsDouble+7;
            while((Game.Manager.IsListening||Game.Manager.ShutdownInProgress)&&Time.realtimeSinceStartupAsDouble<deadline)yield return null;
            if(Game.Manager.IsListening||Game.Manager.ShutdownInProgress)
            {notice="Не удалось завершить соединение. Перезапустите игру.";Refresh();yield break;}
            yield return null;
            if(!IsDeferredEntry(Identity.EntryRootPrefab))
            {notice="Не удалось восстановить главное меню. Перезапустите игру.";Refresh();yield break;}
            var relay=new GameObject("Product entry return");DontDestroyOnLoad(relay);
            relay.AddComponent<OfflineMenuReturn>().Begin(gameObject,Identity.EntryRootPrefab);
        }
        public void AcceptReturnNotice(string value){notice=value??"";Refresh();}
        public void Quit(){if(CanQuit)Application.Quit();}
        private void FailBeforeSession(string message,Exception error)
        {attempt.Fail();notice=message;Refresh();if(error!=null)Debug.LogException(error,this);}
        private void Refresh()
        {
            var handlers=Changed;
            if(handlers!=null)foreach(Action handler in handlers.GetInvocationList())
                try{handler();}catch(Exception error){Debug.LogWarning("Entry view failed: "+error.GetType().Name,this);}
        }
        private void OnDestroy()
        {
            if(Game!=null&&Game.Connection!=null)
            {Game.Connection.Changed-=Refresh;Game.Connection.FriendLobbyFound-=FoundFriend;Game.Connection.InviteAvailable-=Invitation;}
            if(Bootstrap!=null&&Bootstrap.Session!=null)Bootstrap.Session.Changed-=Refresh;
            Changed=null;
        }
    }
}
