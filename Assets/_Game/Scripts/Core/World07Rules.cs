using System;
using System.Collections.Generic;
using NVector3 = System.Numerics.Vector3;

namespace HowToSuck
{
    // Pure arithmetic used by the Unity adapters. No scene state, price, type or money rules.
    [Serializable]
    public sealed class PlayerContactSettings
    {
        public float PlayerMass = 80f;
        public float MinimumApproachSpeed = .15f;
        public float MaximumContactDeltaSpeed = 2.5f;
        public float MaximumStepDeltaSpeed = 3.5f;
        public float MaximumExternalSpeed = 4f;
        public float DecayPerSecond = 7f;
        public float ContactCooldown = .18f;

        public bool IsValid =>
            Positive(PlayerMass) && Nonnegative(MinimumApproachSpeed) &&
            Positive(MaximumContactDeltaSpeed) && Positive(MaximumStepDeltaSpeed) &&
            Positive(MaximumExternalSpeed) && Positive(DecayPerSecond) &&
            Positive(ContactCooldown);
        private static bool Positive(float n) => WorldBoundsRules.Finite(n) && n > 0;
        private static bool Nonnegative(float n) => WorldBoundsRules.Finite(n) && n >= 0;
        internal PlayerContactSettings Copy() => (PlayerContactSettings)MemberwiseClone();
    }

    public sealed class ContactVelocityState
    {
        private readonly PlayerContactSettings settings;
        private readonly Dictionary<ulong, double> nextContact = new Dictionary<ulong, double>();
        private readonly HashSet<ulong> seenThisStep = new HashSet<ulong>();
        private readonly List<ulong> expired = new List<ulong>();
        private double now, lastStep = double.NegativeInfinity;
        private float remaining;
        private bool open;
        public NVector3 Velocity { get; private set; }
        public float AppliedDeltaSpeed { get; private set; }

        public ContactVelocityState(PlayerContactSettings configuration)
        {
            if (configuration == null || !configuration.IsValid)
                throw new ArgumentException("Contact settings must be finite and positive.");
            settings = configuration.Copy();
        }

        public bool BeginStep(double time, float dt)
        {
            open = false;
            if (!WorldBoundsRules.Finite(time) || !WorldBoundsRules.Finite(dt) || dt <= 0 ||
                time <= lastStep) return false;
            now = lastStep = time;
            Velocity *= (float)Math.Exp(-settings.DecayPerSecond * dt);
            if (Velocity.LengthSquared() < 1e-8f) Velocity = NVector3.Zero;
            seenThisStep.Clear(); remaining = settings.MaximumStepDeltaSpeed; AppliedDeltaSpeed = 0;
            // Expiry bounds memory even when many unique items touch over a long run.
            expired.Clear();
            foreach (var pair in nextContact) if (pair.Value <= now) expired.Add(pair.Key);
            foreach (ulong id in expired) nextContact.Remove(id);
            open = true; return true;
        }

        public bool TryContact(ulong bodyId, float bodyMass, NVector3 surfaceVelocity,
            NVector3 towardCapsule, NVector3 playerVelocity)
        {
            if (!open || bodyId == 0 || !WorldBoundsRules.Finite(bodyMass) || bodyMass <= 0 ||
                !WorldBoundsRules.Finite(surfaceVelocity) || !WorldBoundsRules.Finite(towardCapsule) ||
                !WorldBoundsRules.Finite(playerVelocity) || towardCapsule.LengthSquared() < 1e-10f ||
                seenThisStep.Contains(bodyId) || nextContact.ContainsKey(bodyId) || remaining <= 0)
                return false;
            var direction = NVector3.Normalize(towardCapsule);
            // Walking into stationary furniture must never knock the player back.
            float worldApproach = NVector3.Dot(surfaceVelocity, direction);
            float relativeApproach = NVector3.Dot(surfaceVelocity - playerVelocity, direction);
            float approach = Math.Min(worldApproach, relativeApproach);
            if (!WorldBoundsRules.Finite(approach) || approach <= settings.MinimumApproachSpeed) return false;

            float transfer = bodyMass / (bodyMass + settings.PlayerMass);
            float amount = Math.Min(remaining, Math.Min(settings.MaximumContactDeltaSpeed, approach * transfer));
            var proposed = Velocity + direction * amount;
            float length = proposed.Length();
            if (length > settings.MaximumExternalSpeed) proposed *= settings.MaximumExternalSpeed / length;
            float actual = (proposed - Velocity).Length();
            if (actual <= 1e-6f) return false;
            Velocity = proposed;
            remaining = Math.Max(0, remaining - actual); AppliedDeltaSpeed += actual;
            seenThisStep.Add(bodyId); nextContact[bodyId] = now + settings.ContactCooldown;
            return true;
        }

        public void Reset()
        {
            Velocity = NVector3.Zero; AppliedDeltaSpeed = remaining = 0; open = false;
            lastStep = double.NegativeInfinity; nextContact.Clear(); seenThisStep.Clear(); expired.Clear();
        }
    }

    public static class WorldBoundsRules
    {
        public delegate bool ProbeCandidate(NVector3 desired, out NVector3 accepted);
        private static readonly NVector3[] Directions = {
            new NVector3(1,0,0),new NVector3(0,0,1),new NVector3(-1,0,0),new NVector3(0,0,-1),
            new NVector3(.70710678f,0,.70710678f),new NVector3(-.70710678f,0,.70710678f),
            new NVector3(-.70710678f,0,-.70710678f),new NVector3(.70710678f,0,-.70710678f)
        };
        public static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        public static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
        public static bool Finite(NVector3 p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
        public static bool ValidBounds(NVector3 min, NVector3 max, float lowerY) =>
            Finite(min) && Finite(max) && Finite(lowerY) && min.X < max.X && min.Y < max.Y &&
            min.Z < max.Z && lowerY >= min.Y && lowerY < max.Y;
        public static bool Outside(NVector3 p, NVector3 min, NVector3 max, float lowerY) =>
            !Finite(p) || p.X < min.X || p.X > max.X || p.Y < Math.Max(min.Y,lowerY) ||
            p.Y > max.Y || p.Z < min.Z || p.Z > max.Z;

        public static bool TryChooseSafe(IReadOnlyList<NVector3> anchors, int rings, float spacing,
            int maximumProbes, ProbeCandidate probe, out NVector3 position, out int probes)
        {
            position = default; probes = 0;
            if (anchors == null || probe == null || rings < 0 || rings > 4 ||
                !Finite(spacing) || spacing <= 0 || maximumProbes <= 0) return false;
            var visited = new HashSet<NVector3>();
            // All authored anchor centres precede any nearby offset.
            for (int ring = 0; ring <= rings; ring++)
            {
                foreach (var anchor in anchors)
                {
                    if (!Finite(anchor)) continue;
                    int count = ring == 0 ? 1 : Directions.Length;
                    for (int i = 0; i < count; i++)
                    {
                        var candidate = ring == 0 ? anchor : anchor + Directions[i] * spacing * ring;
                        if (!Finite(candidate) || !visited.Add(candidate)) continue;
                        if (probes >= maximumProbes) return false;
                        probes++;
                        if (probe(candidate, out var result) && Finite(result))
                        { position = result; return true; }
                    }
                }
            }
            return false;
        }
    }
}
