using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Eden.Application;
using Eden.Evolution;

namespace Eden.Persistence
{
    /// <summary>
    /// Files of a preset comparison: <c>presets.csv</c> (one row per preset), <c>comparison.json</c> (the shared
    /// definition, each preset's settings hash, results and cost) and <c>comparison.txt</c>, a plain summary that states
    /// the budget each preset used and whether the difference between presets is larger than the spread across runs.
    /// Each preset's own battery files go to a subdirectory named after it (see <see cref="BatteryReport"/>).
    /// </summary>
    public static class PresetComparisonReport
    {
        public const string Format = "eden-preset-comparison";
        public const int SchemaVersion = 1;

        public const string PresetsHeader =
            "preset,population,runs,generations,training_episodes,validation_episodes,config_hash," +
            "val_median_mean,val_median_std,val_median_min,val_median_max,champion_val_mean,champion_val_std," +
            "initial_val_median_mean,val_survival_mean,train_median_mean,median_beats_random,median_beats_heuristic," +
            "champion_beats_heuristic,median_improved,seconds,preliminary";

        public static void Write(string directory, PresetComparison comparison, PresetComparisonResult result,
            string appVersion, DateTime createdUtc)
        {
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("A directory is required.", nameof(directory));
            if (comparison == null)
                throw new ArgumentNullException(nameof(comparison));
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            Directory.CreateDirectory(directory);
            var hashes = new ulong[result.Outcomes.Count];
            using (var presets = new CsvFile(Path.Combine(directory, "presets.csv"), PresetsHeader))
            {
                for (var i = 0; i < result.Outcomes.Count; i++)
                {
                    var outcome = result.Outcomes[i];
                    hashes[i] = Header(comparison, outcome.Preset, appVersion, createdUtc).ConfigHash;
                    presets.Append(FormatRow(outcome, hashes[i]));
                    BatteryReport.Write(Path.Combine(directory, outcome.Preset.Name), outcome.Result);
                }
            }

            var encoding = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(directory, "comparison.json"),
                Json(comparison, result, hashes, appVersion, createdUtc), encoding);
            File.WriteAllText(Path.Combine(directory, "comparison.txt"), string.Join("\n", Describe(result)) + "\n",
                encoding);
        }

        /// <summary>Header of one preset's runs, as their exports describe them (run 0's seed).</summary>
        public static ExperimentHeader Header(PresetComparison comparison, ExperimentPreset preset, string appVersion,
            DateTime createdUtc) =>
            new ExperimentHeader(comparison.Settings(preset), comparison.Battery.ValidationInterval, appVersion,
                createdUtc, preset.Name);

        public static string FormatRow(PresetOutcome outcome, ulong configHash)
        {
            var culture = CultureInfo.InvariantCulture;
            var r = outcome.Result;
            return string.Join(",",
                outcome.Preset.Name,
                outcome.Preset.Population.ToString(culture),
                r.Runs.Count.ToString(culture),
                r.Settings.Generations.ToString(culture),
                outcome.TrainingEpisodes.ToString(culture),
                outcome.ValidationEpisodes.ToString(culture),
                CheckpointCodec.Hex(configHash),
                Number(r.FinalValidationMedian.Mean),
                Number(r.FinalValidationMedian.StdDev),
                Number(r.FinalValidationMedian.Min),
                Number(r.FinalValidationMedian.Max),
                Number(r.FinalChampion.Mean),
                Number(r.FinalChampion.StdDev),
                Number(r.InitialValidationMedian.Mean),
                Number(r.FinalValidationSurvival.Mean),
                Number(r.FinalTrainingMedian.Mean),
                r.MedianBeatsRandom.ToString(culture),
                r.MedianBeatsHeuristic.ToString(culture),
                r.ChampionBeatsHeuristic.ToString(culture),
                r.MedianImproved.ToString(culture),
                outcome.Seconds.ToString("0.###", culture),
                r.IsPreliminary ? "1" : "0");
        }

