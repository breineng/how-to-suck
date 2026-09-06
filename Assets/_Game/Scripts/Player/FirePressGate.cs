namespace HowToSuck
{
    // Input edge qualification only. No cooldown, inventory or authority decision lives here.
    public sealed class FirePressGate
    {
        public bool WaitingForRelease { get; private set; } = true;
        public void Block() => WaitingForRelease = true;
        public bool TryPress(bool anyBoundButtonHeld, bool pressedThisFrame)
        {
            if (WaitingForRelease)
            {
                if (!anyBoundButtonHeld) WaitingForRelease = false;
                return false; // The release/re-enable frame itself cannot become a shot.
            }
            return pressedThisFrame;
        }
    }
}
