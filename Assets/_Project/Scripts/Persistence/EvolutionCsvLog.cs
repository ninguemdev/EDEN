using System;
using System.Globalization;
using System.IO;
using System.Text;
using Eden.Evolution;

namespace Eden.Persistence
{
    /// <summary>
    /// Export of a generational experiment into one directory: <c>experiment.txt</c> and <c>experiment.json</c> (the
    /// settings, their hash, the unit of every column and, for a resumed run, where it came from; see
    /// <see cref="ExperimentHeader"/>), <c>generations.csv</c> (one summary row per generation), <c>individuals.csv</c>
    /// (one row per evaluated individual and evaluation mode, with id, parents and birth generation),
    /// <c>episodes.csv</c> (one row per individual × seed, before aggregation) and <c>summary.json</c>, rewritten after
    /// every generation. NEAT experiments also write <c>neat-generations.csv</c> (species and network sizes per training
    /// generation) and <c>neat-individuals.csv</c> (species and size of every trained individual). Rows of the three CSV files join on generation, evaluation mode and candidate. Numbers use the
    /// invariant culture; non-finite values are written as empty fields.
    /// </summary>
    public sealed class EvolutionCsvLog : IDisposable
    {
        public const string GenerationsHeader =
            "generation,evaluation_mode,population,valid,best,upper_quartile,median,lower_quartile,worst," +
            "mean_fitness,best_individual_id,mean_survival_rate,mean_food_energy,mean_survived_seconds," +
            "weight_std,mean_distance_to_centroid,new_individuals,elites,distinct_parents,mean_mutated_weights";

        public const string IndividualsHeader =
            "generation,evaluation_mode,candidate,individual_id,parent_ids,birth_generation,elite,mutated_weights,valid," +
            "mean_fitness,mean_food_energy,survival_rate,mean_survived_seconds,mean_final_energy";

        public const string NeatGenerationsHeader =
            "generation,species,largest_species,mean_hidden_nodes,max_hidden_nodes,mean_connections,mean_enabled_connections";

        public const string NeatIndividualsHeader =
            "generation,candidate,individual_id,species,hidden_nodes,connections,enabled_connections";

        private readonly ExportTally tally = new ExportTally();
        private CsvFile neatGenerations;
        private CsvFile neatIndividuals;
        private CsvFile generations;
        private CsvFile individuals;
        private CsvFile episodes;

        public EvolutionCsvLog(string directory, ExperimentHeader header)
        {
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("A directory is required.", nameof(directory));

            Header = header ?? throw new ArgumentNullException(nameof(header));
            Directory.CreateDirectory(directory);
            DirectoryPath = directory;
            File.WriteAllText(Path.Combine(directory, "experiment.txt"),
                string.Join("\n", header.Describe()) + "\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "experiment.json"), ExperimentJson.Experiment(header),
                new UTF8Encoding(false));
            WriteSummary();
            generations = new CsvFile(Path.Combine(directory, "generations.csv"), GenerationsHeader);
            individuals = new CsvFile(Path.Combine(directory, "individuals.csv"), IndividualsHeader);
            episodes = new CsvFile(Path.Combine(directory, "episodes.csv"), EvaluationCsv.Header);
            if (!header.IsNeat)
                return;
            neatGenerations = new CsvFile(Path.Combine(directory, "neat-generations.csv"), NeatGenerationsHeader);
            neatIndividuals = new CsvFile(Path.Combine(directory, "neat-individuals.csv"), NeatIndividualsHeader);
        }

        public string DirectoryPath { get; }
        public ExperimentHeader Header { get; }
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
            if (report.Neat != null && neatGenerations != null)
            {
                neatGenerations.Append(FormatNeatGeneration(report));
                for (var c = 0; c < report.Individuals.Count; c++)
                    neatIndividuals.Append(FormatNeatIndividual(report, c));
            }

            GenerationCount++;
            tally.Add(report);
            WriteSummary();
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
                EvaluationCsv.ModeName(report.Evaluation.Mode),
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

        public static string FormatNeatGeneration(GenerationReport report)
        {
            var culture = CultureInfo.InvariantCulture;
            var neat = report.Neat;
            return string.Join(",",
                report.Evaluation.Generation.ToString(culture),
                neat.SpeciesCount.ToString(culture),
                neat.LargestSpecies.ToString(culture),
                Number(neat.MeanHiddenNodes),
                neat.MaxHiddenNodes.ToString(culture),
                Number(neat.MeanConnections),
                Number(neat.MeanEnabledConnections));
        }

        public static string FormatNeatIndividual(GenerationReport report, int candidate)
        {
            var culture = CultureInfo.InvariantCulture;
            var neat = report.Neat;
            return string.Join(",",
                report.Evaluation.Generation.ToString(culture),
                candidate.ToString(culture),
                report.Individuals[candidate].Id.ToString(culture),
                neat.Species[candidate].ToString(culture),
                neat.HiddenNodesOf(candidate).ToString(culture),
                neat.ConnectionsOf(candidate).ToString(culture),
                neat.EnabledConnectionsOf(candidate).ToString(culture));
        }

        public void Dispose()
        {
            generations?.Dispose();
            individuals?.Dispose();
            episodes?.Dispose();
            neatGenerations?.Dispose();
            neatIndividuals?.Dispose();
            generations = null;
            individuals = null;
            episodes = null;
            neatGenerations = null;
            neatIndividuals = null;
        }

        private void WriteSummary() =>
            ExperimentJson.WriteReplacing(Path.Combine(DirectoryPath, "summary.json"), ExperimentJson.Summary(Header, tally));

        private static string Number(float value) =>
            float.IsNaN(value) || float.IsInfinity(value)
                ? string.Empty
                : value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
