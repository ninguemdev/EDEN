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

        public SimulationSettings Clone() => (SimulationSettings)MemberwiseClone();
    }
}
