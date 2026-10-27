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
    /// Files of the plasticity comparison: <c>cells.csv</c> (one row per cell), <c>comparison.json</c> and
    /// <c>comparison.txt</c>, a plain summary that separates what evolution and lifetime learning contribute (plastic
    /// against fixed networks, and the plastic population with learning switched off) and only calls an effect one when
    /// it is larger than the variation between runs. Each cell's battery files go to a subdirectory named after it.
    /// </summary>
    public static class PlasticityComparisonReport
    {
        public const string Format = "eden-plasticity-comparison";
        public const int SchemaVersion = 1;

        public const string CellsHeader =
            "cell,environment,plasticity,runs,generations,training_episodes,validation_episodes,config_hash," +
            "val_median_mean,val_median_std,val_median_min,val_median_max,champion_val_mean,champion_val_std," +
            "val_survival_mean,median_beats_random,median_beats_heuristic,median_improved," +
            "lesioned_val_median_mean,lesioned_val_median_std,random_baseline,heuristic_baseline,seconds,preliminary";

        public static void Write(string directory, PlasticityComparison comparison, PlasticityComparisonResult result,
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
            using (var cells = new CsvFile(Path.Combine(directory, "cells.csv"), CellsHeader))
            {
                for (var i = 0; i < result.Outcomes.Count; i++)
                {
                    var outcome = result.Outcomes[i];
                    hashes[i] = Header(comparison, outcome.Cell, appVersion, createdUtc).ConfigHash;
                    cells.Append(FormatRow(outcome, hashes[i]));
                    BatteryReport.Write(Path.Combine(directory, outcome.Cell.Name), outcome.Result);
                }
            }

            var encoding = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(directory, "comparison.json"),
                Json(comparison, result, hashes, appVersion, createdUtc), encoding);
            File.WriteAllText(Path.Combine(directory, "comparison.txt"), string.Join("\n", Describe(result)) + "\n", encoding);
        }

        public static ExperimentHeader Header(PlasticityComparison comparison, PlasticityCell cell, string appVersion,
            DateTime createdUtc) =>
            new ExperimentHeader(comparison.Settings(cell), comparison.Battery.ValidationInterval, appVersion, createdUtc);

        public static string FormatRow(PlasticityCellOutcome outcome, ulong configHash)
        {
            var culture = CultureInfo.InvariantCulture;
            var r = outcome.Result;
            return string.Join(",",
                outcome.Cell.Name,
                outcome.Cell.Changing ? "changing" : "static",
                outcome.Cell.Plastic ? "plastic" : "fixed",
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
                Number(r.FinalValidationSurvival.Mean),
                r.MedianBeatsRandom.ToString(culture),
                r.MedianBeatsHeuristic.ToString(culture),
                r.MedianImproved.ToString(culture),
                outcome.Lesioned.HasValue ? Number(outcome.Lesioned.Value.Mean) : string.Empty,
                outcome.Lesioned.HasValue ? Number(outcome.Lesioned.Value.StdDev) : string.Empty,
                Number(r.RandomBaseline.MeanFitness),
                Number(r.HeuristicBaseline.MeanFitness),
                outcome.Seconds.ToString("0.###", culture),
                r.IsPreliminary ? "1" : "0");
        }

        /// <summary>Human-readable summary in Portuguese.</summary>
        public static IReadOnlyList<string> Describe(PlasticityComparisonResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var first = result.Outcomes[0];
            var plan = first.Result.Plan;
            var runs = first.Result.Runs;
            var lines = new List<string>
            {
                result.IsPreliminary
                    ? "EDEN — plasticidade ligada × desligada PRELIMINAR (orçamento abaixo da bateria de aceitação 5 × 30, 3 treino, 5 validação)"
                    : "EDEN — plasticidade ligada × desligada na bateria de aceitação",
                Text("Mesmas execuções em todas as células: {0} (seeds de experimento {1}–{2}) · gerações: {3} · " +
                     "{4} seeds de treino por geração · {5} seeds de validação (seed de validação {6}) · episódio isolado de {7} s",
                    runs.Count, runs[0].ExperimentSeed, runs[runs.Count - 1].ExperimentSeed, result.Battery.Generations,
                    plan.TrainingSeedsPerGeneration, plan.ValidationSeedCount, plan.ValidationSeed, plan.EpisodeSeconds),
                Text("Mundo que muda: a comida fica na metade esquerda da arena até {0} s e passa para a direita a partir daí. " +
                     "Cada célula avaliou {1} episódios de treino e {2} de validação (mesmo orçamento).",
                    F(result.FoodShiftSeconds), first.TrainingEpisodes, first.ValidationEpisodes),
                "Lesão: a população plástica final é avaliada de novo nas mesmas seeds de validação com o aprendizado " +
                "desligado (mesmos genomas, coeficientes zerados); a diferença é o que o aprendizado durante a vida acrescenta.",
                string.Empty,
                "Por célula, na validação da geração final (média ± desvio entre execuções [mín, máx]):"
            };

            foreach (var outcome in result.Outcomes)
            {
                var r = outcome.Result;
                lines.Add(Text("  {0}: mediana {1} · campeão do treino {2} · sobrevivência {3} · referências: aleatório {4}, heurístico {5}",
                    Label(outcome.Cell), S(r.FinalValidationMedian), S(r.FinalChampion), F(r.FinalValidationSurvival.Mean),
                    F(r.RandomBaseline.MeanFitness), F(r.HeuristicBaseline.MeanFitness)));
                if (outcome.Lesioned.HasValue)
                    lines.Add(Text("      sem aprender (lesão): mediana {0}", S(outcome.Lesioned.Value)));
            }

            lines.Add(string.Empty);
            foreach (var changing in new[] { false, true })
            {
                var world = changing ? "mundo que muda" : "mundo estático";
                lines.Add(Text("Aprendizado no {0}: plástica − fixa {1} ({2}); com − sem aprender na mesma população {3} ({4})",
                    world, Signed(result.LearningEffect(changing)), Verdict(result.IsLearningEffectSeparated(changing)),
                    Signed(result.LesionEffect(changing)), Verdict(result.IsLesionEffectSeparated(changing))));
            }

            lines.Add(string.Empty);
            lines.Add("Conclusão: " + Conclusion(result));
            return lines;
        }

        private static string Conclusion(PlasticityComparisonResult result)
        {
            if (result.Battery.Runs < 2)
                return "uma única execução por célula não tem variação para comparar; nenhuma diferença é conclusiva.";

            var parts = new List<string>();
            foreach (var changing in new[] { true, false })
            {
                var world = changing ? "no mundo que muda" : "no mundo estático";
                var better = result.IsLearningEffectSeparated(changing);
                var lesion = result.IsLesionEffectSeparated(changing);
                if (better && result.LearningEffect(changing) > 0f && lesion && result.LesionEffect(changing) > 0f)
                    parts.Add($"{world} as redes plásticas superam as fixas e perdem esse ganho sem aprender: o ganho vem do aprendizado durante a vida");
                else if (better && result.LearningEffect(changing) > 0f)
                    parts.Add($"{world} as redes plásticas superam as fixas, mas a lesão não separa o aprendizado da evolução dos genomas");
                else if (better)
                    parts.Add($"{world} as redes plásticas ficam abaixo das fixas");
                else if (lesion)
                    parts.Add($"{world} plástica e fixa não se separam, embora a população plástica dependa do que aprende");
                else
                    parts.Add($"{world} nenhuma diferença passa da variação entre execuções");
            }

            return string.Join("; ", parts) + "." + (result.IsPreliminary ? " Resultado preliminar." : string.Empty);
        }

        private static string Json(PlasticityComparison comparison, PlasticityComparisonResult result, ulong[] hashes,
            string appVersion, DateTime createdUtc)
        {
            var cells = JsonValue.NewArray();
            for (var i = 0; i < result.Outcomes.Count; i++)
            {
                var outcome = result.Outcomes[i];
                var r = outcome.Result;
                cells.Add(JsonValue.NewObject()
                    .Add("name", JsonValue.Of(outcome.Cell.Name))
                    .Add("changingWorld", JsonValue.Of(outcome.Cell.Changing))
                    .Add("plasticity", JsonValue.Of(outcome.Cell.Plastic))
                    .Add("configHash", JsonValue.Of(CheckpointCodec.Hex(hashes[i])))
                    .Add("trainingEpisodes", JsonValue.Of(outcome.TrainingEpisodes))
                    .Add("validationEpisodes", JsonValue.Of(outcome.ValidationEpisodes))
                    .Add("finalValidationMedian", SpreadJson(r.FinalValidationMedian))
                    .Add("finalChampion", SpreadJson(r.FinalChampion))
                    .Add("finalValidationSurvival", SpreadJson(r.FinalValidationSurvival))
                    .Add("lesionedValidationMedian", outcome.Lesioned.HasValue ? SpreadJson(outcome.Lesioned.Value) : JsonValue.Null())
                    .Add("baselines", JsonValue.NewObject()
                        .Add("random", JsonValue.OfAnyFloat(r.RandomBaseline.MeanFitness))
                        .Add("heuristic", JsonValue.OfAnyFloat(r.HeuristicBaseline.MeanFitness)))
                    .Add("seconds", JsonValue.Of((float)outcome.Seconds)));
            }

            var effects = JsonValue.NewObject();
            foreach (var changing in new[] { false, true })
            {
                effects.Add(changing ? "changing" : "static", JsonValue.NewObject()
                    .Add("plasticMinusFixed", JsonValue.OfAnyFloat(result.LearningEffect(changing)))
                    .Add("plasticMinusFixedSeparated", JsonValue.Of(result.IsLearningEffectSeparated(changing)))
                    .Add("withMinusWithoutLearning", JsonValue.OfAnyFloat(result.LesionEffect(changing)))
                    .Add("withMinusWithoutLearningSeparated", JsonValue.Of(result.IsLesionEffectSeparated(changing))));
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
                .Add("foodShiftSeconds", JsonValue.Of(result.FoodShiftSeconds))
                .Add("sharedSettings", Header(comparison, result.Outcomes[0].Cell, appVersion, createdUtc).SettingsJson())
                .Add("cells", cells)
                .Add("effects", effects);
            return JsonText.Write(root);
        }

        private static string Label(PlasticityCell cell) =>
            (cell.Changing ? "mundo que muda" : "mundo estático") + ", " + (cell.Plastic ? "rede plástica" : "rede fixa");

        private static string Verdict(bool separated) =>
            separated ? "separada da variação entre execuções" : "dentro da variação entre execuções";

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

        private static string Signed(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? "inválida" : value.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture);

        private static string S(Spread spread) =>
            Text("{0} ± {1} [{2}, {3}]", F(spread.Mean), F(spread.StdDev), F(spread.Min), F(spread.Max));

        private static string Number(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? string.Empty : value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
