using System;
using System.Collections.Generic;
using Eden.Domain;
using Eden.Evolution;

namespace Eden.Application
{
    /// <summary>
    /// Generational evolution in isolated episodes: evaluate the current population on its training seeds, then
    /// breed the next one from those scores only. The generation counter advances when all episodes are done, never
    /// on a death, and a generation where every creature died still yields a full population. Same settings → same
    /// sequence of generations, ids and parents.
    /// </summary>
    public sealed class GenerationalEvolution
    {
        private readonly IsolatedEvaluator evaluator;
        private readonly EvolutionSettings settings;
        private Individual[] individuals;
        private Genome[] population;
        private Offspring[] origins;

        public GenerationalEvolution(EvolutionSettings settings)
            : this(settings ?? throw new ArgumentNullException(nameof(settings)), new Lineage())
        {
            individuals = Lineage.Founders(InitialGenomes.Population(Rules.ExperimentSeed, Rules.PopulationSize,
                Plan.ExpressedBodyGenes, Plan.Plasticity));
            population = GenomesOf(individuals);
        }

        private GenerationalEvolution(EvolutionSettings settings, Lineage lineage)
        {
            this.settings = settings.WithExperimentSeed(settings.ExperimentSeed);
            Plan = new EvaluationPlan(settings);
            Rules = new ReproductionRules(settings);
            evaluator = new IsolatedEvaluator(Plan);
            Lineage = lineage;
        }

        /// <summary>
        /// Evolution saved at <paramref name="generation"/>: its population (in candidate order) and lineage, plus the
        /// previous generation when there is one, from which <see cref="Origins"/> are rebuilt. Every individual must
        /// match its lineage entry and every parent must be in the previous generation; nothing is built otherwise.
        /// </summary>
        public static GenerationalEvolution Restore(EvolutionSettings settings, int generation,
            IReadOnlyList<Individual> population, Lineage lineage, IReadOnlyList<Individual> previous)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (population == null)
                throw new ArgumentNullException(nameof(population));
            if (lineage == null)
                throw new ArgumentNullException(nameof(lineage));
            if (generation < 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            if ((generation == 0) != (previous == null))
                throw new ArgumentException("Only generation 0 has no previous generation.", nameof(previous));

            var evolution = new GenerationalEvolution(settings, lineage);
            if (population.Count != evolution.Rules.PopulationSize)
                throw new ArgumentException($"Expected {evolution.Rules.PopulationSize} individuals, got {population.Count}.",
                    nameof(population));
            CheckMembers(population, lineage, generation, nameof(population));
            if (previous != null)
            {
                CheckMembers(previous, lineage, generation - 1, nameof(previous));
                evolution.origins = OriginsOf(population, previous, generation);
            }

            evolution.individuals = Copy(population);
            evolution.population = GenomesOf(evolution.individuals);
            evolution.Generation = generation;
            return evolution;
        }

        /// <summary>Copy of the settings this evolution was created with.</summary>
        public EvolutionSettings Settings => settings.WithExperimentSeed(settings.ExperimentSeed);

        public EvaluationPlan Plan { get; }
        public ReproductionRules Rules { get; }

        /// <summary>Ids, parents and birth generations of every individual created so far.</summary>
        public Lineage Lineage { get; }

        /// <summary>Generation of <see cref="Population"/>; 0 is the random initial population.</summary>
        public int Generation { get; private set; }

        public IReadOnlyList<Genome> Population => population;

        /// <summary>The individuals of <see cref="Population"/>, in the same order.</summary>
        public IReadOnlyList<Individual> Individuals => individuals;

        /// <summary>How each genome of <see cref="Population"/> was made; empty for generation 0.</summary>
        public IReadOnlyList<Offspring> Origins => origins ?? Array.Empty<Offspring>();

        /// <summary>Evaluates the current generation, replaces it with its offspring and returns the report.</summary>
        public GenerationReport Advance() => Advance(BeginTraining().RunToEnd());

        /// <summary>Training evaluation of the current generation, to be advanced a few ticks at a time.</summary>
        public EvaluationJob BeginTraining() => evaluator.TrainingJob(population, Generation);

        /// <summary>
        /// Replaces the current generation with its offspring, bred from <paramref name="evaluation"/>: the completed
        /// result of <see cref="BeginTraining"/> for this generation. Anything else is rejected.
        /// </summary>
        public GenerationReport Advance(GenerationEvaluation evaluation)
        {
            if (evaluation == null)
                throw new ArgumentNullException(nameof(evaluation));
            if (evaluation.Mode != EvaluationMode.Training || evaluation.Generation != Generation ||
                evaluation.Candidates.Count != population.Length || !SameSeeds(evaluation.Seeds, Plan.TrainingSeeds(Generation)))
                throw new ArgumentException($"Expected the training evaluation of generation {Generation}.", nameof(evaluation));

            var report = new GenerationReport(evaluation, individuals);
            var fitness = new float[evaluation.Candidates.Count];
            for (var i = 0; i < fitness.Length; i++)
                fitness[i] = evaluation.Candidates[i].MeanFitness;

            origins = Reproduction.Breed(Rules, population, fitness, Generation + 1);
            individuals = Lineage.Descend(individuals, origins, Generation + 1);
            population = GenomesOf(individuals);
            Generation++;
            return report;
        }

