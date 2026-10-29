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

        /// <summary>With evolving eyes, chance that each trait of each eye mutates (proposed 0.1).</summary>
        public float EyeMutationProbability = 0.1f;

        /// <summary>Standard deviation of an eye's orientation change, radians (proposed 0.25, about 14°).</summary>
        public float EyeOrientationSigma = 0.25f;

        /// <summary>Standard deviation of an eye's aperture change, radians (proposed 0.2, about 11°).</summary>
        public float EyeApertureSigma = 0.2f;

        /// <summary>Standard deviation of an eye's range factor change (proposed 0.1).</summary>
        public float EyeRangeSigma = 0.1f;

        /// <summary>With evolving eyes, chance per child of duplicating an eye into a free slot (proposed 0.02).</summary>
        public float EyeAddProbability = 0.02f;

        /// <summary>With evolving eyes, chance per child of losing an eye, never the last one (proposed 0.01).</summary>
        public float EyeRemoveProbability = 0.01f;

        /// <summary>
        /// In crossover, chance that a matching connection disabled in either parent stays disabled in the child
        /// (proposed 0.75).
        /// </summary>
        public float DisabledInheritProbability = 0.75f;

        /// <summary>Compatibility weight c1 of excess connections (proposed 1).</summary>
        public float ExcessCoefficient = 1f;

        /// <summary>Compatibility weight c2 of disjoint connections (proposed 1).</summary>
        public float DisjointCoefficient = 1f;

        /// <summary>Compatibility weight c3 of the mean weight difference of matching connections (proposed 0.4).</summary>
        public float WeightDifferenceCoefficient = 0.4f;

        /// <summary>
        /// Genomes with fewer connections than this are not normalized by size in the compatibility distance (N = 1);
        /// proposed 20, as in the original NEAT.
        /// </summary>
        public int NormalizationThreshold = 20;

        /// <summary>
        /// Compatibility weight c4 of the eye difference (experimental EDEN extension). 0, the default, keeps species purely
        /// neural, as in NEAT.
        /// </summary>
        public float EyeCompatibilityCoefficient;

        /// <summary>Genomes closer than this distance to a species' representative join it (proposed 3).</summary>
        public float CompatibilityThreshold = 3f;

        /// <summary>Generations without a new best fitness after which a species stops reproducing (proposed 15).</summary>
        public int StagnationGenerations = 15;

        /// <summary>Species with the best fitness so far that are never removed for stagnation (proposed 2).</summary>
        public int ProtectedSpecies = 2;

        /// <summary>Species at least this large copy their champion unchanged into the next generation (proposed 5).</summary>
        public int ChampionMinimumSize = 5;

        /// <summary>Best fraction of each species that may be a parent, at least one member (proposed 0.2).</summary>
        public float SurvivalFraction = 0.2f;

        /// <summary>Chance that a child comes from crossover before mutating, rather than from mutation alone (proposed 0.75).</summary>
        public float CrossoverProbability = 0.75f;

        /// <summary>In crossover, chance that the second parent comes from another species (proposed 0.001).</summary>
        public float InterspeciesProbability = 0.001f;

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
            Probability(settings.DisabledInheritProbability, nameof(settings.DisabledInheritProbability), errors);
            Probability(settings.EyeMutationProbability, nameof(settings.EyeMutationProbability), errors);
            Coefficient(settings.EyeOrientationSigma, nameof(settings.EyeOrientationSigma), errors);
            Coefficient(settings.EyeApertureSigma, nameof(settings.EyeApertureSigma), errors);
            Coefficient(settings.EyeRangeSigma, nameof(settings.EyeRangeSigma), errors);
            Probability(settings.EyeAddProbability, nameof(settings.EyeAddProbability), errors);
            Probability(settings.EyeRemoveProbability, nameof(settings.EyeRemoveProbability), errors);
            Coefficient(settings.ExcessCoefficient, nameof(settings.ExcessCoefficient), errors);
            Coefficient(settings.DisjointCoefficient, nameof(settings.DisjointCoefficient), errors);
            Coefficient(settings.WeightDifferenceCoefficient, nameof(settings.WeightDifferenceCoefficient), errors);
            Coefficient(settings.EyeCompatibilityCoefficient, nameof(settings.EyeCompatibilityCoefficient), errors);
            if (settings.NormalizationThreshold < 0 || settings.NormalizationThreshold > NeatNetwork.MaxConnections)
                errors.Add($"{nameof(settings.NormalizationThreshold)} must be in [0, {NeatNetwork.MaxConnections}] " +
                           $"(was {settings.NormalizationThreshold})");
            if (!(settings.CompatibilityThreshold > 0f) || float.IsInfinity(settings.CompatibilityThreshold))
                errors.Add($"{nameof(settings.CompatibilityThreshold)} must be finite and > 0 " +
                           $"(was {settings.CompatibilityThreshold})");
            if (settings.StagnationGenerations < 1)
                errors.Add($"{nameof(settings.StagnationGenerations)} must be >= 1 (was {settings.StagnationGenerations})");
            if (settings.ProtectedSpecies < 1)
                errors.Add($"{nameof(settings.ProtectedSpecies)} must be >= 1 so a species always survives " +
                           $"(was {settings.ProtectedSpecies})");
            if (settings.ChampionMinimumSize < 1)
                errors.Add($"{nameof(settings.ChampionMinimumSize)} must be >= 1 (was {settings.ChampionMinimumSize})");
            if (!(settings.SurvivalFraction > 0f && settings.SurvivalFraction <= 1f))
                errors.Add($"{nameof(settings.SurvivalFraction)} must be in (0, 1] (was {settings.SurvivalFraction})");
            Probability(settings.CrossoverProbability, nameof(settings.CrossoverProbability), errors);
            Probability(settings.InterspeciesProbability, nameof(settings.InterspeciesProbability), errors);
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
            DisabledInheritProbability = settings.DisabledInheritProbability;
            EyeMutation = new EyeMutation(settings.EyeMutationProbability, settings.EyeOrientationSigma,
                settings.EyeApertureSigma, settings.EyeRangeSigma, settings.EyeAddProbability, settings.EyeRemoveProbability);
            ExcessCoefficient = settings.ExcessCoefficient;
            DisjointCoefficient = settings.DisjointCoefficient;
            WeightDifferenceCoefficient = settings.WeightDifferenceCoefficient;
            NormalizationThreshold = settings.NormalizationThreshold;
            EyeCompatibilityCoefficient = settings.EyeCompatibilityCoefficient;
            CompatibilityThreshold = settings.CompatibilityThreshold;
            StagnationGenerations = settings.StagnationGenerations;
            ProtectedSpecies = settings.ProtectedSpecies;
            ChampionMinimumSize = settings.ChampionMinimumSize;
            SurvivalFraction = settings.SurvivalFraction;
            CrossoverProbability = settings.CrossoverProbability;
            InterspeciesProbability = settings.InterspeciesProbability;
        }

        public float InitialWeightLimit { get; }
        public float WeightMutationProbability { get; }
        public float WeightPerturbProbability { get; }
        public float WeightSigma { get; }
        public float AddNodeProbability { get; }
        public float AddConnectionProbability { get; }
        public float ToggleProbability { get; }
        public int AddConnectionAttempts { get; }
        public float DisabledInheritProbability { get; }

        /// <summary>How eyes mutate when an experiment lets them evolve.</summary>
        public EyeMutation EyeMutation { get; }
        public float ExcessCoefficient { get; }
        public float DisjointCoefficient { get; }
        public float WeightDifferenceCoefficient { get; }
        public int NormalizationThreshold { get; }
        public float EyeCompatibilityCoefficient { get; }
        public float CompatibilityThreshold { get; }
        public int StagnationGenerations { get; }
        public int ProtectedSpecies { get; }
        public int ChampionMinimumSize { get; }
        public float SurvivalFraction { get; }
        public float CrossoverProbability { get; }
        public float InterspeciesProbability { get; }

        private static void Coefficient(float value, string name, List<string> errors)
        {
            if (!(value >= 0f) || float.IsInfinity(value))
                errors.Add($"{name} must be finite and >= 0 (was {value})");
        }

        private static void Probability(float value, string name, List<string> errors)
        {
            if (!(value >= 0f && value <= 1f))
                errors.Add($"{name} must be in [0, 1] (was {value})");
        }
    }

    /// <summary>
    /// How eyes mutate: chance per trait and noise of each, and chances to add (duplicate) or remove an eye; the default
    /// never mutates and draws nothing.
    /// </summary>
    public readonly struct EyeMutation
    {
        public EyeMutation(float probability, float orientationSigma, float apertureSigma, float rangeSigma,
            float addProbability = 0f, float removeProbability = 0f)
        {
            Probability = probability;
            OrientationSigma = orientationSigma;
            ApertureSigma = apertureSigma;
            RangeSigma = rangeSigma;
            AddProbability = addProbability;
            RemoveProbability = removeProbability;
        }

        /// <summary>Chance per genome of duplicating an eye into a free slot.</summary>
        public float AddProbability { get; }

        /// <summary>Chance per genome of removing an eye (never the last).</summary>
        public float RemoveProbability { get; }

        public float Probability { get; }
        public float OrientationSigma { get; }
        public float ApertureSigma { get; }
        public float RangeSigma { get; }

        /// <summary>False for the default (fixed eyes): no draws at all.</summary>
        public bool IsEnabled => Probability > 0f || AddProbability > 0f || RemoveProbability > 0f;
    }

    /// <summary>
    /// Body genes and eyes that mutate with a NEAT child, and how (the experiment's chance and noise for the genes, the
    /// eye rules for evolving eyes).
    /// </summary>
    public readonly struct BodyMutation
    {
        public BodyMutation(BodyGeneSet genes, float probability, float sigma, EyeMutation eyes = default)
        {
            Genes = genes;
            Probability = probability;
            Sigma = sigma;
            Eyes = eyes;
        }

        /// <summary>How eyes mutate; the default keeps them fixed.</summary>
        public EyeMutation Eyes { get; }

        /// <summary>Genes that mutate; <see cref="BodyGeneSet.None"/> (the default) copies the body and draws nothing.</summary>
        public BodyGeneSet Genes { get; }

        public float Probability { get; }
        public float Sigma { get; }
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
    /// Structural steps see the result of the previous ones. Then the body genes of <see cref="BodyMutation.Genes"/>
    /// mutate as <see cref="Reproduction.MutateBody"/> does, and then the eyes as <see cref="Reproduction.MutateEyes"/>
    /// does; by default both are copied and draw nothing.
    /// </summary>
    public static class NeatMutation
    {
        public static Genome Mutate(Genome parent, NeatRules rules, InnovationHistory history, GaussianSampler random,
            out NeatMutationCount count, BodyMutation body = default)
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
            var bodyGenes = Reproduction.MutateBody(parent.Body, body.Genes, body.Probability, body.Sigma, random);
            var eyes = parent.Eyes == null ? null : Reproduction.MutateEyes(parent.Eyes, body.Eyes, random);
            return new Genome(new NeatNetwork(network.Contract, genes), bodyGenes, eyes);
        }

        /// <summary>
        /// Weights and body only, the way a shared world mutates a child's genome (no structural change, whose numbering
        /// belongs to a generational evolution): each connection in innovation order with chance
        /// <paramref name="probability"/> gets Gaussian noise of <paramref name="sigma"/>, limited to
        /// ±<see cref="NeatNetwork.MaxAbsWeight"/>; then the body genes of <paramref name="bodyGenes"/> as
        /// <see cref="Reproduction.MutateBody"/> does. <paramref name="mutated"/> counts weights.
        /// </summary>
        public static Genome MutateWeights(Genome parent, float probability, float sigma, BodyGeneSet bodyGenes,
            GaussianSampler random, out int mutated, EyeMutation eyeRules = default)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            var network = parent.Neat ??
                          throw new ArgumentException($"Only NEAT genomes mutate here, got a {parent.Network} one.",
                              nameof(parent));
            var genes = new NeatConnection[network.Connections.Count];
            mutated = 0;
            for (var i = 0; i < genes.Length; i++)
            {
                genes[i] = network.Connections[i];
                if (random.Source.NextFloat() >= probability)
                    continue;
                var value = genes[i].Weight + (float)(sigma * random.NextStandard());
                genes[i] = genes[i].WithWeight(Math.Min(NeatNetwork.MaxAbsWeight, Math.Max(-NeatNetwork.MaxAbsWeight, value)));
                mutated++;
            }

            var body = Reproduction.MutateBody(parent.Body, bodyGenes, probability, sigma, random);
            var eyes = parent.Eyes == null ? null : Reproduction.MutateEyes(parent.Eyes, eyeRules, random);
            return new Genome(new NeatNetwork(network.Contract, genes), body, eyes);
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
