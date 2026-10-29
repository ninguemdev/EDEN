using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Eden.Domain;
using Eden.Evolution;

namespace Eden.Persistence
{
    /// <summary>
    /// JSON files of an experiment export: <c>experiment.json</c> (format, app, algorithms, the canonical settings and
    /// their hash, the physical arena in short, the evaluation mode, the unit of every CSV column and, for a resumed run, its origin) and
    /// <c>summary.json</c> (what the export holds so far, rewritten after every generation). Numbers use the invariant
    /// culture and floats round-trip exactly; an invalid fitness is written as the string "NaN".
    /// </summary>
    public static class ExperimentJson
    {
        public const string ExperimentFormat = "eden-experiment";
        public const string SummaryFormat = "eden-experiment-summary";
        public const int SchemaVersion = 1;

        /// <summary>Unit of every fitness value: food energy eaten per simulated second of the configured episode.</summary>
        public const string FitnessUnit = "food_energy_per_simulated_second";

        /// <summary>
        /// Selection of every experiment so far: fixed-topology weights evolved by elites, tournaments and Gaussian
        /// mutation, without crossover or species (not NEAT).
        /// </summary>
        public const string EvolutionAlgorithm = "fixed-topology-ga-v1";

        /// <summary>NEAT selection: species, fitness sharing, crossover aligned by innovation, structural mutation.</summary>
        public const string NeatEvolutionAlgorithm = "neat-v1";

        public static string EvolutionAlgorithmOf(Evolution.EvolutionAlgorithm algorithm) =>
            algorithm == Evolution.EvolutionAlgorithm.Neat ? NeatEvolutionAlgorithm : EvolutionAlgorithm;

        /// <summary>Unit of each column of the two NEAT CSV files, written only by NEAT experiments.</summary>
        public static readonly IReadOnlyDictionary<string, IReadOnlyList<KeyValuePair<string, string>>> NeatColumns =
            new Dictionary<string, IReadOnlyList<KeyValuePair<string, string>>>
            {
                ["neat-generations.csv"] = Units(
                    ("generation", Generation), ("species", Count), ("largest_species", Count),
                    ("mean_hidden_nodes", Count), ("max_hidden_nodes", Count), ("mean_connections", Count),
                    ("mean_enabled_connections", Count), ("mean_eyes", Count), ("mean_eye_aperture_degrees", "degree"),
                    ("mean_eye_range", "factor_of_sensor_range")),
                ["neat-individuals.csv"] = Units(
                    ("generation", Generation), ("candidate", Index), ("individual_id", Id), ("species", Id),
                    ("hidden_nodes", Count), ("connections", Count), ("enabled_connections", Count), ("eyes", Count))
            };

        private const string Energy = "energy";
        private const string Seconds = "simulated_second";
        private const string Ticks = "tick";
        private const string Fraction = "fraction";
        private const string Weight = "weight";
        private const string Count = "count";
        private const string Index = "index";
        private const string Generation = "generation";
        private const string Id = "id";
        private const string Flag = "flag_0_1";
        private const string Label = "label";

        /// <summary>Unit of each column of the three CSV files, in header order.</summary>
        public static readonly IReadOnlyDictionary<string, IReadOnlyList<KeyValuePair<string, string>>> Columns =
            new Dictionary<string, IReadOnlyList<KeyValuePair<string, string>>>
            {
                ["generations.csv"] = Units(
                    ("generation", Generation), ("evaluation_mode", Label), ("population", Count), ("valid", Count),
                    ("best", FitnessUnit), ("upper_quartile", FitnessUnit), ("median", FitnessUnit),
                    ("lower_quartile", FitnessUnit), ("worst", FitnessUnit), ("mean_fitness", FitnessUnit),
                    ("best_individual_id", Id), ("mean_survival_rate", Fraction), ("mean_food_energy", Energy),
                    ("mean_survived_seconds", Seconds), ("weight_std", Weight), ("mean_distance_to_centroid", Weight),
                    ("new_individuals", Count), ("elites", Count), ("distinct_parents", Count),
                    ("mean_mutated_weights", Count)),
                ["individuals.csv"] = Units(
                    ("generation", Generation), ("evaluation_mode", Label), ("candidate", Index), ("individual_id", Id),
                    ("parent_ids", "id_list_semicolon"), ("birth_generation", Generation), ("elite", Flag),
                    ("mutated_weights", Count), ("valid", Flag), ("mean_fitness", FitnessUnit),
                    ("mean_food_energy", Energy), ("survival_rate", Fraction), ("mean_survived_seconds", Seconds),
                    ("mean_final_energy", Energy)),
                ["episodes.csv"] = Units(
                    ("evaluation_mode", Label), ("generation", Generation), ("candidate", Index), ("seed_index", Index),
                    ("episode_seed", "seed"), ("valid", Flag), ("fitness", FitnessUnit), ("food_energy", Energy),
                    ("survived", Flag), ("survived_ticks", Ticks), ("episode_ticks", Ticks), ("final_energy", Energy))
            };

