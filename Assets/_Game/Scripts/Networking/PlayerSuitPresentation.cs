using System;
namespace HowToSuck
{
    // One observed copy of the existing host suit state. This has no suit mutation or gameplay clock.
    public readonly struct PlayerSuitPresentation
    {
        public readonly PlayerSuitSnapshot State;
        public readonly double ObservedAt;
        public readonly bool Frozen;
        public bool IsKnown=>!string.IsNullOrEmpty(State.RunId);
        public bool InvulnerableAtObservation=>IsKnown&&State.Segments>0&&State.InvulnerableUntil>ObservedAt;
        public PlayerSuitPresentation(PlayerSuitSnapshot state,double observedAt,bool frozen)
        {
            GameplayReplicaPolicy.RequireRun(state.RunId);
            if(!GameplayReplicaPolicy.Player(state.OwnerId)||state.Segments<0||state.Segments>100||state.RepairCharges<0||state.RepairCharges>8||!Finite(state.RepairProgress)||state.RepairProgress<0||state.RepairProgress>1||
                state.RecoveryPending!=(state.Segments==0)||!Finite(state.InvulnerableUntil)||state.InvulnerableUntil<0||!Finite(observedAt)||observedAt<0)
                throw new ArgumentException("Invalid current-owner suit state or server observation time.");
            State=state;ObservedAt=observedAt;Frozen=frozen;
        }
        private static bool Finite(double time)=>!double.IsNaN(time)&&!double.IsInfinity(time);
        public bool SameValues(PlayerSuitPresentation x)=>State.RunId==x.State.RunId&&State.OwnerId==x.State.OwnerId&&
            State.RepairCharges==x.State.RepairCharges&&State.RepairProgress==x.State.RepairProgress&&State.Segments==x.State.Segments&&State.RecoveryPending==x.State.RecoveryPending&&State.InvulnerableUntil==x.State.InvulnerableUntil&&ObservedAt==x.ObservedAt&&Frozen==x.Frozen;
        public static void RequireAdvance(PlayerSuitPresentation previous,PlayerSuitPresentation next)
        {
            if(!next.IsKnown)throw new ArgumentException("Unknown suit is not an accepted snapshot.");
            if(previous.IsKnown&&(previous.State.RunId!=next.State.RunId||previous.State.OwnerId!=next.State.OwnerId||next.ObservedAt<previous.ObservedAt))
                throw new InvalidOperationException("Suit owner/run changed in place or observation time went backwards.");
            // Equal observation time can occur during ordered fixed-step catch-up/freeze publication.
            // A real recovery may increase segments; it is not treated as corrupt enemy-HP resurrection.
        }
    }
}
