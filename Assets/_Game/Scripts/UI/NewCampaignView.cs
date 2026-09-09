using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HowToSuck
{
    public sealed class NewCampaignView : MonoBehaviour, ICancelHandler
    {
        public Button NewGameButton, ConfirmButton, CancelButton;
        public GameObject Panel;
        public TMP_Text Message;
        public SessionMenuView Menu;
        public bool IsOpen => Panel != null && Panel.activeSelf;
        private SessionRoot session;
        private CampaignState observed;
        private Coroutine focusReturn;
        private const string Explanation = "Текущая кампания будет заменена новой. Деньги, купленное снаряжение, дополнительные слоты и прохождение контрактов будут сброшены.\n\nВы начнёте с MK1 и балансом $0. Настройки сохранятся. Друзья останутся в лобби и смогут снова подтвердить готовность.";

        public void Bind(SessionRoot root)
        {
            if (session == root) { Refresh(); return; }
            Unbind(); session = root;
            if (session == null) return;
            session.Changed += Refresh;
            NewGameButton.onClick.AddListener(Open);
            ConfirmButton.onClick.AddListener(Confirm);
            CancelButton.onClick.AddListener(Close);
            Refresh();
        }
        private void Open()
        {
            if (session == null || !session.CanStartNewCampaign || Menu.ModalBlocksLobby) return;
            observed = session.Campaign;
            Message.text = Explanation;
            Panel.SetActive(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(CancelButton.gameObject);
        }
        private void Confirm()
        {
            if (!IsOpen || observed == null || session == null) return;
            if (!session.CanStartNewCampaign || !ReferenceEquals(observed, session.Campaign)) { Close(); return; }
            if (session.StartNewCampaign(observed)) Close();
            else if (IsOpen) Message.text = "Не удалось начать новую игру. Текущая кампания сохранена.\n\n" + session.LastError;
        }
        public void Close()
        {
            observed = null;
            if (!IsOpen) return;
            bool ownsFocus = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                EventSystem.current.currentSelectedGameObject.transform.IsChildOf(Panel.transform);
            Panel.SetActive(false);
            if (ownsFocus)
            {
                EventSystem.current.SetSelectedGameObject(null);
                if (focusReturn != null) StopCoroutine(focusReturn);
                focusReturn = StartCoroutine(ReturnFocus());
            }
        }
        public void OnCancel(BaseEventData data) { if (IsOpen) { Close(); data.Use(); } }
        private IEnumerator ReturnFocus()
        {
            yield return null; // Let the menu layer and any save-recovery dialog update first.
            focusReturn = null;
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null &&
                !Menu.ModalBlocksLobby && NewGameButton.IsActive() && NewGameButton.IsInteractable())
                EventSystem.current.SetSelectedGameObject(NewGameButton.gameObject);
        }
        private void Refresh()
        {
            bool host = session != null && session.HasAuthority;
            NewGameButton.gameObject.SetActive(host);
            NewGameButton.interactable = host && session.CanStartNewCampaign;
            if (IsOpen && (!NewGameButton.interactable || !ReferenceEquals(observed, session.Campaign))) Close();
        }
        private void Unbind()
        {
            if (focusReturn != null) { StopCoroutine(focusReturn); focusReturn = null; }
            if (session != null) session.Changed -= Refresh;
            if (NewGameButton != null) NewGameButton.onClick.RemoveListener(Open);
            if (ConfirmButton != null) ConfirmButton.onClick.RemoveListener(Confirm);
            if (CancelButton != null) CancelButton.onClick.RemoveListener(Close);
            session = null; observed = null;
        }
        private void OnDestroy() => Unbind();
    }
}
