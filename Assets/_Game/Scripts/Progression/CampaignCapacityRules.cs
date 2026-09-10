using System;
namespace HowToSuck
{
    // Initial authored working balance, separate from vacuum model prices.
    public static class CampaignCapacityRules
    {
        public const int MaximumPurchasedExtraSlots = 8;
        private static readonly long[] Prices = {140,240,400,650,1000,1500,2200,3200};
        public static bool TryModel(string tier,out int basis,out int maximum)
        {
            switch(tier){case "mk1":basis=1;maximum=3;return true;case "mk2":basis=2;maximum=5;return true;
                case "mk3":basis=3;maximum=7;return true;case "mk4":basis=4;maximum=9;return true;default:basis=maximum=0;return false;}
        }
        public static bool ValidBonus(int value)=>value>=0&&value<=MaximumPurchasedExtraSlots;
        public static int Effective(string tier,int purchasedExtraSlots)
        {
            if(!ValidBonus(purchasedExtraSlots)||!TryModel(tier,out int basis,out int maximum))throw new ArgumentException("A supported model and purchased bonus 0..8 are required.");
            return Math.Min(maximum,basis+purchasedExtraSlots);
        }
        public static bool TryNext(string tier,int purchasedExtraSlots,out int current,out int next,out long price)
        {
            current=next=0;price=0;if(!ValidBonus(purchasedExtraSlots)||!TryModel(tier,out _,out _))return false;
            current=next=Effective(tier,purchasedExtraSlots);if(purchasedExtraSlots==MaximumPurchasedExtraSlots)return false;
            next=Effective(tier,purchasedExtraSlots+1);if(next<=current)return false;
            price=Prices[purchasedExtraSlots]*(1+CampaignBalance.TierIndex(tier));return true;
        }
    }
}
