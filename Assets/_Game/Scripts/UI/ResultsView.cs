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
        private void Retry() => session?.RetryContract();
        private void Menu() => session?.ReturnToMenu();
        private void Refresh()
        {
            bool visible = session != null && session.Phase == SessionPhase.Results && session.Result != null;
            bool opening = ResultsPanel != null && visible && !ResultsPanel.activeSelf;
            if (ResultsPanel != null) ResultsPanel.SetActive(visible);
            if (!visible) return;
            var result = session.Result;
            if (!ReferenceEquals(displayed, result))
            {
                displayed = result;
                string reason = result.Phase == ContractPhase.Succeeded ? "Эвакуация завершена" :
                    result.Phase == ContractPhase.Failed ? "Время истекло" : "Контракт прерван";
                if (TitleText != null) TitleText.text = reason;
                if (SummaryText != null)
                    SummaryText.text = $"Собрано: ${result.CollectedMoney:N0}\nКвота: ${result.Quota:N0}\n" +
                        $"Коэффициент выплаты: {result.PayoutPercent}%\nВыплата: ${result.Payout:N0}\n\nБаланс: ${session.Campaign.Balance:N0}";
            }
            if (ErrorText != null) ErrorText.text = session.LastError;
            if (RetryButton != null) RetryButton.interactable = session.CanRetry;
            if (MenuButton != null) MenuButton.interactable = session.Progression.PendingResult == null;
            if (opening && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(session.CanRetry && RetryButton != null ? RetryButton.gameObject : MenuButton?.gameObject);
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