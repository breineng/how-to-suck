using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
namespace HowToSuck
{
    [DisallowMultipleComponent]
    public sealed partial class ShopView : MonoBehaviour, ICancelHandler
    {
        public GameObject Panel;
        public Button OpenButton,BuyButton,RetryButton,CloseButton;
        public TMP_Text CampaignText,BalanceText,CurrentText,NextText,StatsText,PriceText,MessageText;
        public Image CurrentPreview,NextPreview,CurrentPowerFill,NextPowerFill,CurrentSizeFill,NextSizeFill;
        public TMP_Text CurrentStats,NextCapacity;
        public Image[] CapacityIcons;
        private SessionRoot session;
        private bool opened,working;
        private string notice;
        public bool IsOpen=>opened;
        public void Bind(SessionRoot root)
        {
            if(session==root){Refresh();return;}
            Unbind();session=root;
            if(session==null)return;
            session.Changed+=Refresh;
            if(CapacityBuyButton!=null)CapacityBuyButton.onClick.AddListener(BuyCapacity);
            OpenButton.onClick.AddListener(Open);BuyButton.onClick.AddListener(Buy);
            RetryButton.onClick.AddListener(Retry);CloseButton.onClick.AddListener(Close);
            Refresh();
        }
        private bool Definitions(out VacuumDefinition current,out VacuumDefinition next)
        {
            current=next=null;
            if(session?.Catalog?.Vacuums==null)return false;
            var entries=session.Catalog.Vacuums;
            for(int i=0;i<entries.Length;i++)if(entries[i]!=null&&entries[i].TierId==session.DisplayedTierId)
            {current=entries[i];if(i+1<entries.Length)next=entries[i+1];return true;}
            return false;
        }
        private ShopOffer Offer(VacuumDefinition current,VacuumDefinition next)
        {
            var p=session!=null?session.Progression:null;
            return ShopOfferPolicy.Evaluate(session!=null&&session.Phase==SessionPhase.Lobby,session!=null&&session.HasAuthority,
                current!=null,p!=null&&p.IsSaving,session!=null&&session.DisplayedSavePending,
                p?.PendingChange?.IsPurchase==true,p!=null&&p.CanStartRun,
                session!=null?session.DisplayedBalance:0,next!=null,next!=null?next.Price:0);
        }
        private void Open()
        {
            if(session==null||session.Phase!=SessionPhase.Lobby||!Definitions(out _,out _))return;
            opened=true;notice=null;Refresh();Select(BuyButton.IsInteractable()?BuyButton:RetryButton.IsInteractable()?RetryButton:CloseButton);
        }
        private void Close(){opened=false;notice=null;Refresh();if(OpenButton!=null&&OpenButton.IsInteractable())Select(OpenButton);}
        public void OnCancel(BaseEventData data)
        {
            if(!opened){GetComponentInParent<SessionMenuView>()?.OnCancel(data);return;}
            if(working)return;Close();data.Use();
        }
        private void Buy()
        {
            if(working||!opened||!Definitions(out var current,out var next)||!Offer(current,next).CanBuy)return;
            working=true;Refresh();string prior=current.TierId;
            try {
                if(session.PurchaseNextTier(next.TierId))Confirmed(prior);
                else notice=session.HasPendingSave?null:"Покупка недоступна. Проверьте баланс и текущий уровень.";
            }finally{working=false;Refresh();FocusAction();}
        }
        private void Retry()
        {
            if(working||!opened||!Definitions(out var current,out var next)||!Offer(current,next).CanRetry)return;
            working=true;Refresh();string prior=current.TierId;
            bool purchase=session.Progression.PendingChange?.Kind==CampaignChangeKind.Purchase;
            bool capacity=session.Progression.PendingChange?.Kind==CampaignChangeKind.CapacityPurchase;
            int priorBonus=session.Campaign.PurchasedExtraSlots;
            try {
                if(session.RetryCampaignSave()) {
                    if(purchase)Confirmed(prior);else if(capacity)ConfirmedCapacity(priorBonus);else notice="Сохранение завершено. Можно продолжать.";
                }
            }finally{working=false;Refresh();FocusAction();}
        }
        private void Confirmed(string priorTier)
        {
            if(session.Campaign==null||session.Campaign.CurrentTierId==priorTier)return;
            notice="Покупка сохранена. Новое оборудование выбрано для следующего контракта.";
            var audio=HowToSuck.Audio.GameAudioRoot.Current;
            // Campaign+new tier uniquely identifies this one-way confirmed upgrade, including save retries.
            if(audio!=null&&audio.Session==session)audio.PurchaseConfirmed(session.Campaign.CampaignId+":"+session.Campaign.CurrentTierId);
        }
        private void Refresh()
        {
            bool known=Definitions(out var current,out var next);
            bool lobby=isActiveAndEnabled&&session!=null&&session.Phase==SessionPhase.Lobby;
            if(!lobby||!known)opened=false;
            if(Panel!=null)Panel.SetActive(opened);
            if(OpenButton!=null)OpenButton.interactable=lobby&&known&&!working;
            if(!opened)return;
            var offer=Offer(current,next);
            HowToSuck.LocalizedText.Set(CampaignText, session.HasAuthority?"Ваша кампания · общее оборудование":"Кампания хоста · общее оборудование");
            HowToSuck.LocalizedText.Set(BalanceText, $"Баланс: ${session.DisplayedBalance:N0}");
            HowToSuck.LocalizedText.Set(CurrentText, current.DisplayName);
            HowToSuck.LocalizedText.Set(NextText, next!=null?next.DisplayName:"МАКСИМУМ");
            var shown=next!=null?next:current;
            if(CurrentPreview!=null){CurrentPreview.sprite=current.Preview;CurrentPreview.enabled=current.Preview!=null;}
            if(NextPreview!=null){NextPreview.sprite=shown.Preview;NextPreview.enabled=shown.Preview!=null;}
            float maxPower=1,maxSize=1;foreach(var definition in session.Catalog.Vacuums){maxPower=Mathf.Max(maxPower,definition.Power);maxSize=Mathf.Max(maxSize,definition.IntakeSize);}
            if(CurrentStats!=null)HowToSuck.LocalizedText.Set(CurrentStats, $"<line-height=66px>МОЩНОСТЬ  {current.Power:N0}\nПРИЁМНИК  {current.IntakeSize:0.##} м");
            if(CurrentPowerFill!=null)CurrentPowerFill.fillAmount=Mathf.Clamp01(current.Power/maxPower);
            if(NextPowerFill!=null)NextPowerFill.fillAmount=Mathf.Clamp01(shown.Power/maxPower);
            if(CurrentSizeFill!=null)CurrentSizeFill.fillAmount=Mathf.Clamp01(current.IntakeSize/maxSize);
            if(NextSizeFill!=null)NextSizeFill.fillAmount=Mathf.Clamp01(shown.IntakeSize/maxSize);
            HowToSuck.LocalizedText.Set(StatsText, $"<line-height=66px>МОЩНОСТЬ  {shown.Power:N0}\nПРИЁМНИК  {shown.IntakeSize:0.##} м");
            HowToSuck.LocalizedText.Set(PriceText, next!=null?$"Модель: ${next.Price:N0}":"Последняя модель уже приобретена");
            bool retryVisible=offer.CanRetry||offer.Kind==ShopOfferKind.Saving;
            bool actionWasSelected=EventSystem.current!=null&&(EventSystem.current.currentSelectedGameObject==BuyButton.gameObject||EventSystem.current.currentSelectedGameObject==RetryButton.gameObject);
            BuyButton.gameObject.SetActive(!retryVisible);
            BuyButton.interactable=!working&&offer.CanBuy;
            RetryButton.gameObject.SetActive(offer.CanRetry||offer.Kind==ShopOfferKind.Saving);
            RetryButton.interactable=!working&&offer.CanRetry;
            CloseButton.interactable=!working;
            var primary=retryVisible?RetryButton:BuyButton;
            var nav=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=CloseButton,selectOnRight=CloseButton,selectOnUp=CloseButton,selectOnDown=CloseButton};
            BuyButton.navigation=RetryButton.navigation=nav;
            var destination=primary.IsInteractable()?primary:CloseButton;
            CloseButton.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=destination,selectOnRight=destination,selectOnUp=destination,selectOnDown=destination};
            if(actionWasSelected&&!working&&(EventSystem.current.currentSelectedGameObject==null||!EventSystem.current.currentSelectedGameObject.activeInHierarchy))Select(destination);
            switch(offer.Kind) {
                case ShopOfferKind.ReadOnly:HowToSuck.LocalizedText.Set(MessageText, session.DisplayedSavePending?"Хост сохраняет изменения. Пока показаны подтверждённые баланс и оборудование.":"Улучшения покупает хозяин кампании. Вся команда получает их в следующем контракте.");break;
                case ShopOfferKind.Saving:HowToSuck.LocalizedText.Set(MessageText, "Сохранение покупки…");break;
                case ShopOfferKind.PendingPurchase:HowToSuck.LocalizedText.Set(MessageText, "Покупка пока не подтверждена. Показаны последние подтверждённые баланс и оборудование. Проверьте свободное место и доступ к папке сохранений, затем нажмите «Сохранить снова».");break;
                case ShopOfferKind.PendingResult:HowToSuck.LocalizedText.Set(MessageText, "Предыдущая выплата ещё не сохранена. Сначала повторите сохранение.");break;
                case ShopOfferKind.Insufficient:HowToSuck.LocalizedText.Set(MessageText, notice??$"Не хватает ${offer.MissingFunds:N0}. Завершайте контракты, чтобы заработать.");break;
                case ShopOfferKind.MaximumTier:HowToSuck.LocalizedText.Set(MessageText, notice??"Последняя модель уже приобретена. Вместимость улучшается отдельно.");break;
                case ShopOfferKind.Available:HowToSuck.LocalizedText.Set(MessageText, notice??"Покупка списывает деньги один раз и сохраняется для всей кампании.");break;
                default:HowToSuck.LocalizedText.Set(MessageText, "Магазин доступен между контрактами после сохранения кампании.");break;
            }
            RefreshCapacity(current,next);
        }
        private void FocusAction()
        {if(opened)Select(BuyButton.IsInteractable()?BuyButton:CapacityBuyButton!=null&&CapacityBuyButton.IsInteractable()?CapacityBuyButton:RetryButton.IsInteractable()?RetryButton:CloseButton);}
        private static void Select(Button button)
        {if(EventSystem.current!=null&&button!=null)EventSystem.current.SetSelectedGameObject(button.gameObject);}
        private void OnEnable(){Refresh();}
        private void OnDisable(){opened=false;if(Panel!=null)Panel.SetActive(false);}
        private void Unbind()
        {
            if(session!=null)session.Changed-=Refresh;
            if(CapacityBuyButton!=null)CapacityBuyButton.onClick.RemoveListener(BuyCapacity);
            if(OpenButton!=null)OpenButton.onClick.RemoveListener(Open);if(BuyButton!=null)BuyButton.onClick.RemoveListener(Buy);
            if(RetryButton!=null)RetryButton.onClick.RemoveListener(Retry);if(CloseButton!=null)CloseButton.onClick.RemoveListener(Close);
            session=null;opened=working=false;notice=null;if(Panel!=null)Panel.SetActive(false);
        }
        private void OnDestroy()=>Unbind();
    }
}
