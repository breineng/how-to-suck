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
        private SessionRoot session;
        private PlayerInputReader input;
        public void Bind(SessionRoot root, PlayerInputReader reader)
        {
            Unbind();
            session = root; input = reader;
            input.MenuChanged += Show;
            if (ResumeButton != null) ResumeButton.onClick.AddListener(Resume);
            if (LeaveButton != null) LeaveButton.onClick.AddListener(Leave);
            Show(input.MenuOpen);
        }
        private void Resume() => input.SetMenuOpen(false);
        private void Leave() => session.ReturnToMenu();
        private void Show(bool visible)
        {
            if (MenuPanel != null) MenuPanel.SetActive(visible);
            if (Crosshair != null) Crosshair.SetActive(!visible);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(visible && ResumeButton != null ? ResumeButton.gameObject : null);
        }
        private void Unbind()
        {
            if (input != null) input.MenuChanged -= Show;
            if (ResumeButton != null) ResumeButton.onClick.RemoveListener(Resume);
            if (LeaveButton != null) LeaveButton.onClick.RemoveListener(Leave);
        }
        private void OnDestroy() => Unbind();
    }
}
