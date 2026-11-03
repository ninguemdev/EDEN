using System;
using System.Collections.Generic;
using Eden.Domain;
using Eden.Evolution;
using Eden.Simulation;

namespace Eden.Application
{
    /// <summary>
    /// What a scoring other than the legacy one measures during an episode, tick by tick: energy absorbed and wasted,
    /// water drunk, costs not charged and, in a search stage, each target reached (when, in what order, how fast) and the
    /// best approach to the nearest target left since the last one. Saved with the episode, since none of it can be read
    /// back from the world.
    /// </summary>
    public sealed class EpisodeTracker
    {
        public ActiveSearchState Active { get; set; }
        private readonly List<int> collected = new List<int>(SearchTargets.MaxTargets);
        private readonly List<long> ticks = new List<long>(SearchTargets.MaxTargets);
        private readonly List<float> speeds = new List<float>(SearchTargets.MaxTargets);

        public EpisodeTracker()
        {
        }

        public EpisodeTracker(double absorbedEnergy, double wastedEnergy, double drunk, double unchargedEnergy,
            IReadOnlyList<int> collected, IReadOnlyList<long> ticks, IReadOnlyList<float> speeds, long segmentStartTick,
            Float2 segmentStart, float segmentDistance, float bestDistance)
        {
            if (collected == null || ticks == null || speeds == null || ticks.Count != collected.Count ||
                speeds.Count != collected.Count)
                throw new ArgumentException("Each target reached needs its tick and speed.");
            AbsorbedEnergy = absorbedEnergy;
            WastedEnergy = wastedEnergy;
            Drunk = drunk;
            UnchargedEnergy = unchargedEnergy;
            this.collected.AddRange(collected);
            this.ticks.AddRange(ticks);
            this.speeds.AddRange(speeds);
            SegmentStartTick = segmentStartTick;
            SegmentStart = segmentStart;
            SegmentDistance = segmentDistance;
            BestDistance = bestDistance;
        }

        /// <summary>Food energy that stayed in the creature (eaten minus what did not fit).</summary>
        public double AbsorbedEnergy { get; private set; }

        public double WastedEnergy { get; private set; }
        public double Drunk { get; private set; }

        /// <summary>Costs the search rules did not charge.</summary>
        public double UnchargedEnergy { get; private set; }

        /// <summary>Targets reached, in the order they were reached.</summary>
        public IReadOnlyList<int> Collected => collected;

        /// <summary>Tick at the end of which each target was reached.</summary>
        public IReadOnlyList<long> CollectionTicks => ticks;

        /// <summary>Speed of each reach (<see cref="StageScores.Speed"/>).</summary>
        public IReadOnlyList<float> Speeds => speeds;

        /// <summary>Where and when the open stretch began (the founder, then the place of the last reach).</summary>
        public long SegmentStartTick { get; private set; }

        public Float2 SegmentStart { get; private set; }

        /// <summary>Path length from the stretch's start to the nearest target left then.</summary>
        public float SegmentDistance { get; private set; }

        /// <summary>Shortest path to the nearest target left reached during the stretch.</summary>
        public float BestDistance { get; private set; }

        public bool IsCollected(int target) => collected.Contains(target);

        public EpisodeTracker Clone() =>
            new EpisodeTracker(AbsorbedEnergy, WastedEnergy, Drunk, UnchargedEnergy, collected, ticks, speeds,
                SegmentStartTick, SegmentStart, SegmentDistance, BestDistance) { Active = Active?.Clone() };

        internal void Begin(SearchLayout layout, Float2 founder)
        {
            SegmentStart = founder;
            SegmentStartTick = 0;
            SegmentDistance = Nearest(layout, founder);
            BestDistance = SegmentDistance;
        }

        internal void AfterTick(WorldSimulation simulation, CreatureState creature, SearchLayout layout)
        {
            var energy = simulation.LastEnergy;
            AbsorbedEnergy += energy.Absorbed;
            WastedEnergy += energy.Overflow;
            UnchargedEnergy += energy.Uncharged;
            Drunk += simulation.LastHydration.Drunk;
            if (Active != null)
            {
                Active.AfterTick(simulation, creature, layout, this);
                return;
            }
            if (layout == null || !creature.IsAlive)
                return;

            var world = simulation.World;
            var reached = false;
            for (var target = 0; target < layout.TargetCount; target++)
            {
                if (IsCollected(target))
                    continue;
                var now = layout.IsWater(target)
                    ? WorldSimulation.ReachesWater(world.Config, creature, layout.Water[target - layout.Food.Count])
                    : !FoodLeft(world, new FoodId(target + 1));
                if (!now)
                    continue;
                var seconds = Math.Max(1L, world.Tick - SegmentStartTick) * (double)world.Config.TickDuration;
                speeds.Add(StageScores.Speed(layout.DistanceTo(target, SegmentStart), creature.Body.MaxSpeed, seconds));
                collected.Add(target);
                ticks.Add(world.Tick);
                SegmentStart = creature.Position;
                SegmentStartTick = world.Tick;
                reached = true;
            }

            if (reached)
            {
                SegmentDistance = Nearest(layout, creature.Position);
                BestDistance = SegmentDistance;
            }
            else
            {
                BestDistance = Math.Min(BestDistance, Nearest(layout, creature.Position));
            }
        }

        internal void CollectActive(int target, long tick)
        {
            if (IsCollected(target)) return;
            collected.Add(target); ticks.Add(tick); speeds.Add(0);
        }

        // Path length to the nearest target not reached yet; 0 once all are.
        private float Nearest(SearchLayout layout, Float2 position)
        {
            var best = float.PositiveInfinity;
            for (var target = 0; target < layout.TargetCount; target++)
            {
                if (!IsCollected(target))
                    best = Math.Min(best, layout.DistanceTo(target, position));
            }

            return float.IsPositiveInfinity(best) && collected.Count == layout.TargetCount ? 0f : best;
        }

        private static bool FoodLeft(WorldState world, FoodId id)
        {
            foreach (var item in world.Food)
            {
                if (item.Id.Equals(id))
                    return !item.IsEaten;
            }

            return false;
        }
    }
}
