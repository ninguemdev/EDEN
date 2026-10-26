using System;
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

        // Eaten items that left the world, reused by AddFood; never part of the state.
        private readonly Stack<FoodState> spareFood;

        public WorldState(SimulationConfig config)
        {
            Config = config;
            spareFood = new Stack<FoodState>(config.InitialFood);
            WorldRandom = SeedDerivation.CreateStream(config.Seed, RandomStream.World);
            SpawnRandom = SeedDerivation.CreateStream(config.Seed, RandomStream.Spawn);
            NextCreatureId = 1;
            NextFoodId = 1;
        }

        private WorldState(SimulationConfig config, long tick, int nextCreatureId, int nextFoodId,
            Pcg32State worldRandom, Pcg32State spawnRandom)
        {
            Config = config;
            spareFood = new Stack<FoodState>(config.InitialFood);
            Tick = tick;
            NextCreatureId = nextCreatureId;
            NextFoodId = nextFoodId;
            WorldRandom = Pcg32.FromState(worldRandom);
            SpawnRandom = Pcg32.FromState(spawnRandom);
        }

        /// <summary>
        /// Rebuilds a world saved between ticks: living creatures and uneaten food in ascending id order, below the
        /// id counters, inside the arena and within the config's limits. Everything is checked before the world is
        /// built and all problems are reported together; the given objects are copied, never shared.
        /// </summary>
        public static WorldState Restore(SimulationConfig config, long tick, int nextCreatureId, int nextFoodId,
            Pcg32State worldRandom, Pcg32State spawnRandom, IReadOnlyList<CreatureState> creatures,
            IReadOnlyList<FoodState> food)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (creatures == null)
                throw new ArgumentNullException(nameof(creatures));
            if (food == null)
                throw new ArgumentNullException(nameof(food));

            var errors = new List<string>();
            if (tick < 0)
                errors.Add($"tick must not be negative (was {tick})");
            if (nextCreatureId < 1)
                errors.Add($"next creature id must be at least 1 (was {nextCreatureId})");
            if (nextFoodId < 1)
                errors.Add($"next food id must be at least 1 (was {nextFoodId})");
            if (creatures.Count > SimulationConfig.MaxPopulation)
                errors.Add($"{creatures.Count} creatures exceed the limit of {SimulationConfig.MaxPopulation}");
            if (food.Count > SimulationConfig.MaxFood)
                errors.Add($"{food.Count} food items exceed the limit of {SimulationConfig.MaxFood}");
            if (errors.Count == 0)
            {
                CheckCreatures(config, creatures, nextCreatureId, errors);
                CheckFood(config, food, nextFoodId, errors);
            }

            if (errors.Count > 0)
                throw new ArgumentException("Invalid saved world: " + string.Join("; ", errors));

            var world = new WorldState(config, tick, nextCreatureId, nextFoodId, worldRandom, spawnRandom);
            foreach (var saved in creatures)
            {
                world.creatures.Add(new CreatureState(saved.Id, saved.Position, saved.Heading, saved.Energy)
                {
                    Velocity = saved.Velocity, AgeTicks = saved.AgeTicks, FoodEnergyEaten = saved.FoodEnergyEaten
                });
            }

            foreach (var saved in food)
                world.food.Add(new FoodState(saved.Id, saved.Position, saved.Energy));
            return world;
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
            var id = new FoodId(NextFoodId++);
            FoodState item;
            if (spareFood.Count > 0)
            {
                item = spareFood.Pop();
                item.Reuse(id, position, energy);
            }
            else
            {
                item = new FoodState(id, position, energy);
            }

            food.Add(item);
            return item;
        }

        /// <summary>Removes eaten food, keeping id order, and keeps the items for reuse. Returns how many were removed.</summary>
        public int RemoveEatenFood()
        {
            var kept = 0;
            for (var i = 0; i < food.Count; i++)
            {
                var item = food[i];
                if (item.IsEaten)
                    spareFood.Push(item);
                else
                    food[kept++] = item;
            }

            var removed = food.Count - kept;
            food.RemoveRange(kept, removed);
            return removed;
        }

        /// <summary>Removes dead creatures, keeping id order. Returns how many creatures were removed.</summary>
        public int RemoveDeadCreatures()
        {
            // A loop rather than RemoveAll: its delegate would be allocated at the first death.
            var kept = 0;
            for (var i = 0; i < creatures.Count; i++)
            {
                if (creatures[i].IsAlive)
                    creatures[kept++] = creatures[i];
            }

            var removed = creatures.Count - kept;
            creatures.RemoveRange(kept, removed);
            return removed;
        }

        private static void CheckCreatures(SimulationConfig config, IReadOnlyList<CreatureState> creatures, int nextId,
            List<string> errors)
        {
            var previous = 0;
            foreach (var creature in creatures)
            {
                if (creature == null)
                {
                    errors.Add("a creature is missing");
                    continue;
                }

                var id = creature.Id.Value;
                var name = $"creature {id}";
                if (id <= previous || id >= nextId)
                    errors.Add($"{name} breaks the ascending id order below {nextId}");
                previous = Math.Max(previous, id);
                if (!creature.IsAlive)
                    errors.Add($"{name} is dead; dead creatures leave the world within their tick");
                CheckInArena(config, creature.Position, name, errors);
                if (!IsFinite(creature.Velocity.X) || !IsFinite(creature.Velocity.Y))
                    errors.Add($"{name} has a non-finite velocity");
                if (!(creature.Heading >= 0f && creature.Heading < 2f * MathF.PI))
                    errors.Add($"{name} heading {creature.Heading} is outside [0, 2π)");
                if (!(creature.Energy > 0f && creature.Energy <= config.MaxEnergy))
                    errors.Add($"{name} energy {creature.Energy} is outside (0, {config.MaxEnergy}]");
                if (creature.AgeTicks < 0 || creature.AgeTicks >= config.MaxAgeTicks)
                    errors.Add($"{name} age {creature.AgeTicks} is outside [0, {config.MaxAgeTicks})");
                if (!(creature.FoodEnergyEaten >= 0f) || !IsFinite(creature.FoodEnergyEaten))
                    errors.Add($"{name} eaten food energy {creature.FoodEnergyEaten} must be finite and not negative");
            }
        }

        private static void CheckFood(SimulationConfig config, IReadOnlyList<FoodState> food, int nextId,
            List<string> errors)
        {
            var previous = 0;
            foreach (var item in food)
            {
                if (item == null)
                {
                    errors.Add("a food item is missing");
                    continue;
                }

                var id = item.Id.Value;
                var name = $"food {id}";
                if (id <= previous || id >= nextId)
                    errors.Add($"{name} breaks the ascending id order below {nextId}");
                previous = Math.Max(previous, id);
                if (item.IsEaten)
                    errors.Add($"{name} is eaten; eaten food leaves the world within its tick");
                CheckInArena(config, item.Position, name, errors);
                if (!(item.Energy > 0f) || !IsFinite(item.Energy))
                    errors.Add($"{name} energy {item.Energy} must be finite and positive");
            }
        }

        private static void CheckInArena(SimulationConfig config, Float2 position, string name, List<string> errors)
        {
            if (!(position.X >= 0f && position.X <= config.ArenaWidth && position.Y >= 0f && position.Y <= config.ArenaHeight))
                errors.Add($"{name} position {position} is outside the arena");
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
