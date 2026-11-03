using System;
using System.Collections.Generic;
using Eden.Domain;
using Eden.Evolution;
using Eden.Simulation;

namespace Eden.Application
{
    /// <summary>Evaluator-only active search state. Grid and IDs never enter a brain or consume random numbers.</summary>
    public sealed class ActiveSearchState
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SearchLayout,
            Dictionary<string, ExplorationGrid>> Masks = new System.Runtime.CompilerServices.ConditionalWeakTable<SearchLayout,
                Dictionary<string, ExplorationGrid>>();
        private ExplorationGrid grid;
        public ActiveSearchOptions Options { get; private set; }
        public int[] Visited = Array.Empty<int>();
        private HashSet<int> visited = new HashSet<int>();

        /// <summary>Eligible cells the senses saw, ascending; empty under rules version 1.</summary>
        public int[] Seen = Array.Empty<int>();

        private bool[] seen;
        private int seenCount;

        /// <summary>Cells swept by the field of view, repetitions included; the denominator of <see cref="Novelty"/>.</summary>
        public long Sightings;

        public int Pending = -1;
        public ulong MaskIdentity;
        public int Goal, Eligible, Initial;

        /// <summary>Cells that must have been seen for a full sight score; 0 under rules version 1.</summary>
        public int SightGoal;
        public bool[] Detected = Array.Empty<bool>();
        public double[] WaterUseful = Array.Empty<double>();
        public long[] ContactTicks = Array.Empty<long>(), SipTicks = Array.Empty<long>(), CollectedTicks = Array.Empty<long>();
        public double[] EnergyAtCollection = Array.Empty<double>(), HydrationAtSip = Array.Empty<double>();
        private double energyBeforeTick, hydrationBeforeTick;

        /// <summary>
        /// Reserves before the tick and, when the creature perceives on it (<paramref name="sensing"/>) and the rules
        /// score what it saw, the cells its senses cover now. The pose is the one the tick's sensing loop will read,
        /// because nothing before it moves the creature.
        /// </summary>
        public void BeforeTick(CreatureState creature, SearchLayout layout, bool sensing)
        {
            energyBeforeTick = creature.Energy; hydrationBeforeTick = creature.Hydration;
            if (sensing && seen != null) Look(creature, layout);
        }

        // What the eyes cover: for each present slot, the cells whose centre lies inside its cone and range
        // (Vision's own geometry, so a cell is seen exactly when the eye could report something there) and that no
        // obstacle hides. A creature with the legacy senses has no cone: its food sense reaches every direction
        // within the sensor range and sees through obstacles, so the sample is that disc.
        private void Look(CreatureState creature, SearchLayout layout)
        {
            var config = layout.Config;
            var eyes = creature.Eyes;
            if (eyes == null)
            {
                Sweep(config, creature.Position, creature.Body.SensorRange, 1f, 0f, -1f, false);
                return;
            }

            for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
            {
                if (!eyes.Has(slot)) continue;
                var eye = eyes[slot];
                var axis = creature.Heading + eye.Orientation;
                Sweep(config, Vision.EyePosition(config, creature, eye), creature.Body.SensorRange * eye.Range,
                    MathF.Cos(axis), MathF.Sin(axis), MathF.Cos(eye.Aperture * 0.5f), true);
            }
        }

        private void Sweep(SimulationConfig config, Float2 origin, float range, float axisX, float axisY, float cosHalf,
            bool occluded)
        {
            if (!(range > 0)) return;
            var side = grid.Side;
            var firstColumn = Math.Max(0, (int)((origin.X - range) / side));
            var lastColumn = Math.Min(grid.Columns - 1, (int)((origin.X + range) / side));
            var firstRow = Math.Max(0, (int)((origin.Y - range) / side));
            var lastRow = Math.Min(grid.Rows - 1, (int)((origin.Y + range) / side));
            for (var row = firstRow; row <= lastRow; row++)
            {
                for (var column = firstColumn; column <= lastColumn; column++)
                {
                    var cell = row * grid.Columns + column;
                    if (!grid.Eligible(cell)) continue;
                    var centre = grid.Centre(cell);
                    if (!Vision.InCone(origin, axisX, axisY, cosHalf, range, centre, out _)) continue;
                    Sightings++;
                    // A cell already credited stays known; only a new one pays for the line of sight test.
                    if (seen[cell] || occluded && !Vision.LineOfSight(config, origin, centre)) continue;
                    seen[cell] = true; seenCount++;
                }
            }
        }
        public double Maintenance, Movement, Turning, Eyes;
        public double InitialEnergy, InitialHydration;
        public int Unique => visited.Count;

