using System;
namespace HowToSuck.Networking
{
    public enum SoloEntryPhase { Choosing, Starting, Connected, Returning, Failed }
    // One attempt per composition. A failed/stopped initialized root never starts a second session.
    public sealed class SoloEntryAttempt
    {
        public SoloEntryPhase Phase { get; private set; }
        public bool Selected { get; private set; }
        public bool TrySelect()
        {
            if (Phase != SoloEntryPhase.Choosing || Selected) return false;
            Selected = true; Phase = SoloEntryPhase.Starting; return true;
        }
        public void Connected()
        { if (Phase != SoloEntryPhase.Starting) throw new InvalidOperationException("Only a selected fresh attempt can connect."); Phase = SoloEntryPhase.Connected; }
        public void Returning()
        { if (!Selected || Phase == SoloEntryPhase.Returning) throw new InvalidOperationException("Return belongs to one selected attempt."); Phase = SoloEntryPhase.Returning; }
        public void Fail() { Phase = SoloEntryPhase.Failed; }
    }
}
