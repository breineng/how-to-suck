using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;
using System.Collections;

namespace HowToSuck
{
    public sealed class CampaignRecoveryView : MonoBehaviour, ICancelHandler
    {
        public GameObject Panel;
        public TMP_Text Title, Message;
        public Button RetryButton, NewButton, ConfirmButton, CancelButton, QuitButton;
        public bool Confirming => confirmation != null;
        private SessionRoot session;
        private SaveOpenResult confirmation;
        private Coroutine focusReturn;

        public void Bind(SessionRoot root)
        {
            if(session==root){Refresh();return;}
            Unbind();session=root;
            if(session==null)return;
            session.Changed+=Refresh;
            RetryButton.onClick.AddListener(Retry);
            NewButton.onClick.AddListener(AskNew);
            ConfirmButton.onClick.AddListener(ConfirmNew);
            CancelButton.onClick.AddListener(CancelNew);
            QuitButton.onClick.AddListener(Quit);
            Refresh();
        }
        private bool Blocked => session!=null && session.HasAuthority && session.Progression==null &&
            session.Phase==SessionPhase.Lobby && session.CampaignOpenStatus!=null && !session.CampaignOpenStatus.Ready;
        private void Retry(){if(!Blocked)return;confirmation=null;session.RetryCampaignOpen();Refresh();}
        private void AskNew(){if(!Blocked||session.CampaignOpenStatus.Kind!=SaveOpenKind.Corrupt)return;confirmation=session.CampaignOpenStatus;Refresh();}
        private void ConfirmNew()
        {
            if(!Blocked||confirmation==null||!ReferenceEquals(confirmation,session.CampaignOpenStatus)||confirmation.Kind!=SaveOpenKind.Corrupt){confirmation=null;Refresh();return;}
            var observed=confirmation;confirmation=null;session.StartNewCampaignAfterCorruption(observed);Refresh();
        }
        private void CancelNew(){confirmation=null;Refresh();}
        public void OnCancel(BaseEventData data){if(!Blocked||confirmation==null)return;CancelNew();data.Use();}
        private void Quit(){if(Blocked)Application.Quit();}
        private void Refresh()
        {
            if(Panel==null)return;
            bool blocked=Blocked;
            if(!blocked || !ReferenceEquals(confirmation,session.CampaignOpenStatus))confirmation=null;
            bool wasVisible=Panel.activeSelf;
            var selectedBefore=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null;
            bool ownedSelection=selectedBefore!=null&&selectedBefore.transform.IsChildOf(Panel.transform);
            Panel.SetActive(blocked);
            if(!blocked)
            {
                // Return focus only when this modal owned it; do not steal another menu's selection.
                if(wasVisible&&ownedSelection&&EventSystem.current!=null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                    if(focusReturn!=null)StopCoroutine(focusReturn);
                    focusReturn=StartCoroutine(ReturnMenuFocus(session));
                }
                return;
            }
            bool confirm=confirmation!=null,corrupt=session.CampaignOpenStatus.Kind==SaveOpenKind.Corrupt;
            Title.text=confirm?"Начать новую кампанию?":"Кампания недоступна";
            Message.text=confirm?"Вы начнёте с MK1 и нулевым балансом. Исходные повреждённые файлы останутся в папке сохранений.":Describe(session.CampaignOpenStatus.Kind);
            RetryButton.gameObject.SetActive(!confirm);
            NewButton.gameObject.SetActive(!confirm&&corrupt);
            ConfirmButton.gameObject.SetActive(confirm);
            CancelButton.gameObject.SetActive(confirm);
            QuitButton.gameObject.SetActive(!confirm);
            var buttons=new List<Button>();
            // The safe cancel action receives initial keyboard focus in the confirmation.
            if(confirm){buttons.Add(CancelButton);buttons.Add(ConfirmButton);}
            else{buttons.Add(RetryButton);if(corrupt)buttons.Add(NewButton);buttons.Add(QuitButton);}
            for(int i=0;i<buttons.Count;i++)
            {
                buttons[i].interactable=true;
                var previous=buttons[(i+buttons.Count-1)%buttons.Count];var next=buttons[(i+1)%buttons.Count];
                buttons[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=previous,selectOnLeft=previous,selectOnDown=next,selectOnRight=next};
            }
            if(EventSystem.current!=null)
            {
                var selected=EventSystem.current.currentSelectedGameObject;
                if(!buttons.Exists(button=>button.gameObject==selected))EventSystem.current.SetSelectedGameObject(buttons[0].gameObject);
            }
        }
        private IEnumerator ReturnMenuFocus(SessionRoot owner)
        {
            // All Changed subscribers must first update their button availability, in any subscription order.
            yield return null;
            focusReturn=null;
            if(session!=owner||Blocked||EventSystem.current==null||EventSystem.current.currentSelectedGameObject!=null)yield break;
            foreach(var menu in FindObjectsByType<SessionMenuView>(FindObjectsSortMode.None))
                if(menu.StartButton!=null&&menu.StartButton.IsInteractable()){EventSystem.current.SetSelectedGameObject(menu.StartButton.gameObject);yield break;}
        }
        private static string Describe(SaveOpenKind kind)
        {
            switch(kind)
            {
                case SaveOpenKind.Corrupt:return "Не удалось восстановить кампанию из основного или резервного файла. Можно повторить загрузку или начать новую кампанию. Исходные файлы будут сохранены.";
                case SaveOpenKind.FutureSchema:return "Эта кампания создана в более новой версии игры. Обновите игру, чтобы продолжить. Сохранение не изменено.";
                case SaveOpenKind.UnknownTier:return "В кампании есть оборудование, которого нет в этой версии игры. Проверьте версию игры. Сохранение не изменено.";
                case SaveOpenKind.OrphanTemporary:return "Обнаружен незавершённый файл сохранения. Игра сохранила его и остановила загрузку кампании. Обратитесь в поддержку, чтобы восстановить прогресс.";
                default:return "Не удалось открыть сохранение кампании. Проверьте доступ к папке сохранений, закройте другие экземпляры игры и повторите загрузку.";
            }
        }
        private void Unbind()
        {
            if(focusReturn!=null){StopCoroutine(focusReturn);focusReturn=null;}
            if(session!=null)session.Changed-=Refresh;
            if(RetryButton!=null)RetryButton.onClick.RemoveListener(Retry);
            if(NewButton!=null)NewButton.onClick.RemoveListener(AskNew);
            if(ConfirmButton!=null)ConfirmButton.onClick.RemoveListener(ConfirmNew);
            if(CancelButton!=null)CancelButton.onClick.RemoveListener(CancelNew);
            if(QuitButton!=null)QuitButton.onClick.RemoveListener(Quit);
            session=null;confirmation=null;
        }
        private void OnDestroy()=>Unbind();
    }
}
