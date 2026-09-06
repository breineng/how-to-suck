using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
namespace HowToSuck.Networking
{
    public sealed class SoloEntryView : MonoBehaviour
    {
        public Button SoloButton,QuitButton,CoopButton;
        public TMP_Text Status,CoopStatus;
        private SoloSessionStartup startup;
        private void Start()
        {
            if(SoloButton==null||QuitButton==null||Status==null){SetButtons(false,false);enabled=false;return;}
            var roots=FindObjectsByType<SoloSessionStartup>(FindObjectsSortMode.None);
            if(roots.Length!=1){if(Status!=null)Status.text="Не удалось открыть главное меню.";SetButtons(false,false);return;}
            startup=roots[0];startup.Changed+=Refresh;
            SoloButton.onClick.AddListener(ChooseSolo);QuitButton.onClick.AddListener(Quit);
            if(CoopButton!=null)CoopButton.interactable=false;
            if(CoopStatus!=null)CoopStatus.text="Совместная игра пока недоступна в этой сборке.";
            Refresh();if(SoloButton.IsInteractable()&&EventSystem.current!=null)EventSystem.current.SetSelectedGameObject(SoloButton.gameObject);
        }
        private void ChooseSolo()=>startup.StartSolo();
        private void Quit()=>startup.Quit();
        private void SetButtons(bool solo,bool quit){if(SoloButton!=null)SoloButton.interactable=solo;if(QuitButton!=null)QuitButton.interactable=quit;if(CoopButton!=null)CoopButton.interactable=false;}
        private void Refresh()
        {
            SetButtons(startup.CanStartSolo,startup.CanQuit);if(Status!=null)Status.text=startup.Status;
            if(EventSystem.current!=null&&EventSystem.current.currentSelectedGameObject==null)
            {
                if(startup.CanStartSolo)EventSystem.current.SetSelectedGameObject(SoloButton.gameObject);
                else if(startup.Phase==SoloEntryPhase.Failed&&startup.CanQuit)EventSystem.current.SetSelectedGameObject(QuitButton.gameObject);
            }
        }
        private void OnDestroy(){if(startup!=null)startup.Changed-=Refresh;if(SoloButton!=null)SoloButton.onClick.RemoveListener(ChooseSolo);if(QuitButton!=null)QuitButton.onClick.RemoveListener(Quit);}
    }
}
