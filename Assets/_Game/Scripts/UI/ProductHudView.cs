using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HowToSuck
{
    [Serializable]
    public struct ProductHudItemIcon
    {
        public string TypeId;
        public Sprite Sprite;
        [Tooltip("Optional real definition for the next item's display name; TypeId must match.")]
        public SuckableDefinition Definition;
    }

    // A read-only presentation input. It grants no storage, combat or extraction authority.
    public readonly struct ProductHudState
    {
        public readonly ContractState Contract;
        public readonly int OwnerId;
        public readonly PlayerStorageSnapshot Storage;
        public readonly PlayerSuitPresentation Suit;
        public readonly bool FireBlocked, AtTruck, AllPlayersInExtraction;
        public readonly string CollectKey, FireKey, InteractKey;

        public ProductHudState(ContractState contract, int ownerId, PlayerStorageSnapshot storage,
            PlayerSuitPresentation suit, bool blocked, bool atTruck, bool allAtTruck,
            string collectKey, string fireKey, string interactKey)
        {
            Contract = contract; OwnerId = ownerId; Storage = storage; Suit = suit;
            FireBlocked = blocked; AtTruck = atTruck; AllPlayersInExtraction = allAtTruck;
            CollectKey = collectKey; FireKey = fireKey; InteractKey = interactKey;
        }
    }

    [DisallowMultipleComponent]
    public sealed class ProductHudView : MonoBehaviour
    {
        public GameObject Root, ExtractionPanel, HoldPanel, BlockedPanel;
        public TMP_Text DeliveredText, QuotaText, TimerText, BossTitleText, BossDetailText;
        public TMP_Text MoneyDeltaText;
        public TMP_Text StorageText, ReservedText, NextItemText, SuitCountText, SuitStatusText;
        public TMP_Text CollectHintText, FireHintText, ExtractionText, BlockedText;
        public Image QuotaFill, HoldFill;
        public Image SuitHealthFill;
        [Tooltip("Three authored fill images; the empty outlines remain in the prefab.")]
        public Image[] SuitSegments = new Image[3];
        public RectTransform SlotContainer;
        public RectTransform StorageHeader;
        public ProductHudSlotView SlotTemplate;
        public ProductHudItemIcon[] ItemIcons = Array.Empty<ProductHudItemIcon>();
        public Color Paper = new Color(.95f, .945f, .928f, 1f);
        public Color Accent = new Color(.82f, .025f, .03f, 1f);
        public Color Muted = new Color(.72f, .71f, .69f, 1f);

        readonly List<ProductHudSlotView> slots = new List<ProductHudSlotView>(16);
        int visibleSlots = -1;
        bool hasState;
        ProductHudState previous;
        double displayedMoney,moneyFrom;
        long moneyTarget,moneyDelta;
        float moneyAt=-10;
        Vector2 deltaPosition;
        TMP_Text boundMoneyDelta;

        public void SetVisible(bool visible) => Active(Root, visible);

        public void Present(in ProductHudState value)
        {
            var state = value.Contract;
            if(boundMoneyDelta!=MoneyDeltaText){boundMoneyDelta=MoneyDeltaText;if(boundMoneyDelta!=null)deltaPosition=boundMoneyDelta.rectTransform.anchoredPosition;}
            bool identityChanged = !hasState || previous.Contract.RunId != state.RunId || previous.OwnerId != value.OwnerId;
            bool knownStorage = value.OwnerId > 0 && value.Storage.IsKnown &&
                value.Storage.OwnerId == value.OwnerId && value.Storage.RunId == state.RunId;
            bool knownSuit = value.OwnerId > 0 && value.Suit.IsKnown &&
                value.Suit.State.OwnerId == value.OwnerId && value.Suit.State.RunId == state.RunId;

            if (identityChanged || previous.Contract.DeliveredValue != state.DeliveredValue || previous.Contract.Quota != state.Quota)
            {
                if(identityChanged||state.DeliveredValue<moneyTarget)
                {
                    displayedMoney=moneyFrom=moneyTarget=state.DeliveredValue;moneyAt=-10;moneyDelta=0;
                    Text(DeliveredText,$"${state.DeliveredValue:N0}");
                    if(MoneyDeltaText!=null){MoneyDeltaText.rectTransform.anchoredPosition=deltaPosition;MoneyDeltaText.alpha=0;}
                }
                else if(state.DeliveredValue>moneyTarget)
                {
                    moneyDelta=(Time.unscaledTime-moneyAt<.45f?moneyDelta:0)+state.DeliveredValue-moneyTarget;
                    moneyFrom=displayedMoney;moneyTarget=state.DeliveredValue;moneyAt=Time.unscaledTime;
                    Text(MoneyDeltaText,$"+${moneyDelta:N0}");
                    HowToSuck.Audio.GameAudioRoot.Current?.Action(HowToSuck.Audio.SfxId.MoneyAdded,Vector3.zero);
                }
                Text(QuotaText, $"/ ${state.Quota:N0}");
                Fill(QuotaFill, state.Quota > 0 ? (float)((double)state.DeliveredValue / state.Quota) : 0f);
            }
            int remaining = (int)Math.Max(0, Math.Ceiling(state.RemainingSeconds));
            if (identityChanged || (int)Math.Max(0, Math.Ceiling(previous.Contract.RemainingSeconds)) != remaining ||
                (previous.Contract.RemainingSeconds <= 20) != (state.RemainingSeconds <= 20))
            {
                Text(TimerText, $"{remaining / 60:00}:{remaining % 60:00}");
                Tint(TimerText, state.RemainingSeconds <= 20 ? Accent : Paper);
            }
            if (identityChanged || previous.Contract.Boss.Status != state.Boss.Status)
            {
                bool delivered = state.Boss.IsDelivered;
                bool defeated = state.Boss.Status == BossObjectiveStatus.Defeated;
                Text(BossTitleText, delivered ? "БОСС ДОСТАВЛЕН" : defeated ? "БОСС ПОВЕРЖЕН" : state.Boss.Status==BossObjectiveStatus.Unassigned?"БОСС СКРЫВАЕТСЯ":"ДОСТАВЬТЕ БОССА");
                Text(BossDetailText, delivered ? "Цель выполнена" : defeated ? "Выстрелите телом в приёмник грузовика" : state.Boss.Status==BossObjectiveStatus.Unassigned?"Появится после сдачи более 50% квоты":"Победите босса и доставьте тело в грузовик");
            }

            if (identityChanged || !previous.Storage.SameValues(value.Storage)) PresentStorage(value.Storage, knownStorage);
            bool previousSuitKnown = hasState && previous.OwnerId > 0 && previous.Suit.IsKnown &&
                previous.Suit.State.OwnerId == previous.OwnerId && previous.Suit.State.RunId == previous.Contract.RunId;
            if (identityChanged || previousSuitKnown != knownSuit || previous.Suit.State.Segments != value.Suit.State.Segments ||
                previous.Suit.State.RecoveryPending != value.Suit.State.RecoveryPending ||
                previous.Suit.InvulnerableAtObservation != value.Suit.InvulnerableAtObservation)
                PresentSuit(value.Suit, knownSuit);

            if (identityChanged || previous.CollectKey != value.CollectKey)
                Text(CollectHintText, Key(value.CollectKey) + " — СОБРАТЬ");
            bool bossNext = knownStorage && value.Storage.Count > 0 && value.Storage.NextCargoRole == CargoRole.BossBody;
            bool previousBossNext = hasState && previous.OwnerId > 0 && previous.Storage.IsKnown &&
                previous.Storage.OwnerId == previous.OwnerId && previous.Storage.RunId == previous.Contract.RunId &&
                previous.Storage.Count > 0 && previous.Storage.NextCargoRole == CargoRole.BossBody;
            if (identityChanged || previous.FireKey != value.FireKey || bossNext != previousBossNext)
                Text(FireHintText, Key(value.FireKey) + (bossNext ? " — ВЫПУСТИТЬ БОССА" : " — ВЫСТРЕЛ · УДЕРЖАТЬ — ЗАРЯД"));
            if (identityChanged || previous.FireBlocked != value.FireBlocked)
            {
                Active(BlockedPanel, value.FireBlocked);
                Text(BlockedText, value.FireBlocked ? "Нет места перед соплом — отойдите" : "");
            }

            Active(ExtractionPanel, value.AtTruck);
            bool canExtract = value.AtTruck && state.ObjectivesComplete && value.AllPlayersInExtraction;
            Active(HoldPanel, canExtract);
            Fill(HoldFill, canExtract ? (float)state.ExtractHoldProgress : 0f);
            if (value.AtTruck && (identityChanged || !previous.AtTruck || previous.Contract.QuotaReached != state.QuotaReached ||
                previous.Contract.Boss.Status != state.Boss.Status || previous.AllPlayersInExtraction != value.AllPlayersInExtraction ||
                (previous.Contract.ExtractHoldProgress > 0) != (state.ExtractHoldProgress > 0) || previous.InteractKey != value.InteractKey))
            {
                string prompt = !state.QuotaReached ? "Доставьте груз в грузовик и выполните квоту" : !state.Boss.IsDelivered ?
                    (state.Boss.Status == BossObjectiveStatus.Defeated ? "Доставьте побеждённого босса в грузовик" : "Победите босса и доставьте его в грузовик") :
                    !value.AllPlayersInExtraction ? "Все игроки должны вернуться к грузовику" :
                    state.ExtractHoldProgress > 0 ? "Удерживайте " + Key(value.InteractKey) + " — эвакуация" :
                    "Удерживайте " + Key(value.InteractKey) + " 2 секунды, чтобы уехать";
                Text(ExtractionText, prompt);
            }
            previous = value;
            hasState = true;
        }

        void PresentStorage(PlayerStorageSnapshot stored, bool known)
        {
            Text(StorageText, known ? $"ХРАНИЛИЩЕ {stored.Count} / {stored.Capacity}" : "ХРАНИЛИЩЕ —");
            Text(ReservedText, known && stored.Reserved > 0 ? $"ЗАГРУЖАЕТСЯ: {stored.Reserved}" : "");
            EnsureSlots(known ? stored.Capacity : 0);
            Sprite nextIcon = null;
            string nextName = "";
            if (known && stored.Count > 0 && ItemIcons != null)
                for (int i = 0; i < ItemIcons.Length; i++)
                {
                    var entry = ItemIcons[i];
                    if (entry.TypeId != stored.NextTypeId) continue;
                    nextIcon = entry.Sprite;
                    if (entry.Definition != null && entry.Definition.TypeId == stored.NextTypeId)
                        nextName = entry.Definition.DisplayName;
                    break;
                }
            Text(NextItemText, nextName);
            for (int i = 0; i < visibleSlots && i < slots.Count; i++)
            {
                Sprite icon=null;string type=stored.TypeAt(i);
                if(ItemIcons!=null)foreach(var entry in ItemIcons)if(entry.TypeId==type){icon=entry.Sprite;break;}
                slots[i].Present(i < stored.Count, i >= stored.Count && i < stored.Count + stored.Reserved,
                    i == 0 && stored.Count > 0, icon, Paper, Accent, Muted);
            }
        }

        void EnsureSlots(int capacity)
        {
            capacity = Mathf.Clamp(capacity, 0, 16);
            if (visibleSlots == capacity) return;
            visibleSlots = capacity;
            if (SlotTemplate == null || SlotContainer == null) return;
            var grid = SlotContainer.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                int columns = Mathf.Clamp(capacity, 1, 8), rows = capacity > 8 ? 2 : 1;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = columns;
                SlotContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                    columns * grid.cellSize.x + (columns - 1) * grid.spacing.x + grid.padding.horizontal);
                float height = rows * grid.cellSize.y + (rows - 1) * grid.spacing.y + grid.padding.vertical;
                SlotContainer.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                if (StorageHeader != null) StorageHeader.anchoredPosition = new Vector2(0f, height + 98f);
                if (ExtractionPanel != null && ExtractionPanel.transform is RectTransform extraction)
                {
                    extraction.anchorMin = extraction.anchorMax = extraction.pivot = new Vector2(.5f, 0f);
                    extraction.anchoredPosition = new Vector2(0f, height + 222f);
                }
            }
            Active(SlotTemplate.gameObject, false);
            while (slots.Count < capacity)
            {
                var slot = Instantiate(SlotTemplate, SlotContainer, false);
                slot.name = "Storage Slot " + (slots.Count + 1);
                slots.Add(slot);
            }
            for (int i = 0; i < slots.Count; i++) Active(slots[i].gameObject, i < capacity);
        }

        void PresentSuit(PlayerSuitPresentation protection, bool known)
        {
            Text(SuitCountText, known ? $"{protection.State.Health}%" : "—");
            Fill(SuitHealthFill,known?protection.State.Health/100f:0);
            Text(SuitStatusText, !known ? "" : protection.State.RecoveryPending ? "ВЫ ВЫВЕДЕНЫ ИЗ СТРОЯ" :
                protection.InvulnerableAtObservation ? "ЗАЩИТА" : "");
            Tint(SuitCountText, known && protection.State.Health <= 25 ? Accent : Paper);
            if (SuitSegments == null) return;
            for (int i = 0; i < SuitSegments.Length; i++)
                if (SuitSegments[i] != null)
                {
                    bool filled = SuitHealthFill==null&&known&&i<3&&i*34<protection.State.Health;
                    if (SuitSegments[i].enabled != filled) SuitSegments[i].enabled = filled;
                }
        }

        static string Key(string key) => InlineKeycaps.Key(key);
        void LateUpdate()
        {
            if(!hasState)return;
            float elapsed=Time.unscaledTime-moneyAt,t=Mathf.Clamp01(elapsed/.6f);
            displayedMoney=moneyFrom+(moneyTarget-moneyFrom)*(1-Math.Pow(1-t,3));
            Text(DeliveredText,$"${Math.Round(displayedMoney):N0}");
            if(DeliveredText!=null)DeliveredText.rectTransform.localScale=Vector3.one*(1+Mathf.Sin(t*Mathf.PI)*.08f);
            if(MoneyDeltaText!=null)
            {
                MoneyDeltaText.alpha=elapsed<.7f?1:Mathf.Clamp01((1.4f-elapsed)/.7f);
                MoneyDeltaText.rectTransform.anchoredPosition=deltaPosition+Vector2.up*Mathf.Clamp(elapsed,0,1.4f)*18;
            }
        }
        static void Active(GameObject target, bool value) { if (target != null && target.activeSelf != value) target.SetActive(value); }
        static void Text(TMP_Text label, string value) => InlineKeycaps.Set(label, value);
        static void Tint(Graphic graphic, Color value) { if (graphic != null && graphic.color != value) graphic.color = value; }
        static void Fill(Image image, float value) { value = Mathf.Clamp01(value); if (image != null && image.fillAmount != value) image.fillAmount = value; }
        void OnEnable() { hasState = false; }
    }
}
