using System;
using Eden.Domain;
using Eden.Simulation;

namespace Eden.Application
{
    /// <summary>Population metrics for one window of simulated ticks.</summary>
    public readonly struct MetricsSample
    {
        public MetricsSample(long tick, double simulatedSeconds, int population, int foodAvailable, int foodEatenInWindow,
            int starvationDeathsInWindow, int oldAgeDeathsInWindow, float meanEnergy, long totalFoodEaten)
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
    }

    /// <summary>Aggregates per-tick results into fixed windows of simulated time. Call once after every tick.</summary>
    public sealed class MetricsRecorder
    {
        private readonly SimulationConfig config;
        private int foodEaten;
        private int starvationDeaths;
        private int oldAgeDeaths;

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

        /// <summary>Returns true when this tick closed a window and <see cref="LastSample"/> was updated.</summary>
        public bool Record(WorldSimulation simulation)
        {
            if (simulation == null)
                throw new ArgumentNullException(nameof(simulation));

            foodEaten += simulation.LastFoodEaten;
            TotalFoodEaten += simulation.LastFoodEaten;
            foreach (var death in simulation.LastDeaths)
            {
                if (death.Cause == DeathCause.Starvation)
                    starvationDeaths++;
                else if (death.Cause == DeathCause.OldAge)
                    oldAgeDeaths++;
            }

            var world = simulation.World;
            if (world.Tick % WindowTicks != 0)
                return false;

            var energy = 0f;
            foreach (var creature in world.Creatures)
                energy += creature.Energy;
            var population = world.Creatures.Count;

            LastSample = new MetricsSample(world.Tick, (double)world.Tick / config.TicksPerSecond, population,
                world.Food.Count, foodEaten, starvationDeaths, oldAgeDeaths,
                population > 0 ? energy / population : 0f, TotalFoodEaten);
            HasSample = true;
            foodEaten = 0;
            starvationDeaths = 0;
            oldAgeDeaths = 0;
            return true;
        }
    }
}