        /// <summary>Human-readable summary in Portuguese, like the battery report.</summary>
        public static IReadOnlyList<string> Describe(PresetComparisonResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var first = result.Outcomes[0].Result;
            var plan = first.Plan;
            var runs = first.Runs;
            var lines = new List<string>
            {
                result.IsPreliminary
                    ? "EDEN — comparação de presets PRELIMINAR (orçamento abaixo da bateria de aceitação 5 × 30, 3 treino, 5 validação)"
                    : "EDEN — comparação de presets na bateria de aceitação",
                Text("Mesmas execuções para todos os presets: {0} (seeds de experimento {1}–{2}) · gerações: {3} · " +
                     "{4} seeds de treino por geração · {5} seeds de validação (seed de validação {6}) · episódio isolado de {7} s",
                    runs.Count, runs[0].ExperimentSeed, runs[runs.Count - 1].ExperimentSeed, result.Battery.Generations,
                    plan.TrainingSeedsPerGeneration, plan.ValidationSeedCount, plan.ValidationSeed, plan.EpisodeSeconds),
                "Os presets mudam só o número de genomas por geração. Um preset maior avalia mais episódios por geração: " +
                "mesmo número de gerações não é o mesmo orçamento.",
                Text("Referências nas mesmas seeds de validação: aleatório {0} · heurístico {1} (fitness em energia de comida por segundo simulado)",
                    F(first.RandomBaseline.MeanFitness), F(first.HeuristicBaseline.MeanFitness)),
                string.Empty,
                "Por preset, na validação da geração final (média ± desvio entre execuções [mín, máx]):"
            };

            foreach (var outcome in result.Outcomes)
            {
                var r = outcome.Result;
                var n = r.Runs.Count;
                lines.Add(Text("  {0} ({1} genomas): mediana {2} · campeão do treino {3} · sobrevivência {4}",
                    outcome.Preset.Name, outcome.Preset.Population, S(r.FinalValidationMedian), S(r.FinalChampion),
                    F(r.FinalValidationSurvival.Mean)));
                lines.Add(Text("      mediana > aleatório {0}/{1} · > heurístico {2}/{1} · > própria geração 0 {3}/{1} · " +
                               "episódios: {4} de treino, {5} de validação · {6} s",
                    r.MedianBeatsRandom, n, r.MedianBeatsHeuristic, r.MedianImproved, outcome.TrainingEpisodes,
                    outcome.ValidationEpisodes, outcome.Seconds.ToString("0", CultureInfo.InvariantCulture)));
            }

            lines.Add(string.Empty);
            lines.Add("Conclusão: " + Conclusion(result));
            return lines;
        }

        private static string Conclusion(PresetComparisonResult result)
        {
            if (result.Outcomes.Count == 1)
                return "um único preset, nada a comparar.";

            var best = result.Best;
            var text = result.IsSeparated
                ? Text("{0} tem a maior mediana de validação final ({1}), acima da faixa média ± desvio de todos os outros presets",
                    best.Preset.Name, F(best.Result.FinalValidationMedian.Mean))
                : Text("{0} tem a maior mediana de validação final ({1}), mas a diferença fica dentro da variação entre " +
                       "execuções: não separa os presets",
                    best.Preset.Name, F(best.Result.FinalValidationMedian.Mean));
            var smallest = result.Outcomes[0];
            foreach (var outcome in result.Outcomes)
            {
                if (outcome.TrainingEpisodes < smallest.TrainingEpisodes)
                    smallest = outcome;
            }

            if (best != smallest)
                text += Text("; ele usou {0}× os episódios de treino de {1}",
                    ((double)best.TrainingEpisodes / smallest.TrainingEpisodes).ToString("0.#", CultureInfo.InvariantCulture),
                    smallest.Preset.Name);
            return text + "." + (result.IsPreliminary ? " Resultado preliminar." : string.Empty);
        }

