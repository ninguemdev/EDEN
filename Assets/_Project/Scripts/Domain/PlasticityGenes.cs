using System;

namespace Eden.Domain
{
    /// <summary>
    /// Heritable coefficients of the lifetime plasticity experiment (M7): a learning rate and a decay for the weight
    /// changes a brain may make during one life. Both 0 (<see cref="Fixed"/>) is the fixed network, which never changes
    /// during a life. The changes themselves are individual state and are never part of a genome.
    /// </summary>
    public readonly struct PlasticityGenes : IEquatable<PlasticityGenes>
    {
        public const float MaxLearningRate = 1f;
        public const float MaxDecay = 0.2f;

        /// <summary>No plasticity: the network stays as the genome built it.</summary>
        public static readonly PlasticityGenes Fixed = default;

        public PlasticityGenes(float learningRate, float decay)
        {
            if (!(learningRate >= 0f && learningRate <= MaxLearningRate))
                throw new ArgumentOutOfRangeException(nameof(learningRate),
                    $"Learning rate {learningRate} must be in [0, {MaxLearningRate}].");
            if (!(decay >= 0f && decay <= MaxDecay))
                throw new ArgumentOutOfRangeException(nameof(decay), $"Decay {decay} must be in [0, {MaxDecay}].");
            LearningRate = learningRate;
            Decay = decay;
        }

        /// <summary>Scale of each change, per decision.</summary>
        public float LearningRate { get; }

        /// <summary>Fraction of the accumulated change forgotten at every decision.</summary>
        public float Decay { get; }

        /// <summary>A zero learning rate never changes a weight, whatever the decay.</summary>
        public bool IsPlastic => LearningRate > 0f;

        public bool IsFixed => LearningRate == 0f && Decay == 0f;

        /// <summary>Uniform within the limits: learning rate, then decay (2 draws).</summary>
        public static PlasticityGenes CreateRandom(IRandomSource random)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            var learningRate = random.NextFloat(0f, MaxLearningRate);
            var decay = random.NextFloat(0f, MaxDecay);
            return new PlasticityGenes(learningRate, decay);
        }

        public bool Equals(PlasticityGenes other) => LearningRate == other.LearningRate && Decay == other.Decay;
        public override bool Equals(object obj) => obj is PlasticityGenes other && Equals(other);
        public override int GetHashCode() => LearningRate.GetHashCode() * 397 ^ Decay.GetHashCode();
        public override string ToString() => $"(learning rate {LearningRate}, decay {Decay})";
    }
}
