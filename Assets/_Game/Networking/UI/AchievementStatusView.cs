using System;
using TMPro;
using UnityEngine;
namespace HowToSuck.Networking
{
    public readonly struct AchievementStatusCopy
    {
        public readonly string Count,Context,Issue;
        public AchievementStatusCopy(string count,string context,string issue){Count=count;Context=context;Issue=issue;}
        // Plain messages deliberately never display service exceptions, file paths, numeric account IDs or API names.
        public static AchievementStatusCopy Compose(bool known,int count,bool openFailed,bool pendingSave,bool saveIssue,
            bool productSteam,bool accountBound,int pendingSteam,bool platformBlocked,bool syncIssue)
        {
            if(!known)return new AchievementStatusCopy("Достижения —/8",openFailed?"История пока недоступна":"Загружаем историю…",openFailed?"Перезапустите игру,\nчтобы повторить загрузку.":"");
            if(count<0||count>8||pendingSteam<0||pendingSteam>8)throw new ArgumentOutOfRangeException(nameof(count));
            string context=productSteam&&accountBound?"История связана со Steam":"Прогресс на этом устройстве";
            string issue="";
            if(pendingSave)issue=saveIssue?"Проверьте место на диске.\nСохранение повторится само.":"Сохраняем достижения…";
            else if(productSteam&&accountBound&&pendingSteam>0&&platformBlocked)issue="Для повтора синхронизации\nперезапустите игру.";
            else if(productSteam&&accountBound&&pendingSteam>0&&syncIssue)issue="Нет связи со Steam.\nПовторим отправку автоматически.";
            return new AchievementStatusCopy("Достижения "+count+"/8",context,issue);
        }
    }
    public enum AchievementStatusPlace { Lobby, Results }
    [DisallowMultipleComponent,RequireComponent(typeof(CanvasGroup))]
    public sealed class AchievementStatusView:MonoBehaviour
    {
        public TMP_Text CountText,ContextText,IssueText;
        public AchievementStatusPlace Place;
        public SessionMenuView LobbyMenu;
        public bool AutoBind=true;
        private AchievementSessionBridge bridge;
        private NetworkConnectionCoordinator connection;
        private SessionRoot session;
        private CanvasGroup group;
        private bool subscribed;
        private double nextPoll;
        public void Bind(AchievementSessionBridge source,NetworkConnectionCoordinator network)
        {
            if(bridge==source&&connection==network){if(isActiveAndEnabled){Subscribe();Refresh();}return;}
            Unsubscribe();bridge=source;connection=network;session=source!=null?source.GetComponent<SessionRoot>():null;
            if(isActiveAndEnabled){Subscribe();Refresh();}
        }
        private void Awake(){group=GetComponent<CanvasGroup>();group.interactable=false;group.blocksRaycasts=false;}
        private void OnEnable(){if(group==null)group=GetComponent<CanvasGroup>();TryDiscover();Subscribe();Refresh();}
        private void TryDiscover()
        {
            if(!AutoBind||bridge!=null)return;
            var root=FindFirstObjectByType<SessionRoot>();
            if(root!=null)Bind(root.GetComponent<AchievementSessionBridge>(),root.GetComponent<NetworkConnectionCoordinator>());
        }
        private void Subscribe()
        {
            if(subscribed||!isActiveAndEnabled||bridge==null)return;
            bridge.Changed+=Refresh;if(connection!=null)connection.Changed+=Refresh;if(session!=null)session.Changed+=Refresh;subscribed=true;
        }
        private void Unsubscribe()
        {
            if(!subscribed)return;
            if(bridge!=null)bridge.Changed-=Refresh;if(connection!=null)connection.Changed-=Refresh;if(session!=null)session.Changed-=Refresh;subscribed=false;
        }
        private void Update()
        {
            // Session flags and asynchronous local-save retry can change without an achievement grant.
            if(Time.unscaledTimeAsDouble<nextPoll)return;nextPoll=Time.unscaledTimeAsDouble+.25;
            if(bridge==null){Unsubscribe();bridge=null;connection=null;session=null;TryDiscover();}Refresh();
        }
        private void Refresh()
        {
            if(!isActiveAndEnabled)return;
            bool visible=session!=null&&(Place==AchievementStatusPlace.Lobby?session.Phase==SessionPhase.Lobby:session.Phase==SessionPhase.Results&&session.Result!=null);
            if(Place==AchievementStatusPlace.Lobby&&LobbyMenu!=null&&LobbyMenu.ModalBlocksLobby)visible=false;
            if(group!=null){group.alpha=visible?1:0;group.interactable=false;group.blocksRaycasts=false;}
            var service=bridge!=null?bridge.Service:null;
            bool selected=connection!=null&&(connection.Mode==ConnectionMode.SteamHost||connection.Mode==ConnectionMode.SteamClient);
            var steam=selected?connection.ActiveSteamRuntime:null;
            bool productSteam=steam!=null&&steam.Initialized&&steam.ActualAppId!=0&&steam.ActualAppId!=480;
            var profile=service?.ActiveProfile;
            bool bound=productSteam&&profile!=null&&profile.SteamId==steam.LocalSteamId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var text=AchievementStatusCopy.Compose(profile!=null,profile?.UnlockedLocal.Length??0,
                bridge!=null&&!string.IsNullOrEmpty(bridge.LastError),service?.HasPendingSave??false,!string.IsNullOrEmpty(service?.LastError),
                productSteam,bound,profile?.PendingSteam.Length??0,service?.PlatformBlocked??false,!string.IsNullOrEmpty(service?.LastError));
            Set(CountText,text.Count);Set(ContextText,text.Context);Set(IssueText,text.Issue);
        }
        private static void Set(TMP_Text text,string value){if(text==null)return;text.richText=false;text.raycastTarget=false;if(text.text!=value)text.text=value;}
        private void OnDisable(){Unsubscribe();if(group!=null)group.alpha=0;}
        private void OnDestroy(){Unsubscribe();bridge=null;connection=null;session=null;}
    }
}
