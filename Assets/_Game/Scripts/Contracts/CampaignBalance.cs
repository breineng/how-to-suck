using System;

namespace HowToSuck
{
    // Shared by encounter rules, editor authoring and the balance simulation.
    public static class CampaignBalance
    {
        public static int Stage(string contract) => Math.Max(0, Array.IndexOf(Contracts, contract));
        public static readonly string[] Contracts = { "old_house", "supermarket", "old_house_ii", "warehouse", "supermarket_ii", "warehouse_ii" };
        public static readonly long[] Quotas = { 1250, 2200, 2150, 3200, 3650, 5100 };
        // Rows follow Contracts, columns are 1–4 connected players. Includes
        // ordinary encounters and the full physical ammunition retrieval cycle.
        private static readonly float[,] CrewDeadlines = {
            { 1080, 900, 900, 900 },
            { 690, 540, 510, 510 },
            { 1020, 810, 720, 690 },
            { 810, 630, 570, 570 },
            { 1110, 870, 810, 810 },
            { 1440, 1140, 990, 1020 }
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
        public static readonly int[] BossHealth = { 240, 500, 700, 800, 1000, 2000 };
        public static readonly int[] BossContactDamage = { 14, 16, 18, 20, 22, 26 };
        public static readonly int[] BossRangedDamage = { 12, 14, 16, 18, 20, 23 };
        public static readonly float[] BossMoveSpeed = { 3f, 3.25f, 3.5f, 3.75f, 4f, 4.5f };
        public static readonly float[] BossRecoverySeconds = { 2.2f, 2f, 1.85f, 1.7f, 1.55f, 1.35f };
        public static readonly float[] BossTellSeconds = { 1.15f, 1.1f, 1.05f, 1f, .9f, .8f };
        public static readonly int[] BossRevealSeconds = { 300, 330, 360, 390, 420, 420 };
        public static readonly int[] ReinforcementBudget = { 0, 0, 1, 2, 2, 4 };
        public static readonly int[] ReinforcementSeconds = { 100, 100, 95, 85, 75, 65 };
        public static int EnemyCap(int stage, int crew) => Math.Min(5, 1 + crew / 2 + stage / 3);
        public static int BossEscortCap(int stage, int crew) => Math.Max(1, crew / 2 + (stage == 5 ? 1 : 0));
        public static float EnemyHealthMultiplier(int stage) => new[] {1f,1.45f,1.85f,2.25f,2.65f,3.25f}[stage];
        public static float EnemyDamageMultiplier(int stage) => 1 + .08f * stage;
        public static float EnemySpeedMultiplier(int stage) => 1 + .045f * stage;
        public static double RevealDelay(string contract)
        {
            int stage = Array.IndexOf(Contracts, contract);
            return stage < 0 ? double.PositiveInfinity : BossRevealSeconds[stage];
        }
        public static readonly long[] ModelPrices = { 0, 1500, 4500, 6800 };
        public static readonly float[] SuctionPower = { 220, 440, 900, 1900 };
        public static readonly float[] IntakeSize = { .45f, 1.1f, 2.1f, 4f };
        public static float CrewHealth(int count, bool boss)
        {
            if (count < 1 || count > 4) throw new ArgumentOutOfRangeException(nameof(count));
            return boss ? new[] { .7f, 1.7f, 2.45f, 3.2f }[count-1] : new[] { .85f, 1.25f, 1.5f, 1.75f }[count-1];
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
        public static int BossHitDamage(int damage, bool recovering) => Math.Max(1, (int)Math.Round(damage * (recovering ? 1.35 : .9)));
    }
}
