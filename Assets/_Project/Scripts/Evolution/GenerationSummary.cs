using System;
using System.Collections.Generic;

namespace Eden.Evolution
{
    /// <summary>Everything known about one evaluated generation: per-episode results, who was evaluated and the summary.</summary>
    public sealed class GenerationReport
    {
        public GenerationReport(GenerationEvaluation evaluation, IReadOnlyList<Individual> individuals)
        {
            Evaluation = evaluation ?? throw new ArgumentNullException(nameof(evaluation));
            Individuals = individuals ?? throw new ArgumentNullException(nameof(individuals));
            Summary = new GenerationSummary(evaluation, individuals);
        }

        /// <summary>Report of a NEAT training generation, with the species of every individual.</summary>
        public GenerationReport(GenerationEvaluation evaluation, IReadOnlyList<Individual> individuals,
            IReadOnlyList<int> species)
            : this(evaluation, individuals)
        {
            Neat = new NeatGenerationSummary(individuals, species);
        }

        public GenerationEvaluation Evaluation { get; }

        /// <summary>Evaluated individuals, in candidate order.</summary>
        public IReadOnlyList<Individual> Individuals { get; }

        public GenerationSummary Summary { get; }

        /// <summary>Species and network sizes of a NEAT training generation; null otherwise.</summary>
        public NeatGenerationSummary Neat { get; }
    }

    /// <summary>
    /// Report of one evaluated generation: fitness distribution (quantiles by linear interpolation over all
    /// candidates, invalid ones counted as <see cref="Fitness.Worst"/>; the mean over valid ones only), the other
    /// outcome metrics averaged, weight diversity and how the generation was bred.
    /// </summary>
    public sealed class GenerationSummary
    {
        public GenerationSummary(GenerationEvaluation evaluation, IReadOnlyList<Individual> individuals)
        {
            if (evaluation == null)
                throw new ArgumentNullException(nameof(evaluation));
            if (individuals == null)
                throw new ArgumentNullException(nameof(individuals));
            var candidates = evaluation.Candidates;
            if (candidates.Count == 0 || candidates.Count != individuals.Count)
                throw new ArgumentException("Every evaluated candidate needs its individual.", nameof(individuals));

            Generation = evaluation.Generation;
            Mode = evaluation.Mode;
            PopulationSize = candidates.Count;

            var fitness = new float[candidates.Count];
            var sorted = new float[candidates.Count];
            double validSum = 0, survival = 0, food = 0, survivedSeconds = 0;
            for (var i = 0; i < candidates.Count; i++)
            {
                fitness[i] = candidates[i].MeanFitness;
                sorted[i] = float.IsNaN(fitness[i]) ? Fitness.Worst : fitness[i];
                if (Fitness.IsValid(fitness[i]))
                {
                    ValidCount++;
                    validSum += fitness[i];
                }

                survival += candidates[i].SurvivalRate;
                food += candidates[i].MeanFoodEnergy;
                survivedSeconds += candidates[i].MeanSurvivedSeconds;
            }

            Array.Sort(sorted);
            Worst = sorted[0];
            LowerQuartile = Quantile(sorted, 0.25);
            Median = Quantile(sorted, 0.5);
            UpperQuartile = Quantile(sorted, 0.75);
            Best = sorted[sorted.Length - 1];
            MeanFitness = ValidCount > 0 ? (float)(validSum / ValidCount) : float.NaN;
            BestIndividualId = individuals[Reproduction.Rank(fitness)[0]].Id;
            MeanSurvivalRate = (float)(survival / PopulationSize);
            MeanFoodEnergy = (float)(food / PopulationSize);
            MeanSurvivedSeconds = (float)(survivedSeconds / PopulationSize);

            ComputeDiversity(individuals);
            CountOrigins(individuals);
        }

        private GenerationSummary()
        {
        }

        /// <summary>
        /// A summary as it was saved (e.g. in a checkpoint), for history that is shown but not recomputed. Counts are
        /// checked; fitness values may be non-finite exactly as a computed summary can be.
        /// </summary>
        public static GenerationSummary Restore(int generation, EvaluationMode mode, int populationSize, int validCount,
            float best, float upperQuartile, float median, float lowerQuartile, float worst, float meanFitness,
            long bestIndividualId, float meanSurvivalRate, float meanFoodEnergy, float meanSurvivedSeconds,
            float weightStdDev, float meanDistanceToCentroid, int newIndividuals, int elites, int distinctParents,
            float meanMutatedWeights)
        {
            if (generation < 0 || populationSize < 1 || validCount < 0 || validCount > populationSize ||
                bestIndividualId < 1 || newIndividuals < 0 || elites < 0 || newIndividuals + elites != populationSize ||
                distinctParents < 0 || distinctParents > newIndividuals)
                throw new ArgumentException($"Saved summary of generation {generation} has inconsistent counts.");
            return new GenerationSummary
            {
                Generation = generation, Mode = mode, PopulationSize = populationSize, ValidCount = validCount,
                Best = best, UpperQuartile = upperQuartile, Median = median, LowerQuartile = lowerQuartile, Worst = worst,
                MeanFitness = meanFitness, BestIndividualId = bestIndividualId, MeanSurvivalRate = meanSurvivalRate,
                MeanFoodEnergy = meanFoodEnergy, MeanSurvivedSeconds = meanSurvivedSeconds, WeightStdDev = weightStdDev,
                MeanDistanceToCentroid = meanDistanceToCentroid, NewIndividuals = newIndividuals, Elites = elites,
                DistinctParents = distinctParents, MeanMutatedWeights = meanMutatedWeights
            };
        }

