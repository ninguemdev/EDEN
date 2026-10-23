using System;
using System.Collections.Generic;
using System.Globalization;
using Eden.Domain;

namespace Eden.Persistence
{
    /// <summary>Plain key=value description of a run, written next to its metrics.</summary>
    public static class RunInfo
    {
        public static IReadOnlyList<string> Describe(SimulationConfig config, string brainName, string appVersion,
            DateTime createdUtc)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            var culture = CultureInfo.InvariantCulture;
            return new[]
            {
                "config_schema=" + config.SchemaVersion.ToString(culture),
                "seed=" + config.Seed.ToString(culture),
                "brain=" + brainName,
                "app_version=" + appVersion,
                "created_utc=" + createdUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", culture),
                "ticks_per_second=" + config.TicksPerSecond.ToString(culture),
                "decision_interval_ticks=" + config.DecisionIntervalTicks.ToString(culture),
                "arena=" + config.ArenaWidth.ToString(culture) + "x" + config.ArenaHeight.ToString(culture),
                "initial_population=" + config.InitialPopulation.ToString(culture),
                "initial_food=" + config.InitialFood.ToString(culture)
            };
        }
    }
}
