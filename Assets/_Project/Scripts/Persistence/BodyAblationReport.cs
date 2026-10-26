using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;

namespace Eden.Persistence
{
    /// <summary>
    /// Files of a body gene ablation: <c>variants.csv</c> (one row per variant: results, difference from the fixed body
    /// and from all genes, final body genes), <c>phenotypes.csv</c> (body genes of every training generation of every
    /// run), <c>ablation.json</c> and <c>ablation.txt</c>, a plain summary that states the shared budget and whether each
    /// difference is larger than the spread across runs. Each variant's battery files go to a subdirectory named after
    /// it (see <see cref="BatteryReport"/>).
    /// </summary>
    public static class BodyAblationReport
    {
        public const string Format = "eden-body-ablation";
        public const int SchemaVersion = 1;

        public const string VariantsHeader =
            "variant,genes,runs,generations,training_episodes,validation_episodes,config_hash," +
            "val_median_mean,val_median_std,val_median_min,val_median_max,champion_val_mean,champion_val_std," +
            "val_survival_mean,train_median_mean,median_beats_random,median_beats_heuristic,median_improved," +
            "delta_vs_fixed,separated_from_fixed,delta_vs_all,separated_from_all," +
            "final_body_diversity_mean,final_body_diversity_std,final_size_mean,final_speed_mean,final_sensor_mean," +
            "final_efficiency_mean,seconds,preliminary";

        public const string PhenotypesHeader =
            "variant,run,experiment_seed,generation,population,size_mean,size_std,speed_mean,speed_std," +
            "sensor_mean,sensor_std,efficiency_mean,efficiency_std,body_diversity";

        public static void Write(string directory, BodyAblation ablation, BodyAblationResult result, string appVersion,
            DateTime createdUtc)
        {
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("A directory is required.", nameof(directory));
            if (ablation == null)
                throw new ArgumentNullException(nameof(ablation));
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            Directory.CreateDirectory(directory);
            var hashes = new ulong[result.Outcomes.Count];
            using (var variants = new CsvFile(Path.Combine(directory, "variants.csv"), VariantsHeader))
            using (var phenotypes = new CsvFile(Path.Combine(directory, "phenotypes.csv"), PhenotypesHeader))
            {
                for (var i = 0; i < result.Outcomes.Count; i++)
                {
                    var outcome = result.Outcomes[i];
                    hashes[i] = Header(ablation, outcome.Variant, appVersion, createdUtc).ConfigHash;
                    variants.Append(FormatVariantRow(result, outcome, hashes[i]));
                    foreach (var phenotype in outcome.Phenotypes)
                        phenotypes.Append(FormatPhenotypeRow(outcome, phenotype));
                    BatteryReport.Write(Path.Combine(directory, outcome.Variant.Name), outcome.Result);
                }
            }

            var encoding = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(directory, "ablation.json"),
                Json(ablation, result, hashes, appVersion, createdUtc), encoding);
            File.WriteAllText(Path.Combine(directory, "ablation.txt"), string.Join("\n", Describe(result)) + "\n",
                encoding);
        }

        /// <summary>Header of one variant's runs, as their exports describe them (run 0's seed).</summary>
        public static ExperimentHeader Header(BodyAblation ablation, BodyVariant variant, string appVersion,
            DateTime createdUtc) =>
            new ExperimentHeader(ablation.Settings(variant), ablation.Battery.ValidationInterval, appVersion, createdUtc);

