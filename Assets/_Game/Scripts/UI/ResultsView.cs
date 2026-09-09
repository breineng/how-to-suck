using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace HowToSuck
{
    public sealed class ResultsView : MonoBehaviour
    {
        public GameObject ResultsPanel;
        public TMP_Text TitleText;
        public TMP_Text SummaryText;
        public TMP_Text ErrorText;
        public Button RetryButton;
        public Button MenuButton;
        private SessionRoot session;
        private ContractResult displayed;

        public void Bind(SessionRoot root)
        {
            Unbind(); session = root; displayed = null;
            if (session != null) session.Changed += Refresh;
            if (RetryButton != null) RetryButton.onClick.AddListener(Retry);
            if (MenuButton != null) MenuButton.onClick.AddListener(Menu);
            Refresh();
        }
        private void Retry()
        {
            if (session == null) return;
            if (session.HasAuthority && session.HasPendingSave) session.RetryCampaignSave();
            else session.RetryContract();
        }
        private void Menu() => session?.ReturnToLobby();
        private void Refresh()
        {
            bool visible = session != null && session.Phase == SessionPhase.Results && session.Result != null;
            bool opening = ResultsPanel != null && visible && !ResultsPanel.activeSelf;
            if (ResultsPanel != null) ResultsPanel.SetActive(visible);
            if (!visible) return;
            var result = session.Result;
            // Confirmed balance can change after saving the same immutable result.
            if (result != null)
            {
                displayed = result;
                string reason = result.Phase == ContractPhase.Succeeded ? "Эвакуация завершена" :
                    result.Phase == ContractPhase.Failed ? "Время истекло" : "Контракт прерван";
                if (TitleText != null) HowToSuck.LocalizedText.Set(TitleText, reason);
                if (SummaryText != null)
                    HowToSuck.LocalizedText.Set(SummaryText, $"Сдано в грузовик: ${result.DeliveredValue:N0}\nКвота: ${result.Quota:N0}\n" +
                        (result.Boss.IsDelivered?"Босс доставлен\n":"Босс не доставлен\n") +
                        $"Коэффициент выплаты: {result.PayoutPercent}%\nВыплата: ${result.Payout:N0}\n\nБаланс: ${session.DisplayedBalance:N0}");
            }
            bool retrySave = session.HasAuthority && session.HasPendingSave;
            var events = EventSystem.current;
            // UGUI clears selection as soon as a selected Selectable is disabled.
            bool ownedRetrySelection = events != null && RetryButton != null && events.currentSelectedGameObject == RetryButton.gameObject;
            if (ErrorText != null) HowToSuck.LocalizedText.Set(ErrorText, retrySave
                ? "Выплата ещё не сохранена. Повторите сохранение, чтобы продолжить."
                : session.LastError);
            if (RetryButton != null)
            {
                RetryButton.interactable = retrySave || session.CanRetry;
                var caption = RetryButton.GetComponentInChildren<TMP_Text>(true);
                if (caption != null) HowToSuck.LocalizedText.Set(caption, retrySave ? "Сохранить снова" : "Ещё раз");
            }
            if (MenuButton != null)
            {
                MenuButton.interactable = session.CanReturnToLobby;
                var caption = MenuButton.GetComponentInChildren<TMP_Text>(true);
                if(caption!=null)HowToSuck.LocalizedText.Set(caption, session.HasAuthority?"В лобби":"Ожидаем хозяина");
            }
            if (events != null && (opening || ownedRetrySelection && RetryButton != null && !RetryButton.interactable))
                events.SetSelectedGameObject((retrySave || session.CanRetry) && RetryButton != null ? RetryButton.gameObject :
                    MenuButton != null && MenuButton.interactable ? MenuButton.gameObject : null);
        }
        private void Unbind()
        {
            if (session != null) session.Changed -= Refresh;
            if (RetryButton != null) RetryButton.onClick.RemoveListener(Retry);
            if (MenuButton != null) MenuButton.onClick.RemoveListener(Menu);
            session = null;
        }
        private void OnDestroy() => Unbind();
    }
}
