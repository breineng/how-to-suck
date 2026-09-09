using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HowToSuck
{
    public sealed class ContractHud : MonoBehaviour
    {
        public GameObject HudPanel;
        public TMP_Text MoneyText,TimerText,BossText,StorageText,SuitText;
        public GameObject ExtractionPanel;
        public TMP_Text ExtractionText;
        public Image HoldFill;
        [Tooltip("Opt in to the approved product HUD. Unassigned scenes retain the legacy presentation.")]
        public ProductHudView ProductView;
        private SessionRoot session;
        private PlayerMotor owner;
        private PlayerStorageView storage;
        private PlayerSuitView suit;
        private PlayerInputReader input;
        private LocalBindingsController bindings;
        private bool inputInitialized;
        private string collectKey="—",fireKey="—",interactKey="—";
        private bool subscribed;
        private double blockedUntil;
        private static readonly Color Paper=new Color(.97f,.94f,.84f),Warning=new Color(1f,.54f,.32f),Complete=new Color(.6f,.9f,.7f);

        public void Bind(SessionRoot root)
        {
            Detach();session=root;
            if(isActiveAndEnabled)Subscribe();
            Refresh();
        }
        private void Subscribe()
        {
            if(session==null||subscribed)return;
            session.Changed+=Refresh;bindings=session.GetComponent<LocalBindingsController>();
            if(bindings!=null)bindings.Changed+=RefreshBindings;
            subscribed=true;
        }
        private void ObserveOwner(PlayerMotor player)
        {
            if(owner==player)return;
            DetachOwner();owner=player;if(owner==null)return;
            storage=owner.GetComponent<PlayerStorageView>();suit=owner.GetComponent<PlayerSuitView>();
            input=owner.GetComponent<PlayerInputReader>();
            if(input!=null)input.MenuChanged+=OnMenuChanged;
            ReadBindings();
            owner.FireFeedbackChanged+=OnFireFeedback;
            if(storage!=null)storage.Changed+=Refresh;if(suit!=null)suit.Changed+=Refresh;
        }
        private static void Text(TMP_Text label,string value)=>InlineKeycaps.Set(label,value);
        private void OnFireFeedback()
        {blockedUntil=owner!=null&&owner.FireWasBlocked?Time.unscaledTimeAsDouble+2.5:0;Refresh();}
        private void LateUpdate()
        {if(blockedUntil>0&&Time.unscaledTimeAsDouble>=blockedUntil){blockedUntil=0;Refresh();}}
        private void Refresh()
        {
            bool playing=session!=null&&session.Phase==SessionPhase.Playing;
            if(HudPanel!=null)HudPanel.SetActive(playing);
            ObserveOwner(playing?session.LocalPlayer:null);
            if(ProductView!=null)ProductView.SetVisible(playing&&input!=null&&!input.MenuOpen&&!input.LocalModalOpen);
            if(!playing){if(ExtractionPanel!=null)ExtractionPanel.SetActive(false);return;}
            var state=session.ContractState;
            if(ProductView!=null)
            {
                if(input!=null&&inputInitialized!=input.IsInitialized)ReadBindings();
                bool inside=owner!=null&&session.World!=null&&session.World.IsPlayerInExtraction(owner.PlayerId);
                ProductView.Present(new ProductHudState(state,owner!=null?owner.PlayerId:0,
                    storage!=null?storage.Value:default,suit!=null?suit.Value:default,
                    owner!=null&&blockedUntil>Time.unscaledTimeAsDouble,inside,
                    session.World!=null&&session.World.AllPlayersInExtraction,collectKey,fireKey,interactKey));
                return;
            }
            Text(MoneyText,$"Сдано ${state.DeliveredValue:N0} / ${state.Quota:N0}");
            double seconds=state.RemainingSeconds;
            Text(TimerText,$"{Math.Floor(seconds/60):00}:{seconds%60:00}");
            if(TimerText!=null)TimerText.color=seconds<=20?Warning:Paper;
            Text(BossText,state.Boss.IsDelivered?"Босс доставлен":state.Boss.Status==BossObjectiveStatus.Defeated?
                "Босс повержен — доставьте тело в грузовик":"Босс: победите и доставьте в грузовик");
            if(BossText!=null)BossText.color=state.Boss.IsDelivered?Complete:Paper;
            var stored=storage!=null?storage.Value:default;
            bool blocked=owner!=null&&blockedUntil>Time.unscaledTimeAsDouble;
            string inventory="Хранилище: —";
            if(stored.IsKnown&&stored.RunId==state.RunId&&owner!=null&&stored.OwnerId==owner.PlayerId)
            {
                inventory=$"Хранилище: {stored.Count} / {stored.Capacity}"+(stored.Reserved>0?$" · загружается: {stored.Reserved}":"");
                inventory+="\n"+(blocked?"Нет места перед соплом — отойдите":stored.Count>0?InlineKeycaps.Key(fireKey)+(stored.NextCargoRole==CargoRole.BossBody?" — выпустить босса":" — выстрелить предметом"):
                    InlineKeycaps.Key(collectKey)+" — собрать предмет");
            }
            Text(StorageText,inventory);
            if(StorageText!=null)StorageText.color=blocked?Warning:Paper;
            var protection=suit!=null?suit.Value:default;
            bool known=protection.IsKnown&&protection.State.RunId==state.RunId&&owner!=null&&protection.State.OwnerId==owner.PlayerId;
            Text(SuitText,!known?"Костюм: —":protection.State.RecoveryPending?"Вы выведены из строя — требуется подъём":
                $"Костюм: {protection.State.Segments}%"+(protection.InvulnerableAtObservation?" · защита":""));
            if(SuitText!=null)SuitText.color=known&&protection.State.Segments<=25?Warning:Paper;
            bool atTruck=owner!=null&&session.World.IsPlayerInExtraction(owner.PlayerId);
            if(ExtractionPanel!=null)ExtractionPanel.SetActive(atTruck);
            if(!atTruck)return;
            string prompt=!state.QuotaReached?"Доставьте груз в грузовик и выполните квоту":!state.Boss.IsDelivered?
                (state.Boss.Status==BossObjectiveStatus.Defeated?"Доставьте побеждённого босса в грузовик":"Победите босса и доставьте его в грузовик"):
                !session.World.AllPlayersInExtraction?"Все игроки должны вернуться к грузовику":
                state.ExtractHoldProgress>0?$"Удерживайте {InlineKeycaps.Key(interactKey)} — эвакуация":$"Удерживайте {InlineKeycaps.Key(interactKey)} 2 секунды, чтобы уехать";
            Text(ExtractionText,prompt);
            if(HoldFill!=null){HoldFill.transform.parent.gameObject.SetActive(state.ObjectivesComplete&&session.World.AllPlayersInExtraction);HoldFill.fillAmount=(float)state.ExtractHoldProgress;}
        }
        private void OnMenuChanged(bool open){if(!open)RefreshBindings();else Refresh();}
        private void ReadBindings()
        {
            inputInitialized=input!=null&&input.IsInitialized;
            collectKey=input!=null?input.GameplayBindingDisplay("Vacuum"):"—";
            fireKey=input!=null?input.GameplayBindingDisplay("Fire"):"—";
            interactKey=input!=null?input.GameplayBindingDisplay("Interact"):"—";
        }
        private void RefreshBindings(){ReadBindings();Refresh();}
        private void DetachOwner()
        {
            if(owner!=null)owner.FireFeedbackChanged-=OnFireFeedback;
            if(storage!=null)storage.Changed-=Refresh;if(suit!=null)suit.Changed-=Refresh;
            if(input!=null)input.MenuChanged-=OnMenuChanged;
            owner=null;storage=null;suit=null;input=null;inputInitialized=false;
            collectKey=fireKey=interactKey="—";blockedUntil=0;
        }
        private void Detach()
        {
            if(subscribed&&session!=null)session.Changed-=Refresh;
            if(bindings!=null)bindings.Changed-=RefreshBindings;
            bindings=null;subscribed=false;DetachOwner();
        }
        private void OnEnable(){Subscribe();Refresh();}
        private void OnDisable()=>Detach();
        private void OnDestroy(){Detach();session=null;}
    }
}
