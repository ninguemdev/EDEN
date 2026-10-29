using System.Collections.Generic;
using System.Globalization;
using Eden.Evolution;

namespace Eden.Persistence
{
    /// <summary>
    /// <c>stages.csv</c>, written only for a profile other than the legacy one: one row per episode with its stage, its
    /// score, the legacy fitness of the same episode and one column per component of any stage (empty when the episode's
    /// stage does not report it).
    /// </summary>
    public static class StageCsv
    {
        public static string Header(RewardProfile profile) =>
            "evaluation_mode,generation,candidate,seed_index,stage,stage_name,episode_seed,valid,score,legacy_fitness," +
            string.Join(",", Columns(profile));

        public static string FormatRow(RewardProfile profile, in EpisodeResult result)
        {
            var culture = CultureInfo.InvariantCulture;
            var stage = profile.Stages[result.Stage];
            var fields = new List<string>
            {
                EvaluationCsv.ModeName(result.Mode),
                result.Generation.ToString(culture),
                result.Candidate.ToString(culture),
                result.SeedIndex.ToString(culture),
                result.Stage.ToString(culture),
                stage.Name,
                result.EpisodeSeed.ToString(culture),
                result.IsValid ? "1" : "0",
                result.IsValid ? result.Fitness.ToString("0.######", culture) : string.Empty,
                Evolution.Fitness.IsValid(result.LegacyFitness)
                    ? result.LegacyFitness.ToString("0.######", culture)
                    : string.Empty
            };
            foreach (var column in Columns(profile))
            {
                var value = string.Empty;
                for (var i = 0; i < stage.Components.Count && result.Components != null; i++)
                {
                    if (stage.Name + "." + stage.Components[i].Name == column)
                        value = result.Components[i].ToString("0.######", culture);
                }

                fields.Add(value);
            }

            return string.Join(",", fields);
        }

        // "stage.component" for every component of every stage, in stage and component order.
        private static IEnumerable<string> Columns(RewardProfile profile)
        {
            foreach (var stage in profile.Stages)
            {
                foreach (var component in stage.Components)
                    yield return stage.Name + "." + component.Name;
            }
        }
    }
}
