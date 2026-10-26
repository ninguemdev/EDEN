namespace Eden.Simulation
{
    /// <summary>
    /// Where creature energy came from and went in one tick (or, added up, in a run). The energy held by all creatures
    /// changes by exactly <see cref="Balance"/>: food eaten, minus the costs of living paid, minus food energy that did
    /// not fit in a full creature, minus the cost of births, minus the energy dying creatures take away. Energy handed
    /// from parents to children (<see cref="Transferred"/>) only moves between creatures. Sums are doubles so a long run
    /// can be audited without the rounding of the float energies piling up.
    /// </summary>
    public struct EnergyFlows
    {
        /// <summary>Energy of the food eaten.</summary>
        public double Food;

        /// <summary>Metabolism, movement and sensor costs actually paid (a creature cannot pay more than it holds).</summary>
        public double Spent;

        /// <summary>Food energy above a creature's capacity, which is lost.</summary>
        public double Overflow;

        /// <summary>Energy parents handed to their children.</summary>
        public double Transferred;

        /// <summary>Energy births cost parents beyond what they handed over; it goes to nobody.</summary>
        public double BirthCosts;

        /// <summary>Energy held by creatures when they died (0 for starvation), which leaves the world with them.</summary>
        public double CarriedAwayByDeaths;

        /// <summary>Change of the total creature energy these flows explain.</summary>
        public double Balance => Food - Spent - Overflow - BirthCosts - CarriedAwayByDeaths;

        public void Add(EnergyFlows other)
        {
            Food += other.Food;
            Spent += other.Spent;
            Overflow += other.Overflow;
            Transferred += other.Transferred;
            BirthCosts += other.BirthCosts;
            CarriedAwayByDeaths += other.CarriedAwayByDeaths;
        }
    }
}
