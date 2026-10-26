using System;
using Eden.Simulation;

namespace Eden.Application
{
    /// <summary>
    /// Durations in milliseconds counted in logarithmic bins (100 per decade, from 0.0001 ms to 10 s), so quantiles of
    /// any number of samples cost a fixed array and no allocation. A quantile is the geometric middle of its bin, within
    /// about 1.2% of the true value; values outside the range fall into the first or last bin.
    /// </summary>
    public sealed class DurationHistogram
    {
        private const double MinMilliseconds = 1e-4;
        private const int BinsPerDecade = 100;
        private const int Decades = 8;

        private readonly long[] bins = new long[BinsPerDecade * Decades];

        public long Count { get; private set; }
        public double Sum { get; private set; }
        public double Max { get; private set; }
        public double Mean => Count > 0 ? Sum / Count : 0.0;

        public void Add(double milliseconds)
        {
            if (double.IsNaN(milliseconds) || milliseconds < 0.0)
                milliseconds = 0.0;
            var bin = milliseconds <= MinMilliseconds
                ? 0
                : (int)(Math.Log10(milliseconds / MinMilliseconds) * BinsPerDecade);
            bins[Math.Min(bin, bins.Length - 1)]++;
            Count++;
            Sum += milliseconds;
            Max = Math.Max(Max, milliseconds);
        }

        /// <summary>Value below which a fraction <paramref name="q"/> of the samples lie; 0 without samples.</summary>
        public double Quantile(double q)
        {
            if (Count == 0)
                return 0.0;
            var rank = Math.Max(1L, (long)Math.Ceiling(q * Count));
            var seen = 0L;
            for (var i = 0; i < bins.Length; i++)
            {
                seen += bins[i];
                if (seen >= rank)
                    return Math.Min(Max, MinMilliseconds * Math.Pow(10.0, (i + 0.5) / BinsPerDecade));
            }

            return Max;
        }

        public double Median => Quantile(0.5);
        public double P95 => Quantile(0.95);
    }

    /// <summary>
    /// Frame and tick costs of a lab run while it is measured: frame time, simulation time and ticks per frame and, when
    /// the simulation reports <see cref="TickTimings"/> (the shared world), the cost of every tick and of its sensing and
    /// deciding phases. Wall-clock only; nothing here feeds back into the run.
    /// </summary>
    public sealed class PerformanceRecorder
    {
        public DurationHistogram Frames { get; } = new DurationHistogram();

        /// <summary>Time spent simulating in each frame (all of that frame's ticks).</summary>
        public DurationHistogram SimulationPerFrame { get; } = new DurationHistogram();

        public DurationHistogram Ticks { get; } = new DurationHistogram();
        public DurationHistogram Sensors { get; } = new DurationHistogram();
        public DurationHistogram Decisions { get; } = new DurationHistogram();

        public long FrameCount { get; private set; }

        /// <summary>Ticks simulated by the measured frames.</summary>
        public long TickCount { get; private set; }

        /// <summary>Sum over the timed ticks of the creatures alive at their start.</summary>
        public long CreatureTicks { get; private set; }

        public double ElapsedSeconds { get; private set; }
        public double SimulationSeconds { get; private set; }

        public double FramesPerSecond => ElapsedSeconds > 0 ? FrameCount / ElapsedSeconds : 0.0;
        public double TicksPerSecond => ElapsedSeconds > 0 ? TickCount / ElapsedSeconds : 0.0;

        /// <summary>Mean simulation cost of one tick, from the frame totals (also without per-tick timings).</summary>
        public double MeanTickMilliseconds => TickCount > 0 ? SimulationSeconds * 1000.0 / TickCount : 0.0;

        /// <summary>Mean creatures per timed tick; 0 when no tick was timed.</summary>
        public double MeanCreatures => Ticks.Count > 0 ? (double)CreatureTicks / Ticks.Count : 0.0;

        public void AddFrame(double frameSeconds, long ticks, double simulationMilliseconds)
        {
            Frames.Add(frameSeconds * 1000.0);
            SimulationPerFrame.Add(simulationMilliseconds);
            FrameCount++;
            TickCount += ticks;
            ElapsedSeconds += frameSeconds;
            SimulationSeconds += simulationMilliseconds / 1000.0;
        }

        public void AddTick(TickTimings timings)
        {
            if (timings == null)
                throw new ArgumentNullException(nameof(timings));
            Ticks.Add(timings.TotalMilliseconds);
            Sensors.Add(timings.SensorsMilliseconds);
            Decisions.Add(timings.DecisionsMilliseconds);
            CreatureTicks += timings.Creatures;
        }
    }
}
