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
        private Individual[] individuals;
        private Genome[] population;
        private Offspring[] origins;

        public GenerationalEvolution(EvolutionSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            Plan = new EvaluationPlan(settings);
            Rules = new ReproductionRules(settings);
            evaluator = new IsolatedEvaluator(Plan);
            Lineage = new Lineage();
            individuals = Lineage.Founders(InitialGenomes.Population(Rules.ExperimentSeed, Rules.PopulationSize));
            population = GenomesOf(individuals);
        }

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
        public GenerationReport Advance()
        {
            var evaluation = evaluator.EvaluateTraining(population, Generation);
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

        private static Genome[] GenomesOf(Individual[] members)
        {
            var genomes = new Genome[members.Length];
            for (var i = 0; i < members.Length; i++)
                genomes[i] = members[i].Genome;
            return genomes;
        }
    }
}
