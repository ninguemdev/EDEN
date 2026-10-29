using System;

namespace Eden.Domain
{
    /// <summary>
    /// Editable run parameters (e.g. filled from an Inspector asset). Freeze them into a validated
    /// <see cref="SimulationConfig"/> before a run starts. Defaults are the proposed starting values from the
    /// planning docs and still have to be measured and tuned. Units: arena units, seconds, energy units.
    /// </summary>
    [Serializable]
    public sealed class SimulationSettings
    {
        public ulong Seed;

        public float ArenaWidth = 50f;
        public float ArenaHeight = 50f;
        public int TicksPerSecond = 20;

        /// <summary>Brains decide every this many ticks and the action is held in between (2 = 10 Hz at 20 Hz).</summary>
        public int DecisionIntervalTicks = 2;

        public int InitialPopulation = 64;
        public int InitialFood = 128;
        public float FoodEnergy = 10f;
        public float FoodRespawnSeconds = 5f;

        public float MaxEnergy = 100f;
        public float InitialEnergy = 50f;
        public float MaxAgeSeconds = 180f;
        public float MetabolismPerSecond = 1f;
        public float MovementCostPerSecond = 2f;
        public float SensorCostPerSecond = 0.25f;

        public float SensorRange = 10f;
        public float EatRadius = 0.75f;
        public float MaxSpeed = 5f;
        public float MaxAcceleration = 10f;
        public float MaxTurnRate = MathF.PI;
        public float Damping = 1f;

        // Costs of the body genes, as exponents of each trait's factor (see CreatureBody); 0 turns a cost off.
        /// <summary>Metabolism and movement cost grow with the size factor to this power (bigger bodies spend more).</summary>
        public float SizeCostExponent = 1f;

        /// <summary>Movement cost grows with the speed factor to this power (faster muscles spend more per push).</summary>
        public float SpeedCostExponent = 2f;

        /// <summary>Sensor cost grows with the range factor to this power (the area sensed).</summary>
        public float SensorCostExponent = 2f;

        /// <summary>Acceleration follows the metabolism factor to this power (a thrifty metabolism gives less power).</summary>
        public float EfficiencyPowerExponent = 1f;

        /// <summary>
        /// A world that changes during a life (M7 experiment): food lives in the left half of the arena until this time and
        /// in the right half from then on, uneaten food moving there at that moment. 0 (the default) keeps food anywhere.
        /// </summary>
        public float FoodShiftSeconds;

        /// <summary>
        /// Turning cost of a creature with eyes, as a fraction of its movement cost at the same intensity: it pays
        /// movement cost × fraction × turn² per second (proposed 0.25). Turning was free before eyes; it now costs, so
        /// sweeping a narrow eye around is not free. Creatures with the legacy senses keep the old rule.
        /// </summary>
        public float TurnCostFraction = 0.25f;

        // Continuous mode: local asexual reproduction. Off by default; isolated episodes never reproduce.
        /// <summary>Living, mature creatures with enough energy and no cooldown give birth near themselves.</summary>
        public bool Reproduction;

        /// <summary>Most creatures alive at once; a birth that would exceed it is refused without any cost.</summary>
        public int PopulationCap = 256;

        /// <summary>Age before a creature can reproduce.</summary>
        public float MaturitySeconds = 20f;

        /// <summary>Share of its own energy capacity a creature needs to reproduce.</summary>
        public float ReproductionEnergyFraction = 0.7f;

        /// <summary>Share of the parent's energy handed to the child (at most the child's capacity).</summary>
        public float OffspringEnergyFraction = 0.4f;

        /// <summary>Energy the parent loses on top of what it hands over; it goes to nobody.</summary>
        public float ReproductionCost = 5f;

        /// <summary>Time after a birth before the same parent can reproduce again.</summary>
        public float ReproductionCooldownSeconds = 15f;

        /// <summary>Distance from the parent at which the child is placed (then kept inside the arena).</summary>
        public float BirthDistance = 1f;

        /// <summary>Chance that each weight or expressed body gene of a child mutates.</summary>
        public float BirthMutationProbability = 0.05f;

        /// <summary>Standard deviation of the Gaussian noise of a mutation at birth.</summary>
        public float BirthMutationSigma = 0.10f;

        public SimulationSettings Clone() => (SimulationSettings)MemberwiseClone();
    }
}
