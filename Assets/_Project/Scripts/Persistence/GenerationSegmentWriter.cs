using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;

namespace Eden.Persistence
{
    /// <summary>Metrics v2: compact episodes, sparse per-target facts, scenarios once, and independently verifiable
    /// generation segments. A partial directory is never a committed generation. Existing segments are not rewritten.</summary>
    public sealed class GenerationSegmentWriter : IDisposable
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private static readonly string[] TargetFields = { "contact_seconds", "sip_seconds", "collection_seconds",
            "energy_before_collection", "hydration_before_sip", "water_useful" };
        private readonly string directory;
        private readonly ExperimentHeader header;
        private readonly string[] coreNames;
        private readonly int[][] coreIndices;
        private readonly Dictionary<string, int>[] indices;
        private readonly HashSet<string> scenarios = new HashSet<string>();
        private readonly ExportTally tally = new ExportTally();
        private readonly bool compatibility;
        private EvolutionCsvLog legacy;
        public string LastCommittedSegment { get; private set; }
        public GenerationSegmentWriter(string directory, ExperimentHeader header, bool compatibilityCsv = false)
        {
            this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
            this.header = header ?? throw new ArgumentNullException(nameof(header));
            compatibility = compatibilityCsv;
            if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length > 0)
                throw new IOException("Choose a new, empty metrics directory; existing experiments are preserved.");
            Directory.CreateDirectory(directory);
            Directory.CreateDirectory(Path.Combine(directory, "segments"));
            Directory.CreateDirectory(Path.Combine(directory, "scenarios"));
            File.WriteAllText(Path.Combine(directory, "experiment.txt"), string.Join("\n", header.Describe()) + "\nmetrics_schema=2\n", Utf8);
            File.WriteAllText(Path.Combine(directory, "experiment.json"), ExperimentJson.Experiment(header), Utf8);
            var core = new List<string>();
            indices = new Dictionary<string, int>[header.Plan.Stages.Count];
            for (var s = 0; s < indices.Length; s++)
            {
                indices[s] = new Dictionary<string, int>();
                var components = header.Plan.Profile.Stages[s].Components;
                for (var c = 0; c < components.Count; c++)
                {
                    var name = components[c].Name; indices[s].Add(name, c);
                    var targetField = false;
                    foreach (var prefix in TargetFields) targetField |= name.StartsWith(prefix + "_", StringComparison.Ordinal);
                    if (!targetField && name != "score" && name != "food_energy" && name != "final_energy" &&
                        name != "configured_seconds" && !core.Contains(name)) core.Add(name);
                }
            }
            coreNames = core.ToArray(); coreIndices = new int[indices.Length][];
            for (var s = 0; s < indices.Length; s++)
            {
                coreIndices[s] = new int[coreNames.Length];
                for (var c = 0; c < coreNames.Length; c++) coreIndices[s][c] = indices[s].TryGetValue(coreNames[c], out var at) ? at : -1;
            }
            var parent = header.ResumedFrom == null ? null :
                MetricsCheckpointCursor.LoadMatching(header.ResumedFrom.CheckpointPath, header.ResumedFrom.StateHash);
            var fields = JsonValue.NewArray(); foreach (var name in coreNames) fields.Add(JsonValue.Of(name));
            File.WriteAllText(Path.Combine(directory, "metrics.json"), JsonText.Write(JsonValue.NewObject()
                .Add("schemaVersion", JsonValue.Of(2)).Add("configHash", JsonValue.Of(CheckpointCodec.Hex(header.ConfigHash)))
                .Add("layout", JsonValue.Of("committed generation segments; scenario_id joins scenarios; candidate/stage/seed_index join targets"))
                .Add("components", fields).Add("parentMetrics", parent == null ? JsonValue.Null() : parent.ToJson()).Add("precision", JsonValue.Of("round-trip; blank means unavailable; fitness is the exact float used by selection"))), Utf8);
        }
        public void Append(GenerationReport report)
        {
            var id = "g" + report.Evaluation.Generation.ToString("D6", Culture) + "-" + EvaluationCsv.ModeName(report.Evaluation.Mode);
            var final = Path.Combine(directory, "segments", id);
            if (Directory.Exists(final)) throw new IOException("Generation segment already committed: " + id);
            var partial = final + ".partial";
            var usedScenarios = new HashSet<string>();
            Directory.CreateDirectory(partial);
            using (var generation = new CsvFile(Path.Combine(partial, "generation.csv"), EvolutionCsvLog.GenerationsHeader, false))
                generation.Append(EvolutionCsvLog.FormatGeneration(report.Summary));
            var neatColumns = report.Neat == null ? "" : ",species,hidden_nodes,connections,enabled_connections,eyes";
            using (var individuals = new CsvFile(Path.Combine(partial, "individuals.csv"), EvolutionCsvLog.IndividualsHeader + neatColumns, false))
                for (var c = 0; c < report.Individuals.Count; c++)
                {
                    var individual = report.Individuals[c]; var evaluated = report.Evaluation.Candidates[c];
                    var parents = new string[individual.ParentIds.Count];
                    for (var p = 0; p < parents.Length; p++) parents[p] = individual.ParentIds[p].ToString(Culture);
                    var row = string.Join(",", report.Evaluation.Generation.ToString(Culture), EvaluationCsv.ModeName(report.Evaluation.Mode),
                        c.ToString(Culture), individual.Id.ToString(Culture), string.Join(";", parents), individual.BirthGeneration.ToString(Culture),
                        individual.BirthGeneration < report.Evaluation.Generation ? "1" : "0", individual.MutatedWeights.ToString(Culture),
                        Fitness.IsValid(evaluated.MeanFitness) ? "1" : "0", Number(evaluated.MeanFitness), Number(evaluated.MeanFoodEnergy),
                        Number(evaluated.SurvivalRate), Number(evaluated.MeanSurvivedSeconds), Number(evaluated.MeanFinalEnergy));
                    if (report.Neat != null) row += "," + report.Neat.Species[c] + "," + report.Neat.HiddenNodesOf(c) + "," +
                        report.Neat.ConnectionsOf(c) + "," + report.Neat.EnabledConnectionsOf(c) + "," + (report.Neat.HasEyes ? report.Neat.EyesOf(c).ToString(Culture) : "");
                    individuals.Append(row);
                }
            using (var episodes = new CsvFile(Path.Combine(partial, "episodes.csv"),
                "candidate,individual_id,stage,seed_index,scenario_id,fitness,valid,food_energy,survived,lived_ticks,configured_ticks,final_energy," + string.Join(",", coreNames), false))
            using (var targets = new CsvFile(Path.Combine(partial, "targets.csv"),
                "candidate,stage,seed_index,target_id,target_type,detected," + string.Join(",", TargetFields), false))
            {
                var line = new StringBuilder(1024);
                foreach (var episode in report.Evaluation.Episodes)
                {
                    var scenario = Scenario(episode); usedScenarios.Add(scenario);
                    line.Clear(); line.Append(episode.Candidate).Append(',').Append(report.Individuals[episode.Candidate].Id).Append(',')
                        .Append(episode.Stage).Append(',').Append(episode.SeedIndex).Append(',').Append(scenario).Append(',')
                        .Append(episode.IsValid ? episode.Fitness.ToString("R", Culture) : "").Append(',').Append(episode.IsValid ? "1" : "0").Append(',')
                        .Append(episode.FoodEnergy.ToString("R", Culture)).Append(',').Append(episode.Survived ? "1" : "0").Append(',')
                        .Append(episode.SurvivedTicks).Append(',').Append(episode.EpisodeTicks).Append(',').Append(episode.FinalEnergy.ToString("R", Culture));
                    foreach (var c in coreIndices[episode.Stage]) { line.Append(','); if (c >= 0) AppendValue(line, Value(episode, c)); }
                    episodes.Append(line.ToString());
                    var stage = header.Plan.Stages[episode.Stage];
                    if (!stage.ActiveSearch) continue;
                    for (var t = 0; t < stage.Search.Targets.Count; t++)
                    {
                        var detected = episode.DetectedTargets != null && episode.DetectedTargets[t];
                        var hasFact = detected;
                        foreach (var field in TargetFields)
                        {
                            var value = Named(episode, field + "_" + t);
                            hasFact |= !double.IsNaN(value) && (field != "water_useful" || value > 0);
                        }
                        if (!hasFact) continue;
                        line.Clear(); line.Append(episode.Candidate).Append(',').Append(episode.Stage).Append(',').Append(episode.SeedIndex)
                            .Append(',').Append(t).Append(',').Append(t < stage.Search.Targets.Food ? "food" : "water").Append(',')
                            .Append(episode.DetectedTargets == null ? "" : detected ? "1" : "0");
                        foreach (var field in TargetFields) { line.Append(','); AppendValue(line, Named(episode, field + "_" + t)); }
                        targets.Append(line.ToString());
                    }
                }
            }
            if (!header.Plan.Profile.IsLegacy)
            using (var summaries = new CsvFile(Path.Combine(partial, "stage-summary.csv"), "stage,stage_name,episodes,score_mean," + string.Join(",", coreNames), false))
            {
                for (var stage = 0; stage < indices.Length; stage++)
                {
                    var sums = new double[coreNames.Length]; var counts = new int[sums.Length]; var n = 0; double score = 0;
                    foreach (var episode in report.Evaluation.Episodes)
                    {
                        if (episode.Stage != stage) continue;
                        n++; score += episode.Fitness;
                        for (var c = 0; c < sums.Length; c++)
                        {
                            var at = coreIndices[stage][c]; var value = at < 0 ? double.NaN : Value(episode, at);
                            if (double.IsNaN(value)) continue;
                            sums[c] += value; counts[c]++;
                        }
                    }
                    var row = new StringBuilder().Append(stage).Append(',').Append(header.Plan.Stages[stage].Name).Append(',').Append(n).Append(',');
                    if (n > 0) AppendValue(row, score / n);
                    for (var c = 0; c < sums.Length; c++) { row.Append(','); if (counts[c] > 0) AppendValue(row, sums[c] / counts[c]); }
                    summaries.Append(row.ToString());
                }
            }
            if (report.Neat != null)
                using (var neural = new CsvFile(Path.Combine(partial, "neat-summary.csv"), EvolutionCsvLog.NeatGenerationsHeader, false))
                    neural.Append(EvolutionCsvLog.FormatNeatGeneration(report));
            if (report.Evaluation.Mode == EvaluationMode.Training)
                for (var c = 0; c < report.Individuals.Count; c++)
                    if (report.Individuals[c].Id == report.Summary.BestIndividualId)
                    {
                        var champion = report.Individuals[c];
                        GenomeFile.Save(Path.Combine(partial, "champion.json"), new GenomeRecord(champion.Genome,
                            report.Evaluation.Candidates[c].MeanFitness,
                            new GenomeSource(header.Plan.ExperimentSeed, report.Evaluation.Generation, champion.Id),
                            header.Plan.Profile.Id, header.Plan.Profile.ParameterHash), header.AppVersion, DateTime.UtcNow);
                        break;
                    }
            var files = JsonValue.NewArray();
            foreach (var path in Directory.GetFiles(partial))
                files.Add(JsonValue.NewObject().Add("name", JsonValue.Of(Path.GetFileName(path)))
                    .Add("bytes", JsonValue.Of(new FileInfo(path).Length)).Add("sha256", JsonValue.Of(Hash(path))));
            var scenarioRefs = JsonValue.NewArray();
            foreach (var idRef in usedScenarios)
                scenarioRefs.Add(JsonValue.NewObject().Add("id", JsonValue.Of(idRef))
                    .Add("sha256", JsonValue.Of(Hash(Path.Combine(directory, "scenarios", idRef + ".json")))));
            File.WriteAllText(Path.Combine(partial, "commit.json"), JsonText.Write(JsonValue.NewObject()
                .Add("schemaVersion", JsonValue.Of(2)).Add("generation", JsonValue.Of(report.Evaluation.Generation))
                .Add("mode", JsonValue.Of(EvaluationCsv.ModeName(report.Evaluation.Mode)))
                .Add("configHash", JsonValue.Of(CheckpointCodec.Hex(header.ConfigHash)))
                .Add("episodes", JsonValue.Of(report.Evaluation.Episodes.Count)).Add("files", files).Add("scenarios", scenarioRefs)), Utf8);
            Directory.Move(partial, final); // Same volume: readers see only the fully closed segment.
            LastCommittedSegment = id;
            tally.Add(report);
            ExperimentJson.WriteReplacing(Path.Combine(directory, "summary.json"), ExperimentJson.Summary(header, tally));
            if (compatibility)
            {
                legacy ??= new EvolutionCsvLog(Path.Combine(directory, "compatibility"), header);
                legacy.Append(report);
            }
        }
        public static string[] VerifiedSegments(string directory)
        {
            var metadata = JsonText.Parse(File.ReadAllText(Path.Combine(directory, "metrics.json"), Utf8));
            if (metadata["schemaVersion"].Int() != 2) throw new IOException("Unsupported metrics schema.");
            var result = new List<string>();
            foreach (var segment in Directory.GetDirectories(Path.Combine(directory, "segments"), "g*-*"))
            {
                if (segment.EndsWith(".partial", StringComparison.Ordinal)) continue;
                var commit = JsonText.Parse(File.ReadAllText(Path.Combine(segment, "commit.json"), Utf8));
                if (commit["configHash"].String() != metadata["configHash"].String()) throw new IOException("Segment configuration differs.");
                var savedFiles = commit["files"].ArrayOf(16);
                for (var f = 0; f < savedFiles.Count; f++)
                {
                    var saved = savedFiles[f];
                    var name = saved["name"].String();
                    if (name != Path.GetFileName(name)) throw new IOException("Invalid segment file name.");
                    var path = Path.Combine(segment, name);
                    if (!File.Exists(path) || new FileInfo(path).Length != saved["bytes"].Long(0) || Hash(path) != saved["sha256"].String())
                        throw new IOException("Incomplete or corrupted metrics segment: " + segment);
                }
                var savedScenarios = commit["scenarios"].ArrayOf(4096);
                for (var i = 0; i < savedScenarios.Count; i++)
                {
                    var id = savedScenarios[i]["id"].String();
                    if (id != Path.GetFileName(id)) throw new IOException("Invalid scenario ID.");
                    if (Hash(Path.Combine(directory, "scenarios", id + ".json")) != savedScenarios[i]["sha256"].String())
                        throw new IOException("Corrupted scenario: " + id);
                }
                result.Add(segment);
            }
            result.Sort(StringComparer.Ordinal); return result.ToArray();
        }
        private static string Number(float value) => Fitness.IsValid(value) ? value.ToString("R", Culture) : "";
        private string Scenario(in EpisodeResult episode)
        {
            var id = "s" + episode.Stage + "-" + episode.EpisodeSeed.ToString(Culture);
            if (!scenarios.Add(id)) return id;
            var stage = header.Plan.Stages[episode.Stage];
            var layout = stage.Search?.Layout(episode.EpisodeSeed);
            var config = layout?.Config ?? stage.EpisodeConfig(episode.EpisodeSeed);
            var obstacles = JsonValue.NewArray(); foreach (var o in config.Obstacles) obstacles.Add(Area(o));
            var water = JsonValue.NewArray(); foreach (var source in config.Water) water.Add(Area(source.Area));
            var food = JsonValue.NewArray(); if (layout != null) foreach (var point in layout.Food) food.Add(Point(point));
            var data = JsonValue.NewObject().Add("stage", JsonValue.Of(stage.Index)).Add("role", JsonValue.Of(stage.Name))
                .Add("seed", JsonValue.Of(episode.EpisodeSeed)).Add("configHash", JsonValue.Of(CheckpointCodec.Hex(header.ConfigHash)))
                .Add("width", JsonValue.Of(config.ArenaWidth)).Add("height", JsonValue.Of(config.ArenaHeight))
                .Add("founder", layout == null ? JsonValue.Null() : Point(layout.Founder))
                .Add("heading", layout == null ? JsonValue.Null() : JsonValue.Of(layout.FounderHeading))
                .Add("obstacles", obstacles).Add("water", water).Add("food", food);
            var path = Path.Combine(directory, "scenarios", id + ".json");
            var temporary = path + ".tmp"; File.WriteAllText(temporary, JsonText.Write(data), Utf8); File.Move(temporary, path);
            return id;
        }
        private double Named(in EpisodeResult episode, string name) => indices[episode.Stage].TryGetValue(name, out var index) ? Value(episode, index) : double.NaN;
        private static double Value(in EpisodeResult e, int index) => e.PreciseComponents != null ? e.PreciseComponents[index] : e.Components == null ? double.NaN : e.Components[index];
        private static void AppendValue(StringBuilder line, double value) { if (!double.IsNaN(value) && !double.IsInfinity(value)) line.Append(value.ToString("R", Culture)); }
        private static JsonValue Point(Float2 point) => JsonValue.NewArray().Add(JsonValue.Of(point.X)).Add(JsonValue.Of(point.Y));
        private static JsonValue Area(Obstacle o) => JsonValue.NewArray().Add(JsonValue.Of(o.MinX)).Add(JsonValue.Of(o.MinY)).Add(JsonValue.Of(o.MaxX)).Add(JsonValue.Of(o.MaxY));
        private static string Hash(string path) { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        public void Dispose() => legacy?.Dispose();
    }
}
