namespace HowToSuck
{
    public enum ShopOfferKind { Unavailable, ReadOnly, Saving, PendingPurchase, PendingResult, Busy, MaximumTier, Insufficient, Available }
    // UI availability only; ProgressionService remains the sole purchase authority.
    public readonly struct ShopOffer
    {
        public readonly ShopOfferKind Kind;
        public readonly long MissingFunds;
        public bool CanBuy=>Kind==ShopOfferKind.Available;
        public bool CanRetry=>Kind==ShopOfferKind.PendingPurchase||Kind==ShopOfferKind.PendingResult;
        public ShopOffer(ShopOfferKind kind,long missing=0){Kind=kind;MissingFunds=missing;}
    }
    public static class ShopOfferPolicy
    {
        public static ShopOffer Evaluate(bool lobby,bool authority,bool campaignKnown,bool saving,bool pending,bool purchasePending,
            bool canStartRun,long balance,bool hasNext,long nextPrice)
        {
            if(!lobby||!campaignKnown||balance<0||nextPrice<0)return new ShopOffer(ShopOfferKind.Unavailable);
            if(!authority)return new ShopOffer(ShopOfferKind.ReadOnly);
            if(saving)return new ShopOffer(ShopOfferKind.Saving);
            if(pending)return new ShopOffer(purchasePending?ShopOfferKind.PendingPurchase:ShopOfferKind.PendingResult);
            if(!canStartRun)return new ShopOffer(ShopOfferKind.Busy);
            if(!hasNext)return new ShopOffer(ShopOfferKind.MaximumTier);
            if(balance<nextPrice)return new ShopOffer(ShopOfferKind.Insufficient,nextPrice-balance);
            return new ShopOffer(ShopOfferKind.Available);
        }
    }
}
