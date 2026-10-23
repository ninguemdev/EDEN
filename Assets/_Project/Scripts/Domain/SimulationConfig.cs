using System;
using System.Collections.Generic;

namespace Eden.Domain
{
    /// <summary>
    /// Immutable run configuration, validated on construction. Any change means a different run.
    /// Energy is in abstract energy units; distances are arena units with the origin at the arena's lower-left corner.
    /// </summary>
    public sealed class SimulationConfig
    {
        public const int CurrentSchemaVersion = 1;
        public const int MaxTicksPerSecond = 240;
        public const int MaxPopulation = 4096;
        public const int MaxFood = 65536;
        public const float MaxArenaSize = 10000f;

        public SimulationConfig(ulong seed, float arenaWidth, float arenaHeight, int ticksPerSecond,
            int initialPopulation, int initialFood, float maxEnergy, float initialEnergy, float foodEnergy)
        {
            var errors = new List<string>();
            RequireRange(errors, nameof(arenaWidth), arenaWidth, 1f, MaxArenaSize);
            RequireRange(errors, nameof(arenaHeight), arenaHeight, 1f, MaxArenaSize);
            if (ticksPerSecond < 1 || ticksPerSecond > MaxTicksPerSecond)
                errors.Add($"{nameof(ticksPerSecond)} must be in [1, {MaxTicksPerSecond}] (was {ticksPerSecond})");
            if (initialPopulation < 0 || initialPopulation > MaxPopulation)
                errors.Add($"{nameof(initialPopulation)} must be in [0, {MaxPopulation}] (was {initialPopulation})");
            if (initialFood < 0 || initialFood > MaxFood)
                errors.Add($"{nameof(initialFood)} must be in [0, {MaxFood}] (was {initialFood})");
            RequirePositive(errors, nameof(maxEnergy), maxEnergy);
            RequirePositive(errors, nameof(foodEnergy), foodEnergy);
            RequirePositive(errors, nameof(initialEnergy), initialEnergy);
            if (initialEnergy > maxEnergy)
                errors.Add($"{nameof(initialEnergy)} ({initialEnergy}) must not exceed {nameof(maxEnergy)} ({maxEnergy})");
            if (errors.Count > 0)
                throw new ArgumentException("Invalid simulation config: " + string.Join("; ", errors));

            Seed = seed;
            ArenaWidth = arenaWidth;
            ArenaHeight = arenaHeight;
            TicksPerSecond = ticksPerSecond;
            InitialPopulation = initialPopulation;
            InitialFood = initialFood;
            MaxEnergy = maxEnergy;
            InitialEnergy = initialEnergy;
            FoodEnergy = foodEnergy;
        }

        /// <summary>Starting values proposed in the planning docs, meant to be measured and tuned.</summary>
        public static SimulationConfig CreateDefault(ulong seed) => new SimulationConfig(
            seed, arenaWidth: 50f, arenaHeight: 50f, ticksPerSecond: 20,
            initialPopulation: 64, initialFood: 128, maxEnergy: 100f, initialEnergy: 50f, foodEnergy: 10f);

        public int SchemaVersion => CurrentSchemaVersion;
        public ulong Seed { get; }
        public float ArenaWidth { get; }
        public float ArenaHeight { get; }
        public int TicksPerSecond { get; }
        public int InitialPopulation { get; }
        public int InitialFood { get; }
        public float MaxEnergy { get; }
        public float InitialEnergy { get; }
        public float FoodEnergy { get; }

        /// <summary>Fixed simulated time per tick, in seconds. Never scaled to speed a run up.</summary>
        public float TickDuration => 1f / TicksPerSecond;

        private static void RequireRange(List<string> errors, string name, float value, float min, float max)
        {
            if (float.IsNaN(value) || value < min || value > max)
                errors.Add($"{name} must be a finite value in [{min}, {max}] (was {value})");
        }

        private static void RequirePositive(List<string> errors, string name, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                errors.Add($"{name} must be finite and positive (was {value})");
        }
    }
}
