using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>
    /// Validated, immutable selection and mutation rules of an experiment. Crossover is not part of the first
    /// version: every child has exactly one parent.
    /// </summary>
    public sealed class ReproductionRules
    {
        public const int MaxPopulationSize = 4096;

        public ReproductionRules(EvolutionSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            var errors = new List<string>();
            if (settings.PopulationSize < 1 || settings.PopulationSize > MaxPopulationSize)
                errors.Add($"{nameof(settings.PopulationSize)} must be in [1, {MaxPopulationSize}] (was {settings.PopulationSize})");
            if (settings.EliteCount < 0 || settings.EliteCount > settings.PopulationSize)
                errors.Add($"{nameof(settings.EliteCount)} must be in [0, {nameof(settings.PopulationSize)}] (was {settings.EliteCount})");
            if (settings.TournamentSize < 1 || settings.TournamentSize > MaxPopulationSize)
                errors.Add($"{nameof(settings.TournamentSize)} must be in [1, {MaxPopulationSize}] (was {settings.TournamentSize})");
            if (!(settings.MutationProbability >= 0f && settings.MutationProbability <= 1f))
                errors.Add($"{nameof(settings.MutationProbability)} must be in [0, 1] (was {settings.MutationProbability})");
            if (!(settings.MutationSigma >= 0f) || float.IsInfinity(settings.MutationSigma))
                errors.Add($"{nameof(settings.MutationSigma)} must be finite and >= 0 (was {settings.MutationSigma})");
            if (!BodyGenes.IsValidSet(settings.ExpressedBodyGenes))
                errors.Add($"{nameof(settings.ExpressedBodyGenes)} has unknown genes ({(int)settings.ExpressedBodyGenes})");
            if (errors.Count > 0)
                throw new ArgumentException("Invalid reproduction settings: " + string.Join("; ", errors));

            ExperimentSeed = settings.ExperimentSeed;
            PopulationSize = settings.PopulationSize;
            EliteCount = settings.EliteCount;
            TournamentSize = settings.TournamentSize;
            MutationProbability = settings.MutationProbability;
            MutationSigma = settings.MutationSigma;
            MutableBodyGenes = settings.ExpressedBodyGenes;
        }

        public ulong ExperimentSeed { get; }
        public int PopulationSize { get; }
        public int EliteCount { get; }
        public int TournamentSize { get; }
        public float MutationProbability { get; }
        public float MutationSigma { get; }

        /// <summary>Body genes that mutate: the expressed ones. The others stay neutral and draw nothing.</summary>
        public BodyGeneSet MutableBodyGenes { get; }

        /// <summary>
        /// Random stream used to create generation <paramref name="generation"/> (tournaments and mutations):
        /// sub-stream <c>generation</c> of <see cref="RandomStream.Mutation"/> for the experiment seed, so any
        /// generation can be rebuilt from its parents without replaying earlier ones.
        /// </summary>
        public GaussianSampler MutationRandom(int generation)
        {
            if (generation < 1)
                throw new ArgumentOutOfRangeException(nameof(generation), "Only generations after the first are bred.");
            return new GaussianSampler(
                SeedDerivation.CreateIndexedStream(ExperimentSeed, RandomStream.Mutation, (ulong)generation));
        }
    }

    /// <summary>A genome of the next generation and where it came from.</summary>
    public readonly struct Offspring
    {
        public Offspring(Genome genome, int parentIndex, bool isElite, int mutatedWeights)
        {
            Genome = genome ?? throw new ArgumentNullException(nameof(genome));
            ParentIndex = parentIndex;
            IsElite = isElite;
            MutatedWeights = mutatedWeights;
        }

        public Genome Genome { get; }

        /// <summary>Index of the single parent in the previous generation.</summary>
        public int ParentIndex { get; }

        /// <summary>Copied unchanged from the parent; it carries no score and is evaluated again.</summary>
        public bool IsElite { get; }

        public int MutatedWeights { get; }
    }

    /// <summary>
    /// Builds the next generation from an evaluated one: the <see cref="ReproductionRules.EliteCount"/> best genomes
    /// unchanged, then children of tournament winners, each a mutated copy of its parent. Higher fitness ranks first;
    /// ties and invalid scores (NaN or <see cref="Fitness.Worst"/>, both ranked last) are broken by the lower index,
    /// so the order of draws alone decides the result. Parents are never modified: genomes are immutable and every
    /// child is built from a fresh copy of its parent's weights.
    /// </summary>
    public static class Reproduction
    {
        public static Offspring[] Breed(ReproductionRules rules, IReadOnlyList<Genome> parents,
            IReadOnlyList<float> fitness, int childGeneration)
        {
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));
            if (parents == null)
                throw new ArgumentNullException(nameof(parents));
            if (fitness == null)
                throw new ArgumentNullException(nameof(fitness));
            if (parents.Count == 0 || parents.Count != fitness.Count)
                throw new ArgumentException("Every parent needs exactly one fitness value.", nameof(fitness));
            if (parents.Count < rules.EliteCount)
                throw new ArgumentException($"At least {rules.EliteCount} parents are needed for the elites.", nameof(parents));
            for (var i = 0; i < parents.Count; i++)
            {
                if (parents[i] == null)
                    throw new ArgumentException($"Parent {i} is null.", nameof(parents));
            }

            var random = rules.MutationRandom(childGeneration);
            var ranking = Rank(fitness);
            var children = new Offspring[rules.PopulationSize];
            for (var e = 0; e < rules.EliteCount; e++)
                children[e] = new Offspring(parents[ranking[e]], ranking[e], true, 0);

            for (var c = rules.EliteCount; c < children.Length; c++)
            {
                var parent = Tournament(fitness, rules.TournamentSize, random.Source);
                var child = Mutate(parents[parent], rules.MutationProbability, rules.MutationSigma, rules.MutableBodyGenes,
                    random, out var mutated);
                children[c] = new Offspring(child, parent, false, mutated);
            }

            return children;
        }

        /// <summary>Weights-only mutation: body genes are copied unchanged.</summary>
        public static Genome Mutate(Genome parent, float probability, float sigma, GaussianSampler random, out int mutated) =>
            Mutate(parent, probability, sigma, BodyGeneSet.None, random, out mutated);

        /// <summary>
        /// Copy of <paramref name="parent"/> where each weight, with chance <paramref name="probability"/>, gets
        /// Gaussian noise of standard deviation <paramref name="sigma"/> and is then limited to ±<see cref="Genome.MaxAbsWeight"/>;
        /// then each body gene of <paramref name="bodyGenes"/>, in <see cref="BodyGene"/> order, the same way within
        /// ±<see cref="BodyGenes.MaxAbsValue"/>. Draws per weight or gene: one uniform coin, plus one Gaussian sample
        /// when it mutates. Body genes outside the set are copied and draw nothing, so a fixed-body run draws exactly
        /// what it drew before body genes existed. <paramref name="mutated"/> counts weights only.
        /// </summary>
        public static Genome Mutate(Genome parent, float probability, float sigma, BodyGeneSet bodyGenes,
            GaussianSampler random, out int mutated)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));
            if (random == null)
                throw new ArgumentNullException(nameof(random));

            var weights = parent.CopyWeights();
            mutated = 0;
            for (var i = 0; i < weights.Length; i++)
            {
                if (random.Source.NextFloat() >= probability)
                    continue;
                var value = weights[i] + (float)(sigma * random.NextStandard());
                weights[i] = Math.Min(Genome.MaxAbsWeight, Math.Max(-Genome.MaxAbsWeight, value));
                mutated++;
            }

            BodyGenes.RequireSet(bodyGenes);
            var body = parent.Body;
            if (bodyGenes != BodyGeneSet.None)
            {
                var genes = new float[BodyGenes.Count];
                for (var g = 0; g < genes.Length; g++)
                {
                    genes[g] = body[(BodyGene)g];
                    if (((int)bodyGenes & (1 << g)) == 0 || random.Source.NextFloat() >= probability)
                        continue;
                    var value = genes[g] + (float)(sigma * random.NextStandard());
                    genes[g] = Math.Min(BodyGenes.MaxAbsValue, Math.Max(-BodyGenes.MaxAbsValue, value));
                }

                body = new BodyGenes(genes[0], genes[1], genes[2], genes[3]);
            }

            return new Genome(parent.Topology, weights, body);
        }

        /// <summary>Parent indices from best to worst.</summary>
        public static int[] Rank(IReadOnlyList<float> fitness)
        {
            var order = new int[fitness.Count];
            for (var i = 0; i < order.Length; i++)
                order[i] = i;
            Array.Sort(order, (a, b) => IsBetter(fitness, a, b) ? -1 : IsBetter(fitness, b, a) ? 1 : 0);
            return order;
        }

        private static int Tournament(IReadOnlyList<float> fitness, int size, IRandomSource random)
        {
            var winner = (int)random.NextUInt((uint)fitness.Count);
            for (var i = 1; i < size; i++)
            {
                var contender = (int)random.NextUInt((uint)fitness.Count);
                if (IsBetter(fitness, contender, winner))
                    winner = contender;
            }

            return winner;
        }

        private static bool IsBetter(IReadOnlyList<float> fitness, int a, int b)
        {
            var fa = Score(fitness[a]);
            var fb = Score(fitness[b]);
            return fa > fb || (fa == fb && a < b);
        }

        private static float Score(float fitness) => float.IsNaN(fitness) ? Fitness.Worst : fitness;
    }
}