        /// <summary>
        /// Evaluates the individuals of a training report on the validation seeds. Read-only: the result never reaches
        /// selection, ids or the next generation, which depend on training results alone.
        /// </summary>
        public GenerationReport Validate(GenerationReport training) =>
            Validate(training, BeginValidation(training).RunToEnd());

        /// <summary>Validation of the individuals of a training report, to be advanced a few ticks at a time.</summary>
        public EvaluationJob BeginValidation(GenerationReport training)
        {
            RequireTraining(training);
            return evaluator.ValidationJob(GenomesOf(training.Individuals), training.Evaluation.Generation);
        }

        /// <summary>Validation report from the completed result of <see cref="BeginValidation"/> for the same report.</summary>
        public GenerationReport Validate(GenerationReport training, GenerationEvaluation validation)
        {
            RequireTraining(training);
            if (validation == null)
                throw new ArgumentNullException(nameof(validation));
            if (validation.Mode != EvaluationMode.Validation || validation.Generation != training.Evaluation.Generation ||
                validation.Candidates.Count != training.Individuals.Count || !SameSeeds(validation.Seeds, Plan.ValidationSeeds()))
                throw new ArgumentException("Expected the validation of the same generation.", nameof(validation));
            return new GenerationReport(validation, training.Individuals);
        }

        private static void RequireTraining(GenerationReport training)
        {
            if (training == null)
                throw new ArgumentNullException(nameof(training));
            if (training.Evaluation.Mode != EvaluationMode.Training)
                throw new ArgumentException("Validation starts from a training report.", nameof(training));
        }

        private static bool SameSeeds(IReadOnlyList<ulong> actual, ulong[] expected)
        {
            if (actual.Count != expected.Length)
                return false;
            for (var i = 0; i < expected.Length; i++)
            {
                if (actual[i] != expected[i])
                    return false;
            }

            return true;
        }

        private static void CheckMembers(IReadOnlyList<Individual> members, Lineage lineage, int generation, string name)
        {
            var ids = new HashSet<long>();
            foreach (var member in members)
            {
                if (member == null || !ids.Add(member.Id))
                    throw new ArgumentException("Every individual must be present once.", name);
                if (!lineage.Contains(member.Id) || lineage.BirthGeneration(member.Id) != member.BirthGeneration ||
                    member.BirthGeneration > generation || !SameIds(lineage.ParentIds(member.Id), member.ParentIds))
                    throw new ArgumentException($"Individual {member.Id} does not match its lineage.", name);
                if (!member.Genome.Topology.Equals(NetworkTopology.Mlp12x16x3))
                    throw new ArgumentException($"Individual {member.Id} has a {member.Genome.Topology} genome.", name);
            }
        }

        // Elites are the same individuals as in the previous generation; every other child names its parent there.
        private static Offspring[] OriginsOf(IReadOnlyList<Individual> population, IReadOnlyList<Individual> previous,
            int generation)
        {
            var index = new Dictionary<long, int>();
            for (var i = 0; i < previous.Count; i++)
                index[previous[i].Id] = i;

            var origins = new Offspring[population.Count];
            for (var i = 0; i < origins.Length; i++)
            {
                var member = population[i];
                var elite = member.BirthGeneration < generation;
                var parentId = elite ? member.Id : member.ParentIds.Count == 1 ? member.ParentIds[0] : 0;
                if (!index.TryGetValue(parentId, out var parent) || (!elite && member.BirthGeneration != generation))
                    throw new ArgumentException($"Individual {member.Id} has no parent in generation {generation - 1}.",
                        nameof(previous));
                origins[i] = new Offspring(member.Genome, parent, elite, elite ? 0 : member.MutatedWeights);
            }

            return origins;
        }

        private static bool SameIds(IReadOnlyList<long> a, IReadOnlyList<long> b)
        {
            if (a.Count != b.Count)
                return false;
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        private static Individual[] Copy(IReadOnlyList<Individual> members)
        {
            var copy = new Individual[members.Count];
            for (var i = 0; i < copy.Length; i++)
                copy[i] = members[i];
            return copy;
        }

        private static Genome[] GenomesOf(IReadOnlyList<Individual> members)
        {
            var genomes = new Genome[members.Count];
            for (var i = 0; i < members.Count; i++)
                genomes[i] = members[i].Genome;
            return genomes;
        }
    }
}
