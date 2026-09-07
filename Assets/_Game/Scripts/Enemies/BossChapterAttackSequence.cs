using System;

namespace HowToSuck
{
    public enum BossChapterPattern { None, AlternatingLunge, RedirectedCharge }

    // Authority-only sequencing. Existing phase/time/root snapshots still drive presentation.
    public sealed class BossChapterAttackSequence
    {
        public const float LungeSpeed = 2.8f;
        public const float LungeRange = .9f;
        public const float LungeAngle = 70f;
        public const float RedirectPauseSeconds = .65f;
        // Current Elder Attack blends from Tell until 22%, then rises/lands over its full 1s clip.
        public const double LungeStartFraction = .22, LungeEndFraction = .65;
        public BossChapterPattern Pattern { get; private set; }
        public bool IsLunge { get; private set; }
        public bool IsFollowUp { get; private set; }
        public bool LungeStopped { get; private set; }
        public bool PendingFollowUp { get; private set; }
        public bool Active => Pattern != BossChapterPattern.None;
        private bool completed = true;

        public void BeginNormal(BossChapterPattern pattern, bool advancedEncounter, bool boss, uint attackRevision)
        {
            Cancel();
            if (!Enum.IsDefined(typeof(BossChapterPattern), pattern)) throw new ArgumentOutOfRangeException(nameof(pattern));
            if (!advancedEncounter || !boss || pattern == BossChapterPattern.None) return;
            if (attackRevision == 0) throw new ArgumentOutOfRangeException(nameof(attackRevision));
            Pattern = pattern;
            IsLunge = pattern == BossChapterPattern.AlternatingLunge && attackRevision % 2 == 0;
            completed = false;
        }

        public void StopLunge(){if(IsLunge)LungeStopped=true;}

        public void Complete(bool blocked)
        {
            if (completed) return;
            completed = true;
            PendingFollowUp = Pattern == BossChapterPattern.RedirectedCharge && !IsFollowUp && !blocked;
        }

        // Consume before target/LOS checks. A vanished target cannot bank a later surprise charge.
        public bool TryBeginFollowUp()
        {
            if (!PendingFollowUp) return false;
            PendingFollowUp = false;
            IsFollowUp = true;
            completed = false;
            return true;
        }

        public void Cancel()
        {
            Pattern = BossChapterPattern.None;
            IsLunge = IsFollowUp = PendingFollowUp = LungeStopped = false;
            completed = true;
        }

        // Integrate only the part of this native step inside the visible lunge window.
        // Never catch up several missed seconds as one displacement.
        public static float LungeStep(double elapsed, float dt, float attackSeconds)
        {
            if (!Finite(elapsed) || !Finite(dt) || !Finite(attackSeconds) || dt <= 0 || attackSeconds <= 0) return 0;
            double start = attackSeconds * LungeStartFraction, end = attackSeconds * LungeEndFraction;
            return (float)Math.Max(0, Math.Min(elapsed, end) - Math.Max(elapsed - dt, start));
        }
        private static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x);
    }
}
