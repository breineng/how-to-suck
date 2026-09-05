using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace HowToSuck
{
    public sealed class SessionMenuView : MonoBehaviour
    {
        public Button StartButton;
        public Button BackButton;
        public Text Status;
        private SessionRoot session;
        public void Bind(SessionRoot root)
        {
            Unbind();
            session = root;
            session.Changed += Refresh;
            if (StartButton != null) StartButton.onClick.AddListener(StartSelected);
            if (BackButton != null) BackButton.onClick.AddListener(Back);
            Refresh();
            if (StartButton != null && StartButton.interactable && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(StartButton.gameObject);
        }
        private void StartSelected()
        {
            if (session.Catalog != null && session.Catalog.Contracts != null && session.Catalog.Contracts.Length > 0)
                session.StartContract(session.Catalog.Contracts[0]);
        }
        private void Back() => session.ReturnToMenu();
        private void Refresh()
        {
            bool loading = session.Phase == SessionPhase.Loading;
            if (StartButton != null) StartButton.interactable = session.Phase == SessionPhase.Lobby
                && session.Catalog != null && session.Catalog.TryValidate(out _);
            if (BackButton != null) BackButton.interactable = !loading;
            if (Status != null) Status.text = loading ? "Loading…" : session.LastError;
        }
        private void Unbind()
        {
            if (session != null) session.Changed -= Refresh;
            if (StartButton != null) StartButton.onClick.RemoveListener(StartSelected);
            if (BackButton != null) BackButton.onClick.RemoveListener(Back);
        }
        private void OnDestroy() => Unbind();
    }
}