        public static string Experiment(ExperimentHeader header)
        {
            if (header == null)
                throw new ArgumentNullException(nameof(header));

            var validationSeeds = JsonValue.NewArray();
            foreach (var seed in header.Plan.ValidationSeeds())
                validationSeeds.Add(JsonValue.Of(seed));
            var files = JsonValue.NewObject();
            var neat = header.IsNeat;
            foreach (var file in neat ? Columns.Concat(NeatColumns) : Columns)
            {
                var columns = JsonValue.NewObject();
                foreach (var column in file.Value)
                    columns.Add(column.Key, JsonValue.Of(column.Value));
                files.Add(file.Key, columns);
            }

            var root = JsonValue.NewObject()
                .Add("format", JsonValue.Of(ExperimentFormat))
                .Add("schemaVersion", JsonValue.Of(SchemaVersion))
                .Add("app", JsonValue.NewObject().Add("version", JsonValue.Of(header.AppVersion)))
                .Add("createdUtc", JsonValue.Of(ExperimentHeader.Utc(header.CreatedUtc)))
                .Add("algorithms", CheckpointCodec.EncodeAlgorithms())
                .Add("evaluationMode", JsonValue.Of("isolated"))
                .Add("evaluationModes", JsonValue.NewArray()
                    .Add(JsonValue.Of(EvaluationCsv.ModeName(EvaluationMode.Training)))
                    .Add(JsonValue.Of(EvaluationCsv.ModeName(EvaluationMode.Validation))))
                .Add("preset", header.Preset == null ? JsonValue.Null() : JsonValue.Of(header.Preset))
                .Add("configHash", JsonValue.Of(CheckpointCodec.Hex(header.ConfigHash)))
                .Add("settings", header.SettingsJson())
                .Add("arena", Arena(header.Plan.EpisodeConfig(0)))
                .Add("episodeTicks", JsonValue.Of(header.Plan.EpisodeTicks))
                .Add("validationSeeds", validationSeeds)
                .Add("fitnessUnit", JsonValue.Of(FitnessUnit))
                .Add("fitnessProfile", JsonValue.Of(Fitness.Profile))
                .Add("neuralContract", JsonValue.Of(header.Plan.Contract.Id))
                .Add("network", JsonValue.Of(neat ? NeatNetwork.Kind : Genome.MlpNetwork))
                .Add("topology", neat
                    ? JsonValue.Null()
                    : JsonValue.NewArray()
                        .Add(JsonValue.Of(NetworkTopology.Mlp12x16x3.Inputs))
                        .Add(JsonValue.Of(NetworkTopology.Mlp12x16x3.Hidden))
                        .Add(JsonValue.Of(NetworkTopology.Mlp12x16x3.Outputs)))
                .Add("evolutionAlgorithm", JsonValue.Of(EvolutionAlgorithmOf(header.Plan.Algorithm)))
                .Add("columns", files)
                .Add("resumedFrom", header.ResumedFrom == null ? JsonValue.Null() : header.ResumedFrom.ToJson());
            return JsonText.Write(root);
        }

