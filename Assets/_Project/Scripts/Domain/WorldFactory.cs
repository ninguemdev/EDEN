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
        /// A creature starts with the initial energy or its capacity if smaller. With point bodies placement does not
        /// depend on the genes. With solid bodies every founder's disc (its own radius) and every food item go only where
        /// they fit (see <see cref="Placement"/>); when one does not, a <see cref="ScenarioInfeasibleException"/> says how
        /// many fit: the scenario is never reduced silently.
        /// </summary>
        public static WorldState CreateInitial(SimulationConfig config, Func<int, BodyGenes> genesOf)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (genesOf == null)
                throw new ArgumentNullException(nameof(genesOf));

            var world = new WorldState(config);
            if (config.SolidBodies)
                return PlaceSolid(world, genesOf);
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

        // Each founder draws positions until its disc fits (then its heading), each food item until it fits.
        private static WorldState PlaceSolid(WorldState world, Func<int, BodyGenes> genesOf)
        {
            var config = world.Config;
            var random = world.SpawnRandom;
            for (var i = 0; i < config.InitialPopulation; i++)
            {
                var genes = genesOf(i);
                var body = genes.IsNeutral ? world.ReferenceBody : CreatureBody.Express(config, genes);
                var placed = false;
                var position = default(Float2);
                for (var attempt = 0; attempt < Placement.FounderAttempts && !placed; attempt++)
                {
                    position = RandomArenaPosition(random, config);
                    placed = Placement.BodyFits(config, world.Creatures, position, body.Radius);
                }

                if (!placed)
                    throw new ScenarioInfeasibleException("creatures", config.InitialPopulation, i);
                var heading = random.NextFloat() * FullTurn;
                world.AddCreature(position, heading, Math.Min(config.InitialEnergy, body.MaxEnergy), genes);
            }

            for (var i = 0; i < config.InitialFood; i++)
            {
                if (!Placement.TryPlaceFood(random, config, world.Creatures, 0, out var position))
                    throw new ScenarioInfeasibleException("food items", config.InitialFood, i);
                world.AddFood(position, config.FoodEnergy);
            }

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
