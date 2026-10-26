using System;
using System.Collections.Generic;

namespace Eden.Domain
{
    /// <summary>
    /// Immutable, validated run configuration. Any change means a different run.
    /// Distances are arena units with the origin at the arena's lower-left corner; angles are radians.
    /// </summary>
    public sealed class SimulationConfig
    {
        public const int CurrentSchemaVersion = 4;
        public const int MaxTicksPerSecond = 240;
        public const int MaxDecisionIntervalTicks = 1000;
        public const int MaxPopulation = 4096;
        public const int MaxFood = 65536;
        public const float MaxArenaSize = 10000f;
        public const float MaxDurationSeconds = 1000000f;
        public const float MaxCostExponent = 4f;

        public SimulationConfig(SimulationSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var s = settings;
            var errors = new List<string>();
            RequireRange(errors, nameof(s.ArenaWidth), s.ArenaWidth, 1f, MaxArenaSize);
            RequireRange(errors, nameof(s.ArenaHeight), s.ArenaHeight, 1f, MaxArenaSize);
            RequireRange(errors, nameof(s.TicksPerSecond), s.TicksPerSecond, 1, MaxTicksPerSecond);
            RequireRange(errors, nameof(s.DecisionIntervalTicks), s.DecisionIntervalTicks, 1, MaxDecisionIntervalTicks);
            RequireRange(errors, nameof(s.InitialPopulation), s.InitialPopulation, 0, MaxPopulation);
            RequireRange(errors, nameof(s.InitialFood), s.InitialFood, 0, MaxFood);
            RequirePositive(errors, nameof(s.FoodEnergy), s.FoodEnergy);
            RequirePositive(errors, nameof(s.FoodRespawnSeconds), s.FoodRespawnSeconds, MaxDurationSeconds);
            RequirePositive(errors, nameof(s.MaxEnergy), s.MaxEnergy);
            RequirePositive(errors, nameof(s.InitialEnergy), s.InitialEnergy);
            if (s.InitialEnergy > s.MaxEnergy)
                errors.Add($"{nameof(s.InitialEnergy)} ({s.InitialEnergy}) must not exceed {nameof(s.MaxEnergy)} ({s.MaxEnergy})");
            RequirePositive(errors, nameof(s.MaxAgeSeconds), s.MaxAgeSeconds, MaxDurationSeconds);
            RequireNonNegative(errors, nameof(s.MetabolismPerSecond), s.MetabolismPerSecond);
            RequireNonNegative(errors, nameof(s.MovementCostPerSecond), s.MovementCostPerSecond);
            RequireNonNegative(errors, nameof(s.SensorCostPerSecond), s.SensorCostPerSecond);
            RequirePositive(errors, nameof(s.SensorRange), s.SensorRange, MaxArenaSize);
            RequirePositive(errors, nameof(s.EatRadius), s.EatRadius, MaxArenaSize);
            RequirePositive(errors, nameof(s.MaxSpeed), s.MaxSpeed);
            RequirePositive(errors, nameof(s.MaxAcceleration), s.MaxAcceleration);
            RequirePositive(errors, nameof(s.MaxTurnRate), s.MaxTurnRate);
            RequireNonNegative(errors, nameof(s.Damping), s.Damping);
            RequireRange(errors, nameof(s.SizeCostExponent), s.SizeCostExponent, 0f, MaxCostExponent);
            RequireRange(errors, nameof(s.SpeedCostExponent), s.SpeedCostExponent, 0f, MaxCostExponent);
            RequireRange(errors, nameof(s.SensorCostExponent), s.SensorCostExponent, 0f, MaxCostExponent);
            RequireRange(errors, nameof(s.EfficiencyPowerExponent), s.EfficiencyPowerExponent, 0f, MaxCostExponent);
            if (errors.Count > 0)
                throw new ArgumentException("Invalid simulation config: " + string.Join("; ", errors));

            Seed = s.Seed;
            ArenaWidth = s.ArenaWidth;
            ArenaHeight = s.ArenaHeight;
            TicksPerSecond = s.TicksPerSecond;
            DecisionIntervalTicks = s.DecisionIntervalTicks;
            InitialPopulation = s.InitialPopulation;
            InitialFood = s.InitialFood;
            FoodEnergy = s.FoodEnergy;
            FoodRespawnSeconds = s.FoodRespawnSeconds;
            MaxEnergy = s.MaxEnergy;
            InitialEnergy = s.InitialEnergy;
            MaxAgeSeconds = s.MaxAgeSeconds;
            MetabolismPerSecond = s.MetabolismPerSecond;
            MovementCostPerSecond = s.MovementCostPerSecond;
            SensorCostPerSecond = s.SensorCostPerSecond;
            SensorRange = s.SensorRange;
            EatRadius = s.EatRadius;
            MaxSpeed = s.MaxSpeed;
            MaxAcceleration = s.MaxAcceleration;
            MaxTurnRate = s.MaxTurnRate;
            Damping = s.Damping;
            SizeCostExponent = s.SizeCostExponent;
            SpeedCostExponent = s.SpeedCostExponent;
            SensorCostExponent = s.SensorCostExponent;
            EfficiencyPowerExponent = s.EfficiencyPowerExponent;

            TickDuration = 1f / TicksPerSecond;
            MaxAgeTicks = Math.Max(1L, (long)Math.Round((double)MaxAgeSeconds * TicksPerSecond));
            FoodRespawnTicks = Math.Max(1L, (long)Math.Round((double)FoodRespawnSeconds * TicksPerSecond));
        }

