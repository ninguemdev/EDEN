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
        public const int CurrentSchemaVersion = 6;
        public const int MaxTicksPerSecond = 240;
        public const int MaxDecisionIntervalTicks = 1000;
        public const int MaxPopulation = 4096;
        public const int MaxFood = 65536;
        public const float MaxArenaSize = 10000f;
        public const float MaxDurationSeconds = 1000000f;
        public const float MaxCostExponent = 4f;
        public const int MaxSolidSubsteps = 64;

        /// <summary>
        /// Most separation passes after each solid-body move (they stop as soon as nothing overlaps). Bodies jammed against
        /// a wall by a crowd may keep an overlap of a couple of percent of their radii.
        /// </summary>
        public const int ContactPasses = 16;

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
            RequireRange(errors, nameof(s.FoodShiftSeconds), s.FoodShiftSeconds, 0f, MaxDurationSeconds);
            RequireRange(errors, nameof(s.TurnCostFraction), s.TurnCostFraction, 0f, MaxCostExponent);
            RequirePositive(errors, nameof(s.BodyRadius), s.BodyRadius, MaxArenaSize);
            var substeps = SubstepsFor(s);
            if (s.SolidBodies && errors.Count == 0)
            {
                var largest = 2f * s.BodyRadius * CreatureBody.Factor(BodyGenes.MaxAbsValue);
                if (largest > Math.Min(s.ArenaWidth, s.ArenaHeight))
                    errors.Add($"the largest solid body ({largest} across) does not fit the arena");
                if (substeps > MaxSolidSubsteps)
                    errors.Add($"solid bodies would need {substeps} substeps per tick (at most {MaxSolidSubsteps}): " +
                               $"{nameof(s.MaxSpeed)} is too high for {nameof(s.BodyRadius)} at this tick rate");
            }

            RequireRange(errors, nameof(s.PopulationCap), s.PopulationCap, 1, MaxPopulation);
            if (s.Reproduction && s.PopulationCap < s.InitialPopulation)
                errors.Add($"{nameof(s.PopulationCap)} ({s.PopulationCap}) must not be below {nameof(s.InitialPopulation)} " +
                           $"({s.InitialPopulation})");
            RequirePositive(errors, nameof(s.MaturitySeconds), s.MaturitySeconds, MaxDurationSeconds);
            RequireRange(errors, nameof(s.ReproductionEnergyFraction), s.ReproductionEnergyFraction, 0.01f, 1f);
            RequireRange(errors, nameof(s.OffspringEnergyFraction), s.OffspringEnergyFraction, 0.01f, 0.99f);
            RequireNonNegative(errors, nameof(s.ReproductionCost), s.ReproductionCost);
            RequireRange(errors, nameof(s.ReproductionCooldownSeconds), s.ReproductionCooldownSeconds, 0f, MaxDurationSeconds);
            RequirePositive(errors, nameof(s.BirthDistance), s.BirthDistance, MaxArenaSize);
            RequireRange(errors, nameof(s.BirthMutationProbability), s.BirthMutationProbability, 0f, 1f);
            RequireRange(errors, nameof(s.BirthMutationSigma), s.BirthMutationSigma, 0f, 3f);
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
            FoodShiftSeconds = s.FoodShiftSeconds;
            TurnCostFraction = s.TurnCostFraction;
            SolidBodies = s.SolidBodies;
            BodyRadius = s.BodyRadius;
            SolidSubsteps = s.SolidBodies ? substeps : 1;
            Reproduction = s.Reproduction;
            PopulationCap = s.PopulationCap;
            MaturitySeconds = s.MaturitySeconds;
            ReproductionEnergyFraction = s.ReproductionEnergyFraction;
            OffspringEnergyFraction = s.OffspringEnergyFraction;
            ReproductionCost = s.ReproductionCost;
            ReproductionCooldownSeconds = s.ReproductionCooldownSeconds;
            BirthDistance = s.BirthDistance;
            BirthMutationProbability = s.BirthMutationProbability;
            BirthMutationSigma = s.BirthMutationSigma;

            TickDuration = 1f / TicksPerSecond;
            MaxAgeTicks = Math.Max(1L, (long)Math.Round((double)MaxAgeSeconds * TicksPerSecond));
            FoodRespawnTicks = Math.Max(1L, (long)Math.Round((double)FoodRespawnSeconds * TicksPerSecond));
            MaturityTicks = Math.Max(1L, (long)Math.Round((double)MaturitySeconds * TicksPerSecond));
            FoodShiftTicks = FoodShiftSeconds > 0f ? Math.Max(1L, (long)Math.Round((double)FoodShiftSeconds * TicksPerSecond)) : 0L;
            ReproductionCooldownTicks = (long)Math.Round((double)ReproductionCooldownSeconds * TicksPerSecond);
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

        /// <summary>Time food moves from the left to the right half of the arena; 0 when it never does.</summary>
        public float FoodShiftSeconds { get; }

        /// <summary>Turning cost of creatures with eyes as a fraction of their movement cost (see the settings).</summary>
        public float TurnCostFraction { get; }

        /// <summary>Whether creatures are solid discs that do not overlap and push each other (see the settings).</summary>
        public bool SolidBodies { get; }

        /// <summary>Radius of the reference body's solid disc.</summary>
        public float BodyRadius { get; }

        /// <summary>
        /// Moves a tick is split into with solid bodies, so no body moves farther than the smallest body's radius in one:
        /// two bodies then cannot pass through each other between contact checks. 1 without solid bodies.
        /// </summary>
        public int SolidSubsteps { get; }

        /// <summary>Tick at the start of which food moves; 0 when it never does.</summary>
        public long FoodShiftTicks { get; }

        public bool FoodShifts => FoodShiftTicks > 0;

        /// <summary>Whether creatures reproduce (continuous mode); the values below only matter when they do.</summary>
        public bool Reproduction { get; }

        public int PopulationCap { get; }
        public float MaturitySeconds { get; }
        public float ReproductionEnergyFraction { get; }
        public float OffspringEnergyFraction { get; }
        public float ReproductionCost { get; }
        public float ReproductionCooldownSeconds { get; }
        public float BirthDistance { get; }
        public float BirthMutationProbability { get; }
        public float BirthMutationSigma { get; }
        public long MaturityTicks { get; }
        public long ReproductionCooldownTicks { get; }

        /// <summary>Fixed simulated time per tick, in seconds. Never scaled to speed a run up.</summary>
        public float TickDuration { get; }

        public long MaxAgeTicks { get; }
        public long FoodRespawnTicks { get; }

        // Fastest possible step over the smallest possible radius, rounded up; meaningless if the values are invalid.
        private static int SubstepsFor(SimulationSettings s)
        {
            var fastest = (double)s.MaxSpeed * CreatureBody.Factor(BodyGenes.MaxAbsValue) / s.TicksPerSecond;
            var smallest = (double)s.BodyRadius * CreatureBody.Factor(-BodyGenes.MaxAbsValue);
            var substeps = Math.Ceiling(fastest / smallest);
            if (!(substeps <= MaxSolidSubsteps))
                return MaxSolidSubsteps + 1;
            return substeps < 1.0 ? 1 : (int)substeps;
        }

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
