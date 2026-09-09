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
        public ProductCoopPanelView CoopView;
        private SoloSessionStartup startup;
        private bool wired;
        private void Start()
        {
            if(SoloButton==null||QuitButton==null||Status==null){SetButtons(false,false);enabled=false;return;}
            SoloButton.onClick.AddListener(ChooseSolo);QuitButton.onClick.AddListener(Quit);if(CoopButton!=null)CoopButton.onClick.AddListener(OpenCoop);wired=true;
            if(CoopStatus!=null)HowToSuck.LocalizedText.Set(CoopStatus, "Совместная игра пока недоступна в этой сборке.");
            BindCurrent();
        }
        private void OnEnable(){if(wired)BindCurrent();}
        private void OnDisable(){if(startup!=null)startup.Changed-=Refresh;startup=null;SetButtons(false,false);}
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
            startup=next;if(startup!=null)startup.Changed+=Refresh;if(CoopView!=null)CoopView.Bind(startup);
            Refresh();
        }
        private void OpenCoop(){if(CoopView!=null)CoopView.Open();}
        private void ChooseSolo(){if(startup!=null)startup.StartSolo();}
        private void Quit(){if(startup!=null)startup.Quit();}
        private void SetButtons(bool solo,bool quit){if(SoloButton!=null)SoloButton.interactable=solo;if(QuitButton!=null)QuitButton.interactable=quit;if(CoopButton!=null)CoopButton.interactable=solo&&CoopView!=null;}
        private void Refresh()
        {
            if(startup==null){SetButtons(false,false);if(Status!=null)HowToSuck.LocalizedText.Set(Status, "Открываем главное меню…");return;}
            SetButtons(startup.CanStartSolo,startup.CanQuit);if(Status!=null)HowToSuck.LocalizedText.Set(Status, startup.Status);
            if(CoopStatus!=null)HowToSuck.LocalizedText.Set(CoopStatus, CoopView!=null?"":startup.CoopStatus);
            if(EventSystem.current!=null&&EventSystem.current.currentSelectedGameObject==null)
            {
                if(startup.CanStartSolo)EventSystem.current.SetSelectedGameObject(SoloButton.gameObject);
                else if(startup.Phase==SoloEntryPhase.Failed&&startup.CanQuit)EventSystem.current.SetSelectedGameObject(QuitButton.gameObject);
            }
        }
        private void OnDestroy(){if(startup!=null)startup.Changed-=Refresh;if(SoloButton!=null)SoloButton.onClick.RemoveListener(ChooseSolo);if(QuitButton!=null)QuitButton.onClick.RemoveListener(Quit);if(CoopButton!=null)CoopButton.onClick.RemoveListener(OpenCoop);if(CoopView!=null)CoopView.Bind(null);}
    }
}
