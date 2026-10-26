using System;

namespace Eden.Domain
{
    /// <summary>Shape of a fully connected network with one hidden layer (inputs → hidden → outputs).</summary>
    public readonly struct NetworkTopology : IEquatable<NetworkTopology>
    {
        /// <summary>The fixed brain of the MVP: 12 observation inputs, 16 hidden neurons, 3 action outputs.</summary>
        public static readonly NetworkTopology Mlp12x16x3 = new NetworkTopology(Observation.Size, 16, 3);

        public NetworkTopology(int inputs, int hidden, int outputs)
        {
            if (inputs <= 0 || hidden <= 0 || outputs <= 0)
                throw new ArgumentOutOfRangeException(nameof(inputs), "Layer sizes must be positive.");
            Inputs = inputs;
            Hidden = hidden;
            Outputs = outputs;
        }

        public int Inputs { get; }
        public int Hidden { get; }
        public int Outputs { get; }

        /// <summary>Weights and biases of both layers.</summary>
        public int ParameterCount => Inputs * Hidden + Hidden + Hidden * Outputs + Outputs;

        public bool Equals(NetworkTopology other) =>
            Inputs == other.Inputs && Hidden == other.Hidden && Outputs == other.Outputs;

        public override bool Equals(object obj) => obj is NetworkTopology other && Equals(other);
        public override int GetHashCode() => (Inputs * 397 ^ Hidden) * 397 ^ Outputs;
        public override string ToString() => $"mlp-{Inputs}-{Hidden}-{Outputs}";
    }

    /// <summary>
    /// Heritable description of a creature: network topology, base weights and <see cref="BodyGenes"/> (plasticity
    /// coefficients come later). It never holds energy, age, position or any brain state. Immutable: weights are
    /// copied in on construction and copied out by <see cref="CopyWeights"/>, so variants (e.g. mutations) are
    /// built as new genomes and no two owners can share mutable weight storage. Schema 2 added the body genes; a
    /// schema 1 genome is the same genome with neutral body genes.
    /// </summary>
    public sealed class Genome
    {
        public const int CurrentSchemaVersion = 2;

        /// <summary>Weights are kept within [-MaxAbsWeight, MaxAbsWeight].</summary>
        public const float MaxAbsWeight = 3f;

        private readonly float[] weights;

        /// <summary>Genome with neutral body genes (the reference body).</summary>
        public Genome(NetworkTopology topology, float[] weights)
            : this(topology, weights, BodyGenes.Neutral)
        {
        }

        /// <remarks><see cref="BodyGenes"/> can only hold values within their limits, so every genome is valid.</remarks>
        public Genome(NetworkTopology topology, float[] weights, BodyGenes body)
        {
            if (weights == null)
                throw new ArgumentNullException(nameof(weights));
            if (weights.Length != topology.ParameterCount)
                throw new ArgumentException(
                    $"{topology} needs {topology.ParameterCount} weights, got {weights.Length}.", nameof(weights));
            for (var i = 0; i < weights.Length; i++)
            {
                var value = weights[i];
                if (float.IsNaN(value) || float.IsInfinity(value) || value < -MaxAbsWeight || value > MaxAbsWeight)
                    throw new ArgumentException(
                        $"Weight {i} = {value} must be finite and within ±{MaxAbsWeight}.", nameof(weights));
            }

            Topology = topology;
            this.weights = (float[])weights.Clone();
            Body = body;
        }

        public int SchemaVersion => CurrentSchemaVersion;
        public NetworkTopology Topology { get; }
        public int WeightCount => weights.Length;
        public BodyGenes Body { get; }

        public float GetWeight(int index) => weights[index];

        /// <summary>Fresh copy of the weights; changing it never affects this genome.</summary>
        public float[] CopyWeights() => (float[])weights.Clone();

        public void CopyWeightsTo(float[] destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (destination.Length < weights.Length)
                throw new ArgumentException("Destination is too small.", nameof(destination));
            Array.Copy(weights, destination, weights.Length);
        }

        /// <summary>Same weights with other body genes.</summary>
        public Genome WithBody(BodyGenes body) => new Genome(Topology, weights, body);

        public bool HasSameContent(Genome other)
        {
            if (other == null || !Topology.Equals(other.Topology) || !Body.Equals(other.Body))
                return false;
            for (var i = 0; i < weights.Length; i++)
            {
                if (weights[i] != other.weights[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Weights drawn uniformly in [-limit, limit] from <paramref name="random"/>, in parameter order; body genes
        /// are neutral.
        /// </summary>
        public static Genome CreateRandom(NetworkTopology topology, IRandomSource random, float limit)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (float.IsNaN(limit) || limit <= 0f || limit > MaxAbsWeight)
                throw new ArgumentOutOfRangeException(nameof(limit), $"Limit must be in (0, {MaxAbsWeight}].");

            var weights = new float[topology.ParameterCount];
            for (var i = 0; i < weights.Length; i++)
                weights[i] = random.NextFloat(-limit, limit);
            return new Genome(topology, weights);
        }
    }
}
