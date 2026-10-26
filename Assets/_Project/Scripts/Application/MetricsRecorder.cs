using System;
using Eden.Domain;
using Eden.Simulation;

namespace Eden.Application
{
    /// <summary>Population metrics for one window of simulated ticks.</summary>
    public readonly struct MetricsSample
    {
        public MetricsSample(long tick, double simulatedSeconds, int population, int foodAvailable, int foodEatenInWindow,
            int starvationDeathsInWindow, int oldAgeDeathsInWindow, float meanEnergy, long totalFoodEaten,
            int birthsInWindow = 0, int birthsRefusedInWindow = 0, int maxGeneration = 0, bool extinct = false)
        {
            Tick = tick;
            SimulatedSeconds = simulatedSeconds;
            Population = population;
            FoodAvailable = foodAvailable;
            FoodEatenInWindow = foodEatenInWindow;
            StarvationDeathsInWindow = starvationDeathsInWindow;
            OldAgeDeathsInWindow = oldAgeDeathsInWindow;
            MeanEnergy = meanEnergy;
            TotalFoodEaten = totalFoodEaten;
            BirthsInWindow = birthsInWindow;
            BirthsRefusedInWindow = birthsRefusedInWindow;
            MaxGeneration = maxGeneration;
            Extinct = extinct;
        }

        /// <summary>Ticks completed when the window closed.</summary>
        public long Tick { get; }

        public double SimulatedSeconds { get; }
        public int Population { get; }
        public int FoodAvailable { get; }
        public int FoodEatenInWindow { get; }
        public int StarvationDeathsInWindow { get; }
        public int OldAgeDeathsInWindow { get; }

        /// <summary>Mean energy of living creatures; 0 when the population is extinct.</summary>
        public float MeanEnergy { get; }

        public long TotalFoodEaten { get; }
        public int BirthsInWindow { get; }

        /// <summary>Births refused at the population cap (nobody paid for them).</summary>
        public int BirthsRefusedInWindow { get; }

        /// <summary>Highest genealogical generation among the living; 0 without reproduction.</summary>
        public int MaxGeneration { get; }

        /// <summary>The last creature died in this window; it closed at once and no other follows.</summary>
        public bool Extinct { get; }
    }

    /// <summary>
    /// Aggregates per-tick results into fixed windows of simulated time. Call once after every tick. The tick the last
    /// creature dies closes its window early, marked <see cref="MetricsSample.Extinct"/>, and sets <see cref="ExtinctionTick"/>.
    /// </summary>
    public sealed class MetricsRecorder
    {
        private readonly SimulationConfig config;
        private int foodEaten;
        private int starvationDeaths;
        private int oldAgeDeaths;
        private int births;
        private int birthsRefused;

        public MetricsRecorder(SimulationConfig config, int windowTicks)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            if (windowTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(windowTicks), "Must be positive.");
            WindowTicks = windowTicks;
        }

        public int WindowTicks { get; }
        public long TotalFoodEaten { get; private set; }
        public bool HasSample { get; private set; }
        public MetricsSample LastSample { get; private set; }

        /// <summary>Tick during which the population died out; null while anyone lives (or nobody ever did).</summary>
        public long? ExtinctionTick { get; private set; }

        /// <summary>Returns true when this tick closed a window and <see cref="LastSample"/> was updated.</summary>
        public bool Record(WorldSimulation simulation)
        {
            if (simulation == null)
                throw new ArgumentNullException(nameof(simulation));

            foodEaten += simulation.LastFoodEaten;
            TotalFoodEaten += simulation.LastFoodEaten;
            births += simulation.LastBirths.Count;
            birthsRefused += simulation.LastBirthsRefused;
            var deaths = simulation.LastDeaths;
            for (var i = 0; i < deaths.Count; i++)
            {
                var death = deaths[i];
                if (death.Cause == DeathCause.Starvation)
                    starvationDeaths++;
                else if (death.Cause == DeathCause.OldAge)
                    oldAgeDeaths++;
            }

            var world = simulation.World;
            var extinct = world.Creatures.Count == 0 && deaths.Count > 0;
            if (extinct)
                ExtinctionTick = deaths[0].Tick;
            if (world.Tick % WindowTicks != 0 && !extinct)
                return false;

            var energy = 0f;
            var generation = 0;
            for (var i = 0; i < world.Creatures.Count; i++)
            {
                energy += world.Creatures[i].Energy;
                generation = Math.Max(generation, world.Creatures[i].Generation);
            }

            var population = world.Creatures.Count;
            LastSample = new MetricsSample(world.Tick, (double)world.Tick / config.TicksPerSecond, population,
                world.Food.Count, foodEaten, starvationDeaths, oldAgeDeaths,
                population > 0 ? energy / population : 0f, TotalFoodEaten, births, birthsRefused, generation, extinct);
            HasSample = true;
            foodEaten = 0;
            starvationDeaths = 0;
            oldAgeDeaths = 0;
            births = 0;
            birthsRefused = 0;
            return true;
        }
    }
}
