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
        public Image MapPreview;
        public TMP_Text QuotaValue,TimeValue,FailureValue;
        public SessionMenuView Menu;
        public ShopView Shop;
        [Tooltip("Optional existing guest-ready action. Authored by the networking lobby without a Runtime-to-Networking dependency.")]
        public Button ReadyNavigationButton;
        private Button[] navigationButtons;private int navigationMask=-1;
        private SessionRoot session;
        private bool wasBlocked;
        private int shownCrew;
        public void Bind(SessionRoot root)
        {
            if(session==root){Refresh();return;}
            Unbind();session=root;if(session==null)return;
            var localMenu=Menu!=null?Menu.GetComponent<LocalMenuView>():GetComponentInParent<LocalMenuView>();
            // Nine optional actions fit the bitmask. Runtime visibility still owns host/guest and modal availability.
            navigationButtons=new[]{PreviousButton,NextButton,Menu!=null?Menu.StartButton:null,ReadyNavigationButton,
                Menu!=null?Menu.BackButton:null,Shop!=null?Shop.OpenButton:null,
                localMenu!=null?localMenu.SettingsButton:null};navigationMask=-1;
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
            bool blocked=Menu!=null&&Menu.ModalBlocksLobby;if(wasBlocked!=blocked||shownCrew!=(session?.DisplayedCrewSize??1))Refresh();RefreshNavigation();
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
            shownCrew=session?.DisplayedCrewSize??1;
            bool canChoose=isActiveAndEnabled&&session!=null&&session.HasAuthority&&session.Phase==SessionPhase.Lobby&&!wasBlocked&&index>=0&&entries.Length>1;
            if(PreviousButton!=null)PreviousButton.interactable=canChoose;if(NextButton!=null)NextButton.interactable=canChoose;
            if(NameText==null)return;
            if(selected==null){HowToSuck.LocalizedText.Set(NameText, "Выбор карты");HowToSuck.LocalizedText.Set(IndexText, "");HowToSuck.LocalizedText.Set(DetailsText, session!=null&&session.HasAuthority?"Доступная карта не найдена.":"Ожидаем выбор хозяина…");HowToSuck.LocalizedText.Set(RecommendationText, "");HowToSuck.LocalizedText.Set(RoleText, "Карту и начало контракта выбирает хозяин.");return;}
            HowToSuck.LocalizedText.Set(NameText, selected.DisplayName);HowToSuck.LocalizedText.Set(IndexText, (index+1)+" / "+entries.Length);
            if(MapPreview!=null){MapPreview.sprite=selected.Preview;MapPreview.enabled=selected.Preview!=null;}
            int seconds=Mathf.CeilToInt(selected.TimeLimitSeconds);
            long quota=session.DisplayedQuota(selected);
            if(QuotaValue!=null)HowToSuck.LocalizedText.Set(QuotaValue, $"${quota:N0}");
            if(TimeValue!=null)HowToSuck.LocalizedText.Set(TimeValue, $"{seconds/60}:{seconds%60:00}");
            if(FailureValue!=null)HowToSuck.LocalizedText.Set(FailureValue, $"При неудаче: {selected.FailurePercent}% сданной стоимости");
            HowToSuck.LocalizedText.Set(DetailsText, $"Квота: ${quota:N0}    Время: {seconds/60}:{seconds%60:00}\nПри неудаче: {selected.FailurePercent}% сданной стоимости\nЦель: квота и доставка побеждённого босса");
            if(QuotaValue!=null)HowToSuck.LocalizedText.Set(DetailsText, "Сдайте квоту и тело босса в грузовик.\nБосс появится после сдачи более 50% квоты.");
            string current=TierName(session.DisplayedTierId),recommended=TierName(selected.RecommendedTierId);
            HowToSuck.LocalizedText.Set(RecommendationText, (current!=null?"Команда: "+current:"Ожидаем оборудование…")+
                (recommended!=null?"\nРекомендуется: "+recommended:"\nЛюбая модель"));
            bool unlocked=session.DisplayedContractUnlocked(selected.ContractId);
            string prerequisite=CampaignContractAccess.Prerequisite(selected.ContractId),priorName=prerequisite;
            if(entries!=null)foreach(var entry in entries)if(entry!=null&&entry.ContractId==prerequisite)priorName=entry.DisplayName;
            HowToSuck.LocalizedText.Set(RoleText, !unlocked?"Чтобы открыть, завершите «"+priorName+"»":session.HasAuthority?"Выберите главу и начните контракт.":"Выбор хозяина · ожидаем начала контракта");
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
