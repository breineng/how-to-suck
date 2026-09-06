using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace HowToSuck
{
    public sealed class MenuInputController : MonoBehaviour
    {
        public GameObject MenuPanel;
        public GameObject Crosshair;
        public Button ResumeButton;
        public Button LeaveButton;
        public GameObject ConfirmLeavePanel;
        public Button ConfirmLeaveButton;
        public Button CancelLeaveButton;
        private SessionRoot session;
        private PlayerInputReader input;
        private bool confirming;

        public void Bind(SessionRoot root, PlayerInputReader reader)
        {
            Unbind();
            session = root; input = reader; confirming = false;
            if (input != null) input.MenuChanged += OnMenuChanged;
            if (session != null) session.Changed += Refresh;
            if (ResumeButton != null) ResumeButton.onClick.AddListener(Resume);
            if (LeaveButton != null) LeaveButton.onClick.AddListener(Leave);
            if (ConfirmLeaveButton != null) ConfirmLeaveButton.onClick.AddListener(ConfirmLeave);
            if (CancelLeaveButton != null) CancelLeaveButton.onClick.AddListener(CancelLeave);
            Refresh();
        }
        private void Resume() { if (session != null && session.Phase == SessionPhase.Playing) input?.SetMenuOpen(false); }
        private void Leave()
        {
            if (session == null || session.Phase != SessionPhase.Playing || ConfirmLeavePanel == null) return;
            confirming = true;
            Refresh();
            Select(CancelLeaveButton);
        }
        private void ConfirmLeave()
        {
            if (!confirming || session == null || session.Phase != SessionPhase.Playing) return;
            confirming = false;
            session.AbandonToMenu();
            Refresh();
        }
        private void CancelLeave() { confirming = false; Refresh(); Select(ResumeButton); }
        private void OnMenuChanged(bool visible) { if (!visible) confirming = false; Refresh(); }
        private void Refresh()
        {
            bool playing = session != null && session.Phase == SessionPhase.Playing;
            bool visible = playing && input != null && input.MenuOpen;
            bool opening = MenuPanel != null && visible && !MenuPanel.activeSelf;
            if (!visible) confirming = false;
            if (MenuPanel != null) MenuPanel.SetActive(visible);
            if (Crosshair != null) Crosshair.SetActive(playing && !visible);
            if (ConfirmLeavePanel != null) ConfirmLeavePanel.SetActive(visible && confirming);
            if (ResumeButton != null) ResumeButton.interactable = visible && !confirming;
            if (LeaveButton != null) LeaveButton.interactable = visible && !confirming;
            if (opening) Select(ResumeButton);
        }
        private static void Select(Button button)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button != null ? button.gameObject : null);
        }
        private void Unbind()
        {
            if (input != null) input.MenuChanged -= OnMenuChanged;
            if (session != null) session.Changed -= Refresh;
            if (ResumeButton != null) ResumeButton.onClick.RemoveListener(Resume);
            if (LeaveButton != null) LeaveButton.onClick.RemoveListener(Leave);
            if (ConfirmLeaveButton != null) ConfirmLeaveButton.onClick.RemoveListener(ConfirmLeave);
            if (CancelLeaveButton != null) CancelLeaveButton.onClick.RemoveListener(CancelLeave);
            session = null; input = null;
        }
        private void OnDestroy() => Unbind();
    }
}