        public static SimulationConfig CreateDefault(ulong seed) => new SimulationConfig(new SimulationSettings { Seed = seed });

        public int SchemaVersion => CurrentSchemaVersion;
        public ulong Seed { get; }
        public float ArenaWidth { get; }
        public float ArenaHeight { get; }
        public int TicksPerSecond { get; }

        /// <summary>Ticks between brain decisions; actions are held in between.</summary>
        public int DecisionIntervalTicks { get; }

        public int InitialPopulation { get; }
        public int InitialFood { get; }
        public float FoodEnergy { get; }
        public float FoodRespawnSeconds { get; }
        public float MaxEnergy { get; }
        public float InitialEnergy { get; }
        public float MaxAgeSeconds { get; }
        public float MetabolismPerSecond { get; }

        /// <summary>Extra energy per second at full effort; effort is thrust² + strafe².</summary>
        public float MovementCostPerSecond { get; }

        public float SensorCostPerSecond { get; }
        public float SensorRange { get; }

        /// <summary>Maximum creature-to-food distance at which food is eaten.</summary>
        public float EatRadius { get; }

        public float MaxSpeed { get; }
        public float MaxAcceleration { get; }

        /// <summary>Radians per second at full turn.</summary>
        public float MaxTurnRate { get; }

        /// <summary>Fraction of velocity lost per second.</summary>
        public float Damping { get; }

        /// <summary>Power of the size factor in a body's metabolism and movement cost.</summary>
        public float SizeCostExponent { get; }

        /// <summary>Power of the speed factor in a body's movement cost.</summary>
        public float SpeedCostExponent { get; }

        /// <summary>Power of the range factor in a body's sensor cost.</summary>
        public float SensorCostExponent { get; }

        /// <summary>Power of the metabolism factor in a body's acceleration.</summary>
        public float EfficiencyPowerExponent { get; }

        /// <summary>Fixed simulated time per tick, in seconds. Never scaled to speed a run up.</summary>
        public float TickDuration { get; }

        public long MaxAgeTicks { get; }
        public long FoodRespawnTicks { get; }

        private static void RequireRange(List<string> errors, string name, float value, float min, float max)
        {
            if (float.IsNaN(value) || value < min || value > max)
                errors.Add($"{name} must be a finite value in [{min}, {max}] (was {value})");
        }

        private static void RequireRange(List<string> errors, string name, int value, int min, int max)
        {
            if (value < min || value > max)
                errors.Add($"{name} must be in [{min}, {max}] (was {value})");
        }

        private static void RequirePositive(List<string> errors, string name, float value, float max = float.MaxValue)
        {
            if (float.IsNaN(value) || value <= 0f || value > max)
                errors.Add($"{name} must be finite, positive and at most {max} (was {value})");
        }

        private static void RequireNonNegative(List<string> errors, string name, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                errors.Add($"{name} must be finite and not negative (was {value})");
        }
    }
}
