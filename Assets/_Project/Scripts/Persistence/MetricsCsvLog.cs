using System;
using System.Globalization;
using Eden.Application;

namespace Eden.Persistence
{
    /// <summary>
    /// Appends metrics samples to a CSV file. Numbers always use the invariant culture (dot decimals),
    /// whatever the machine's locale, and each row is flushed so a crash keeps the data written so far.
    /// </summary>
    public sealed class MetricsCsvLog : IDisposable
    {
        public const string Header =
            "tick,simulated_seconds,population,food_available,food_eaten_window," +
            "starvation_deaths_window,old_age_deaths_window,mean_energy,total_food_eaten,births_window," +
            "births_refused_window,max_generation,extinct";

        private CsvFile file;

        public MetricsCsvLog(string path)
        {
            file = new CsvFile(path, Header);
            FilePath = path;
        }

        public string FilePath { get; }
        public int RowCount { get; private set; }

        public void Append(MetricsSample sample)
        {
            if (file == null)
                throw new ObjectDisposedException(nameof(MetricsCsvLog));
            file.Append(FormatRow(sample));
            RowCount++;
        }

        public static string FormatRow(MetricsSample sample)
        {
            var culture = CultureInfo.InvariantCulture;
            return string.Join(",",
                sample.Tick.ToString(culture),
                sample.SimulatedSeconds.ToString("0.###", culture),
                sample.Population.ToString(culture),
                sample.FoodAvailable.ToString(culture),
                sample.FoodEatenInWindow.ToString(culture),
                sample.StarvationDeathsInWindow.ToString(culture),
                sample.OldAgeDeathsInWindow.ToString(culture),
                sample.MeanEnergy.ToString("0.####", culture),
                sample.TotalFoodEaten.ToString(culture),
                sample.BirthsInWindow.ToString(culture),
                sample.BirthsRefusedInWindow.ToString(culture),
                sample.MaxGeneration.ToString(culture),
                sample.Extinct ? "1" : "0");
        }

        public void Dispose()
        {
            file?.Dispose();
            file = null;
        }
    }
}
