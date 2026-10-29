using System;
using System.Collections.Generic;
using System.Globalization;
using Eden.Domain;
using Eden.Evolution;

namespace Eden.Persistence
{
    /// <summary>Plain key=value description of a shared world run, written next to its metrics.</summary>
    public static class RunInfo
    {
        /// <param name="scenario">Arena size and resource scaling (see the scenario's description), or null for the control.</param>
        public static IReadOnlyList<string> Describe(SimulationConfig config, string brainName, string appVersion,
            DateTime createdUtc, string scenario = null)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var culture = CultureInfo.InvariantCulture;
            var lines = new List<string>
            {
                "config_schema=" + config.SchemaVersion.ToString(culture),
                "seed=" + config.Seed.ToString(culture),
                "brain=" + brainName,
                "evaluation_mode=" + EvaluationCsv.ModeName(EvaluationMode.SharedInspection),
                "app_version=" + appVersion,
                "created_utc=" + createdUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", culture),
                "ticks_per_second=" + config.TicksPerSecond.ToString(culture),
                "decision_interval_ticks=" + config.DecisionIntervalTicks.ToString(culture),
                "arena=" + config.ArenaWidth.ToString(culture) + "x" + config.ArenaHeight.ToString(culture),
                "initial_population=" + config.InitialPopulation.ToString(culture),
                "initial_food=" + config.InitialFood.ToString(culture)
            };
            // The physical scenario only when there is one, so runs without it are described as before.
            if (config.SolidBodies)
                lines.Add("bodies=solid;radius " + config.BodyRadius.ToString(culture) + ";substeps " +
                          config.SolidSubsteps.ToString(culture));
            if (config.Obstacles.Count > 0)
            {
                lines.Add("obstacle_layout=" + ExperimentHeader.LayoutName(config));
                lines.Add("obstacles=" + config.Obstacles.Count.ToString(culture));
                lines.Add("free_regions=" + config.FreeSpace.Regions.ToString(culture));
            }

            lines.AddRange(ExperimentHeader.WaterLines(config));
            lines.AddRange(ExperimentHeader.ResourceLines(config, config.InitialPopulation));
            if (scenario != null)
                lines.Add("scenario=" + scenario);

            return lines;
        }
    }
}
