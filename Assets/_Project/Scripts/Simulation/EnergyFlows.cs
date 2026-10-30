namespace Eden.Simulation
{
    /// <summary>
    /// Where creature energy came from and went in one tick (or, added up, in a run). The energy held by all creatures
    /// changes by exactly <see cref="Balance"/>: food eaten, minus the costs of living paid, minus food energy that did
    /// not fit in a full creature, minus the cost of births, minus the energy dying creatures take away. Energy handed
    /// from parents to children (<see cref="Transferred"/>) only moves between creatures. The food lying in the world
    /// changes by exactly <see cref="FoodStockBalance"/>: what appeared minus what was eaten. The costs charged are split
    /// by what they paid for (maintenance, legacy sensor, effort, eyes, turning); a creature that runs dry pays only what
    /// it holds, the rest is <see cref="Unpaid"/>. Pushing another body costs nothing of its own: it is the effort of
    /// thrusting while touching. Sums are doubles so a long run can be audited without the rounding of the float
    /// energies piling up.
    /// </summary>
    public struct EnergyFlows
    {
        /// <summary>Energy of the food that appeared in the world (respawns; the food a world starts with is its stock).</summary>
        public double FoodAppeared;

        /// <summary>Energy of the food eaten.</summary>
        public double Food;

        /// <summary>Every cost charged this tick, before a creature runs dry (the sum of the parts below).</summary>
        public double Charged;

        /// <summary>Part of the costs charged that was metabolism (maintenance of the body).</summary>
        public double Maintenance;

        /// <summary>Part of the costs charged that was the legacy circular sensor.</summary>
        public double SensorCosts;

        /// <summary>Part of the costs charged that was thrust and strafe (effort, including pushing other bodies).</summary>
        public double Effort;

        /// <summary>Part of the costs charged that was eye upkeep.</summary>
        public double EyeCosts;

        /// <summary>Part of the costs charged that was turning, for creatures with eyes.</summary>
        public double TurnCosts;

        /// <summary>Metabolism, movement and sensor costs actually paid (a creature cannot pay more than it holds).</summary>
        public double Spent;

        /// <summary>Food energy above a creature's capacity, which is lost (waste).</summary>
        public double Overflow;

        /// <summary>Energy parents handed to their children.</summary>
        public double Transferred;

        /// <summary>Energy births cost parents beyond what they handed over; it goes to nobody.</summary>
        public double BirthCosts;

        /// <summary>Energy held by creatures when they died (0 for starvation), which leaves the world with them.</summary>
        public double CarriedAwayByDeaths;

        /// <summary>
        /// Costs search rules did not charge (<see cref="WorldSimulation.FrozenNeeds"/>): then the parts above add up to
        /// this instead of <see cref="Charged"/>, and the food eaten is all <see cref="Overflow"/>.
        /// </summary>
        public double Uncharged;

        /// <summary>Food energy that stayed in a creature: eaten minus wasted.</summary>
        public double Absorbed => Food - Overflow;

        /// <summary>Costs charged that a creature could not pay because it ran dry (and starved).</summary>
        public double Unpaid => Charged - Spent;

        /// <summary>Change of the total creature energy these flows explain.</summary>
        public double Balance => Food - Spent - Overflow - BirthCosts - CarriedAwayByDeaths;

        /// <summary>Change of the energy of the food lying in the world these flows explain.</summary>
        public double FoodStockBalance => FoodAppeared - Food;

        public void Add(EnergyFlows other)
        {
            FoodAppeared += other.FoodAppeared;
            Food += other.Food;
            Charged += other.Charged;
            Maintenance += other.Maintenance;
            SensorCosts += other.SensorCosts;
            Effort += other.Effort;
            EyeCosts += other.EyeCosts;
            TurnCosts += other.TurnCosts;
            Spent += other.Spent;
            Overflow += other.Overflow;
            Transferred += other.Transferred;
            BirthCosts += other.BirthCosts;
            CarriedAwayByDeaths += other.CarriedAwayByDeaths;
            Uncharged += other.Uncharged;
        }
    }
}
