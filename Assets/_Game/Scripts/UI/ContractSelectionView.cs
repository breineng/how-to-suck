using UnityEngine;
using UnityEngine.UI;
using TMPro;
namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed class ContractSelectionView : MonoBehaviour
    {
        public Button PreviousButton,NextButton;
        public TMP_Text NameText,IndexText,DetailsText,RecommendationText,RoleText;
        public SessionMenuView Menu;
        public ShopView Shop;
        private Button[] navigationButtons;private int navigationMask=-1;
        private SessionRoot session;
        private bool wasBlocked;
        public void Bind(SessionRoot root)
        {
            if(session==root){Refresh();return;}
            Unbind();session=root;if(session==null)return;
            navigationButtons=new[]{PreviousButton,NextButton,Menu!=null?Menu.StartButton:null,Menu!=null?Menu.BackButton:null,Shop!=null?Shop.OpenButton:null};navigationMask=-1;
            session.Changed+=Refresh;PreviousButton.onClick.AddListener(Previous);NextButton.onClick.AddListener(Next);Refresh();
        }
        private void Previous()=>Change(-1);
        private void Next()=>Change(1);
        private void Change(int direction)
        {
            if(session==null||!session.HasAuthority||session.Phase!=SessionPhase.Lobby||Menu!=null&&Menu.ModalBlocksLobby)return;
            var entries=session.Catalog?.Contracts;if(entries==null||entries.Length<2)return;
            int index=System.Array.IndexOf(entries,session.SelectedLobbyContract);if(index<0)return;
            session.SelectLobbyContract(entries[(index+direction+entries.Length)%entries.Length].ContractId);
        }
        private void Update()
        {
            bool blocked=Menu!=null&&Menu.ModalBlocksLobby;if(wasBlocked!=blocked)Refresh();RefreshNavigation();
        }
        private void RefreshNavigation()
        {
            if(navigationButtons==null)return;int mask=0;
            for(int i=0;i<navigationButtons.Length;i++)if(!wasBlocked&&navigationButtons[i]!=null&&navigationButtons[i].IsInteractable()&&navigationButtons[i].gameObject.activeInHierarchy)mask|=1<<i;
            if(mask==navigationMask)return;navigationMask=mask;
            for(int i=0;i<navigationButtons.Length;i++){
                var button=navigationButtons[i];if(button==null)continue;
                if((mask&(1<<i))==0){button.navigation=new Navigation{mode=Navigation.Mode.None};continue;}
                int before=i,after=i;
                do{before=(before+navigationButtons.Length-1)%navigationButtons.Length;}while((mask&(1<<before))==0);
                do{after=(after+1)%navigationButtons.Length;}while((mask&(1<<after))==0);
                button.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=navigationButtons[before],selectOnUp=navigationButtons[before],selectOnRight=navigationButtons[after],selectOnDown=navigationButtons[after]};
            }
        }
        private string TierName(string id)
        {
            if(session?.Catalog?.Vacuums!=null)foreach(var entry in session.Catalog.Vacuums)if(entry!=null&&entry.TierId==id)return entry.DisplayName;
            return null;
        }
        private void Refresh()
        {
            var entries=session?.Catalog?.Contracts;var selected=session?.SelectedLobbyContract;
            int index=entries!=null?System.Array.IndexOf(entries,selected):-1;
            wasBlocked=Menu!=null&&Menu.ModalBlocksLobby;
            bool canChoose=isActiveAndEnabled&&session!=null&&session.HasAuthority&&session.Phase==SessionPhase.Lobby&&!wasBlocked&&index>=0&&entries.Length>1;
            if(PreviousButton!=null)PreviousButton.interactable=canChoose;if(NextButton!=null)NextButton.interactable=canChoose;
            if(NameText==null)return;
            if(selected==null){NameText.text="Выбор карты";IndexText.text="";DetailsText.text=session!=null&&session.HasAuthority?"Доступная карта не найдена.":"Ожидаем выбор хозяина…";RecommendationText.text="";RoleText.text="Карту и начало контракта выбирает хозяин.";return;}
            NameText.text=selected.DisplayName;IndexText.text=(index+1)+" / "+entries.Length;
            int seconds=Mathf.CeilToInt(selected.TimeLimitSeconds);
            DetailsText.text=$"Квота: ${selected.Quota:N0}    Время: {seconds/60}:{seconds%60:00}\nПри неудаче: {selected.FailurePercent}% собранной суммы\nДосрочный выход: без выплаты";
            string current=TierName(session.DisplayedTierId),recommended=TierName(selected.RecommendedTierId);
            RecommendationText.text=(current!=null?"Оборудование команды: "+current:"Ожидаем оборудование команды…")+
                (recommended!=null?"\nРекомендуется "+recommended+". Можно играть с любым уровнем.":"\nКарта доступна с любым уровнем оборудования.");
            RoleText.text=session.HasAuthority?"Выберите карту и начните контракт.":"Выбор хозяина · ожидаем начала контракта";
        }
        private void Unbind()
        {
            if(session!=null)session.Changed-=Refresh;
            if(PreviousButton!=null)PreviousButton.onClick.RemoveListener(Previous);if(NextButton!=null)NextButton.onClick.RemoveListener(Next);session=null;
        }
        private void OnEnable()=>Refresh();
        private void OnDestroy()=>Unbind();
    }
}
