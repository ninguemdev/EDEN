using System;

namespace Eden.Domain
{
    /// <summary>
    /// What a creature's body can do and what it costs, expressed from its <see cref="BodyGenes"/> in a run config when
    /// the creature is created and fixed for its whole life. Each gene g gives a factor f = 1 + g·<see cref="BodyGenes.TraitSpan"/>
    /// (efficiency gives the metabolism factor m = 1 − g·TraitSpan), and every benefit comes with a cost:
    /// <list type="bullet">
    /// <item>size fs: eat radius and energy capacity × fs; metabolism and movement cost × fs^<see cref="SimulationConfig.SizeCostExponent"/>;</item>
    /// <item>speed fv: speed cap × fv; movement cost × fv^<see cref="SimulationConfig.SpeedCostExponent"/>;</item>
    /// <item>sensor fr: sensor range × fr; sensor cost × fr^<see cref="SimulationConfig.SensorCostExponent"/>;</item>
    /// <item>efficiency m: metabolism × m; acceleration × m^<see cref="SimulationConfig.EfficiencyPowerExponent"/>.</item>
    /// </list>
    /// Neutral genes give exactly the config's own values (the reference body), whatever the exponents.
    /// </summary>
    public readonly struct CreatureBody : IEquatable<CreatureBody>
    {
        public CreatureBody(float eatRadius, float maxEnergy, float maxSpeed, float maxAcceleration, float sensorRange,
            float metabolismPerSecond, float movementCostPerSecond, float sensorCostPerSecond)
        {
            EatRadius = eatRadius;
            MaxEnergy = maxEnergy;
            MaxSpeed = maxSpeed;
            MaxAcceleration = maxAcceleration;
            SensorRange = sensorRange;
            MetabolismPerSecond = metabolismPerSecond;
            MovementCostPerSecond = movementCostPerSecond;
            SensorCostPerSecond = sensorCostPerSecond;
        }

        /// <summary>Body radius: food within this distance of the creature is eaten.</summary>
        public float EatRadius { get; }

        /// <summary>Energy capacity.</summary>
        public float MaxEnergy { get; }

        public float MaxSpeed { get; }
        public float MaxAcceleration { get; }

        /// <summary>Range of the food sensor and of the wall readings.</summary>
        public float SensorRange { get; }

        /// <summary>Resting energy use per second.</summary>
        public float MetabolismPerSecond { get; }

        /// <summary>Extra energy per second at full effort (thrust² + strafe² = 1).</summary>
        public float MovementCostPerSecond { get; }

        public float SensorCostPerSecond { get; }

        /// <summary>The config's own values: the body of neutral genes.</summary>
        public static CreatureBody Reference(SimulationConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            return new CreatureBody(config.EatRadius, config.MaxEnergy, config.MaxSpeed, config.MaxAcceleration,
                config.SensorRange, config.MetabolismPerSecond, config.MovementCostPerSecond, config.SensorCostPerSecond);
        }

        /// <summary>Body of <paramref name="genes"/> (all of them count; mask them first to express only some).</summary>
        public static CreatureBody Express(SimulationConfig config, BodyGenes genes)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            var size = Factor(genes.Size);
            var speed = Factor(genes.Speed);
            var range = Factor(genes.Sensor);
            var metabolism = Factor(-genes.Efficiency);
            var sizeCost = Power(size, config.SizeCostExponent);
            return new CreatureBody(
                config.EatRadius * size,
                config.MaxEnergy * size,
                config.MaxSpeed * speed,
                config.MaxAcceleration * Power(metabolism, config.EfficiencyPowerExponent),
                config.SensorRange * range,
                config.MetabolismPerSecond * metabolism * sizeCost,
                config.MovementCostPerSecond * sizeCost * Power(speed, config.SpeedCostExponent),
                config.SensorCostPerSecond * Power(range, config.SensorCostExponent));
        }

        /// <summary>Largest energy capacity any body can have in <paramref name="config"/>.</summary>
        public static float MaxPossibleEnergy(SimulationConfig config) =>
            config.MaxEnergy * Factor(BodyGenes.MaxAbsValue);

        /// <summary>Factor of a gene: 1 + gene·<see cref="BodyGenes.TraitSpan"/>, within [0.5, 1.5].</summary>
        public static float Factor(float gene) => 1f + gene * BodyGenes.TraitSpan;

        public bool Equals(CreatureBody other) =>
            EatRadius == other.EatRadius && MaxEnergy == other.MaxEnergy && MaxSpeed == other.MaxSpeed &&
            MaxAcceleration == other.MaxAcceleration && SensorRange == other.SensorRange &&
            MetabolismPerSecond == other.MetabolismPerSecond && MovementCostPerSecond == other.MovementCostPerSecond &&
            SensorCostPerSecond == other.SensorCostPerSecond;

        public override bool Equals(object obj) => obj is CreatureBody other && Equals(other);

        public override int GetHashCode() =>
            ((((EatRadius.GetHashCode() * 397 ^ MaxEnergy.GetHashCode()) * 397 ^ MaxSpeed.GetHashCode()) * 397 ^
              SensorRange.GetHashCode()) * 397 ^ MetabolismPerSecond.GetHashCode()) * 397 ^ MovementCostPerSecond.GetHashCode();

        public override string ToString() =>
            $"(eat radius {EatRadius}, capacity {MaxEnergy}, max speed {MaxSpeed}, acceleration {MaxAcceleration}, " +
            $"sensor range {SensorRange}, metabolism {MetabolismPerSecond}, movement cost {MovementCostPerSecond}, " +
            $"sensor cost {SensorCostPerSecond})";

        // A factor of exactly 1 stays 1 for any exponent, so the reference body never depends on how pow rounds.
        private static float Power(float factor, float exponent) => factor == 1f ? 1f : MathF.Pow(factor, exponent);
    }
}