        /// <summary>Distinct eligible cells the senses saw.</summary>
        public int SeenUnique => seenCount;

        /// <summary>Progress towards <see cref="SightGoal"/>; 0 under rules version 1.</summary>
        public double Sight => SightGoal > 0 ? Math.Min(1.0, (double)seenCount / SightGoal) : 0;

        /// <summary>Progress towards the goal of cells entered: the whole exploration term under rules version 1.</summary>
        public double Travel => Math.Min(1.0, (double)Unique / Goal);

        /// <summary>Share of the connected mask the senses saw.</summary>
        public double SightCoverage => (double)seenCount / Eligible;

        /// <summary>Share of what the field of view swept that was new; measured only, never scored.</summary>
        public double Novelty => Sightings > 0 ? seenCount / (double)Sightings : 0;

        public double Exploration => Options.Sight
            ? Options.SightWeight * Sight + (1 - Options.SightWeight) * Travel
            : Travel;
        public double Detection { get { var n = 0; foreach (var d in Detected) if (d) n++; return (double)n / Detected.Length; } }
        public double Collection { get { var n = 0; foreach (var t in CollectedTicks) if (t >= 0) n++; return (double)n / Detected.Length; } }
        public double Base => 0.65 * Exploration + 0.35 * Collection;
        public double Multiplier => 1 + 0.5 * Detection + 2 * Collection;
        public float Score => StageScores.ExploreCollect(Exploration, Detection, Collection);
        public ActiveSearchState() { }
        public ActiveSearchState(SearchLayout layout, ActiveSearchOptions options, CreatureState creature)
        {
            Options = options.Clone(); Bind(layout);
            MaskIdentity = grid.Identity; Goal = Math.Min((int)Options.ExplorationGoal, grid.EligibleCount);
            Eligible = grid.EligibleCount; Initial = grid.Initial;
            SightGoal = SightGoalOf(Options, Eligible);
            visited = new HashSet<int>(Eligible);
            if (Options.Sight) seen = new bool[grid.Columns * grid.Rows];
            Detected = new bool[layout.TargetCount]; WaterUseful = new double[layout.Water.Count];
            ContactTicks = EmptyTimes(layout.TargetCount); SipTicks = EmptyTimes(layout.TargetCount);
            CollectedTicks = EmptyTimes(layout.TargetCount);
            EnergyAtCollection = Missing(layout.TargetCount); HydrationAtSip = Missing(layout.TargetCount);
            InitialEnergy = creature.Energy; InitialHydration = creature.Hydration;
        }
        /// <summary>Cells a full sight score requires: the configured share of the connected mask, at least one.</summary>
        public static int SightGoalOf(ActiveSearchOptions options, int eligible) =>
            options.Sight ? Math.Max(1, (int)Math.Round(options.SightGoalFraction * eligible)) : 0;