        public static string FormatVariantRow(BodyAblationResult result, BodyVariantOutcome outcome, ulong configHash)
        {
            var culture = CultureInfo.InvariantCulture;
            var r = outcome.Result;
            return string.Join(",",
                outcome.Variant.Name,
                GenesText(outcome.Variant.Genes, ";"),
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
                Number(r.FinalTrainingMedian.Mean),
                r.MedianBeatsRandom.ToString(culture),
                r.MedianBeatsHeuristic.ToString(culture),
                r.MedianImproved.ToString(culture),
                Delta(outcome, result.Control),
                Flag(result, outcome, result.Control),
                Delta(outcome, result.AllGenes),
                Flag(result, outcome, result.AllGenes),
                Number(outcome.FinalDiversity.Mean),
                Number(outcome.FinalDiversity.StdDev),
                Number(outcome.FinalGeneMean(BodyGene.Size).Mean),
                Number(outcome.FinalGeneMean(BodyGene.Speed).Mean),
                Number(outcome.FinalGeneMean(BodyGene.Sensor).Mean),
                Number(outcome.FinalGeneMean(BodyGene.Efficiency).Mean),
                outcome.Seconds.ToString("0.###", culture),
                r.IsPreliminary ? "1" : "0");
        }

        public static string FormatPhenotypeRow(BodyVariantOutcome outcome, RunPhenotype phenotype)
        {
            var culture = CultureInfo.InvariantCulture;
            var s = phenotype.Summary;
            return string.Join(",",
                outcome.Variant.Name,
                phenotype.Run.ToString(culture),
                outcome.Result.Runs[phenotype.Run].ExperimentSeed.ToString(culture),
                s.Generation.ToString(culture),
                s.PopulationSize.ToString(culture),
                Number(s.Mean(BodyGene.Size)), Number(s.StdDev(BodyGene.Size)),
                Number(s.Mean(BodyGene.Speed)), Number(s.StdDev(BodyGene.Speed)),
                Number(s.Mean(BodyGene.Sensor)), Number(s.StdDev(BodyGene.Sensor)),
                Number(s.Mean(BodyGene.Efficiency)), Number(s.StdDev(BodyGene.Efficiency)),
                Number(s.Diversity(outcome.Variant.Genes)));
        }

        /// <summary>Human-readable summary in Portuguese, like the battery report.</summary>
        public static IReadOnlyList<string> Describe(BodyAblationResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var first = result.Outcomes[0];
            var plan = first.Result.Plan;
            var runs = first.Result.Runs;
            var world = plan.EpisodeConfig(0);
            var lines = new List<string>
            {
                result.IsPreliminary
                    ? "EDEN — ablação dos genes de corpo PRELIMINAR (orçamento abaixo da bateria de aceitação 5 × 30, 3 treino, 5 validação)"
                    : "EDEN — ablação dos genes de corpo na bateria de aceitação",
                Text("Mesmas execuções para todas as variantes: {0} (seeds de experimento {1}–{2}) · gerações: {3} · " +
                     "{4} seeds de treino por geração · {5} seeds de validação (seed de validação {6}) · episódio isolado de {7} s",
                    runs.Count, runs[0].ExperimentSeed, runs[runs.Count - 1].ExperimentSeed, result.Battery.Generations,
                    plan.TrainingSeedsPerGeneration, plan.ValidationSeedCount, plan.ValidationSeed, plan.EpisodeSeconds),
                Text("As variantes mudam só os genes de corpo expressos; cada uma avaliou {0} episódios de treino e {1} de " +
                     "validação (mesmo orçamento).", first.TrainingEpisodes, first.ValidationEpisodes),
                Text("Custos (expoentes): tamanho {0} · velocidade {1} · sensor {2} · potência da eficiência {3}",
                    F(world.SizeCostExponent), F(world.SpeedCostExponent), F(world.SensorCostExponent),
                    F(world.EfficiencyPowerExponent)),
                Text("Referências nas mesmas seeds de validação (corpo de referência): aleatório {0} · heurístico {1} " +
                     "(fitness em energia de comida por segundo simulado)",
                    F(first.Result.RandomBaseline.MeanFitness), F(first.Result.HeuristicBaseline.MeanFitness)),
                string.Empty,
                "Por variante, na validação da geração final (média ± desvio entre execuções [mín, máx]):"
            };

            foreach (var outcome in result.Outcomes)
            {
                var r = outcome.Result;
                var n = r.Runs.Count;
                lines.Add(Text("  {0} ({1}): mediana {2} · campeão do treino {3} · sobrevivência {4}",
                    outcome.Variant.Name, GenesLabel(outcome.Variant.Genes), S(r.FinalValidationMedian), S(r.FinalChampion),
                    F(r.FinalValidationSurvival.Mean)));
                lines.Add(Text("      mediana > aleatório {0}/{1} · > heurístico {2}/{1} · > própria geração 0 {3}/{1} · {4} s",
                    r.MedianBeatsRandom, n, r.MedianBeatsHeuristic, r.MedianImproved,
                    outcome.Seconds.ToString("0", CultureInfo.InvariantCulture)));
                if (outcome.Variant.Genes != BodyGeneSet.None)
                    lines.Add(Text("      corpo final: diversidade {0} · genes médios: tamanho {1} · velocidade {2} · sensor {3} · " +
                                   "eficiência {4}",
                        S(outcome.FinalDiversity), G(outcome, BodyGene.Size), G(outcome, BodyGene.Speed),
                        G(outcome, BodyGene.Sensor), G(outcome, BodyGene.Efficiency)));
                if (result.Control != null && outcome != result.Control)
                    lines.Add("      " + Comparison(result, outcome, result.Control, "o corpo fixo"));
                if (result.AllGenes != null && outcome != result.AllGenes && outcome != result.Control)
                    lines.Add("      " + Comparison(result, outcome, result.AllGenes, "todos os genes"));
            }

            lines.Add(string.Empty);
            lines.Add("Conclusão: " + Conclusion(result));
            return lines;
        }

