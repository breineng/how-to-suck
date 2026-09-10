using System;
namespace HowToSuck.Networking
{
    public enum ProductEntryMode { None, Solo, SteamHost, SteamGuest, EpicHost, EpicGuest }
    // A mode is selected once, before driver creation. Browsing reserves this root for Steam,
    // but does not select a role or open a campaign. Cancel always requires a fresh root.
    public sealed class ProductEntryChoice
    {
        public SoloEntryPhase Phase { get; private set; }
        public ProductEntryMode Mode { get; private set; }
        public bool Browsing { get; private set; }
        public bool Selected => Mode != ProductEntryMode.None;
        public bool CanChooseSolo => Phase == SoloEntryPhase.Choosing && !Selected && !Browsing;
        public bool CanChooseSteam => Phase == SoloEntryPhase.Choosing && !Selected && Browsing;
        public bool TryBrowse()
        {
            if (CanChooseSteam) return true;
            if (!CanChooseSolo) return false;
            Browsing = true; return true;
        }
        public bool TrySelect(ProductEntryMode mode)
        {
            if (mode == ProductEntryMode.Solo ? !CanChooseSolo :
                (mode != ProductEntryMode.SteamHost && mode != ProductEntryMode.SteamGuest && mode != ProductEntryMode.EpicHost && mode != ProductEntryMode.EpicGuest) || !CanChooseSteam) return false;
            Mode = mode; Phase = SoloEntryPhase.Starting; return true;
        }
        public bool ResolveGuest()
        {
            if (!Selected || Phase != SoloEntryPhase.Starting)
                throw new InvalidOperationException("Choose a session role before initialization.");
            return Mode == ProductEntryMode.SteamGuest || Mode == ProductEntryMode.EpicGuest;
        }
        public void Connected()
        {
            if (!Selected || Phase != SoloEntryPhase.Starting) throw new InvalidOperationException("Only a selected attempt can connect.");
            Phase = SoloEntryPhase.Connected;
        }
        public bool TryReturn()
        {
            if (Phase == SoloEntryPhase.Returning || (!Selected && !Browsing && Phase != SoloEntryPhase.Failed)) return false;
            Phase = SoloEntryPhase.Returning; return true;
        }
        public void Fail() { if (Phase != SoloEntryPhase.Returning) Phase = SoloEntryPhase.Failed; }
    }
}
