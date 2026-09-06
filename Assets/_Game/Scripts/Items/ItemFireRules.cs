using System;
namespace HowToSuck
{
    public static class ItemFireRules
    {
        public const double Cooldown = .30;
        public const double FlightLifetime = 3.0;
        public const float LaunchSpeed = 18f;
        public const float MinimumHitSpeed = 3f;
        public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public static bool Newer(uint value, uint previous) => unchecked((int)(value - previous)) > 0;
        public static int Damage(float mass)
        {
            if (!Finite(mass) || mass <= 0) throw new ArgumentOutOfRangeException(nameof(mass));
            return (int)Math.Round(Math.Min(100.0, Math.Max(12.0, 12.0 + 8.0 * Math.Sqrt(mass))), MidpointRounding.ToEven);
        }
        public static bool CanDamage(bool ordinary, double now, double expires, float relativeSpeed) =>
            ordinary && Finite(now) && Finite(expires) && now < expires && Finite(relativeSpeed) && relativeSpeed >= MinimumHitSpeed;
    }

    // One per registered player and run. Rejected edges are never retained for later execution.
    public sealed class ItemFireCommandGate
    {
        public string RunId { get; }
        public uint Watermark { get; private set; }
        public double NextAllowedAt { get; private set; } = double.NegativeInfinity;
        private double observedAt = double.NegativeInfinity;
        public ItemFireCommandGate(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("A firing gate needs the current run.", nameof(runId));
            RunId = runId;
        }
        public bool Consume(string runId, uint counter, double now, bool suppressed)
        {
            if (runId != RunId || !ItemFireRules.Finite(now) || !ItemFireRules.Newer(counter, Watermark)) return false;
            Watermark = counter; // Before suppression, cooldown and ALL caller-owned rejection conditions.
            bool timeValid = now >= observedAt;
            observedAt = Math.Max(observedAt, now);
            return timeValid && !suppressed && now >= NextAllowedAt;
        }
        public void CommitLaunch(double now)
        {
            if (!ItemFireRules.Finite(now) || now < observedAt || now < NextAllowedAt) throw new InvalidOperationException("Invalid launch time.");
            observedAt = now; NextAllowedAt = now + ItemFireRules.Cooldown;
        }
    }
}
