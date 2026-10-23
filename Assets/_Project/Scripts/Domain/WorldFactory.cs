using System;

namespace Eden.Domain
{
    /// <summary>Builds the initial world of a run. Placement draws only from the spawn stream.</summary>
    public static class WorldFactory
    {
        private const float FullTurn = 2f * MathF.PI;

        public static WorldState CreateInitial(SimulationConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var world = new WorldState(config);
            var random = world.SpawnRandom;
            for (var i = 0; i < config.InitialPopulation; i++)
            {
                var position = RandomPosition(random, config);
                var heading = random.NextFloat() * FullTurn;
                world.AddCreature(position, heading, config.InitialEnergy);
            }

            for (var i = 0; i < config.InitialFood; i++)
                world.AddFood(RandomPosition(random, config), config.FoodEnergy);

            return world;
        }

        private static Float2 RandomPosition(IRandomSource random, SimulationConfig config)
        {
            var x = random.NextFloat(0f, config.ArenaWidth);
            var y = random.NextFloat(0f, config.ArenaHeight);
            return new Float2(x, y);
        }
    }
}