        private static string Comparison(BodyAblationResult result, BodyVariantOutcome outcome,
            BodyVariantOutcome baseline, string name) =>
            Text("diferença para {0}: {1} ({2})", name, Signed(BodyAblationResult.Difference(outcome, baseline)),
                result.IsSeparated(outcome, baseline) ? "separada da variação entre execuções" : "dentro da variação entre execuções");

        private static string Conclusion(BodyAblationResult result)
        {
            if (result.Outcomes.Count == 1)
                return "uma única variante, nada a comparar.";
            if (result.Battery.Runs < 2)
                return "uma única execução por variante não tem variação para comparar; nenhuma diferença é conclusiva.";

            var parts = new List<string>();
            if (result.Control != null)
            {
                var separated = new List<string>();
                foreach (var outcome in result.Outcomes)
                {
                    if (outcome != result.Control && result.IsSeparated(outcome, result.Control))
                        separated.Add(Text("{0} ({1})", outcome.Variant.Name,
                            Signed(BodyAblationResult.Difference(outcome, result.Control))));
                }

                parts.Add(separated.Count == 0
                    ? "nenhuma variante se separa do corpo fixo além da variação entre execuções"
                    : "separadas do corpo fixo: " + string.Join(", ", separated));
            }

            if (result.AllGenes != null)
            {
                var effects = new List<string>();
                foreach (var outcome in result.Outcomes)
                {
                    if (outcome == result.AllGenes || outcome == result.Control ||
                        !result.IsSeparated(outcome, result.AllGenes))
                        continue;
                    effects.Add(Text("{0} ({1})", outcome.Variant.Name,
                        Signed(BodyAblationResult.Difference(outcome, result.AllGenes))));
                }

                parts.Add(effects.Count == 0
                    ? "nenhuma variante se separa de todos os genes: com este orçamento nenhum gene isolado mostra efeito"
                    : "separadas de todos os genes: " + string.Join(", ", effects));
            }

            return string.Join("; ", parts) + "." + (result.IsPreliminary ? " Resultado preliminar." : string.Empty);
        }

