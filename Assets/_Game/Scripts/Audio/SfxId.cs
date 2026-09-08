namespace HowToSuck.Audio
{
    // Stable authored IDs. Pending entries remain present and explicitly unbound.
    public enum SfxId : byte
    {
        VacuumIdle, VacuumActive, VacuumRattle, SwallowTiny, SwallowMedium, SwallowHeavy,
        TruckIdle, TruckSwallowA, TruckSwallowB, ImpactSmallA, ImpactSmallB,
        ImpactWoodA, ImpactWoodB, ImpactHeavyA, ImpactHeavyB, QuotaReady, ExtractSuccess,
        ContractFail, UiPurchase, ShotBlast, EnemyDamage, EnemyBurst, BossBurst,
        PlayerDamage, Repair, VacuumBlocked, ItemStored, FootstepWood, FootstepConcrete,
        Jump, Land, UiClick, EnemyLeap, BossWarning, AmbientHouse, AmbientMarket, AmbientWarehouse,
        UiHover, MenuMusic, MoneyAdded
    }
    public enum AudioBus : byte { Master, Vacuum, Truck, Impacts, UI }
    public enum ClipReviewStatus : byte { PendingReplacement, ProvisionalCandidate, MixAccepted }
    public enum ImpactClass : byte { Small, Wood, Heavy }
}
