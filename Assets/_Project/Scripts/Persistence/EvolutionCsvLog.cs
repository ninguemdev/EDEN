using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Eden.Evolution;

namespace Eden.Persistence
{
    /// <summary>
    /// Export of a generational experiment into one directory: <c>experiment.txt</c> (key=value settings),
    /// <c>generations.csv</c> (one summary row per generation), <c>individuals.csv</c> (one row per evaluated
    /// individual, with id, parents and birth generation) and <c>episodes.csv</c> (one row per individual × seed,
    /// before aggregation). Rows of the three CSV files join on generation and candidate. Numbers use the invariant
    /// culture; non-finite values are written as empty fields.
    /// </summary>
    public sealed class EvolutionCsvLog : IDisposable
    {
        public const string GenerationsHeader =
            "generation,evaluation_mode,population,valid,best,upper_quartile,median,lower_quartile,worst," +
            "mean_fitness,best_individual_id,mean_survival_rate,mean_food_energy,mean_survived_seconds," +
            "weight_std,mean_distance_to_centroid,new_individuals,elites,distinct_parents,mean_mutated_weights";

        public const string IndividualsHeader =
            "generation,candidate,individual_id,parent_ids,birth_generation,elite,mutated_weights,valid,mean_fitness," +
            "mean_food_energy,survival_rate,mean_survived_seconds,mean_final_energy";

        private CsvFile generations;
        private CsvFile individuals;
        private CsvFile episodes;

        public EvolutionCsvLog(string directory, IEnumerable<string> experimentInfo)
        {
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("A directory is required.", nameof(directory));
            if (experimentInfo == null)
                throw new ArgumentNullException(nameof(experimentInfo));

            Directory.CreateDirectory(directory);
            DirectoryPath = directory;
            File.WriteAllText(Path.Combine(directory, "experiment.txt"),
                string.Join("\n", experimentInfo) + "\n", new UTF8Encoding(false));
            generations = new CsvFile(Path.Combine(directory, "generations.csv"), GenerationsHeader);
            individuals = new CsvFile(Path.Combine(directory, "individuals.csv"), IndividualsHeader);
            episodes = new CsvFile(Path.Combine(directory, "episodes.csv"), EvaluationCsv.Header);
        }

        public string DirectoryPath { get; }
        public int GenerationCount { get; private set; }

        public void Append(GenerationReport report)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));
            if (generations == null)
                throw new ObjectDisposedException(nameof(EvolutionCsvLog));

            generations.Append(FormatGeneration(report.Summary));
            for (var c = 0; c < report.Individuals.Count; c++)
                individuals.Append(FormatIndividual(report, c));
            foreach (var episode in report.Evaluation.Episodes)
                episodes.Append(EvaluationCsv.FormatRow(episode));
            GenerationCount++;
        }

        public static string FormatGeneration(GenerationSummary summary)
        {
            var culture = CultureInfo.InvariantCulture;
            return string.Join(",",
                summary.Generation.ToString(culture),
                EvaluationCsv.ModeName(summary.Mode),
                summary.PopulationSize.ToString(culture),
                summary.ValidCount.ToString(culture),
                Number(summary.Best),
                Number(summary.UpperQuartile),
                Number(summary.Median),
                Number(summary.LowerQuartile),
                Number(summary.Worst),
                Number(summary.MeanFitness),
                summary.BestIndividualId.ToString(culture),
                Number(summary.MeanSurvivalRate),
                Number(summary.MeanFoodEnergy),
                Number(summary.MeanSurvivedSeconds),
                Number(summary.WeightStdDev),
                Number(summary.MeanDistanceToCentroid),
                summary.NewIndividuals.ToString(culture),
                summary.Elites.ToString(culture),
                summary.DistinctParents.ToString(culture),
                Number(summary.MeanMutatedWeights));
        }

        public static string FormatIndividual(GenerationReport report, int candidate)
        {
            var culture = CultureInfo.InvariantCulture;
            var individual = report.Individuals[candidate];
            var result = report.Evaluation.Candidates[candidate];
            var generation = report.Evaluation.Generation;
            var parents = new string[individual.ParentIds.Count];
            for (var i = 0; i < parents.Length; i++)
                parents[i] = individual.ParentIds[i].ToString(culture);

            return string.Join(",",
                generation.ToString(culture),
                candidate.ToString(culture),
                individual.Id.ToString(culture),
                string.Join(";", parents),
                individual.BirthGeneration.ToString(culture),
                individual.BirthGeneration < generation ? "1" : "0",
                individual.MutatedWeights.ToString(culture),
                Fitness.IsValid(result.MeanFitness) ? "1" : "0",
                Number(result.MeanFitness),
                Number(result.MeanFoodEnergy),
                Number(result.SurvivalRate),
                Number(result.MeanSurvivedSeconds),
                Number(result.MeanFinalEnergy));
        }

        /// <summary>Settings that define an experiment, written next to its data.</summary>
        public static IReadOnlyList<string> Describe(EvaluationPlan plan, ReproductionRules rules, string appVersion,
            DateTime createdUtc)
        {
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));

            var culture = CultureInfo.InvariantCulture;
            var world = plan.EpisodeConfig(0);
            return new[]
            {
                "experiment_seed=" + plan.ExperimentSeed.ToString(culture),
                "app_version=" + appVersion,
                "created_utc=" + createdUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", culture),
                "evaluation_mode=isolated",
                "seed_scheme=" + EvaluationPlan.SeedSchemeVersion.ToString(culture),
                "training_seeds_per_generation=" + plan.TrainingSeedsPerGeneration.ToString(culture),
                "episode_seconds=" + plan.EpisodeSeconds.ToString(culture),
                "episode_ticks=" + plan.EpisodeTicks.ToString(culture),
                "config_schema=" + world.SchemaVersion.ToString(culture),
                "ticks_per_second=" + world.TicksPerSecond.ToString(culture),
                "arena=" + world.ArenaWidth.ToString(culture) + "x" + world.ArenaHeight.ToString(culture),
                "episode_food=" + world.InitialFood.ToString(culture),
                "population=" + rules.PopulationSize.ToString(culture),
                "elites=" + rules.EliteCount.ToString(culture),
                "tournament_size=" + rules.TournamentSize.ToString(culture),
                "mutation_probability=" + rules.MutationProbability.ToString(culture),
                "mutation_sigma=" + rules.MutationSigma.ToString(culture),
                "crossover=off"
            };
        }

        public void Dispose()
        {
            generations?.Dispose();
            individuals?.Dispose();
            episodes?.Dispose();
            generations = null;
            individuals = null;
            episodes = null;
        }

        private static string Number(float value) =>
            float.IsNaN(value) || float.IsInfinity(value)
                ? string.Empty
                : value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
