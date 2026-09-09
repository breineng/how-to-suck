using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace HowToSuck.Networking
{
    // Normal lobby readiness and accurate leave copy; never changes quota, positions or collection totals.
    public sealed class NetworkLobbyView : MonoBehaviour
    {
        public Button ReadyButton;
        public Button ReplacedGuestStart;
        private bool originalStartActive;
        public GameObject LobbyOnlyPanel;
        public SessionMenuView Menu;
        public TMP_Text ReadyLabel,Status,LeaveConfirmation;
        private NgoGameSession game;
        private void Start()
        {game=NgoGameSession.RequireCurrent();if(ReplacedGuestStart!=null)originalStartActive=ReplacedGuestStart.gameObject.activeSelf;if(ReadyButton!=null)ReadyButton.onClick.AddListener(ToggleReady);}
        private void ToggleReady()
        {if(isActiveAndEnabled&&game!=null&&game.Control!=null)game.SetLocalReady(!game.Control.LocalReady);}
        private void Update()
        {
            if(game==null)return;
            bool lobby=game.Session.Phase==SessionPhase.Lobby;
            var control=game.Control;
            if(LobbyOnlyPanel!=null)LobbyOnlyPanel.SetActive(lobby&&!game.HasAuthority&&game.Connection.Mode!=ConnectionMode.SoloLoopback&&(Menu==null||!Menu.ModalBlocksLobby));
            if(ReadyButton!=null){ReadyButton.gameObject.SetActive(lobby&&!game.HasAuthority);ReadyButton.interactable=control!=null&&control.IsSpawned&&control.HasAcceptedCurrentSnapshot&&(Menu==null||!Menu.ModalBlocksLobby);}
            if(ReadyLabel!=null)HowToSuck.LocalizedText.Set(ReadyLabel, control!=null&&control.LocalReady?"Не готов":"Готов");
            if(Status!=null)HowToSuck.LocalizedText.Set(Status, !lobby?"":game.HasAuthority?(game.Session.CanStartContract?"Все готовы":"Ожидаем готовность игроков"):
                control!=null&&control.LocalReady?"Вы готовы. Контракт выбирает хозяин":"Подтвердите готовность");
            if(LeaveConfirmation!=null)HowToSuck.LocalizedText.Set(LeaveConfirmation, game.HasAuthority?
                "Прервать контракт для всех? Выплата при ручном выходе — $0.":
                "Покинуть сессию? Остальные игроки смогут продолжить контракт. Ваша локальная кампания не изменится.");
        }
        private void LateUpdate(){if(game!=null&&ReplacedGuestStart!=null)ReplacedGuestStart.gameObject.SetActive(originalStartActive&&(game.HasAuthority||game.Session.Phase!=SessionPhase.Lobby));}
        private void OnDisable(){if(LobbyOnlyPanel!=null)LobbyOnlyPanel.SetActive(false);if(ReplacedGuestStart!=null)ReplacedGuestStart.gameObject.SetActive(originalStartActive);}
        private void OnDestroy(){if(ReadyButton!=null)ReadyButton.onClick.RemoveListener(ToggleReady);}
    }
}
