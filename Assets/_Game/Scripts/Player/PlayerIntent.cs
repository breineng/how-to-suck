using UnityEngine;

namespace HowToSuck
{
    public struct PlayerIntent
    {
        public string RunId;
        public uint Sequence;
        public uint JumpPressSequence;
        public Vector2 Move;
        public bool SprintHeld;
        public bool VacuumHeld;
        public bool InteractHeld;
        public float Yaw;
        public float Pitch;
        internal bool SuppressJump;

        public PlayerIntent Neutral()
        {
            var intent = this;
            intent.Move = Vector2.zero;
            intent.SprintHeld = false;
            intent.VacuumHeld = false;
            intent.InteractHeld = false;
            intent.SuppressJump = true;
            return intent;
        }

        public bool IsFinite => Finite(Move.x) && Finite(Move.y) && Finite(Yaw) && Finite(Pitch);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public static bool IsNewer(uint candidate, uint previous) => unchecked((int)(candidate - previous)) > 0;
    }
}