        private static string Json(PresetComparison comparison, PresetComparisonResult result, ulong[] hashes,
            string appVersion, DateTime createdUtc)
        {
            var first = result.Outcomes[0].Result;
            var presets = JsonValue.NewArray();
            for (var i = 0; i < result.Outcomes.Count; i++)
            {
                var outcome = result.Outcomes[i];
                var r = outcome.Result;
                presets.Add(JsonValue.NewObject()
                    .Add("name", JsonValue.Of(outcome.Preset.Name))
                    .Add("population", JsonValue.Of(outcome.Preset.Population))
                    .Add("configHash", JsonValue.Of(CheckpointCodec.Hex(hashes[i])))
                    .Add("trainingEpisodes", JsonValue.Of(outcome.TrainingEpisodes))
                    .Add("validationEpisodes", JsonValue.Of(outcome.ValidationEpisodes))
                    .Add("finalValidationMedian", SpreadJson(r.FinalValidationMedian))
                    .Add("finalChampion", SpreadJson(r.FinalChampion))
                    .Add("initialValidationMedian", SpreadJson(r.InitialValidationMedian))
                    .Add("finalTrainingMedian", SpreadJson(r.FinalTrainingMedian))
                    .Add("finalValidationSurvival", SpreadJson(r.FinalValidationSurvival))
                    .Add("medianBeatsRandom", JsonValue.Of(r.MedianBeatsRandom))
                    .Add("medianBeatsHeuristic", JsonValue.Of(r.MedianBeatsHeuristic))
                    .Add("championBeatsHeuristic", JsonValue.Of(r.ChampionBeatsHeuristic))
                    .Add("medianImproved", JsonValue.Of(r.MedianImproved))
                    .Add("seconds", JsonValue.Of((float)outcome.Seconds)));
            }

            var root = JsonValue.NewObject()
                .Add("format", JsonValue.Of(Format))
                .Add("schemaVersion", JsonValue.Of(SchemaVersion))
                .Add("app", JsonValue.NewObject().Add("version", JsonValue.Of(appVersion ?? string.Empty)))
                .Add("createdUtc", JsonValue.Of(ExperimentHeader.Utc(createdUtc)))
                .Add("evaluationMode", JsonValue.Of(EvaluationCsv.ModeName(EvaluationMode.Validation)))
                .Add("fitnessUnit", JsonValue.Of(ExperimentJson.FitnessUnit))
                .Add("preliminary", JsonValue.Of(result.IsPreliminary))
                .Add("runs", JsonValue.Of(result.Battery.Runs))
                .Add("generations", JsonValue.Of(result.Battery.Generations))
                .Add("validationInterval", JsonValue.Of(result.Battery.ValidationInterval))
                .Add("firstExperimentSeed", JsonValue.Of(first.Runs[0].ExperimentSeed))
                .Add("sharedSettings", Header(comparison, result.Outcomes[0].Preset, appVersion, createdUtc).SettingsJson())
                .Add("baselines", JsonValue.NewObject()
                    .Add("random", JsonValue.OfAnyFloat(first.RandomBaseline.MeanFitness))
                    .Add("heuristic", JsonValue.OfAnyFloat(first.HeuristicBaseline.MeanFitness)))
                .Add("presets", presets)
                .Add("best", JsonValue.Of(result.Best.Preset.Name))
                .Add("separated", JsonValue.Of(result.IsSeparated));
            return JsonText.Write(root);
        }

        private static JsonValue SpreadJson(Spread spread) =>
            JsonValue.NewObject()
                .Add("mean", JsonValue.OfAnyFloat(spread.Mean))
                .Add("stdDev", JsonValue.OfAnyFloat(spread.StdDev))
                .Add("min", JsonValue.OfAnyFloat(spread.Min))
                .Add("max", JsonValue.OfAnyFloat(spread.Max));

        private static string Text(string format, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, format, args);

        private static string F(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? "inválido" : value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string S(Spread spread) =>
            Text("{0} ± {1} [{2}, {3}]", F(spread.Mean), F(spread.StdDev), F(spread.Min), F(spread.Max));

        private static string Number(float value) =>
            float.IsNaN(value) || float.IsInfinity(value)
                ? string.Empty
                : value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
