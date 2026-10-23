using System;
using System.Globalization;
using System.IO;
using System.Text;
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
            "starvation_deaths_window,old_age_deaths_window,mean_energy,total_food_eaten";

        private StreamWriter writer;

        public MetricsCsvLog(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A file path is required.", nameof(path));

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            FilePath = path;
            writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
            writer.WriteLine(Header);
            writer.Flush();
        }

        public string FilePath { get; }
        public int RowCount { get; private set; }

        public void Append(MetricsSample sample)
        {
            if (writer == null)
                throw new ObjectDisposedException(nameof(MetricsCsvLog));
            writer.WriteLine(FormatRow(sample));
            writer.Flush();
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
                sample.TotalFoodEaten.ToString(culture));
        }

        public void Dispose()
        {
            writer?.Dispose();
            writer = null;
        }
    }
}