        public int Generation { get; private set; }
        public EvaluationMode Mode { get; private set; }
        public int PopulationSize { get; private set; }
        public int ValidCount { get; private set; }

        public float Best { get; private set; }
        public float UpperQuartile { get; private set; }
        public float Median { get; private set; }
        public float LowerQuartile { get; private set; }
        public float Worst { get; private set; }

        /// <summary>Mean of the valid fitness values; NaN when none is valid.</summary>
        public float MeanFitness { get; private set; }

        /// <summary>Individual with the best fitness (lowest index on ties).</summary>
        public long BestIndividualId { get; private set; }

        public float MeanSurvivalRate { get; private set; }
        public float MeanFoodEnergy { get; private set; }
        public float MeanSurvivedSeconds { get; private set; }

        /// <summary>Standard deviation of each weight across the population, averaged over all weights.</summary>
        public float WeightStdDev { get; private set; }

        /// <summary>Mean Euclidean distance between a genome's weights and the population's mean weights.</summary>
        public float MeanDistanceToCentroid { get; private set; }

        /// <summary>Individuals born in this generation (all of them in generation 0).</summary>
        public int NewIndividuals { get; private set; }

        /// <summary>Individuals carried over unchanged from an earlier generation.</summary>
        public int Elites { get; private set; }

        /// <summary>Distinct parents of the individuals born in this generation.</summary>
        public int DistinctParents { get; private set; }

        /// <summary>Mean number of mutated weights of the individuals born in this generation.</summary>
        public float MeanMutatedWeights { get; private set; }

        // Weight diversity compares weights by position, which only means something for the fixed-topology MLP; NEAT
        // generations leave it undefined (NaN, an empty field in the CSV) and report species instead.
        private void ComputeDiversity(IReadOnlyList<Individual> individuals)
        {
            if (individuals[0].Genome.Neat != null)
            {
                WeightStdDev = float.NaN;
                MeanDistanceToCentroid = float.NaN;
                return;
            }

            var count = individuals.Count;
            var weightCount = individuals[0].Genome.WeightCount;
            var mean = new double[weightCount];
            foreach (var individual in individuals)
            {
                if (individual.Genome.WeightCount != weightCount)
                    throw new ArgumentException("All genomes of a generation need the same topology.", nameof(individuals));
                for (var w = 0; w < weightCount; w++)
                    mean[w] += individual.Genome.GetWeight(w);
            }

            for (var w = 0; w < weightCount; w++)
                mean[w] /= count;

            var variance = new double[weightCount];
            double distanceSum = 0;
            foreach (var individual in individuals)
            {
                double squared = 0;
                for (var w = 0; w < weightCount; w++)
                {
                    var delta = individual.Genome.GetWeight(w) - mean[w];
                    variance[w] += delta * delta;
                    squared += delta * delta;
                }

                distanceSum += Math.Sqrt(squared);
            }

            double stdSum = 0;
            for (var w = 0; w < weightCount; w++)
                stdSum += Math.Sqrt(variance[w] / count);
            WeightStdDev = (float)(stdSum / weightCount);
            MeanDistanceToCentroid = (float)(distanceSum / count);
        }

        private void CountOrigins(IReadOnlyList<Individual> individuals)
        {
            var parents = new HashSet<long>();
            long mutated = 0;
            foreach (var individual in individuals)
            {
                if (individual.BirthGeneration < Generation)
                {
                    Elites++;
                    continue;
                }

                NewIndividuals++;
                mutated += individual.MutatedWeights;
                foreach (var parent in individual.ParentIds)
                    parents.Add(parent);
            }

            DistinctParents = parents.Count;
            MeanMutatedWeights = NewIndividuals > 0 ? (float)mutated / NewIndividuals : 0f;
        }

        // Linear interpolation between closest ranks; next to an invalid (-inf) value the lower value is returned.
        private static float Quantile(float[] sorted, double q)
        {
            var position = q * (sorted.Length - 1);
            var lower = (int)Math.Floor(position);
            var upper = Math.Min(lower + 1, sorted.Length - 1);
            var a = sorted[lower];
            var b = sorted[upper];
            if (lower == upper || float.IsInfinity(a) || float.IsInfinity(b))
                return a;
            return (float)(a + (b - a) * (position - lower));
        }
    }
}
