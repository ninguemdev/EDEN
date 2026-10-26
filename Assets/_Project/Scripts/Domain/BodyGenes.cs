using System;

namespace Eden.Domain
{
    /// <summary>Biological genes in genome order. Persisted by position: never renumber.</summary>
    public enum BodyGene
    {
        /// <summary>Body radius, which is the distance at which food is eaten.</summary>
        Size = 0,

        /// <summary>Speed cap.</summary>
        Speed = 1,

        /// <summary>Sensor range for food and walls.</summary>
        Sensor = 2,

        /// <summary>Metabolic efficiency: a higher value lowers the resting metabolism.</summary>
        Efficiency = 3
    }

    /// <summary>Body genes a run expresses. <see cref="None"/> gives every creature the fixed reference body (control).</summary>
    [Flags]
    public enum BodyGeneSet
    {
        None = 0,
        Size = 1,
        Speed = 2,
        Sensor = 4,
        Efficiency = 8,
        All = Size | Speed | Sensor | Efficiency
    }

    /// <summary>
    /// Heritable body genes, each within ±<see cref="MaxAbsValue"/>; 0 is the reference body of the run config. A gene
    /// g scales its trait by 1 + g·<see cref="TraitSpan"/> (efficiency scales the metabolism by 1 − g·TraitSpan), so
    /// every trait stays between half and one and a half times its reference value. Genes say nothing about costs:
    /// what a trait costs is a rule of the world (<see cref="CreatureBody"/>).
    /// </summary>
    public readonly struct BodyGenes : IEquatable<BodyGenes>
    {
        public const int Count = 4;
        public const float MaxAbsValue = 1f;

        /// <summary>Fraction of the reference value a gene at ±<see cref="MaxAbsValue"/> adds or removes (proposed 0.5).</summary>
        public const float TraitSpan = 0.5f;

        /// <summary>All genes 0: the reference body.</summary>
        public static readonly BodyGenes Neutral = default;

        public BodyGenes(float size, float speed, float sensor, float efficiency)
        {
            Require(size, BodyGene.Size);
            Require(speed, BodyGene.Speed);
            Require(sensor, BodyGene.Sensor);
            Require(efficiency, BodyGene.Efficiency);
            Size = size;
            Speed = speed;
            Sensor = sensor;
            Efficiency = efficiency;
        }

        public float Size { get; }
        public float Speed { get; }
        public float Sensor { get; }
        public float Efficiency { get; }

        public float this[BodyGene gene]
        {
            get
            {
                switch (gene)
                {
                    case BodyGene.Size: return Size;
                    case BodyGene.Speed: return Speed;
                    case BodyGene.Sensor: return Sensor;
                    case BodyGene.Efficiency: return Efficiency;
                    default: throw new ArgumentOutOfRangeException(nameof(gene));
                }
            }
        }

        public bool IsNeutral => Size == 0f && Speed == 0f && Sensor == 0f && Efficiency == 0f;

        public static bool IsValidValue(float value) => value >= -MaxAbsValue && value <= MaxAbsValue;

        public static bool IsValidSet(BodyGeneSet set) => (set & ~BodyGeneSet.All) == 0;

        /// <summary>Copy where every gene outside <paramref name="expressed"/> is 0.</summary>
        public BodyGenes Masked(BodyGeneSet expressed)
        {
            RequireSet(expressed);
            return new BodyGenes(
                Has(expressed, BodyGeneSet.Size) ? Size : 0f,
                Has(expressed, BodyGeneSet.Speed) ? Speed : 0f,
                Has(expressed, BodyGeneSet.Sensor) ? Sensor : 0f,
                Has(expressed, BodyGeneSet.Efficiency) ? Efficiency : 0f);
        }

        /// <summary>Genes drawn uniformly within ±<see cref="MaxAbsValue"/>, in <see cref="BodyGene"/> order (4 draws).</summary>
        public static BodyGenes CreateRandom(IRandomSource random)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            var size = random.NextFloat(-MaxAbsValue, MaxAbsValue);
            var speed = random.NextFloat(-MaxAbsValue, MaxAbsValue);
            var sensor = random.NextFloat(-MaxAbsValue, MaxAbsValue);
            var efficiency = random.NextFloat(-MaxAbsValue, MaxAbsValue);
            return new BodyGenes(size, speed, sensor, efficiency);
        }

        public static void RequireSet(BodyGeneSet set)
        {
            if (!IsValidSet(set))
                throw new ArgumentOutOfRangeException(nameof(set), $"Unknown body genes in {(int)set}.");
        }

        public bool Equals(BodyGenes other) =>
            Size == other.Size && Speed == other.Speed && Sensor == other.Sensor && Efficiency == other.Efficiency;

        public override bool Equals(object obj) => obj is BodyGenes other && Equals(other);

        public override int GetHashCode() =>
            ((Size.GetHashCode() * 397 ^ Speed.GetHashCode()) * 397 ^ Sensor.GetHashCode()) * 397 ^ Efficiency.GetHashCode();

        public override string ToString() => $"(size {Size}, speed {Speed}, sensor {Sensor}, efficiency {Efficiency})";

        private static bool Has(BodyGeneSet set, BodyGeneSet gene) => (set & gene) != 0;

        private static void Require(float value, BodyGene gene)
        {
            if (!IsValidValue(value))
                throw new ArgumentOutOfRangeException(gene.ToString(),
                    $"Body gene {gene} = {value} must be finite and within ±{MaxAbsValue}.");
        }
    }
}
