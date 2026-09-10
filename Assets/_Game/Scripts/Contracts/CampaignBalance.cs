using System;

namespace HowToSuck
{
    // Shared by encounter rules, editor authoring and the balance simulation.
    public static class CampaignBalance
    {
        public static int Stage(string contract) => Math.Max(0, Array.IndexOf(Contracts, contract));
        public static readonly string[] Contracts = { "old_house", "supermarket", "old_house_ii", "warehouse", "supermarket_ii", "warehouse_ii" };
        public static readonly long[] Quotas = { 1250, 2200, 2150, 3200, 3650, 5100 };
        // Rows follow Contracts, columns are 1–4 connected players. Calibrated
        // against recommended equipment without paid slots; see time-pressure report.
        private static readonly float[,] CrewDeadlines = {
            { 960, 870, 870, 900 },
            { 1020, 990, 1020, 1050 },
            { 960, 870, 900, 930 },
            { 780, 750, 750, 780 },
            { 1080, 1020, 1020, 1080 },
            { 900, 870, 900, 960 }
        };
        // Authored asset fallback describes the two-player contract.
        public static readonly float[] Deadlines = Array.ConvertAll(Contracts, id => TimeLimitForCrew(id, 2));
        public static float TimeLimitForCrew(string contract, int count)
        {
            if (count < 1 || count > 4) throw new ArgumentOutOfRangeException(nameof(count));
            int stage = Array.IndexOf(Contracts, contract);
            if (stage < 0) throw new ArgumentException("Unknown campaign contract.", nameof(contract));
            return CrewDeadlines[stage, count - 1];
        }
        public const int ReviveHealth = 50;
        public static readonly int[] BossHealth = { 2600, 4000, 3700, 5300, 5700, 7500 };
        public static readonly long[] ModelPrices = { 0, 1500, 4500, 6800 };
        public static readonly float[] SuctionPower = { 220, 440, 900, 1900 };
        public static readonly float[] IntakeSize = { .45f, 1.1f, 2.1f, 4f };
        public static float CrewHealth(int count, bool boss)
        {
            if (count < 1 || count > 4) throw new ArgumentOutOfRangeException(nameof(count));
            return boss ? new[] { .8f, 1.85f, 2.95f, 4.1f }[count-1] : new[] { .9f, 1.5f, 2.05f, 2.6f }[count-1];
        }
        public static long QuotaForCrew(long quota, int count)
        {
            if (quota < 1) throw new ArgumentOutOfRangeException(nameof(quota));
            if (count < 1 || count > 4) throw new ArgumentOutOfRangeException(nameof(count));
            int percent = new[] { 85, 100, 118, 135 }[count-1];
            return Math.Max(1, checked(quota / 100 * percent + quota % 100 * percent / 100));
        }
        public static int TierIndex(string tier) => tier == "mk4" ? 3 : tier == "mk3" ? 2 : tier == "mk2" ? 1 : 0;
        public static float ShotMultiplier(string tier) => 1 + .08f * TierIndex(tier);
        // Heavy ammunition remains useful, but does not bypass an entire boss encounter.
        public static int BossHitDamage(int damage, bool recovering) => Math.Max(1, (int)Math.Round(damage * (recovering ? 1.35 : .65)));
    }
}
