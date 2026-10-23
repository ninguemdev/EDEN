using System.Globalization;
using Eden.Evolution;

namespace Eden.Persistence
{
    /// <summary>
    /// One CSV row per episode (candidate × seed), before any aggregation. Numbers use the invariant culture;
    /// an invalid fitness is written as an empty field with <c>valid = 0</c>.
    /// </summary>
    public static class EvaluationCsv
    {
        public const string Header =
            "evaluation_mode,generation,candidate,seed_index,episode_seed,valid,fitness,food_energy," +
            "survived,survived_ticks,episode_ticks,final_energy";

        public static string FormatRow(in EpisodeResult result)
        {
            var culture = CultureInfo.InvariantCulture;
            return string.Join(",",
                ModeName(result.Mode),
                result.Generation.ToString(culture),
                result.Candidate.ToString(culture),
                result.SeedIndex.ToString(culture),
                result.EpisodeSeed.ToString(culture),
                result.IsValid ? "1" : "0",
                result.IsValid ? result.Fitness.ToString("0.######", culture) : string.Empty,
                result.FoodEnergy.ToString("0.###", culture),
                result.Survived ? "1" : "0",
                result.SurvivedTicks.ToString(culture),
                result.EpisodeTicks.ToString(culture),
                result.FinalEnergy.ToString("0.###", culture));
        }

        public static string ModeName(EvaluationMode mode)
        {
            switch (mode)
            {
                case EvaluationMode.Training: return "training";
                case EvaluationMode.Validation: return "validation";
                case EvaluationMode.SharedInspection: return "shared_inspection";
                default: return mode.ToString().ToLowerInvariant();
            }
        }
    }
}
