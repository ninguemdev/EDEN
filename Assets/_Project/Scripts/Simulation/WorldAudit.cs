using System;
using Eden.Domain;

namespace Eden.Simulation
{
    /// <summary>
    /// The energy and water of a world since a starting point, and how well every balance closes. It keeps the stocks of
    /// that moment (energy and hydration held by creatures, energy of the food lying in the world, water held by limited
    /// sources) and adds up each tick's <see cref="EnergyFlows"/> and <see cref="HydrationFlows"/>; each error is what the
    /// stock changed by minus what the flows explain, so a complete audit leaves only float rounding. Call
    /// <see cref="Add"/> after every tick of the simulation it follows.
    /// </summary>
    public sealed class WorldAudit
    {
        private readonly double creatureEnergyAtStart;
        private readonly double foodEnergyAtStart;
        private readonly double hydrationAtStart;
        private readonly double waterAtStart;
        private EnergyFlows energy;
        private HydrationFlows hydration;

        public WorldAudit(WorldState world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            creatureEnergyAtStart = CreatureEnergy(world);
            foodEnergyAtStart = FoodEnergy(world);
            hydrationAtStart = CreatureHydration(world);
            waterAtStart = LimitedWater(world);
        }

        /// <summary>Ticks added since the start.</summary>
        public long Ticks { get; private set; }

        public EnergyFlows Energy => energy;
        public HydrationFlows Hydration => hydration;

        public void Add(WorldSimulation simulation)
        {
            if (simulation == null)
                throw new ArgumentNullException(nameof(simulation));
            energy.Add(simulation.LastEnergy);
            hydration.Add(simulation.LastHydration);
            Ticks++;
        }

        /// <summary>Change of the creatures' energy the flows do not explain.</summary>
        public double CreatureEnergyError(WorldState world) =>
            CreatureEnergy(world) - creatureEnergyAtStart - energy.Balance;

        /// <summary>Change of the energy of the food lying in the world the flows do not explain.</summary>
        public double FoodEnergyError(WorldState world) => FoodEnergy(world) - foodEnergyAtStart - energy.FoodStockBalance;

        /// <summary>Change of the creatures' hydration the flows do not explain (0 without water).</summary>
        public double HydrationError(WorldState world) =>
            CreatureHydration(world) - hydrationAtStart - hydration.CreatureBalance;

        /// <summary>Change of the water held by limited sources the flows do not explain (0 without water).</summary>
        public double WaterError(WorldState world) => LimitedWater(world) - waterAtStart - hydration.SourceBalance;

        /// <summary>Costs charged that the parts (maintenance, sensor, effort, eyes, turning) do not explain.</summary>
        public double CostSplitError =>
            energy.Charged - energy.Maintenance - energy.SensorCosts - energy.Effort - energy.EyeCosts - energy.TurnCosts;

        /// <summary>Largest of the errors above, against the scale of what flowed, for a pass/fail check.</summary>
        public double LargestError(WorldState world) =>
            Math.Max(Math.Max(Math.Abs(CreatureEnergyError(world)), Math.Abs(FoodEnergyError(world))),
                Math.Max(Math.Max(Math.Abs(HydrationError(world)), Math.Abs(WaterError(world))), Math.Abs(CostSplitError)));

        public static double CreatureEnergy(WorldState world)
        {
            var total = 0.0;
            foreach (var creature in world.Creatures)
                total += creature.Energy;
            return total;
        }

        public static double FoodEnergy(WorldState world)
        {
            var total = 0.0;
            foreach (var item in world.Food)
                total += item.Energy;
            return total;
        }

        public static double CreatureHydration(WorldState world)
        {
            var total = 0.0;
            foreach (var creature in world.Creatures)
                total += creature.Hydration;
            return total;
        }

        // Inexhaustible sources never change and are left out.
        public static double LimitedWater(WorldState world)
        {
            var total = 0.0;
            var water = world.Config.Water;
            for (var i = 0; i < water.Count; i++)
            {
                if (!water[i].Inexhaustible)
                    total += world.WaterReserves[i];
            }

            return total;
        }
    }
}
