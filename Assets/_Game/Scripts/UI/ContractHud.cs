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
        private SessionRoot session;
        private PlayerMotor owner;
        private PlayerStorageView storage;
        private PlayerSuitView suit;
        private bool subscribed;
        private static readonly Color Paper=new Color(.97f,.94f,.84f),Warning=new Color(1f,.54f,.32f),Complete=new Color(.6f,.9f,.7f);

        public void Bind(SessionRoot root)
        {
            Detach();session=root;
            if(isActiveAndEnabled)Subscribe();
            Refresh();
        }
        private void Subscribe(){if(session==null||subscribed)return;session.Changed+=Refresh;subscribed=true;}
        private void ObserveOwner(PlayerMotor player)
        {
            if(owner==player)return;
            DetachOwner();owner=player;if(owner==null)return;
            storage=owner.GetComponent<PlayerStorageView>();suit=owner.GetComponent<PlayerSuitView>();
            if(storage!=null)storage.Changed+=Refresh;if(suit!=null)suit.Changed+=Refresh;
        }
        private static void Text(TMP_Text label,string value){if(label!=null&&label.text!=value)label.text=value;}
        private void Refresh()
        {
            bool playing=session!=null&&session.Phase==SessionPhase.Playing;
            if(HudPanel!=null)HudPanel.SetActive(playing);
            ObserveOwner(playing?session.LocalPlayer:null);
            if(!playing){if(ExtractionPanel!=null)ExtractionPanel.SetActive(false);return;}
            var state=session.ContractState;
            Text(MoneyText,$"Сдано ${state.DeliveredValue:N0} / ${state.Quota:N0}");
            double seconds=state.RemainingSeconds;
            Text(TimerText,$"{Math.Floor(seconds/60):00}:{seconds%60:00}");
            if(TimerText!=null)TimerText.color=seconds<=20?Warning:Paper;
            Text(BossText,state.Boss.IsDelivered?"Босс доставлен":state.Boss.Status==BossObjectiveStatus.Defeated?
                "Босс повержен — доставьте тело в грузовик":"Босс: победите и доставьте в грузовик");
            if(BossText!=null)BossText.color=state.Boss.IsDelivered?Complete:Paper;
            var stored=storage!=null?storage.Value:default;
            string inventory="Хранилище: —";
            if(stored.IsKnown&&stored.RunId==state.RunId&&owner!=null&&stored.OwnerId==owner.PlayerId)
            {
                inventory=$"Хранилище: {stored.Count} / {stored.Capacity}"+(stored.Reserved>0?$" · загружается: {stored.Reserved}":"");
                inventory+="\n"+(stored.Count>0?(stored.NextCargoRole==CargoRole.BossBody?"ЛКМ — выпустить босса":"ЛКМ — выстрелить предметом"):
                    "ПКМ — собрать предмет");
            }
            Text(StorageText,inventory);
            var protection=suit!=null?suit.Value:default;
            bool known=protection.IsKnown&&protection.State.RunId==state.RunId&&owner!=null&&protection.State.OwnerId==owner.PlayerId;
            Text(SuitText,!known?"Костюм: —":protection.State.RecoveryPending?"Возвращение к грузовику · −15 с":
                $"Костюм: {protection.State.Segments} / 3"+(protection.InvulnerableAtObservation?" · защита":""));
            if(SuitText!=null)SuitText.color=known&&protection.State.Segments<=1?Warning:Paper;
            bool atTruck=owner!=null&&session.World.IsPlayerInExtraction(owner.PlayerId);
            if(ExtractionPanel!=null)ExtractionPanel.SetActive(atTruck);
            if(!atTruck)return;
            string prompt=!state.QuotaReached?"Доставьте груз в грузовик и выполните квоту":!state.Boss.IsDelivered?
                (state.Boss.Status==BossObjectiveStatus.Defeated?"Доставьте побеждённого босса в грузовик":"Победите босса и доставьте его в грузовик"):
                !session.World.AllPlayersInExtraction?"Все игроки должны вернуться к грузовику":
                state.ExtractHoldProgress>0?"Удерживайте E — эвакуация":"Удерживайте E 2 секунды, чтобы уехать";
            Text(ExtractionText,prompt);
            if(HoldFill!=null){HoldFill.transform.parent.gameObject.SetActive(state.ObjectivesComplete&&session.World.AllPlayersInExtraction);HoldFill.fillAmount=(float)state.ExtractHoldProgress;}
        }
        private void DetachOwner(){if(storage!=null)storage.Changed-=Refresh;if(suit!=null)suit.Changed-=Refresh;owner=null;storage=null;suit=null;}
        private void Detach(){if(subscribed&&session!=null)session.Changed-=Refresh;subscribed=false;DetachOwner();}
        private void OnEnable(){Subscribe();Refresh();}
        private void OnDisable()=>Detach();
        private void OnDestroy(){Detach();session=null;}
    }
}
