using Eden.Simulation;

namespace Eden.Presentation
{
    /// <summary>Running totals of the physical arena of a shared world, since it was started or loaded.</summary>
    public readonly struct ArenaTally
    {
        public ArenaTally(long blockedBirths, long cappedBirths, long delayedRespawns)
        {
            BlockedBirths = blockedBirths;
            CappedBirths = cappedBirths;
            DelayedRespawns = delayedRespawns;
        }

        /// <summary>Births refused for want of room next to the parent.</summary>
        public long BlockedBirths { get; }

        /// <summary>Births refused because the population was at its cap.</summary>
        public long CappedBirths { get; }

        /// <summary>Food respawns that found no free place and waited for a later tick (one count per wait).</summary>
        public long DelayedRespawns { get; }

        /// <summary>These totals with the tick <paramref name="simulation"/> just ran.</summary>
        public ArenaTally Add(WorldSimulation simulation) =>
            new ArenaTally(BlockedBirths + simulation.LastBirthsBlocked, CappedBirths + simulation.LastBirthsRefused,
                DelayedRespawns + simulation.LastFoodRespawnsDelayed);
    }
}
