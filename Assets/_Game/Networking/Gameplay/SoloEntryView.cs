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
        private bool wired;
        private void Start()
        {
            if(SoloButton==null||QuitButton==null||Status==null){SetButtons(false,false);enabled=false;return;}
            SoloButton.onClick.AddListener(ChooseSolo);QuitButton.onClick.AddListener(Quit);wired=true;
            if(CoopStatus!=null)CoopStatus.text="Совместная игра пока недоступна в этой сборке.";
            BindCurrent();
        }
        private void Update()
        {
            // Cancelling a deferred connection can replace its root while ProductEntry stays loaded.
            if(wired&&(startup==null||!startup.isActiveAndEnabled))BindCurrent();
        }
        private void BindCurrent()
        {
            var roots=FindObjectsByType<SoloSessionStartup>(FindObjectsSortMode.None);
            var next=roots.Length==1?roots[0]:null;
            if(startup!=null&&startup==next)return;
            if(startup!=null)startup.Changed-=Refresh;
            startup=next;if(startup!=null)startup.Changed+=Refresh;
            Refresh();
        }
        private void ChooseSolo(){if(startup!=null)startup.StartSolo();}
        private void Quit(){if(startup!=null)startup.Quit();}
        private void SetButtons(bool solo,bool quit){if(SoloButton!=null)SoloButton.interactable=solo;if(QuitButton!=null)QuitButton.interactable=quit;if(CoopButton!=null)CoopButton.interactable=false;}
        private void Refresh()
        {
            if(startup==null){SetButtons(false,false);if(Status!=null)Status.text="Открываем главное меню…";return;}
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
