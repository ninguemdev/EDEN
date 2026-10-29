using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>
    /// Editable NEAT parameters of an experiment; frozen into <see cref="NeatRules"/>. Defaults follow the proportions
    /// of Stanley and Miikkulainen (2002) for small populations and are starting points to be measured, not calibrated
    /// values.
    /// </summary>
    [Serializable]
    public sealed class NeatSettings
    {
        /// <summary>Range of the weights of the minimal network and of new or replaced connections (proposed ±1).</summary>
        public float InitialWeightLimit = 1f;

        /// <summary>Chance that a child's weights are mutated at all (proposed 0.8).</summary>
        public float WeightMutationProbability = 0.8f;

        /// <summary>When weights mutate, chance that a connection is perturbed rather than replaced (proposed 0.9).</summary>
        public float WeightPerturbProbability = 0.9f;

        /// <summary>Standard deviation of the Gaussian perturbation of a weight (proposed 0.3).</summary>
        public float WeightSigma = 0.3f;

        /// <summary>Chance of splitting an enabled connection with a new hidden node (proposed 0.03).</summary>
        public float AddNodeProbability = 0.03f;

        /// <summary>Chance of adding a new connection (proposed 0.05).</summary>
        public float AddConnectionProbability = 0.05f;

        /// <summary>Chance of flipping one connection between enabled and disabled (proposed 0.01).</summary>
        public float ToggleProbability = 0.01f;

        /// <summary>Random (source, target) pairs tried before giving up on adding a connection (proposed 20).</summary>
        public int AddConnectionAttempts = 20;

        public NeatSettings Clone() => (NeatSettings)MemberwiseClone();
    }

    /// <summary>Validated, immutable NEAT parameters.</summary>
    public sealed class NeatRules
    {
        public const int MaxAddConnectionAttempts = 1000;

        public NeatRules(NeatSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var errors = new List<string>();
            if (!(settings.InitialWeightLimit > 0f && settings.InitialWeightLimit <= NeatNetwork.MaxAbsWeight))
                errors.Add($"{nameof(settings.InitialWeightLimit)} must be in (0, {NeatNetwork.MaxAbsWeight}] " +
                           $"(was {settings.InitialWeightLimit})");
            Probability(settings.WeightMutationProbability, nameof(settings.WeightMutationProbability), errors);
            Probability(settings.WeightPerturbProbability, nameof(settings.WeightPerturbProbability), errors);
            Probability(settings.AddNodeProbability, nameof(settings.AddNodeProbability), errors);
            Probability(settings.AddConnectionProbability, nameof(settings.AddConnectionProbability), errors);
            Probability(settings.ToggleProbability, nameof(settings.ToggleProbability), errors);
            if (!(settings.WeightSigma >= 0f) || float.IsInfinity(settings.WeightSigma))
                errors.Add($"{nameof(settings.WeightSigma)} must be finite and >= 0 (was {settings.WeightSigma})");
            if (settings.AddConnectionAttempts < 1 || settings.AddConnectionAttempts > MaxAddConnectionAttempts)
                errors.Add($"{nameof(settings.AddConnectionAttempts)} must be in [1, {MaxAddConnectionAttempts}] " +
                           $"(was {settings.AddConnectionAttempts})");
            if (errors.Count > 0)
                throw new ArgumentException("Invalid NEAT settings: " + string.Join("; ", errors));

            InitialWeightLimit = settings.InitialWeightLimit;
            WeightMutationProbability = settings.WeightMutationProbability;
            WeightPerturbProbability = settings.WeightPerturbProbability;
            WeightSigma = settings.WeightSigma;
            AddNodeProbability = settings.AddNodeProbability;
            AddConnectionProbability = settings.AddConnectionProbability;
            ToggleProbability = settings.ToggleProbability;
            AddConnectionAttempts = settings.AddConnectionAttempts;
        }

        public float InitialWeightLimit { get; }
        public float WeightMutationProbability { get; }
        public float WeightPerturbProbability { get; }
        public float WeightSigma { get; }
        public float AddNodeProbability { get; }
        public float AddConnectionProbability { get; }
        public float ToggleProbability { get; }
        public int AddConnectionAttempts { get; }

        private static void Probability(float value, string name, List<string> errors)
        {
            if (!(value >= 0f && value <= 1f))
                errors.Add($"{name} must be in [0, 1] (was {value})");
        }
    }

    /// <summary>What one NEAT mutation changed.</summary>
    public readonly struct NeatMutationCount
    {
        public NeatMutationCount(int perturbedWeights, int replacedWeights, int addedNodes, int addedConnections,
            int toggledConnections)
        {
            PerturbedWeights = perturbedWeights;
            ReplacedWeights = replacedWeights;
            AddedNodes = addedNodes;
            AddedConnections = addedConnections;
            ToggledConnections = toggledConnections;
        }

        public int PerturbedWeights { get; }
        public int ReplacedWeights { get; }
        public int AddedNodes { get; }
        public int AddedConnections { get; }
        public int ToggledConnections { get; }

        /// <summary>Weights that changed value, the counterpart of the MLP's mutated weights.</summary>
        public int MutatedWeights => PerturbedWeights + ReplacedWeights;

        public bool IsStructural => AddedNodes + AddedConnections + ToggledConnections > 0;
    }

    /// <summary>
    /// Mutation of a NEAT genome: a new genome, the parent untouched. Draws, in this order, from one stream:
    /// <list type="number">
    /// <item>a coin for the weights; when it hits, for every connection in innovation order a coin chooses a Gaussian
    /// perturbation (σ = <see cref="NeatRules.WeightSigma"/>) or a uniform replacement within
    /// ±<see cref="NeatRules.InitialWeightLimit"/>, then one draw for the new value, limited to
    /// ±<see cref="NeatNetwork.MaxAbsWeight"/>;</item>
    /// <item>a coin for a new node; when it hits and the network has an enabled connection, one draw picks it (in
    /// innovation order among the enabled ones) and it is split: disabled, plus source → node with weight 1 and
    /// node → target with the old weight, numbered by the <see cref="InnovationHistory"/>;</item>
    /// <item>a coin for a new connection; when it hits, up to <see cref="NeatRules.AddConnectionAttempts"/> pairs are
    /// drawn (source among bias, inputs and hidden nodes, target among hidden and output nodes, both by id order) until
    /// one is new and keeps the network free of cycles; it gets a uniform weight and its number from the history;</item>
    /// <item>a coin for a toggle; when it hits, one draw picks a connection (innovation order) and flips it.</item>
    /// </list>
    /// Structural steps see the result of the previous ones. Body genes are copied unchanged.
    /// </summary>
    public static class NeatMutation
    {
        public static Genome Mutate(Genome parent, NeatRules rules, InnovationHistory history, GaussianSampler random,
            out NeatMutationCount count)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));
            if (history == null)
                throw new ArgumentNullException(nameof(history));
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            var network = parent.Neat ??
                          throw new ArgumentException($"Only NEAT genomes mutate here, got a {parent.Network} one.",
                              nameof(parent));
            if (network.Contract != history.Contract)
                throw new ArgumentException($"The genome uses {network.Contract.Id}, the history {history.Contract.Id}.",
                    nameof(history));

            var genes = new List<NeatConnection>(network.Connections);
            int perturbed = 0, replaced = 0, nodes = 0, added = 0, toggled = 0;
            var source = random.Source;

            if (source.NextFloat() < rules.WeightMutationProbability)
            {
                for (var i = 0; i < genes.Count; i++)
                {
                    float value;
                    if (source.NextFloat() < rules.WeightPerturbProbability)
                    {
                        value = genes[i].Weight + (float)(rules.WeightSigma * random.NextStandard());
                        perturbed++;
                    }
                    else
                    {
                        value = source.NextFloat(-rules.InitialWeightLimit, rules.InitialWeightLimit);
                        replaced++;
                    }

                    genes[i] = genes[i].WithWeight(Math.Min(NeatNetwork.MaxAbsWeight, Math.Max(-NeatNetwork.MaxAbsWeight, value)));
                }
            }

            if (source.NextFloat() < rules.AddNodeProbability && AddNode(genes, history, source))
                nodes++;

            if (source.NextFloat() < rules.AddConnectionProbability &&
                AddConnection(genes, network.Contract, rules, history, source))
                added++;

            if (source.NextFloat() < rules.ToggleProbability && genes.Count > 0)
            {
                var index = (int)source.NextUInt((uint)genes.Count);
                genes[index] = genes[index].WithEnabled(!genes[index].Enabled);
                toggled++;
            }

            count = new NeatMutationCount(perturbed, replaced, nodes, added, toggled);
            return new Genome(new NeatNetwork(network.Contract, genes), parent.Body);
        }

        // Splits one enabled connection, drawn in innovation order; false when none is enabled (no draw).
        private static bool AddNode(List<NeatConnection> genes, InnovationHistory history, IRandomSource source)
        {
            var enabled = 0;
            foreach (var gene in genes)
            {
                if (gene.Enabled)
                    enabled++;
            }

            if (enabled == 0)
                return false;
            var pick = (int)source.NextUInt((uint)enabled);
            for (var i = 0; i < genes.Count; i++)
            {
                if (!genes[i].Enabled || pick-- > 0)
                    continue;
                var old = genes[i];
                var split = history.Split(old.Innovation, old.From, old.To);
                if (Mentions(genes, split.NodeId))
                    return false; // this genome already holds the node of that split: nothing new to add
                genes[i] = old.WithEnabled(false);
                genes.Add(new NeatConnection(split.InInnovation, old.From, split.NodeId, 1f, true));
                genes.Add(new NeatConnection(split.OutInnovation, split.NodeId, old.To, old.Weight, true));
                return true;
            }

            return false;
        }

        private static bool Mentions(List<NeatConnection> genes, int node)
        {
            foreach (var gene in genes)
            {
                if (gene.From == node || gene.To == node)
                    return true;
            }

            return false;
        }

        private static bool AddConnection(List<NeatConnection> genes, NeuralContract contract, NeatRules rules,
            InnovationHistory history, IRandomSource source)
        {
            var current = new NeatNetwork(contract, genes);
            var hidden = current.HiddenNodes;
            var sourceCount = 1 + contract.InputCount + hidden.Count;
            var targetCount = contract.OutputCount + hidden.Count;
            for (var attempt = 0; attempt < rules.AddConnectionAttempts; attempt++)
            {
                var s = (int)source.NextUInt((uint)sourceCount);
                var t = (int)source.NextUInt((uint)targetCount);
                var from = s <= contract.InputCount ? s : hidden[s - 1 - contract.InputCount];
                var to = t < contract.OutputCount ? NeatNetwork.OutputNodeId(contract, t) : hidden[t - contract.OutputCount];
                if (from == to || current.HasConnection(from, to) || current.WouldCreateCycle(from, to))
                    continue;
                var weight = source.NextFloat(-rules.InitialWeightLimit, rules.InitialWeightLimit);
                genes.Add(new NeatConnection(history.ConnectionInnovation(from, to), from, to, weight, true));
                return true;
            }

            return false;
        }
    }
}
