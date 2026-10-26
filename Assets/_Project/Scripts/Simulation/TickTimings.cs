using System.Diagnostics;

namespace Eden.Simulation
{
    /// <summary>
    /// Wall-clock cost of the last tick of a <see cref="WorldSimulation"/>, split into sensing (due events, then
    /// observe and sanitize every creature), deciding (the controller, which runs the brains) and the whole tick. Only measured while
    /// assigned to <see cref="WorldSimulation.Timings"/>; it is read by people and never by the rules.
    /// </summary>
    public sealed class TickTimings
    {
        public static readonly double MillisecondsPerTimestamp = 1000.0 / Stopwatch.Frequency;

        /// <summary>Creatures alive at the start of the tick.</summary>
        public int Creatures { get; private set; }

        public double SensorsMilliseconds { get; private set; }
        public double DecisionsMilliseconds { get; private set; }
        public double TotalMilliseconds { get; private set; }

        internal static long Now() => Stopwatch.GetTimestamp();

        internal void Set(int creatures, long start, long sensed, long decided, long end)
        {
            Creatures = creatures;
            SensorsMilliseconds = (sensed - start) * MillisecondsPerTimestamp;
            DecisionsMilliseconds = (decided - sensed) * MillisecondsPerTimestamp;
            TotalMilliseconds = (end - start) * MillisecondsPerTimestamp;
        }
    }
}
