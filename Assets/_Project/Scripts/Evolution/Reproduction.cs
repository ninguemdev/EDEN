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
            MutablePlasticity = settings.Plasticity;
        }

        public ulong ExperimentSeed { get; }
        public int PopulationSize { get; }
        public int EliteCount { get; }
        public int TournamentSize { get; }
        public float MutationProbability { get; }
        public float MutationSigma { get; }

        /// <summary>Body genes that mutate: the expressed ones. The others stay neutral and draw nothing.</summary>
        public BodyGeneSet MutableBodyGenes { get; }

        /// <summary>Whether plasticity coefficients mutate (only in experiments with plasticity; otherwise no draws).</summary>
        public bool MutablePlasticity { get; }

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
        public Offspring(Genome genome, int parentIndex, bool isElite, int mutatedWeights, int secondParentIndex = -1)
        {
            Genome = genome ?? throw new ArgumentNullException(nameof(genome));
            ParentIndex = parentIndex;
            IsElite = isElite;
            MutatedWeights = mutatedWeights;
            SecondParentIndex = secondParentIndex;
        }

        public Genome Genome { get; }

        /// <summary>Index of the single parent in the previous generation (the fitter one after a crossover).</summary>
        public int ParentIndex { get; }

        /// <summary>Index of the other crossover parent (NEAT), or -1 for a child of one parent.</summary>
        public int SecondParentIndex { get; }

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
                    rules.MutablePlasticity, random, out var mutated);
                children[c] = new Offspring(child, parent, false, mutated);
            }

            return children;
        }

        /// <summary>
        /// Generation 0 built from an imported pool instead of random weights (e.g. the best individuals exported from
        /// another experiment): the best <c>min(pool.Count, rules.PopulationSize)</c> genomes, by
        /// <paramref name="poolFitness"/>, cloned unchanged, then the rest of <see cref="ReproductionRules.PopulationSize"/>
        /// filled by tournament selection within the pool and mutation — the same selection and mutation <see cref="Breed"/>
        /// uses for an ordinary generation, except the elite count is however much of the pool fits, never
        /// <see cref="ReproductionRules.EliteCount"/> (a pool curated by hand is not a generation to thin out the same way).
        /// Returns genomes only: every one becomes a fresh, parentless founder of generation 0 (see <see cref="Lineage.Founders"/>),
        /// whichever experiment and generation it was exported from.
        /// </summary>
        public static Genome[] Seed(ReproductionRules rules, IReadOnlyList<Genome> pool, IReadOnlyList<float> poolFitness,
            GaussianSampler random)
        {
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));
            if (pool == null)
                throw new ArgumentNullException(nameof(pool));
            if (poolFitness == null)
                throw new ArgumentNullException(nameof(poolFitness));
            if (pool.Count == 0 || pool.Count != poolFitness.Count)
                throw new ArgumentException("Every pool genome needs exactly one fitness value.", nameof(poolFitness));
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            for (var i = 0; i < pool.Count; i++)
            {
                if (pool[i] == null)
                    throw new ArgumentException($"Pool genome {i} is null.", nameof(pool));
                if (pool[i].Network != Genome.MlpNetwork)
                    throw new ArgumentException($"Pool genome {i} is a {pool[i].Network} genome; this evolution breeds " +
                                                $"{Genome.MlpNetwork} genomes.", nameof(pool));
            }

            var ranking = Rank(poolFitness);
            var eliteCount = Math.Min(pool.Count, rules.PopulationSize);
            var genomes = new Genome[rules.PopulationSize];
            for (var e = 0; e < eliteCount; e++)
                genomes[e] = pool[ranking[e]];

            for (var c = eliteCount; c < genomes.Length; c++)
            {
                var parent = Tournament(poolFitness, rules.TournamentSize, random.Source);
                genomes[c] = Mutate(pool[parent], rules.MutationProbability, rules.MutationSigma, rules.MutableBodyGenes,
                    rules.MutablePlasticity, random, out _);
            }

            return genomes;
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
            GaussianSampler random, out int mutated) =>
            Mutate(parent, probability, sigma, bodyGenes, false, random, out mutated);

        /// <summary>
        /// As above; with <paramref name="plasticity"/> the learning rate and then the decay also mutate, after the body
        /// genes, each with chance <paramref name="probability"/> and noise <paramref name="sigma"/> times its range,
        /// limited to that range. A child inherits base weights and coefficients only: what its parent learned during
        /// its life lives in the parent's brain state and is never read here.
        /// </summary>
        public static Genome Mutate(Genome parent, float probability, float sigma, BodyGeneSet bodyGenes, bool plasticity,
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

            var coefficients = parent.Plasticity;
            if (plasticity)
            {
                var rate = MutateWithin(coefficients.LearningRate, PlasticityGenes.MaxLearningRate, probability, sigma, random);
                var decay = MutateWithin(coefficients.Decay, PlasticityGenes.MaxDecay, probability, sigma, random);
                coefficients = new PlasticityGenes(rate, decay);
            }

            return new Genome(parent.Topology, weights, body, coefficients);
        }

        /// <summary>
        /// Body genes of <paramref name="genes"/> mutated the way <c>Mutate</c> mutates them for the MLP (in <see cref="BodyGene"/> order: one coin, plus one Gaussian sample when it mutates, limited to
        /// ±<see cref="BodyGenes.MaxAbsValue"/>); the others are copied and draw nothing. Used by NEAT, whose network
        /// mutates elsewhere.
        /// </summary>
        public static BodyGenes MutateBody(BodyGenes body, BodyGeneSet genes, float probability, float sigma,
            GaussianSampler random)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            BodyGenes.RequireSet(genes);
            if (genes == BodyGeneSet.None)
                return body;
            var values = new float[BodyGenes.Count];
            for (var g = 0; g < values.Length; g++)
            {
                values[g] = body[(BodyGene)g];
                if (((int)genes & (1 << g)) == 0 || random.Source.NextFloat() >= probability)
                    continue;
                var value = values[g] + (float)(sigma * random.NextStandard());
                values[g] = Math.Min(BodyGenes.MaxAbsValue, Math.Max(-BodyGenes.MaxAbsValue, value));
            }

            return new BodyGenes(values[0], values[1], values[2], values[3]);
        }

        /// <summary>
        /// Eyes mutated by <paramref name="rules"/>: for each slot holding an eye, in slot order, its orientation, aperture
        /// and range in that order, each with one coin and, when it mutates, one Gaussian sample. Orientation wraps around
        /// ±π; aperture and range stay within the <see cref="Eye"/> limits. Then the structure: one coin to add an eye
        /// (only while a slot is free), which duplicates an eye drawn among the present ones (one draw) into the lowest
        /// free slot with each trait nudged by one Gaussian sample, so it starts close to the original and can diverge;
        /// then one coin to remove an eye (only while there are at least two, so a genome never goes blind), which draws
        /// the eye to clear. Slots keep their identity: an eye never moves to another slot, so the other eyes' input
        /// channels keep their meaning. Disabled rules return the same set and draw nothing.
        /// </summary>
        public static EyeSet MutateEyes(EyeSet eyes, EyeMutation rules, GaussianSampler random)
        {
            if (eyes == null)
                throw new ArgumentNullException(nameof(eyes));
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (!rules.IsEnabled)
                return eyes;
            var slots = new Eye?[EyeSet.MaxEyes];
            for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
            {
                if (!eyes.Has(slot))
                    continue;
                var eye = eyes[slot];
                var orientation = eye.Orientation;
                if (random.Source.NextFloat() < rules.Probability)
                    orientation = Wrap(orientation + (float)(rules.OrientationSigma * random.NextStandard()));
                var aperture = eye.Aperture;
                if (random.Source.NextFloat() < rules.Probability)
                    aperture = Math.Min(Eye.MaxAperture, Math.Max(Eye.MinAperture,
                        aperture + (float)(rules.ApertureSigma * random.NextStandard())));
                var range = eye.Range;
                if (random.Source.NextFloat() < rules.Probability)
                    range = Math.Min(Eye.MaxRange, Math.Max(Eye.MinRange, range + (float)(rules.RangeSigma * random.NextStandard())));
                slots[slot] = new Eye(orientation, aperture, range);
            }

            var count = eyes.Count;
            if (random.Source.NextFloat() < rules.AddProbability && count < EyeSet.MaxEyes)
            {
                var source = slots[NthPresent(slots, (int)random.Source.NextUInt((uint)count))].Value;
                var free = 0;
                while (slots[free].HasValue)
                    free++;
                slots[free] = Nudge(source, rules, random);
                count++;
            }

            if (random.Source.NextFloat() < rules.RemoveProbability && count > 1)
                slots[NthPresent(slots, (int)random.Source.NextUInt((uint)count))] = null;
            return new EyeSet(slots);
        }

        // Index of the n-th (0-based) slot holding an eye.
        private static int NthPresent(Eye?[] slots, int n)
        {
            for (var slot = 0; slot < slots.Length; slot++)
            {
                if (slots[slot].HasValue && n-- == 0)
                    return slot;
            }

            throw new ArgumentOutOfRangeException(nameof(n));
        }

        // A copy of an eye with every trait moved by one Gaussian sample of its sigma, within the limits.
        private static Eye Nudge(Eye eye, EyeMutation rules, GaussianSampler random)
        {
            var orientation = Wrap(eye.Orientation + (float)(rules.OrientationSigma * random.NextStandard()));
            var aperture = Math.Min(Eye.MaxAperture, Math.Max(Eye.MinAperture,
                eye.Aperture + (float)(rules.ApertureSigma * random.NextStandard())));
            var range = Math.Min(Eye.MaxRange, Math.Max(Eye.MinRange, eye.Range + (float)(rules.RangeSigma * random.NextStandard())));
            return new Eye(orientation, aperture, range);
        }

        /// <summary>
        /// Eyes of a crossover child, aligned by slot (an eye's identity), never by angle: for each slot in order, an eye
        /// both parents have comes from either by one coin (no coin when both eyes are the same, as with fixed eyes); an eye
        /// only the fitter parent has is inherited from it; an eye only the other parent has is not, as with disjoint
        /// connection genes. Null for genomes without eyes (no draws).
        /// </summary>
        public static EyeSet CrossEyes(EyeSet fitter, EyeSet other, IRandomSource random)
        {
            if (fitter == null)
                return null;
            if (other == null)
                return fitter;
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            var slots = new Eye?[EyeSet.MaxEyes];
            for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
            {
                if (!fitter.Has(slot))
                    continue;
                var differs = other.Has(slot) && !other[slot].Equals(fitter[slot]);
                slots[slot] = differs && random.NextFloat() >= 0.5f ? other[slot] : fitter[slot];
            }

            return new EyeSet(slots);
        }

        // An angle brought back into [-π, π].
        private static float Wrap(float angle)
        {
            const float turn = 2f * MathF.PI;
            var wrapped = angle - turn * MathF.Floor((angle + MathF.PI) / turn);
            return wrapped < -MathF.PI ? -MathF.PI : wrapped > MathF.PI ? MathF.PI : wrapped;
        }

        // One coin, plus a Gaussian sample scaled to [0, max] when it mutates; the result stays within [0, max].
        private static float MutateWithin(float value, float max, float probability, float sigma, GaussianSampler random)
        {
            if (random.Source.NextFloat() >= probability)
                return value;
            var mutated = value + (float)(sigma * max * random.NextStandard());
            return Math.Min(max, Math.Max(0f, mutated));
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

        internal static int Tournament(IReadOnlyList<float> fitness, int size, IRandomSource random)
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
