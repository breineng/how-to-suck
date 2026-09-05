using UnityEngine;

namespace HowToSuck
{
    // One buffer per registered player; timestamps come from the authority, never the sender.
    public sealed class PlayerIntentBuffer
    {
        public const double TimeoutSeconds = 0.25;
        public double LastAcceptedAt { get; private set; }
        public bool HasIntent { get; private set; }

        private PlayerIntent latest;

        public bool TrySubmit(PlayerIntent intent, double now)
        {
            if (!intent.IsFinite || double.IsNaN(now) || double.IsInfinity(now)) return false;
            if (HasIntent && (!PlayerIntent.IsNewer(intent.Sequence, latest.Sequence) || now < LastAcceptedAt))
                return false;
            if (HasIntent && intent.JumpPressSequence != latest.JumpPressSequence &&
                !PlayerIntent.IsNewer(intent.JumpPressSequence, latest.JumpPressSequence)) return false;

            intent.Move = Vector2.ClampMagnitude(intent.Move, 1f);
            intent.Yaw = Mathf.Repeat(intent.Yaw, 360f);
            intent.Pitch = Mathf.Clamp(intent.Pitch, -80f, 80f);
            latest = intent;
            LastAcceptedAt = now;
            HasIntent = true;
            return true;
        }

        public PlayerIntent Read(double now)
        {
            if (!HasIntent || double.IsNaN(now) || double.IsInfinity(now) ||
                now < LastAcceptedAt || now - LastAcceptedAt >= TimeoutSeconds)
                return latest.Neutral();
            return latest;
        }

        public void Clear(float yaw = 0f, float pitch = 0f)
        {
            latest = new PlayerIntent { Yaw = yaw, Pitch = pitch };
            if (!latest.IsFinite) latest = default;
            HasIntent = false;
            LastAcceptedAt = 0;
        }
    }
}
