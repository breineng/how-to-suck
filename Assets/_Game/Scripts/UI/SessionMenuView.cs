using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace HowToSuck
{
    public sealed class SessionMenuView : MonoBehaviour, ICancelHandler
    {
        public Button StartButton;
        public Button BackButton;
        public Text Status;
        public GameObject[] ExtraModalPanels;
        public bool ModalBlocksLobby {
            get {
                if(ExtraModalPanels!=null)foreach(var panel in ExtraModalPanels)if(panel!=null&&panel.activeInHierarchy)return true;
                foreach(var localMenu in GetComponentsInChildren<LocalMenuView>(true))if(localMenu.IsOpen)return true;
                foreach(var shop in GetComponentsInChildren<ShopView>(true))if(shop.IsOpen)return true;
                foreach(var recovery in GetComponentsInChildren<CampaignRecoveryView>(true))if(recovery.Panel!=null&&recovery.Panel.activeInHierarchy)return true;
                return false;
            }
        }
        public void OnCancel(BaseEventData data)
        {if(!ModalBlocksLobby&&BackButton!=null&&BackButton.IsInteractable()){Back();data.Use();}}
        private SessionRoot session;
        private ISessionMenuExit exit;
        public void Bind(SessionRoot root)
        {
            Unbind();
            session = root;
            exit = null;
            foreach (var component in root.GetComponents<MonoBehaviour>())
                if (component is ISessionMenuExit value)
                {
                    if (exit != null) throw new System.InvalidOperationException("One menu exit owner per session is required.");
                    exit = value;
                }
            session.Changed += Refresh;
            if (StartButton != null) StartButton.onClick.AddListener(StartSelected);
            if (BackButton != null) BackButton.onClick.AddListener(Back);
            Refresh();
            if (StartButton != null && StartButton.interactable && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(StartButton.gameObject);
        }
        private void StartSelected()
        {
            if(session!=null&&!ModalBlocksLobby)session.StartSelectedContract();
        }
        private void Back()
        {
            if(session==null||ModalBlocksLobby)return;
            if (exit != null && session.Phase == SessionPhase.Lobby) exit.ExitSessionMenu(session);
            else session.ReturnToMenu();
        }
        private void Refresh()
        {
            bool loading = session.Phase == SessionPhase.Loading;
            if (StartButton != null) StartButton.interactable = session.CanStartContract && session.SelectedLobbyContract!=null
                && session.Catalog != null && session.Catalog.TryValidate(out _)
                && session.DisplayedContractUnlocked(session.SelectedLobbyContract.ContractId);
            if (BackButton != null) BackButton.interactable = exit != null && session.Phase == SessionPhase.Lobby ? exit.CanExitSessionMenu(session) : !loading && session.Phase != SessionPhase.ShuttingDown;
            if (Status != null) Status.text = loading ? "Загрузка…" : session.LastError;
        }
        private void Unbind()
        {
            if (session != null) session.Changed -= Refresh;
            exit = null;
            if (StartButton != null) StartButton.onClick.RemoveListener(StartSelected);
            if (BackButton != null) BackButton.onClick.RemoveListener(Back);
        }
        private void OnDestroy() => Unbind();
    }
}
