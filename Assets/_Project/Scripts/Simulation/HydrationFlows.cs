namespace Eden.Simulation
{
    /// <summary>
    /// Where water came from and went in one tick (or, added up, in a run), in a run with water. The hydration held by all
    /// creatures changes by exactly <see cref="CreatureBalance"/>: water drunk, minus what time took, minus what dying
    /// creatures took away; what parents hand to children (<see cref="Transferred"/>) only moves between creatures. The
    /// water held by the limited sources changes by exactly <see cref="SourceBalance"/>. Drinking never creates energy and
    /// eating never hydrates, so these flows and <see cref="EnergyFlows"/> are separate. Sums are doubles, as for energy.
    /// </summary>
    public struct HydrationFlows
    {
        /// <summary>Hydration creatures took in by drinking, from any source.</summary>
        public double Drunk;

        /// <summary>Part of <see cref="Drunk"/> that came from inexhaustible sources, which never change.</summary>
        public double DrunkFromInexhaustible;

        /// <summary>Water the limited sources lost to drinking (what <see cref="Drunk"/> took from them, as stored).</summary>
        public double TakenFromSources;

        /// <summary>Water the limited sources gained back, up to their capacity.</summary>
        public double Recharged;

        /// <summary>Hydration lost with time (a creature cannot lose more than it holds).</summary>
        public double Lost;

        /// <summary>Hydration parents handed to their children.</summary>
        public double Transferred;

        /// <summary>Hydration held by creatures when they died (0 for dehydration), which leaves the world with them.</summary>
        public double CarriedAwayByDeaths;

        /// <summary>Change of the total creature hydration these flows explain.</summary>
        public double CreatureBalance => Drunk - Lost - CarriedAwayByDeaths;

        /// <summary>Change of the total water of the limited sources these flows explain.</summary>
        public double SourceBalance => Recharged - TakenFromSources;

        public void Add(HydrationFlows other)
        {
            Drunk += other.Drunk;
            DrunkFromInexhaustible += other.DrunkFromInexhaustible;
            TakenFromSources += other.TakenFromSources;
            Recharged += other.Recharged;
            Lost += other.Lost;
            Transferred += other.Transferred;
            CarriedAwayByDeaths += other.CarriedAwayByDeaths;
        }
    }
}
