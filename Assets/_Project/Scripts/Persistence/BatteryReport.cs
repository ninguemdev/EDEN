using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Eden.Evolution;

namespace Eden.Persistence
{
    /// <summary>
    /// Files of a finished battery: <c>validation.csv</c> (one row per run × validated generation),
    /// <c>baselines.csv</c> (reference brains on the same validation seeds) and <c>report.txt</c>, a plain summary
    /// that states the budget, whether it is preliminary, the spread across runs and every comparison, including
    /// the ones without gain. Per-run details go to the run folders written by <see cref="EvolutionCsvLog"/>.
    /// </summary>
    public static class BatteryReport
    {
        public const string ValidationHeader =
            "run,experiment_seed,generation,train_best,train_median,val_best,val_upper_quartile,val_median," +
            "val_lower_quartile,val_worst,val_survival_rate,champion_id,champion_val_fitness,champion_val_survival_rate," +
            "weight_std";

        public const string BaselinesHeader =
            "brain,val_fitness,val_survival_rate,val_mean_food_energy,val_mean_survived_seconds";

        public static void Write(string directory, BatteryResult result)
        {
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("A directory is required.", nameof(directory));
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            Directory.CreateDirectory(directory);
            using (var validation = new CsvFile(Path.Combine(directory, "validation.csv"), ValidationHeader))
            {
                foreach (var run in result.Runs)
                {
                    foreach (var point in run.Points)
                        validation.Append(FormatValidationRow(run, point));
                }
            }

            using (var baselines = new CsvFile(Path.Combine(directory, "baselines.csv"), BaselinesHeader))
            {
                baselines.Append(FormatBaselineRow("random", result.RandomBaseline));
                baselines.Append(FormatBaselineRow("heuristic", result.HeuristicBaseline));
            }

            File.WriteAllText(Path.Combine(directory, "report.txt"), string.Join("\n", Describe(result)) + "\n",
                new UTF8Encoding(false));
        }

        public static string FormatValidationRow(ExperimentRun run, ValidationPoint point)
        {
            var culture = CultureInfo.InvariantCulture;
            return string.Join(",",
                run.Index.ToString(culture),
                run.ExperimentSeed.ToString(culture),
                point.Generation.ToString(culture),
                Number(point.Training.Best),
                Number(point.Training.Median),
                Number(point.Validation.Best),
                Number(point.Validation.UpperQuartile),
                Number(point.Validation.Median),
                Number(point.Validation.LowerQuartile),
                Number(point.Validation.Worst),
                Number(point.Validation.MeanSurvivalRate),
                point.ChampionId.ToString(culture),
                Number(point.ChampionFitness),
                Number(point.ChampionSurvivalRate),
                Number(point.Training.WeightStdDev));
        }

        public static string FormatBaselineRow(string brain, CandidateEvaluation evaluation) =>
            string.Join(",", brain, Number(evaluation.MeanFitness), Number(evaluation.SurvivalRate),
                Number(evaluation.MeanFoodEnergy), Number(evaluation.MeanSurvivedSeconds));

