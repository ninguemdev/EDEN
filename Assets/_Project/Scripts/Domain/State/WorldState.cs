using System.Collections.Generic;

namespace Eden.Domain
{
    /// <summary>
    /// Authoritative world state: everything needed to continue a run, including the random streams.
    /// Creatures and food are kept in ascending id order, which is the stable iteration order for rules.
    /// </summary>
    public sealed class WorldState
    {
        private readonly List<CreatureState> creatures = new List<CreatureState>();
        private readonly List<FoodState> food = new List<FoodState>();

        public WorldState(SimulationConfig config)
        {
            Config = config;
            WorldRandom = SeedDerivation.CreateStream(config.Seed, RandomStream.World);
            SpawnRandom = SeedDerivation.CreateStream(config.Seed, RandomStream.Spawn);
            NextCreatureId = 1;
            NextFoodId = 1;
        }

        public SimulationConfig Config { get; }
        public long Tick { get; private set; }

        /// <summary>Stream for rule-level randomness during ticks.</summary>
        public Pcg32 WorldRandom { get; }

        /// <summary>Stream for placing creatures and food.</summary>
        public Pcg32 SpawnRandom { get; }

        public int NextCreatureId { get; private set; }
        public int NextFoodId { get; private set; }
        public IReadOnlyList<CreatureState> Creatures => creatures;
        public IReadOnlyList<FoodState> Food => food;

        public void AdvanceTick() => Tick++;

        public CreatureState AddCreature(Float2 position, float heading, float energy)
        {
            var creature = new CreatureState(new CreatureId(NextCreatureId++), position, heading, energy);
            creatures.Add(creature);
            return creature;
        }

        public FoodState AddFood(Float2 position, float energy)
        {
            var item = new FoodState(new FoodId(NextFoodId++), position, energy);
            food.Add(item);
            return item;
        }
    }
}
