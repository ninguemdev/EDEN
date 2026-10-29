using System;

namespace Eden.Domain
{
    /// <summary>Builds the initial world of a run. Placement draws only from the spawn stream.</summary>
    public static class WorldFactory
    {
        private const float FullTurn = 2f * MathF.PI;

        /// <summary>Initial world where every creature has neutral genes (the reference body).</summary>
        public static WorldState CreateInitial(SimulationConfig config) => CreateInitial(config, _ => BodyGenes.Neutral);

        /// <summary>
        /// Initial world where creature i (0-based, so id i + 1) has the body genes <paramref name="genesOf"/> gives.
        /// Placement does not depend on the genes; a creature starts with the initial energy or its capacity if smaller.
        /// </summary>
        public static WorldState CreateInitial(SimulationConfig config, Func<int, BodyGenes> genesOf)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (genesOf == null)
                throw new ArgumentNullException(nameof(genesOf));

            var world = new WorldState(config);
            var random = world.SpawnRandom;
            for (var i = 0; i < config.InitialPopulation; i++)
            {
                var position = RandomArenaPosition(random, config);
                var heading = random.NextFloat() * FullTurn;
                var genes = genesOf(i);
                var capacity = genes.IsNeutral ? config.MaxEnergy : CreatureBody.Express(config, genes).MaxEnergy;
                world.AddCreature(position, heading, Math.Min(config.InitialEnergy, capacity), genes);
            }

            for (var i = 0; i < config.InitialFood; i++)
                world.AddFood(RandomFoodPosition(random, config, 0), config.FoodEnergy);

            return world;
        }

        /// <summary>
        /// Where food appears at <paramref name="tick"/>: anywhere in the arena, or, when the config shifts food, in the
        /// left half before the shift tick and in the right half from it on. Draws x, then y.
        /// </summary>
        public static Float2 RandomFoodPosition(IRandomSource random, SimulationConfig config, long tick)
        {
            if (!config.FoodShifts)
                return RandomArenaPosition(random, config);
            var half = config.ArenaWidth * 0.5f;
            var left = tick < config.FoodShiftTicks ? 0f : half;
            var x = random.NextFloat(left, left + half);
            var y = random.NextFloat(0f, config.ArenaHeight);
            return new Float2(x, y);
        }

        /// <summary>Uniform position inside the arena (borders included). Draws x, then y.</summary>
        public static Float2 RandomArenaPosition(IRandomSource random, SimulationConfig config)
        {
            var x = random.NextFloat(0f, config.ArenaWidth);
            var y = random.NextFloat(0f, config.ArenaHeight);
            return new Float2(x, y);
        }
    }
}