        private static double[] Missing(int count)
        { var values = new double[count]; for (var i = 0; i < count; i++) values[i] = double.NaN; return values; }
        private static long[] EmptyTimes(int count) { var a = new long[count]; for (var i = 0; i < count; i++) a[i] = -1; return a; }
        private void Bind(SearchLayout layout)
        {
            var cache = Masks.GetValue(layout, _ => new Dictionary<string, ExplorationGrid>());
            var key = Options.CellSide.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            lock (cache)
            {
                if (!cache.TryGetValue(key, out grid))
                    cache[key] = grid = new ExplorationGrid(layout.Config, layout.Founder, (float)Options.CellSide);
            }
        }
        public void Restore(SearchLayout layout, ActiveSearchOptions options, long tick, EpisodeTracker tracker, CreatureState creature)
        {
            Options = options.Clone(); Bind(layout);
            if (MaskIdentity != grid.Identity || Initial != grid.Initial || Eligible != grid.EligibleCount ||
                Goal != Math.Min((int)options.ExplorationGoal, Eligible) || SightGoal != SightGoalOf(Options, Eligible) ||
                Detected.Length != layout.TargetCount ||
                WaterUseful.Length != layout.Water.Count || ContactTicks.Length != layout.TargetCount ||
                SipTicks.Length != layout.TargetCount || CollectedTicks.Length != layout.TargetCount || EnergyAtCollection.Length != layout.TargetCount ||
                HydrationAtSip.Length != layout.TargetCount ||
                Pending < -1 || Pending >= grid.Columns * grid.Rows)
                throw new ArgumentException("Active search mask or target state differs from layout/profile.");
            visited = new HashSet<int>();
            foreach (var cell in Visited)
                if (!grid.Eligible(cell) || !visited.Add(cell)) throw new ArgumentException("Invalid or duplicate exploration cell.");
            if (!Options.Sight)
            {
                if (Seen.Length != 0 || Sightings != 0 || SightGoal != 0)
                    throw new ArgumentException("Cells seen in a state scored by the cells entered.");
                seen = null; seenCount = 0;
            }
            else
            {
                seen = new bool[grid.Columns * grid.Rows]; seenCount = 0;
                foreach (var cell in Seen)
                {
                    if (!grid.Eligible(cell) || seen[cell]) throw new ArgumentException("Invalid or duplicate cell seen.");
                    seen[cell] = true; seenCount++;
                }
                if (Sightings < seenCount) throw new ArgumentException("Fewer sightings than cells seen.");
            }
            foreach (var amount in new[] { Maintenance, Movement, Turning, Eyes, InitialEnergy, InitialHydration })
                if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0) throw new ArgumentException("Invalid active search audit.");
            if (InitialEnergy != Math.Min(layout.Config.InitialEnergy, creature.Body.MaxEnergy) || InitialHydration != layout.Config.MaxHydration)
                throw new ArgumentException("Invalid initial active reserves.");
            var collected = new HashSet<int>();
            for (var i = 0; i < tracker.Collected.Count; i++)
            {
                var t = tracker.Collected[i];
                if (t < 0 || t >= layout.TargetCount || !collected.Add(t) || CollectedTicks[t] != tracker.CollectionTicks[i])
                    throw new ArgumentException("Inconsistent active collection set.");
            }
            for (var i = 0; i < layout.TargetCount; i++)
            {
                if (ContactTicks[i] < -1 || ContactTicks[i] > tick || SipTicks[i] < -1 || SipTicks[i] > tick ||
                    CollectedTicks[i] < -1 || CollectedTicks[i] > tick || collected.Contains(i) != (CollectedTicks[i] >= 0))
                    throw new ArgumentException("Invalid active event time.");
                if ((CollectedTicks[i] >= 0) != !double.IsNaN(EnergyAtCollection[i]) ||
                    !double.IsNaN(EnergyAtCollection[i]) && (EnergyAtCollection[i] < 0 || EnergyAtCollection[i] > creature.Body.MaxEnergy))
                    throw new ArgumentException("Invalid reserve before collection.");
                if (!layout.IsWater(i)) continue;
                if ((SipTicks[i] >= 0) != !double.IsNaN(HydrationAtSip[i]) ||
                    !double.IsNaN(HydrationAtSip[i]) && (HydrationAtSip[i] < 0 || HydrationAtSip[i] > layout.Config.MaxHydration))
                    throw new ArgumentException("Invalid reserve before first sip.");
                var amount = WaterUseful[i - layout.Food.Count];
                if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0 ||
                    (amount > 0) != (SipTicks[i] >= 0) || SipTicks[i] >= 0 &&
                    (ContactTicks[i] < 0 || ContactTicks[i] > SipTicks[i]) ||
                    (amount >= Options.WaterThreshold) != collected.Contains(i))
                    throw new ArgumentException("Invalid per-source useful water.");
            }
            double waterSum = 0;
            foreach (var amount in WaterUseful) waterSum += amount;
            if (waterSum != tracker.Drunk || tracker.AbsorbedEnergy < 0 || tracker.WastedEnergy < 0 ||
                double.IsNaN(tracker.Drunk) || double.IsInfinity(tracker.Drunk))
                throw new ArgumentException("Active water/energy audit totals differ.");
            // Collection without prior detection is valid.
        }
        public void Detect(int resource, bool water, SearchLayout layout)
        {
            var target = water ? layout.Food.Count + resource : resource - 1;
            if (target >= 0 && target < Detected.Length) Detected[target] = true;
        }
        internal void AfterTick(WorldSimulation simulation, CreatureState creature, SearchLayout layout, EpisodeTracker tracker)
        {
            var tick = simulation.World.Tick; var cell = grid.CellAt(creature.Position);
            Pending = cell;
            if (grid.Eligible(cell) && grid.Interior(creature.Position, cell, (float)Options.Hysteresis)) visited.Add(cell);
            var costs = simulation.LastEnergy;
            Maintenance += costs.Maintenance; Movement += costs.Effort; Turning += costs.TurnCosts; Eyes += costs.EyeCosts;
            for (var i = 0; i < layout.Water.Count; i++)
            {
                var target = layout.Food.Count + i;
                if (ContactTicks[target] < 0 && WorldSimulation.ReachesWater(layout.Config, creature, layout.Water[i]))
                    ContactTicks[target] = tick;
            }
            for (var f = 0; f < simulation.LastFoodConsumption.Count; f++)
            {
                var food = simulation.LastFoodConsumption[f];
                if (food.Creature == creature.Id)
                {
                    var t = food.Food.Value - 1;
                    if (t >= 0 && t < layout.Food.Count && CollectedTicks[t] < 0)
                    { ContactTicks[t] = tick; CollectedTicks[t] = tick; EnergyAtCollection[t] = energyBeforeTick; tracker.CollectActive(t, tick); }
                }
            }
            for (var w = 0; w < simulation.LastWaterConsumption.Count; w++)
            {
                var drink = simulation.LastWaterConsumption[w];
                if (drink.Creature == creature.Id)
                {
                    var t = layout.Food.Count + drink.Source;
                    WaterUseful[drink.Source] += drink.Useful;
                    if (SipTicks[t] < 0) { SipTicks[t] = tick; HydrationAtSip[t] = hydrationBeforeTick; }
                    if (CollectedTicks[t] < 0 && WaterUseful[drink.Source] >= Options.WaterThreshold)
                    { CollectedTicks[t] = tick; EnergyAtCollection[t] = energyBeforeTick; tracker.CollectActive(t, tick); }
                }
            }
        }
        public ActiveSearchState Clone()
        {
            var copy = (ActiveSearchState)MemberwiseClone();
            copy.Options = Options?.Clone();
            copy.visited = grid == null ? new HashSet<int>(Visited) : new HashSet<int>(visited);
            copy.Visited = grid == null ? (int[])Visited.Clone() : new int[copy.visited.Count];
            if (grid != null) copy.visited.CopyTo(copy.Visited);
            Array.Sort(copy.Visited);
            if (seen == null)
                copy.Seen = (int[])Seen.Clone();
            else
            {
                copy.seen = (bool[])seen.Clone();
                copy.Seen = new int[seenCount];
                var next = 0;
                for (var cell = 0; cell < seen.Length; cell++)
                    if (seen[cell]) copy.Seen[next++] = cell;
            }
            copy.Detected = (bool[])Detected.Clone(); copy.WaterUseful = (double[])WaterUseful.Clone();
            copy.ContactTicks = (long[])ContactTicks.Clone(); copy.SipTicks = (long[])SipTicks.Clone();
            copy.CollectedTicks = (long[])CollectedTicks.Clone();
            copy.EnergyAtCollection = (double[])EnergyAtCollection.Clone(); copy.HydrationAtSip = (double[])HydrationAtSip.Clone();
            return copy;
        }
    }
}
