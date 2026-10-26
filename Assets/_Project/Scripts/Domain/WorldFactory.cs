using System;

namespace Eden.Domain
{
    /// <summary>Builds the initial world of a run. Placement draws only from the spawn stream.</summary>
    public static class WorldFactory
    {
        private const float FullTurn = 2f * MathF.PI;

        /// <summary>Initial world where every creature has the reference body.</summary>
        public static WorldState CreateInitial(SimulationConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            return CreateInitial(config, CreatureBody.Reference(config));
        }

        /// <summary>Initial world where every creature has <paramref name="body"/>; placement does not depend on it.</summary>
        public static WorldState CreateInitial(SimulationConfig config, CreatureBody body)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var world = new WorldState(config);
            var random = world.SpawnRandom;
            for (var i = 0; i < config.InitialPopulation; i++)
            {
                var position = RandomArenaPosition(random, config);
                var heading = random.NextFloat() * FullTurn;
                world.AddCreature(position, heading, config.InitialEnergy, body);
            }

            for (var i = 0; i < config.InitialFood; i++)
                world.AddFood(RandomArenaPosition(random, config), config.FoodEnergy);

            return world;
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
