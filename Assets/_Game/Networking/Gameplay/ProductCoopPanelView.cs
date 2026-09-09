using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace HowToSuck.Networking
{
    // Approved menu presentation. Network work occurs only in explicit button callbacks.
    public sealed class ProductCoopPanelView:MonoBehaviour,ICancelHandler
    {
        public GameObject Panel;
        public CanvasGroup BaseGroup,MainRail;
        public LocalMenuView SettingsMenu;
        public Button HostButton,RefreshButton,AcceptInvitationButton,BackButton,FriendTemplate;
        public Transform FriendContent;
        public TMP_Text Message;
        public bool IsOpen{get;private set;}
        private SoloSessionStartup source;
        private readonly List<Button> rows=new List<Button>();
        private readonly List<ulong> displayed=new List<ulong>();
        private bool baseInteractable,baseRaycasts,wired;
        private GameObject previousSelection;
        private NetworkManager observedManager;
        private NetworkSceneManager observedScenes;
        private bool lobbyLoadHandedOff;
        public void Bind(SoloSessionStartup owner)
        {
            if(source==owner&&source!=null)return;
            if(source!=null)source.Changed-=Refresh;
            UnbindSceneEvents();CloseView(false);source=owner;lobbyLoadHandedOff=false;
            if(source!=null&&isActiveAndEnabled){source.Changed+=Refresh;BindSceneEvents();}
            Refresh();
        }
        private void OnEnable()
        {
            if(!wired){HostButton.onClick.AddListener(Host);RefreshButton.onClick.AddListener(Discover);AcceptInvitationButton.onClick.AddListener(Accept);BackButton.onClick.AddListener(Back);wired=true;}
            if(source!=null){source.Changed-=Refresh;source.Changed+=Refresh;BindSceneEvents();}Refresh();
        }
        public void Open()
        {
            if(source==null||!source.isActiveAndEnabled||SettingsMenu!=null&&SettingsMenu.IsOpen||
                (!source.CanStartSolo&&!source.IsBrowsingCoop))return;
            lobbyLoadHandedOff=false;
            previousSelection=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            if(!IsOpen){baseInteractable=BaseGroup.interactable;baseRaycasts=BaseGroup.blocksRaycasts;}
            IsOpen=true;BaseGroup.interactable=false;BaseGroup.blocksRaycasts=false;Panel.SetActive(true);
            source.BeginCoop(); // Explicit click only. Unconfigured returns false and creates no session or Steam owner.
            Refresh();Select(BackButton);
        }
        private void Host(){if(IsOpen&&source!=null&&source.IsBrowsingCoop)source.StartSteamHost();}
        private void Discover(){if(IsOpen&&source!=null&&source.IsBrowsingCoop)source.RefreshFriendLobbies();}
        private void Accept(){if(IsOpen&&source!=null&&source.IsBrowsingCoop)source.AcceptPendingInvitation();}
        private void Join(ulong lobby){if(IsOpen&&source!=null&&source.IsBrowsingCoop)source.JoinSteamLobby(lobby);}
        private void Back()
        {
            if(!IsOpen)return;
            if(source!=null&&source.CanCancelEntry){if(!source.CancelEntry())return;}
            else if(source!=null&&source.Phase==SoloEntryPhase.Starting)return;
            CloseView(true);
        }
        public void OnCancel(BaseEventData data){if(IsOpen){Back();data.Use();}}
        private void CloseView(bool restore)
        {
            if(IsOpen&&BaseGroup!=null){BaseGroup.interactable=baseInteractable;BaseGroup.blocksRaycasts=baseRaycasts;}
            IsOpen=false;if(Panel!=null)Panel.SetActive(false);
            if(restore&&EventSystem.current!=null&&previousSelection!=null&&previousSelection.activeInHierarchy)EventSystem.current.SetSelectedGameObject(previousSelection);
            previousSelection=null;ClearRows();
        }
        private void Refresh()
        {
            if(!IsOpen||source==null)return;
            if(source.Phase==SoloEntryPhase.Connected||source.Phase==SoloEntryPhase.Returning){CloseView(false);return;}
            bool browse=source.IsBrowsingCoop;HostButton.interactable=RefreshButton.interactable=browse;
            AcceptInvitationButton.gameObject.SetActive(browse&&source.HasPendingInvitation);
            BackButton.interactable=source.CanCancelEntry||source.CanStartSolo;
            HowToSuck.LocalizedText.Set(Message, browse?(string.IsNullOrWhiteSpace(source.Status)?"Создайте игру или выберите друга.":source.Status):
                source.Phase==SoloEntryPhase.Starting?"Подключаемся…":!string.IsNullOrWhiteSpace(source.Status)?source.Status:source.CoopStatus);
            var friends=source.FriendLobbies;int count=Math.Min(friends.Count,128);bool rebuild=count!=displayed.Count;
            if(!rebuild)for(int i=0;i<count;i++)if(friends[i].LobbyId!=displayed[i]){rebuild=true;break;}
            if(rebuild){ClearRows();foreach(var friend in friends){if(rows.Count==128)break;
                var row=Instantiate(FriendTemplate,FriendContent);row.name="FriendGame";row.gameObject.SetActive(true);
                ulong lobby=friend.LobbyId;row.onClick.AddListener(()=>Join(lobby));rows.Add(row);displayed.Add(lobby);
                var cancel=row.GetComponent<ProductCoopCancel>();if(cancel!=null)cancel.Panel=this;
            }}
            for(int i=0;i<rows.Count;i++){
                var text=rows[i].GetComponentInChildren<TMP_Text>();text.richText=false;HowToSuck.LocalizedText.SetLiteral(text, string.IsNullOrWhiteSpace(friends[i].HostName)?GameLocalization.Text("Игра друга"):friends[i].HostName);
                rows[i].interactable=browse;
            }
        }
        private void ClearRows(){foreach(var row in rows)if(row!=null)Destroy(row.gameObject);rows.Clear();displayed.Clear();}
        private void LateUpdate()
        {
            bool hide=IsOpen||SettingsMenu!=null&&SettingsMenu.IsOpen;
            if(MainRail!=null){MainRail.alpha=hide?0:1;MainRail.interactable=!hide;MainRail.blocksRaycasts=!hide;}
            if(!IsOpen||EventSystem.current==null)return;var selected=EventSystem.current.currentSelectedGameObject;
            if(selected==null||!selected.activeInHierarchy||!selected.transform.IsChildOf(Panel.transform))Select(BackButton);
        }
        private static void Select(Selectable control){if(EventSystem.current!=null&&control!=null&&control.IsInteractable())EventSystem.current.SetSelectedGameObject(control.gameObject);}
        private void BindSceneEvents()
        {
            if(source==null||source.Game==null||source.Game.Manager==null)return;
            var manager=source.Game.Manager;
            if(observedManager!=manager){UnbindSceneEvents();observedManager=manager;
                observedManager.OnClientStarted+=BindScenes;observedManager.OnServerStarted+=BindScenes;}
            BindScenes();
        }
        private void BindScenes()
        {
            var next=observedManager!=null?observedManager.SceneManager:null;
            if(ReferenceEquals(observedScenes,next))return;
            if(observedScenes!=null)observedScenes.OnLoad-=LobbyLoad;
            observedScenes=next;if(observedScenes!=null)observedScenes.OnLoad+=LobbyLoad;
        }
        private void LobbyLoad(ulong client,string scene,LoadSceneMode mode,AsyncOperation operation)
        {
            // Initial guest synchronization loads the scene BEFORE IsConnectedClient/Connected.
            // Only this actual local lobby load transfers the UI attempt to the persistent owner.
            if(!IsOpen||source==null||observedManager==null||operation==null||client!=observedManager.LocalClientId||
                mode!=LoadSceneMode.Single||source.Phase!=SoloEntryPhase.Starting||source.Game==null||
                source.Game.IsStopping||source.Game.Driver==null||source.Bootstrap==null||source.Bootstrap.Session==null||
                !source.Bootstrap.Session.IsInitialized||scene!=source.Bootstrap.Session.MenuSceneName)return;
            lobbyLoadHandedOff=true;
        }
        private void UnbindSceneEvents()
        {
            if(observedManager!=null){observedManager.OnClientStarted-=BindScenes;observedManager.OnServerStarted-=BindScenes;}
            if(observedScenes!=null)observedScenes.OnLoad-=LobbyLoad;
            observedManager=null;observedScenes=null;
        }
        private void OnDisable()
        {
            if(source!=null){source.Changed-=Refresh;
                if(IsOpen&&!lobbyLoadHandedOff&&source.CanCancelEntry)source.CancelEntry();}
            UnbindSceneEvents();
            CloseView(false);if(MainRail!=null){MainRail.alpha=1;MainRail.interactable=MainRail.blocksRaycasts=true;}
        }
        private void OnDestroy()
        {
            UnbindSceneEvents();
            if(source!=null)source.Changed-=Refresh;
            if(wired){HostButton.onClick.RemoveListener(Host);RefreshButton.onClick.RemoveListener(Discover);AcceptInvitationButton.onClick.RemoveListener(Accept);BackButton.onClick.RemoveListener(Back);}ClearRows();
        }
    }
}
