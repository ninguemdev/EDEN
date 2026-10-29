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
    /// sequence of generations, ids and parents. With <see cref="EvolutionAlgorithm.Neat"/> the population starts from
    /// minimal NEAT networks and every generation is placed into species and bred by <see cref="NeatSpeciation"/>
    /// (crossover and structural mutation numbered by one <see cref="InnovationHistory"/>); species champions play the
    /// role of the MLP's elites.
    /// </summary>
    public sealed class GenerationalEvolution
    {
        private readonly IsolatedEvaluator evaluator;
        private readonly EvolutionSettings settings;
        private Individual[] individuals;
        private Genome[] population;
        private Offspring[] origins;
        private readonly NeatRules neatRules;
        private NeatSpeciation speciation;
        private InnovationHistory innovations;
        private int[] lastSpecies;

        public GenerationalEvolution(EvolutionSettings settings)
            : this(settings ?? throw new ArgumentNullException(nameof(settings)), new Lineage())
        {
            individuals = Lineage.Founders(neatRules == null
                ? InitialGenomes.Population(Rules.ExperimentSeed, Rules.PopulationSize, Plan.ExpressedBodyGenes,
                    Plan.Plasticity)
                : InitialGenomes.NeatPopulation(Rules.ExperimentSeed, Rules.PopulationSize, Plan.ExpressedBodyGenes,
                    neatRules.InitialWeightLimit, Plan.Contract));
            population = GenomesOf(individuals);
        }

        /// <summary>
        /// A new evolution whose generation 0 is built from an imported pool (<see cref="Reproduction.Seed"/>, or
        /// <see cref="NeatSeeding.Seed"/> for NEAT, which renumbers the pool first) instead of random weights: the best of
        /// the pool cloned, plus mutated children of the pool filling the rest of the population. Every pool genome needs
        /// a matching fitness, in the same order; one exported without a known fitness of its own should be given 0, so it
        /// never outranks a pool genome with a real score by accident. The pool must hold the kind of network this
        /// evolution breeds.
        /// </summary>
        public static GenerationalEvolution FromSeedPool(EvolutionSettings settings, IReadOnlyList<Genome> pool,
            IReadOnlyList<float> poolFitness)
        {
            var evolution = new GenerationalEvolution(settings ?? throw new ArgumentNullException(nameof(settings)),
                new Lineage());
            var random = new GaussianSampler(
                SeedDerivation.CreateStream(evolution.Rules.ExperimentSeed, RandomStream.SeededGeneration));
            var seeded = evolution.neatRules == null
                ? Reproduction.Seed(evolution.Rules, pool, poolFitness, random)
                : NeatSeeding.Seed(evolution.Rules, evolution.neatRules, pool, poolFitness, evolution.innovations, random,
                    evolution.BodyMutationRules);
            evolution.individuals = evolution.Lineage.Founders(seeded);
            evolution.population = GenomesOf(evolution.individuals);
            return evolution;
        }

        private GenerationalEvolution(EvolutionSettings settings, Lineage lineage)
        {
            this.settings = settings.WithExperimentSeed(settings.ExperimentSeed);
            Plan = new EvaluationPlan(settings);
            Rules = new ReproductionRules(settings);
            evaluator = new IsolatedEvaluator(Plan);
            Lineage = lineage;
            if (Plan.Algorithm != EvolutionAlgorithm.Neat)
                return;
            neatRules = new NeatRules(settings.Neat);
            speciation = new NeatSpeciation(neatRules);
            innovations = new InnovationHistory(Plan.Contract);
        }

        /// <summary>
        /// Evolution saved at <paramref name="generation"/>: its population (in candidate order) and lineage, plus the
        /// previous generation when there is one, from which <see cref="Origins"/> are rebuilt. Every individual must
        /// match its lineage entry and every parent must be in the previous generation; nothing is built otherwise. A NEAT
        /// evolution also needs <paramref name="neat"/>, whose counters must lie beyond every saved structure.
        /// </summary>
        public static GenerationalEvolution Restore(EvolutionSettings settings, int generation,
            IReadOnlyList<Individual> population, Lineage lineage, IReadOnlyList<Individual> previous,
            NeatEvolutionState neat = null)
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
            var network = evolution.neatRules == null ? Genome.MlpNetwork : NeatNetwork.Kind;
            CheckMembers(population, lineage, generation, network, nameof(population));
            if (previous != null)
            {
                CheckMembers(previous, lineage, generation - 1, network, nameof(previous));
                evolution.origins = OriginsOf(population, previous, generation);
            }

            if (evolution.neatRules == null)
            {
                if (neat != null)
                    throw new ArgumentException("An MLP evolution has no NEAT state.", nameof(neat));
            }
            else
            {
                evolution.RestoreNeat(neat ?? throw new ArgumentException("A NEAT evolution needs its NEAT state.",
                    nameof(neat)), generation, population, previous);
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

        /// <summary>NEAT parameters, or null for the MLP.</summary>
        public NeatRules NeatRules => neatRules;

        /// <summary>Species of a NEAT evolution (those of the last bred generation), or null for the MLP.</summary>
        public NeatSpeciation Speciation => speciation;

        /// <summary>Historical markings of a NEAT evolution, or null for the MLP.</summary>
        public InnovationHistory Innovations => innovations;

        /// <summary>Species of each individual of the last trained generation (NEAT); null before the first or for the MLP.</summary>
        public IReadOnlyList<int> LastTrainingSpecies => lastSpecies;

        /// <summary>What a NEAT evolution keeps between generations; null for the MLP.</summary>
        public NeatEvolutionState CaptureNeat() =>
            neatRules == null
                ? null
                : new NeatEvolutionState(innovations.Capture(), speciation.Capture(), speciation.NextSpeciesId, lastSpecies);

        private BodyMutation BodyMutationRules =>
            new BodyMutation(Plan.ExpressedBodyGenes, Rules.MutationProbability, Rules.MutationSigma,
                Plan.EvolvingEyes ? neatRules.EyeMutation : default);

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

            var fitness = new float[evaluation.Candidates.Count];
            for (var i = 0; i < fitness.Length; i++)
                fitness[i] = evaluation.Candidates[i].MeanFitness;

            GenerationReport report;
            if (neatRules == null)
            {
                report = new GenerationReport(evaluation, individuals);
                origins = Reproduction.Breed(Rules, population, fitness, Generation + 1);
            }
            else
            {
                lastSpecies = speciation.Assign(population, Generation);
                report = new GenerationReport(evaluation, individuals, lastSpecies);
                origins = BreedNeat(fitness);
            }

            individuals = Lineage.Descend(individuals, origins, Generation + 1);
            population = GenomesOf(individuals);
            Generation++;
            return report;
        }

        // Species breed the next generation from the same stream the MLP uses; expressed body genes mutate with the
        // experiment's chance and noise, as in the MLP.
        private Offspring[] BreedNeat(float[] fitness)
        {
            var children = speciation.Breed(population, fitness, Generation, Rules.PopulationSize, innovations,
                Rules.MutationRandom(Generation + 1), BodyMutationRules);
            var bred = new Offspring[children.Length];
            for (var i = 0; i < bred.Length; i++)
            {
                // A crossover of a genome with itself still has one parent, as the lineage records it.
                var child = children[i];
                var second = child.SecondParent == child.FirstParent ? -1 : child.SecondParent;
                bred[i] = new Offspring(child.Genome, child.FirstParent, child.IsChampion, child.Mutation.MutatedWeights,
                    second);
            }

            return bred;
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

        // The history must have numbered generation g's breeding already, and no saved structure may carry a number the
        // history would hand out again.
        private void RestoreNeat(NeatEvolutionState neat, int generation, IReadOnlyList<Individual> population,
            IReadOnlyList<Individual> previous)
        {
            var history = InnovationHistory.Restore(neat.Innovations);
            if (history.Contract != Plan.Contract || history.Generation != generation)
                throw new ArgumentException($"The innovation history is at generation {history.Generation} of " +
                                            $"{history.Contract.Id}, the evolution at {generation} of " +
                                            $"{Plan.Contract.Id}.", nameof(neat));
            var restored = NeatSpeciation.Restore(neatRules, neat.Species, neat.NextSpeciesId);
            foreach (var species in restored.Species)
                CheckNumbered(species.Representative, history, $"species {species.Id}");
            foreach (var member in population)
                CheckNumbered(member.Genome, history, $"individual {member.Id}");
            if ((previous == null) != (neat.LastTrainingSpecies == null))
                throw new ArgumentException("Only a trained generation has species.", nameof(neat));
            if (previous != null)
            {
                if (neat.LastTrainingSpecies.Count != previous.Count)
                    throw new ArgumentException("Every individual of the last trained generation needs its species.",
                        nameof(neat));
                foreach (var member in previous)
                    CheckNumbered(member.Genome, history, $"individual {member.Id}");
                foreach (var id in neat.LastTrainingSpecies)
                {
                    if (id < 1 || id >= neat.NextSpeciesId)
                        throw new ArgumentException($"Species {id} was never created.", nameof(neat));
                }
            }

            innovations = history;
            speciation = restored;
            lastSpecies = neat.LastTrainingSpecies == null ? null : new List<int>(neat.LastTrainingSpecies).ToArray();
        }

        private static void CheckNumbered(Genome genome, InnovationHistory history, string owner)
        {
            var network = genome?.Neat ?? throw new ArgumentException($"{owner} has no NEAT genome.");
            if (network.Contract != history.Contract)
                throw new ArgumentException($"{owner} uses {network.Contract.Id}.");
            foreach (var gene in network.Connections)
            {
                if (gene.Innovation >= history.NextInnovation)
                    throw new ArgumentException($"{owner} has innovation {gene.Innovation}, not yet handed out.");
            }

            foreach (var node in network.HiddenNodes)
            {
                if (node >= history.NextNodeId)
                    throw new ArgumentException($"{owner} has node {node}, not yet handed out.");
            }
        }

        private static void CheckMembers(IReadOnlyList<Individual> members, Lineage lineage, int generation,
            string network, string name)
        {
            var ids = new HashSet<long>();
            foreach (var member in members)
            {
                if (member == null || !ids.Add(member.Id))
                    throw new ArgumentException("Every individual must be present once.", name);
                if (!lineage.Contains(member.Id) || lineage.BirthGeneration(member.Id) != member.BirthGeneration ||
                    member.BirthGeneration > generation || !SameIds(lineage.ParentIds(member.Id), member.ParentIds))
                    throw new ArgumentException($"Individual {member.Id} does not match its lineage.", name);
                if (member.Genome.Network != network)
                    throw new ArgumentException($"Individual {member.Id} has a {member.Genome.Network} genome.", name);
                if (network == Genome.MlpNetwork && !member.Genome.Topology.Equals(NetworkTopology.Mlp12x16x3))
                    throw new ArgumentException($"Individual {member.Id} has a {member.Genome.Topology} genome.", name);
            }
        }

        // Elites are the same individuals as in the previous generation; every other child names its parent there (a
        // NEAT crossover child names two, the fitter first).
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
                var parentId = elite ? member.Id : member.ParentIds.Count >= 1 ? member.ParentIds[0] : 0;
                var second = -1;
                if (!index.TryGetValue(parentId, out var parent) || (!elite && member.BirthGeneration != generation) ||
                    (!elite && member.ParentIds.Count == 2 && !index.TryGetValue(member.ParentIds[1], out second)))
                    throw new ArgumentException($"Individual {member.Id} has no parent in generation {generation - 1}.",
                        nameof(previous));
                origins[i] = new Offspring(member.Genome, parent, elite, elite ? 0 : member.MutatedWeights,
                    elite ? -1 : second);
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
