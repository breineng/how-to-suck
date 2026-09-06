using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections.Generic;
namespace HowToSuck
{
    public sealed partial class ShopView
    {
        public Button CapacityBuyButton;
        public TMP_Text CapacityText,CapacityPriceText,CapacityMessageText;
        private ShopOffer CapacityOffer(out int bonus,out int current,out int next,out long price)
        {
            bonus=session?.DisplayedExtraSlots??-1;current=next=0;price=0;
            bool known=CampaignCapacityRules.ValidBonus(bonus)&&CampaignCapacityRules.TryModel(session?.DisplayedTierId,out _,out _);
            bool hasNext=known&&CampaignCapacityRules.TryNext(session.DisplayedTierId,bonus,out current,out next,out price);
            var p=session?.Progression;
            return ShopOfferPolicy.Evaluate(session!=null&&session.Phase==SessionPhase.Lobby,session!=null&&session.HasAuthority,
                known,p!=null&&p.IsSaving,session!=null&&session.DisplayedSavePending,p?.PendingChange?.IsPurchase==true,
                p!=null&&p.CanStartRun,session?.DisplayedBalance??0,hasNext,price);
        }
        private void BuyCapacity()
        {
            if(working||!opened||!CapacityOffer(out int bonus,out _,out _,out _).CanBuy)return;
            working=true;Refresh();
            try {
                if(session.PurchaseExtraSlot(bonus))ConfirmedCapacity(bonus);
                else notice=session.HasPendingSave?null:"Вместимость не куплена. Проверьте цену, баланс и максимум модели.";
            }finally{working=false;Refresh();FocusAction();}
        }
        private void ConfirmedCapacity(int prior)
        {
            if(session.Campaign==null||session.Campaign.PurchasedExtraSlots!=prior+1)return;
            notice="Дополнительный слот сохранён. Бонус сохраняется при смене модели.";
            var audio=HowToSuck.Audio.GameAudioRoot.Current;
            if(audio!=null&&audio.Session==session)audio.PurchaseConfirmed(session.Campaign.CampaignId+":capacity:"+session.Campaign.PurchasedExtraSlots);
        }
        private void RefreshCapacity(VacuumDefinition currentModel,VacuumDefinition nextModel)
        {
            // Compatibility with pre-authoring scenes: no invented guest data and no unbound controls.
            if(CapacityBuyButton==null||CapacityText==null||CapacityPriceText==null||CapacityMessageText==null)return;
            var offer=CapacityOffer(out int bonus,out int current,out int next,out long price);
            bool known=bonus>=0&&CampaignCapacityRules.TryModel(currentModel.TierId,out _,out _);
            bool pending=session.DisplayedSavePending;
            CapacityBuyButton.gameObject.SetActive(!pending);CapacityBuyButton.interactable=!working&&offer.CanBuy;
            if(!known){
                CapacityText.text="Вместимость: ожидаем данные кампании";CapacityPriceText.text="";
                CapacityMessageText.text="Покупки совершает хозяин кампании.";
            }else{
                CampaignCapacityRules.TryModel(currentModel.TierId,out int basis,out int max);
                CapacityText.text=$"Слоты: {current} / {max} · база {basis}, общий бонус +{bonus}";
                CapacityPriceText.text=next>current?$"Слот: {current} → {next} · ${price:N0}":$"Максимум этой модели: {max} слотов";
                if(nextModel!=null) {
                    int future=CampaignCapacityRules.Effective(nextModel.TierId,bonus);
                    CampaignCapacityRules.TryModel(nextModel.TierId,out _,out int futureMax);
                    StatsText.text+=$"\nСлоты при покупке модели: {current} → {future} / {futureMax}";
                }
                switch(offer.Kind){
                    case ShopOfferKind.ReadOnly:CapacityMessageText.text="Общий бонус сохраняется при смене модели. Покупает хост.";break;
                    case ShopOfferKind.Insufficient:CapacityMessageText.text=$"Для слота не хватает ${offer.MissingFunds:N0}.";break;
                    case ShopOfferKind.MaximumTier:CapacityMessageText.text="Бонус сохранён. Следующая модель покупается отдельно.";break;
                    case ShopOfferKind.PendingPurchase:case ShopOfferKind.PendingResult:case ShopOfferKind.Saving:
                        CapacityMessageText.text="Показана подтверждённая вместимость. Сначала завершите сохранение.";break;
                    default:CapacityMessageText.text="Слот и модель — отдельные покупки. Заполнять максимум перед сменой модели не нужно.";break;
                }
            }
            var buttons=new List<Button>();foreach(var b in new[]{BuyButton,CapacityBuyButton,RetryButton,CloseButton})if(b!=null&&b.gameObject.activeInHierarchy&&b.IsInteractable())buttons.Add(b);
            for(int i=0;i<buttons.Count;i++)buttons[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,
                selectOnLeft=buttons[(i+buttons.Count-1)%buttons.Count],selectOnUp=buttons[(i+buttons.Count-1)%buttons.Count],
                selectOnRight=buttons[(i+1)%buttons.Count],selectOnDown=buttons[(i+1)%buttons.Count]};
            var selected=EventSystem.current?.currentSelectedGameObject;
            if(!working&&(selected==CapacityBuyButton.gameObject||selected==BuyButton.gameObject||selected==RetryButton.gameObject)&&
                (!selected.activeInHierarchy||!selected.GetComponent<Button>().IsInteractable()))FocusAction();
        }
    }
}
