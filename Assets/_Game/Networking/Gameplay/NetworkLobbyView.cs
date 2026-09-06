using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace HowToSuck.Networking
{
    // Normal lobby readiness and accurate leave copy; never changes quota, positions or collection totals.
    public sealed class NetworkLobbyView : MonoBehaviour
    {
        public Button ReadyButton;
        public TMP_Text ReadyLabel,Status,LeaveConfirmation;
        private NgoGameSession game;
        private void Start()
        {game=NgoGameSession.RequireCurrent();if(ReadyButton!=null)ReadyButton.onClick.AddListener(ToggleReady);}
        private void ToggleReady()
        {if(game.Control!=null)game.SetLocalReady(!game.Control.LocalReady);}
        private void Update()
        {
            if(game==null)return;
            bool lobby=game.Session.Phase==SessionPhase.Lobby;
            var control=game.Control;
            if(ReadyButton!=null){ReadyButton.gameObject.SetActive(lobby&&!game.HasAuthority);ReadyButton.interactable=control!=null&&control.IsSpawned;}
            if(ReadyLabel!=null)ReadyLabel.text=control!=null&&control.LocalReady?"Не готов":"Готов";
            if(Status!=null)Status.text=!lobby?"":game.HasAuthority?(game.Session.CanStartContract?"Все готовы":"Ожидаем готовность игроков"):
                control!=null&&control.LocalReady?"Вы готовы. Контракт выбирает хозяин":"Подтвердите готовность";
            if(LeaveConfirmation!=null)LeaveConfirmation.text=game.HasAuthority?
                "Прервать контракт для всех? Выплата при ручном выходе — $0.":
                "Покинуть сессию? Остальные игроки смогут продолжить контракт. Ваша локальная кампания не изменится.";
        }
        private void OnDestroy(){if(ReadyButton!=null)ReadyButton.onClick.RemoveListener(ToggleReady);}
    }
}