        private static string Json(BodyAblation ablation, BodyAblationResult result, ulong[] hashes, string appVersion,
            DateTime createdUtc)
        {
            var first = result.Outcomes[0].Result;
            var variants = JsonValue.NewArray();
            for (var i = 0; i < result.Outcomes.Count; i++)
            {
                var outcome = result.Outcomes[i];
                var r = outcome.Result;
                var genes = JsonValue.NewArray();
                foreach (var name in GenesText(outcome.Variant.Genes, ";").Split(';'))
                {
                    if (name.Length > 0)
                        genes.Add(JsonValue.Of(name));
                }

                var finalGenes = JsonValue.NewObject();
                for (var g = 0; g < BodyGenes.Count; g++)
                    finalGenes.Add(BodyVariant.GeneName((BodyGene)g), SpreadJson(outcome.FinalGeneMean((BodyGene)g)));
                variants.Add(JsonValue.NewObject()
                    .Add("name", JsonValue.Of(outcome.Variant.Name))
                    .Add("expressedBodyGenes", genes)
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
                    .Add("medianImproved", JsonValue.Of(r.MedianImproved))
                    .Add("separatedFromFixed", JsonValue.Of(result.IsSeparated(outcome, result.Control)))
                    .Add("separatedFromAll", JsonValue.Of(result.IsSeparated(outcome, result.AllGenes)))
                    .Add("finalBodyDiversity", SpreadJson(outcome.FinalDiversity))
                    .Add("finalGeneMeans", finalGenes)
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
                .Add("sharedSettings", Header(ablation, result.Outcomes[0].Variant, appVersion, createdUtc).SettingsJson())
                .Add("baselines", JsonValue.NewObject()
                    .Add("random", JsonValue.OfAnyFloat(first.RandomBaseline.MeanFitness))
                    .Add("heuristic", JsonValue.OfAnyFloat(first.HeuristicBaseline.MeanFitness)))
                .Add("variants", variants);
            return JsonText.Write(root);
        }

        private static string Delta(BodyVariantOutcome outcome, BodyVariantOutcome baseline) =>
            baseline == null || baseline == outcome ? string.Empty : Number(BodyAblationResult.Difference(outcome, baseline));

        private static string Flag(BodyAblationResult result, BodyVariantOutcome outcome, BodyVariantOutcome baseline) =>
            baseline == null || baseline == outcome ? string.Empty : result.IsSeparated(outcome, baseline) ? "1" : "0";

        private static string GenesText(BodyGeneSet genes, string separator)
        {
            var names = new List<string>();
            for (var g = 0; g < BodyGenes.Count; g++)
            {
                if (((int)genes & (1 << g)) != 0)
                    names.Add(BodyVariant.GeneName((BodyGene)g));
            }

            return string.Join(separator, names);
        }

        private static string GenesLabel(BodyGeneSet genes)
        {
            if (genes == BodyGeneSet.None)
                return "corpo fixo, controle";
            var names = new List<string>();
            string[] labels = { "tamanho", "velocidade", "sensor", "eficiência" };
            for (var g = 0; g < BodyGenes.Count; g++)
            {
                if (((int)genes & (1 << g)) != 0)
                    names.Add(labels[g]);
            }

            return string.Join(", ", names);
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

        private static string Signed(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? "inválida" : value.ToString("+0.###;-0.###;0", CultureInfo.InvariantCulture);

        private static string G(BodyVariantOutcome outcome, BodyGene gene) =>
            ((int)outcome.Variant.Genes & (1 << (int)gene)) == 0
                ? "—"
                : Signed(outcome.FinalGeneMean(gene).Mean) + " ± " + F(outcome.FinalGeneMean(gene).StdDev);

        private static string S(Spread spread) =>
            Text("{0} ± {1} [{2}, {3}]", F(spread.Mean), F(spread.StdDev), F(spread.Min), F(spread.Max));

        private static string Number(float value) =>
            float.IsNaN(value) || float.IsInfinity(value)
                ? string.Empty
                : value.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