        internal static string Summary(ExperimentHeader header, ExportTally tally)
        {
            var root = JsonValue.NewObject()
                .Add("format", JsonValue.Of(SummaryFormat))
                .Add("schemaVersion", JsonValue.Of(SchemaVersion))
                .Add("configHash", JsonValue.Of(CheckpointCodec.Hex(header.ConfigHash)))
                .Add("fitnessUnit", JsonValue.Of(FitnessUnit))
                .Add("resumed", JsonValue.Of(header.ResumedFrom != null))
                .Add("training", JsonValue.NewObject()
                    .Add("generations", JsonValue.Of(tally.TrainingGenerations))
                    .Add("episodes", JsonValue.Of(tally.TrainingEpisodes))
                    .Add("first", SummaryOrNull(tally.FirstTraining))
                    .Add("last", SummaryOrNull(tally.LastTraining))
                    .Add("best", tally.BestTraining == null
                        ? JsonValue.Null()
                        : JsonValue.NewObject()
                            .Add("generation", JsonValue.Of(tally.BestTraining.Generation))
                            .Add("individualId", JsonValue.Of(tally.BestTraining.BestIndividualId))
                            .Add("fitness", JsonValue.OfAnyFloat(tally.BestTraining.Best))))
                .Add("validation", JsonValue.NewObject()
                    .Add("generations", JsonValue.Of(tally.ValidationGenerations))
                    .Add("episodes", JsonValue.Of(tally.ValidationEpisodes))
                    .Add("first", SummaryOrNull(tally.FirstValidation))
                    .Add("last", SummaryOrNull(tally.LastValidation))
                    .Add("champion", tally.Champion == null
                        ? JsonValue.Null()
                        : JsonValue.NewObject()
                            .Add("generation", JsonValue.Of(tally.LastValidation.Generation))
                            .Add("individualId", JsonValue.Of(tally.ChampionId))
                            .Add("fitness", JsonValue.OfAnyFloat(tally.Champion.MeanFitness))
                            .Add("survivalRate", JsonValue.OfAnyFloat(tally.Champion.SurvivalRate))));
            return JsonText.Write(root);
        }

        /// <summary>Writes <c>path.tmp</c> next to the file and swaps it in, so a reader never sees half a file.</summary>
        internal static void WriteReplacing(string path, string text)
        {
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temporary, path, null, true);
            else
                File.Move(temporary, path);
        }

        // The physical scenario at a glance; its whole geometry is in "settings".
        private static JsonValue Arena(SimulationConfig world) =>
            JsonValue.NewObject()
                .Add("bodies", JsonValue.Of(world.SolidBodies ? "solid" : "points"))
                .Add("obstacleLayout", JsonValue.Of(ExperimentHeader.LayoutName(world)))
                .Add("obstacles", JsonValue.Of(world.Obstacles.Count))
                .Add("freeRegions", world.FreeSpace == null ? JsonValue.Null() : JsonValue.Of(world.FreeSpace.Regions));

        private static JsonValue SummaryOrNull(GenerationSummary summary) =>
            summary == null ? JsonValue.Null() : CheckpointCodec.EncodeSummary(summary);

        private static IReadOnlyList<KeyValuePair<string, string>> Units(params (string column, string unit)[] units)
        {
            var list = new KeyValuePair<string, string>[units.Length];
            for (var i = 0; i < units.Length; i++)
                list[i] = new KeyValuePair<string, string>(units[i].column, units[i].unit);
            return list;
        }
    }

    /// <summary>
    /// Running totals of an export for <c>summary.json</c>. The champion of a validated generation (the individual
    /// selection ranked first in that generation's training) is known only when the same export also holds that
    /// training; a run resumed during a validation has it in the export it was resumed from.
    /// </summary>
    internal sealed class ExportTally
    {
        public int TrainingGenerations { get; private set; }
        public long TrainingEpisodes { get; private set; }
        public GenerationSummary FirstTraining { get; private set; }
        public GenerationSummary LastTraining { get; private set; }
        public GenerationSummary BestTraining { get; private set; }
        public int ValidationGenerations { get; private set; }
        public long ValidationEpisodes { get; private set; }
        public GenerationSummary FirstValidation { get; private set; }
        public GenerationSummary LastValidation { get; private set; }
        public long ChampionId { get; private set; }
        public CandidateEvaluation Champion { get; private set; }

        public void Add(GenerationReport report)
        {
            var summary = report.Summary;
            if (summary.Mode == EvaluationMode.Training)
            {
                TrainingGenerations++;
                TrainingEpisodes += report.Evaluation.Episodes.Count;
                FirstTraining ??= summary;
                LastTraining = summary;
                if (Fitness.IsValid(summary.Best) && (BestTraining == null || summary.Best > BestTraining.Best))
                    BestTraining = summary;
                return;
            }

            ValidationGenerations++;
            ValidationEpisodes += report.Evaluation.Episodes.Count;
            FirstValidation ??= summary;
            LastValidation = summary;
            Champion = null;
            if (LastTraining == null || LastTraining.Generation != summary.Generation)
                return;
            ChampionId = LastTraining.BestIndividualId;
            for (var i = 0; i < report.Individuals.Count; i++)
            {
                if (report.Individuals[i].Id == ChampionId)
                    Champion = report.Evaluation.Candidates[i];
            }
        }
    }
}
