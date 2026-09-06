using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HowToSuck
{
    public sealed class ContractHud : MonoBehaviour
    {
        public GameObject HudPanel;
        public TMP_Text MoneyText;
        public TMP_Text TimerText;
        public GameObject ExtractionPanel;
        public TMP_Text ExtractionText;
        public Image HoldFill;
        private SessionRoot session;
        private long shownMoney = -1, shownQuota = -1;
        private double shownSeconds = -1;

        public void Bind(SessionRoot root)
        {
            Unbind(); session = root;
            shownMoney = shownQuota = -1; shownSeconds = -1;
            if (session != null) session.Changed += Refresh;
            Refresh();
        }
        private void Refresh()
        {
            bool playing = session != null && session.Phase == SessionPhase.Playing;
            if (HudPanel != null) HudPanel.SetActive(playing);
            if (!playing) { if (ExtractionPanel != null) ExtractionPanel.SetActive(false); return; }
            var state = session.ContractState;
            if (MoneyText != null && (shownMoney != state.CollectedMoney || shownQuota != state.Quota))
            {
                shownMoney = state.CollectedMoney; shownQuota = state.Quota;
                MoneyText.text = $"Собрано ${shownMoney:N0} / ${shownQuota:N0}";
            }
            double seconds = state.RemainingSeconds;
            if (TimerText != null && shownSeconds != seconds)
            {
                shownSeconds = seconds;
                TimerText.text = $"{Math.Floor(seconds / 60):00}:{seconds % 60:00}";
                TimerText.color = seconds <= 20 ? new Color(1f, .54f, .32f) : new Color(.97f, .94f, .84f);
            }
            bool atTruck = session.LocalPlayer != null && session.World.IsPlayerInExtraction(session.LocalPlayer.PlayerId);
            if (ExtractionPanel != null) ExtractionPanel.SetActive(atTruck);
            if (!atTruck) return;
            string prompt = !state.QuotaReached ? "Для эвакуации выполните квоту" :
                !session.World.AllPlayersInExtraction ? "Все игроки должны вернуться к грузовику" :
                state.ExtractHoldProgress > 0 ? "Удерживайте E — эвакуация" : "Удерживайте E 2 секунды, чтобы уехать";
            if (ExtractionText != null && ExtractionText.text != prompt) ExtractionText.text = prompt;
            if (HoldFill != null)
            {
                HoldFill.transform.parent.gameObject.SetActive(state.QuotaReached && session.World.AllPlayersInExtraction);
                HoldFill.fillAmount = (float)state.ExtractHoldProgress;
            }
        }
        private void Unbind() { if (session != null) session.Changed -= Refresh; session = null; }
        private void OnDestroy() => Unbind();
    }
}