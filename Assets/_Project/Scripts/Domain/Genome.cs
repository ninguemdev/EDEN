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
    /// Heritable description of a creature: a network, <see cref="BodyGenes"/> and <see cref="PlasticityGenes"/>. It
    /// never holds energy, age, position or any brain state. The network is one of two formats, told apart by
    /// <see cref="Network"/>: the fixed-topology MLP (<see cref="MlpNetwork"/>: <see cref="Topology"/> and flat base
    /// weights, the legacy control) or a NEAT network (<see cref="NeatNetwork.Kind"/>: <see cref="Neat"/>, nodes and
    /// connections with historical markings, always fixed during a life). The weight members only exist for the MLP and
    /// throw for a NEAT genome. Immutable: weights are copied in on construction and copied out by
    /// <see cref="CopyWeights"/>, so variants (e.g. mutations) are built as new genomes and no two owners can share
    /// mutable weight storage. Schema 2 added the body genes and schema 3 the plasticity coefficients; an older genome is
    /// the same genome with neutral body genes and a fixed network.
    /// </summary>
    public sealed class Genome
    {
        public const int CurrentSchemaVersion = 3;

        /// <summary>Weights are kept within [-MaxAbsWeight, MaxAbsWeight].</summary>
        public const float MaxAbsWeight = 3f;

        private readonly NetworkTopology topology;
        private readonly float[] weights;
        private readonly NeatNetwork neat;

        /// <summary>Genome with neutral body genes (the reference body).</summary>
        public Genome(NetworkTopology topology, float[] weights)
            : this(topology, weights, BodyGenes.Neutral)
        {
        }

        /// <summary>Genome of a fixed network (no plasticity).</summary>
        public Genome(NetworkTopology topology, float[] weights, BodyGenes body)
            : this(topology, weights, body, PlasticityGenes.Fixed)
        {
        }

        /// <remarks>
        /// <see cref="BodyGenes"/> and <see cref="PlasticityGenes"/> can only hold values within their limits, so every
        /// genome is valid.
        /// </remarks>
        public Genome(NetworkTopology topology, float[] weights, BodyGenes body, PlasticityGenes plasticity)
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

            this.topology = topology;
            this.weights = (float[])weights.Clone();
            Body = body;
            Plasticity = plasticity;
        }

        /// <summary>NEAT genome with neutral body genes (the reference body).</summary>
        public Genome(NeatNetwork network)
            : this(network, BodyGenes.Neutral)
        {
        }

        /// <summary>NEAT genome of the legacy senses (no eyes); its network is fixed during a life.</summary>
        public Genome(NeatNetwork network, BodyGenes body)
            : this(network, body, null)
        {
        }

        /// <summary>
        /// NEAT genome; its network is fixed during a life (no plasticity coefficients). A network of an eye contract
        /// (<see cref="NeuralContract.HasEyes"/>) needs <paramref name="eyes"/> (possibly without any eye); one of the
        /// legacy contract has none.
        /// </summary>
        public Genome(NeatNetwork network, BodyGenes body, EyeSet eyes)
        {
            neat = network ?? throw new ArgumentNullException(nameof(network));
            if (network.Contract.HasEyes != (eyes != null))
                throw new ArgumentException(eyes == null
                    ? $"A network of {network.Contract.Id} needs its eyes."
                    : $"Only a network of {NeuralContract.EyesId} or {NeuralContract.EyesWaterId} has eyes.", nameof(eyes));
            Body = body;
            Eyes = eyes;
            Plasticity = PlasticityGenes.Fixed;
        }

        /// <summary>Kind of network recorded in files: a fixed-topology MLP described by <see cref="Topology"/> and flat weights.</summary>
        public const string MlpNetwork = "mlp";

        public int SchemaVersion => CurrentSchemaVersion;

        /// <summary>Kind of network: <see cref="MlpNetwork"/> or <see cref="NeatNetwork.Kind"/>; different formats.</summary>
        public string Network => neat == null ? MlpNetwork : NeatNetwork.Kind;

        /// <summary>The NEAT network, or null for an MLP genome.</summary>
        public NeatNetwork Neat => neat;

        /// <summary>
        /// What the network reads and writes: the NEAT network's contract, and <see cref="NeuralContract.Legacy"/> for
        /// every MLP genome.
        /// </summary>
        public NeuralContract Contract => neat?.Contract ?? NeuralContract.Legacy;

        /// <summary>Shape of the MLP; throws for a NEAT genome.</summary>
        public NetworkTopology Topology => RequireMlp().topology;

        /// <summary>Number of MLP parameters; throws for a NEAT genome.</summary>
        public int WeightCount => RequireMlp().weights.Length;

        public BodyGenes Body { get; }

        /// <summary>The eyes of a genome of an eye contract; null for the legacy senses.</summary>
        public EyeSet Eyes { get; }

        /// <summary>Lifetime plasticity coefficients; <see cref="PlasticityGenes.Fixed"/> for a fixed network.</summary>
        public PlasticityGenes Plasticity { get; }

        public float GetWeight(int index) => RequireMlp().weights[index];

        /// <summary>Fresh copy of the MLP weights; changing it never affects this genome. Throws for a NEAT genome.</summary>
        public float[] CopyWeights() => (float[])RequireMlp().weights.Clone();

        public void CopyWeightsTo(float[] destination)
        {
            RequireMlp();
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (destination.Length < weights.Length)
                throw new ArgumentException("Destination is too small.", nameof(destination));
            Array.Copy(weights, destination, weights.Length);
        }

        /// <summary>Same network and plasticity with other body genes.</summary>
        public Genome WithBody(BodyGenes body) =>
            neat == null ? new Genome(topology, weights, body, Plasticity) : new Genome(neat, body, Eyes);

        /// <summary>
        /// Same weights and body genes with other plasticity coefficients. Only the MLP has a plastic rule; a NEAT genome
        /// only accepts <see cref="PlasticityGenes.Fixed"/>.
        /// </summary>
        public Genome WithPlasticity(PlasticityGenes plasticity)
        {
            if (neat == null)
                return new Genome(topology, weights, Body, plasticity);
            if (!plasticity.IsFixed)
                throw new InvalidOperationException("NEAT networks have no lifetime plasticity.");
            return this;
        }

        public bool HasSameContent(Genome other)
        {
            if (other == null || Network != other.Network || !Body.Equals(other.Body) ||
                !Plasticity.Equals(other.Plasticity) || !Equals(Eyes, other.Eyes))
                return false;
            if (neat != null)
                return neat.HasSameContent(other.neat);
            if (!topology.Equals(other.topology))
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

        private Genome RequireMlp()
        {
            if (neat != null)
                throw new InvalidOperationException("Topology and weights belong to MLP genomes; this one is NEAT.");
            return this;
        }
    }
}
