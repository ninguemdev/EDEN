using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Eden.Application;
using Eden.Evolution;

namespace Eden.Persistence
{
    /// <summary>Offline export of confirmed v2 segments to the familiar flat CSV layouts. No simulation is advanced.</summary>
    public static class MetricsCompatibilityExporter
    {
        public static void Export(string source, string destination)
        {
            if (Directory.Exists(destination) && Directory.GetFileSystemEntries(destination).Length > 0)
                throw new IOException("Compatibility output must be a new, empty directory.");
            var segments = GenerationSegmentWriter.VerifiedSegments(source);
            var metadata = JsonText.Parse(File.ReadAllText(Path.Combine(source, "experiment.json")));
            var settings = CheckpointCodec.DecodeEvolutionSettings(metadata["settings"], out var interval);
            var header = new ExperimentHeader(settings, interval, metadata["app"]["version"].String(), DateTime.UtcNow);
            var profile = header.Plan.Profile;
            Directory.CreateDirectory(destination);
            File.Copy(Path.Combine(source, "experiment.json"), Path.Combine(destination, "experiment.json"));
            File.WriteAllText(Path.Combine(destination, "experiment.txt"),
                File.ReadAllText(Path.Combine(source, "experiment.txt")).Replace("metrics_schema=2", "metrics_schema=1"));
            using (var generations = new CsvFile(Path.Combine(destination, "generations.csv"), EvolutionCsvLog.GenerationsHeader, false))
            using (var individuals = new CsvFile(Path.Combine(destination, "individuals.csv"), EvolutionCsvLog.IndividualsHeader, false))
            using (var episodes = new CsvFile(Path.Combine(destination, "episodes.csv"), EvaluationCsv.Header, false))
            using (var stages = profile.IsLegacy ? null : new CsvFile(Path.Combine(destination, "stages.csv"), StageCsv.Header(profile), false))
            using (var summaries = profile.IsLegacy ? null : new CsvFile(Path.Combine(destination, "stage-summaries.csv"), StageCsv.SummaryHeader(profile), false))
            using (var neural = header.IsNeat ? new CsvFile(Path.Combine(destination, "neat-generations.csv"), EvolutionCsvLog.NeatGenerationsHeader, false) : null)
            using (var networks = header.IsNeat ? new CsvFile(Path.Combine(destination, "neat-individuals.csv"), EvolutionCsvLog.NeatIndividualsHeader, false) : null)
            foreach (var segment in segments)
            {
                var commit = JsonText.Parse(File.ReadAllText(Path.Combine(segment, "commit.json")));
                var generation = commit["generation"].Int(0);
                var mode = commit["mode"].String() == "training" ? EvaluationMode.Training : EvaluationMode.Validation;
                foreach (var row in Data(Path.Combine(segment, "generation.csv"))) generations.Append(string.Join(",", row));
                foreach (var row in Data(Path.Combine(segment, "individuals.csv")))
                {
                    individuals.Append(string.Join(",", Slice(row, 0, 14)));
                    if (row.Length > 14) networks?.Append(string.Join(",", row[0], row[2], row[3], row[14], row[15], row[16], row[17], row[18]));
                }
                if (File.Exists(Path.Combine(segment, "neat-summary.csv")))
                    foreach (var row in Data(Path.Combine(segment, "neat-summary.csv"))) neural?.Append(string.Join(",", row));
                var facts = new Dictionary<string, List<string[]>>();
                foreach (var row in Data(Path.Combine(segment, "targets.csv")))
                {
                    var key = Key(row[0], row[1], row[2]);
                    if (!facts.TryGetValue(key, out var list)) facts.Add(key, list = new List<string[]>());
                    list.Add(row);
                }
                var path = Path.Combine(segment, "episodes.csv");
                var names = File.ReadLines(path).GetEnumerator();
                string[] columns;
                using (names) { if (!names.MoveNext()) throw new IOException("Missing episode header."); columns = names.Current.Split(','); }
                var results = new List<EpisodeResult>();
                foreach (var row in Data(path))
                {
                    var stage = int.Parse(row[2], CultureInfo.InvariantCulture); var candidate = int.Parse(row[0], CultureInfo.InvariantCulture);
                    var seedIndex = int.Parse(row[3], CultureInfo.InvariantCulture);
                    var definition = profile.Stages[stage]; var map = new Dictionary<string, int>();
                    for (var c = 0; c < definition.Components.Count; c++) map.Add(definition.Components[c].Name, c);
                    var precise = new double[map.Count]; for (var c = 0; c < precise.Length; c++) precise[c] = double.NaN;
                    void Set(string name, double value) { if (map.TryGetValue(name, out var at)) precise[at] = value; }
                    for (var c = 12; c < columns.Length; c++) Set(columns[c], Number(row[c]));
                    var score = (float)Number(row[5]); var food = (float)Number(row[7]); var energy = (float)Number(row[11]);
                    Set("score", score); Set("food_energy", food); Set("final_energy", energy);
                    Set("configured_seconds", header.Plan.Stages[stage].EpisodeSeconds);
                    var scenario = JsonText.Parse(File.ReadAllText(Path.Combine(source, "scenarios", row[4] + ".json")));
                    var seed = scenario["seed"].ULong();
                    if (header.Plan.Stages[stage].ActiveSearch)
                        for (var t = header.Plan.Stages[stage].Search.Targets.Food; t < header.Plan.Stages[stage].Search.Targets.Count; t++) Set("water_useful_" + t, 0);
                    if (facts.TryGetValue(Key(row[0], row[2], row[3]), out var found))
                        foreach (var target in found)
                            for (var c = 0; c < TargetNames.Length; c++) Set(TargetNames[c] + "_" + target[3], Number(target[6 + c]));
                    var rounded = new float[precise.Length]; for (var c = 0; c < precise.Length; c++) rounded[c] = (float)precise[c];
                    var episode = new EpisodeResult(mode, generation, candidate, seedIndex, seed,
                        long.Parse(row[10], CultureInfo.InvariantCulture), header.Plan.Stages[stage].EpisodeSeconds, food,
                        long.Parse(row[9], CultureInfo.InvariantCulture), row[8] == "1", energy,
                        profile.IsLegacy ? null : rounded, stage, profile.IsLegacy ? (float?)null : score,
                        header.Plan.Stages[stage].ActiveSearch ? precise : null);
                    results.Add(episode); episodes.Append(EvaluationCsv.FormatRow(episode)); stages?.Append(StageCsv.FormatRow(profile, episode));
                }
                if (summaries != null)
                {
                    var seeds = mode == EvaluationMode.Training ? header.Plan.TrainingSeeds(generation) : header.Plan.ValidationSeeds();
                    var evaluation = GenerationEvaluation.FromEpisodes(generation, mode, seeds, results,
                        header.Plan.Stages[0].TemplateConfig.TicksPerSecond, profile);
                    foreach (var summary in StageCsv.Summaries(profile, evaluation)) summaries.Append(summary);
                }
            }
        }
        private static readonly string[] TargetNames = { "contact_seconds", "sip_seconds", "collection_seconds",
            "energy_before_collection", "hydration_before_sip", "water_useful" };
        private static string Key(string candidate, string stage, string seed) => candidate + ":" + stage + ":" + seed;
        private static double Number(string value) => string.IsNullOrEmpty(value) ? double.NaN : double.Parse(value, CultureInfo.InvariantCulture);
        private static string[] Slice(string[] row, int start, int count) { var result = new string[count]; Array.Copy(row, start, result, 0, count); return result; }
        private static IEnumerable<string[]> Data(string path)
        {
            var first = true;
            foreach (var line in File.ReadLines(path))
            {
                if (first) { first = false; continue; }
                if (!string.IsNullOrEmpty(line)) yield return line.Split(',');
            }
        }
    }
}
