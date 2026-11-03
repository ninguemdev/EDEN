using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Eden.Evolution;

namespace Eden.Persistence
{
    /// <summary>Legacy CSV layout, with a column map compiled once per immutable reward profile.</summary>
    public static class StageCsv
    {
        private sealed class Layout
        {
            public readonly string[] Columns;
            public readonly int[][] Indices;
            public Layout(RewardProfile profile)
            {
                var columns = new List<string>();
                foreach (var stage in profile.Stages)
                    foreach (var component in stage.Components) columns.Add(stage.Name + "." + component.Name);
                Columns = columns.ToArray();
                Indices = new int[profile.Stages.Count][];
                for (var s = 0; s < Indices.Length; s++)
                {
                    var stage = profile.Stages[s];
                    var own = new Dictionary<string, int>();
                    for (var c = 0; c < stage.Components.Count; c++) own.Add(stage.Name + "." + stage.Components[c].Name, c);
                    Indices[s] = new int[Columns.Length];
                    for (var c = 0; c < Columns.Length; c++)
                        Indices[s][c] = own.TryGetValue(Columns[c], out var at) ? at : -1;
                }
            }
        }
        private static readonly ConditionalWeakTable<RewardProfile, Layout> Layouts = new ConditionalWeakTable<RewardProfile, Layout>();
        [ThreadStatic] private static StringBuilder buffer;
        private static Layout Of(RewardProfile profile) => Layouts.GetValue(profile, p => new Layout(p));
        private static StringBuilder Buffer()
        {
            var text = buffer ?? (buffer = new StringBuilder(2048)); text.Clear(); return text;
        }
        public static string Header(RewardProfile profile) =>
            "evaluation_mode,generation,candidate,seed_index,stage,stage_name,episode_seed,valid,score,legacy_fitness," +
            string.Join(",", Of(profile).Columns);

        public static string FormatRow(RewardProfile profile, in EpisodeResult result)
        {
            var culture = CultureInfo.InvariantCulture;
            var text = Buffer();
            text.Append(EvaluationCsv.ModeName(result.Mode)).Append(',').Append(result.Generation).Append(',')
                .Append(result.Candidate).Append(',').Append(result.SeedIndex).Append(',').Append(result.Stage).Append(',')
                .Append(profile.Stages[result.Stage].Name).Append(',').Append(result.EpisodeSeed).Append(',')
                .Append(result.IsValid ? "1" : "0").Append(',');
            var active = RewardProfiles.IsActive(profile);
            if (result.IsValid) text.Append(result.Fitness.ToString(active ? "R" : "0.######", culture));
            text.Append(',');
            if (Fitness.IsValid(result.LegacyFitness)) text.Append(result.LegacyFitness.ToString("0.######", culture));
            foreach (var index in Of(profile).Indices[result.Stage])
            {
                text.Append(',');
                if (index < 0 || result.Components == null) continue;
                if (!active) text.Append(result.Components[index].ToString("0.######", culture));
                else
                {
                    var value = result.PreciseComponents != null ? result.PreciseComponents[index] : result.Components[index];
                    if (!double.IsNaN(value)) text.Append(value.ToString("R", culture));
                }
            }
            return text.ToString();
        }
        public static string SummaryHeader(RewardProfile profile) =>
            "evaluation_mode,generation,stage,stage_name,episodes,score_mean," + string.Join(",", Of(profile).Columns);

        public static IEnumerable<string> Summaries(RewardProfile profile, GenerationEvaluation evaluation)
        {
            var layout = Of(profile);
            for (var s = 0; s < profile.Stages.Count; s++)
            {
                var stage = profile.Stages[s]; var n = 0; double score = 0;
                var sums = new double[stage.Components.Count]; var counts = new int[sums.Length];
                foreach (var episode in evaluation.Episodes)
                {
                    if (episode.Stage != s) continue;
                    n++; score += episode.Fitness;
                    for (var c = 0; c < sums.Length; c++)
                    {
                        var value = episode.PreciseComponents != null ? episode.PreciseComponents[c] : episode.Components[c];
                        if (double.IsNaN(value)) continue;
                        sums[c] += value; counts[c]++;
                    }
                }
                var text = Buffer(); text.Append(EvaluationCsv.ModeName(evaluation.Mode)).Append(',').Append(evaluation.Generation)
                    .Append(',').Append(s).Append(',').Append(stage.Name).Append(',').Append(n).Append(',');
                if (n > 0) text.Append((score / n).ToString("R", CultureInfo.InvariantCulture));
                foreach (var c in layout.Indices[s])
                {
                    text.Append(',');
                    if (c >= 0 && counts[c] > 0) text.Append((sums[c] / counts[c]).ToString("R", CultureInfo.InvariantCulture));
                }
                yield return text.ToString();
            }
        }
    }
}
