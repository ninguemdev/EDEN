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
    /// sequence of generations.
    /// </summary>
    public sealed class GenerationalEvolution
    {
        private readonly IsolatedEvaluator evaluator;
        private Genome[] population;
        private Offspring[] origins;

        public GenerationalEvolution(EvolutionSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            Plan = new EvaluationPlan(settings);
            Rules = new ReproductionRules(settings);
            evaluator = new IsolatedEvaluator(Plan);
            population = InitialGenomes.Population(Rules.ExperimentSeed, Rules.PopulationSize);
        }

        public EvaluationPlan Plan { get; }
        public ReproductionRules Rules { get; }

        /// <summary>Generation of <see cref="Population"/>; 0 is the random initial population.</summary>
        public int Generation { get; private set; }

        public IReadOnlyList<Genome> Population => population;

        /// <summary>How each genome of <see cref="Population"/> was made; empty for generation 0.</summary>
        public IReadOnlyList<Offspring> Origins => origins ?? Array.Empty<Offspring>();

        /// <summary>Evaluates the current generation, replaces it with its offspring and returns the evaluation.</summary>
        public GenerationEvaluation Advance()
        {
            var evaluation = evaluator.EvaluateTraining(population, Generation);
            var fitness = new float[evaluation.Candidates.Count];
            for (var i = 0; i < fitness.Length; i++)
                fitness[i] = evaluation.Candidates[i].MeanFitness;

            origins = Reproduction.Breed(Rules, population, fitness, Generation + 1);
            population = new Genome[origins.Length];
            for (var i = 0; i < origins.Length; i++)
                population[i] = origins[i].Genome;
            Generation++;
            return evaluation;
        }
    }
}