        /// <summary>Human-readable summary in Portuguese, like the lab HUD.</summary>
        public static IReadOnlyList<string> Describe(BatteryResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var plan = result.Plan;
            var settings = result.Settings;
            var runs = result.Runs;
            var n = runs.Count;
            var lines = new List<string>
            {
                result.IsPreliminary
                    ? "EDEN — bateria de evolução PRELIMINAR (orçamento abaixo da bateria de aceitação 5 × 30, 3 treino, 5 validação)"
                    : "EDEN — bateria de aceitação da evolução por gerações",
                Text("Execuções: {0} (seeds de experimento {1}–{2}) · gerações: {3} · população: {4}",
                    n, runs[0].ExperimentSeed, runs[n - 1].ExperimentSeed, settings.Generations,
                    runs[0].Final.Training.PopulationSize),
                Text("Seeds de treino por geração: {0} · seeds de validação: {1} (seed de validação {2}), fora da seleção · " +
                     "validação da população inteira a cada {3} gerações e na última",
                    plan.TrainingSeedsPerGeneration, plan.ValidationSeedCount, plan.ValidationSeed, settings.ValidationInterval),
                Text("Episódio isolado de {0} s · fitness = energia de comida por segundo simulado, média das seeds de validação",
                    plan.EpisodeSeconds),
                string.Empty,
                "Referências (mesmas seeds de validação, um episódio por seed):",
                Text("  aleatório:  fitness {0} · sobrevivência {1}", F(result.RandomBaseline.MeanFitness),
                    F(result.RandomBaseline.SurvivalRate)),
                Text("  heurístico: fitness {0} · sobrevivência {1}", F(result.HeuristicBaseline.MeanFitness),
                    F(result.HeuristicBaseline.SurvivalRate)),
                string.Empty,
                "Por execução, na validação (geração 0 → geração final):"
            };

            foreach (var run in runs)
            {
                lines.Add(Text("  execução {0} (seed {1}): mediana {2} → {3} · campeão do treino {4} → {5} (id {6}) · " +
                               "melhor da validação {7} · sobrevivência {8} · treino mediana {9} · {10} s",
                    run.Index, run.ExperimentSeed, F(run.Initial.Validation.Median), F(run.Final.Validation.Median),
                    F(run.Initial.ChampionFitness), F(run.Final.ChampionFitness), run.Final.ChampionId,
                    F(run.Final.Validation.Best), F(run.Final.Validation.MeanSurvivalRate), F(run.Final.Training.Median),
                    run.Seconds.ToString("0", CultureInfo.InvariantCulture)));
            }

            lines.Add(string.Empty);
            lines.Add("Entre execuções (média ± desvio [mín, máx]):");
            lines.Add("  mediana de validação final:        " + S(result.FinalValidationMedian));
            lines.Add("  campeão do treino na validação:    " + S(result.FinalChampion));
            lines.Add("  mediana de validação na geração 0: " + S(result.InitialValidationMedian));
            lines.Add("  mediana de treino final:           " + S(result.FinalTrainingMedian));
            lines.Add("  sobrevivência média na validação:  " + S(result.FinalValidationSurvival));
            lines.Add("  (\"melhor da validação\" é escolhido pela própria validação e é otimista; o campeão do treino não.)");
            lines.Add(string.Empty);
            lines.Add("Comparações na geração final (execuções em que vale, estritamente maior):");
            lines.Add(Text("  mediana > aleatório:        {0}/{1}", result.MedianBeatsRandom, n));
            lines.Add(Text("  mediana > heurístico:       {0}/{1}", result.MedianBeatsHeuristic, n));
            lines.Add(Text("  campeão > aleatório:        {0}/{1}", result.ChampionBeatsRandom, n));
            lines.Add(Text("  campeão > heurístico:       {0}/{1}", result.ChampionBeatsHeuristic, n));
            lines.Add(Text("  mediana > própria geração 0: {0}/{1}", result.MedianImproved, n));
            lines.Add(string.Empty);
            lines.Add("Conclusão: " + Conclusion(result));

            var seconds = 0.0;
            foreach (var run in runs)
                seconds += run.Seconds;
            lines.Add(Text("Custo: {0} s no total, {1} s por execução em média (tempo de relógio, só informativo).",
                seconds.ToString("0", CultureInfo.InvariantCulture),
                (seconds / n).ToString("0", CultureInfo.InvariantCulture)));
            return lines;
        }

        private static string Conclusion(BatteryResult result)
        {
            var n = result.Runs.Count;
            string gain;
            if (result.MedianImproved == 0)
                gain = "sem ganho na validação: a mediana final não superou a da geração 0 em nenhuma execução";
            else if (result.MedianImproved == n)
                gain = "ganho na validação em todas as execuções (mediana final acima da geração 0)";
            else
                gain = Text("ganho na validação só em {0} de {1} execuções", result.MedianImproved, n);

            string reference;
            if (result.MedianBeatsHeuristic == n)
                reference = "a mediana evoluída supera o heurístico em todas";
            else if (result.MedianBeatsRandom == n)
                reference = Text("supera o aleatório em todas, mas o heurístico só em {0}", result.MedianBeatsHeuristic);
            else
                reference = Text("não supera o aleatório em {0} execuções", n - result.MedianBeatsRandom);

            var label = result.IsPreliminary ? " Resultado preliminar." : string.Empty;
            return gain + "; " + reference + "." + label;
        }

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
