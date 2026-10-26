using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Simulation
{
    /// <summary>
    /// Applies the world rules one fixed tick at a time. A tick never depends on frame time, wall-clock time or
    /// global random sources, so the same config, seed, controller and tick count always give the same state.
    /// </summary>
    public sealed class WorldSimulation
    {
        private static readonly SimulationEvent FoodRespawn = new SimulationEvent(SimulationEventKind.FoodRespawn);

        private readonly TickScheduler<SimulationEvent> agenda;
        private readonly List<ScheduledEvent<SimulationEvent>> dueEvents = new List<ScheduledEvent<SimulationEvent>>();
        private readonly List<CreatureDeath> deaths = new List<CreatureDeath>();
        private Observation[] observations = Array.Empty<Observation>();
        private CreatureAction[] actions = Array.Empty<CreatureAction>();
        private float[] foodGains = Array.Empty<float>();

        public WorldSimulation(WorldState world)
            : this(world, new TickScheduler<SimulationEvent>())
        {
        }

        private WorldSimulation(WorldState world, TickScheduler<SimulationEvent> agenda)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            this.agenda = agenda;

            // Room for the most there can be (food is never created, only respawned; creatures are never born), so
            // ticks do not allocate as lists grow; a new isolated episode is allocation-free from its first tick.
            agenda.Reserve(world.Config.InitialFood);
            dueEvents.Capacity = world.Config.InitialFood;
            deaths.Capacity = Math.Max(world.Config.InitialPopulation, world.Creatures.Count);
            EnsureCapacity(world.Creatures.Count);
        }

        /// <summary>
        /// Rebuilds a simulation from <paramref name="saved"/> with the config it ran with. The saved world is checked by
        /// <see cref="WorldState.Restore"/> and the agenda by <see cref="TickScheduler{T}.Restore"/>; on any problem
        /// nothing is built and an <see cref="ArgumentException"/> explains why.
        /// </summary>
        public static WorldSimulation Restore(SimulationConfig config, SimulationSnapshot saved)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (saved == null)
                throw new ArgumentNullException(nameof(saved));
            if (saved.CorrectedInputValues < 0)
                throw new ArgumentException("The corrected input count must not be negative.", nameof(saved));
            foreach (var pending in saved.Agenda)
            {
                if (pending.Payload.Kind != SimulationEventKind.FoodRespawn)
                    throw new ArgumentException($"Unknown event kind {pending.Payload.Kind}.", nameof(saved));
            }

            var world = WorldState.Restore(config, saved.Tick, saved.NextCreatureId, saved.NextFoodId, saved.WorldRandom,
                saved.SpawnRandom, saved.Creatures, saved.Food);
            var agenda = TickScheduler<SimulationEvent>.Restore(saved.Agenda, saved.NextEventSequence, saved.Tick - 1);
            return new WorldSimulation(world, agenda) { CorrectedInputValues = saved.CorrectedInputValues };
        }

        /// <summary>Detached copy of the state between ticks; later steps never change it.</summary>
        public SimulationSnapshot Capture() =>
            new SimulationSnapshot(World.Tick, World.NextCreatureId, World.NextFoodId, World.WorldRandom.State,
                World.SpawnRandom.State, World.Creatures, World.Food, agenda.SnapshotPending(), agenda.NextSequence,
                CorrectedInputValues);

        public WorldState World { get; }
        public bool IsExtinct => World.Creatures.Count == 0;

        /// <summary>Creatures that died during the last tick, in id order.</summary>
        public IReadOnlyList<CreatureDeath> LastDeaths => deaths;

        public int LastFoodEaten { get; private set; }

        /// <summary>Observation values corrected by <see cref="ObservationLimits.Sanitize"/> in the last tick.</summary>
        public int LastCorrectedInputValues { get; private set; }

        /// <summary>Observation values corrected since the run started; stays 0 while sensors honor the contract.</summary>
        public long CorrectedInputValues { get; private set; }

        public ScheduledEvent<SimulationEvent>[] SnapshotAgenda() => agenda.SnapshotPending();

        /// <summary>When set, receives the cost of every tick; null (the default) measures nothing.</summary>
        public TickTimings Timings { get; set; }

        /// <summary>
        /// Runs one tick: agenda, observe all (checked against <see cref="ObservationLimits"/>), decide in id order,
        /// move, eat, energy/age/death, cleanup.
        /// </summary>
        public void Step(ICreatureController controller)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));

            var timings = Timings;
            var start = timings != null ? TickTimings.Now() : 0L;
            var config = World.Config;
            var tick = World.Tick;
            var creatures = World.Creatures;
            deaths.Clear();
            LastFoodEaten = 0;
            LastCorrectedInputValues = 0;

            RunDueEvents(tick);

            var count = creatures.Count;
            EnsureCapacity(count);
            for (var i = 0; i < count; i++)
            {
                observations[i] = ObservationLimits.Sanitize(Sensors.Observe(World, creatures[i]), out var corrected);
                LastCorrectedInputValues += corrected;
            }

            CorrectedInputValues += LastCorrectedInputValues;
            var sensed = timings != null ? TickTimings.Now() : 0L;
            for (var i = 0; i < count; i++)
                actions[i] = controller.Decide(creatures[i].Id, observations[i]);
            var decided = timings != null ? TickTimings.Now() : 0L;
            for (var i = 0; i < count; i++)
                Movement.Apply(creatures[i], actions[i], config);

            ResolveFeeding(tick, count);
            ApplyEnergyAndAging(tick, count);

            World.RemoveEatenFood();
            World.RemoveDeadCreatures();
            World.AdvanceTick();
            timings?.Set(count, start, sensed, decided, TickTimings.Now());
        }

        /// <summary>World fingerprint extended with the pending agenda.</summary>
        public ulong ComputeFingerprint()
        {
            var hash = StateHasher.Create();
            hash.Add(WorldFingerprint.Compute(World));
            hash.Add(agenda.NextSequence);
            hash.Add(agenda.LastDrainedTick);
            foreach (var pending in agenda.SnapshotPending())
            {
                hash.Add(pending.DueTick);
                hash.Add(pending.Sequence);
                hash.Add((ulong)pending.Payload.Kind);
            }

            return hash.Value;
        }

        private void RunDueEvents(long tick)
        {
            dueEvents.Clear();
            agenda.DrainDue(tick, dueEvents);
            foreach (var due in dueEvents)
            {
                switch (due.Payload.Kind)
                {
                    case SimulationEventKind.FoodRespawn:
                        World.AddFood(WorldFactory.RandomArenaPosition(World.SpawnRandom, World.Config), World.Config.FoodEnergy);
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown event kind {due.Payload.Kind}.");
                }
            }
        }

        // Each food goes to the closest creature within its own eat radius; equal distance goes to the lower id.
        private void ResolveFeeding(long tick, int count)
        {
            Array.Clear(foodGains, 0, count);
            var creatures = World.Creatures;
            var food = World.Food;

            for (var f = 0; f < food.Count; f++)
            {
                var item = food[f];
                var winner = -1;
                var best = float.MaxValue;
                for (var c = 0; c < count; c++)
                {
                    var dx = creatures[c].Position.X - item.Position.X;
                    var dy = creatures[c].Position.Y - item.Position.Y;
                    var distanceSquared = dx * dx + dy * dy;
                    var eatRadius = creatures[c].Body.EatRadius;
                    if (distanceSquared <= eatRadius * eatRadius && distanceSquared < best)
                    {
                        winner = c;
                        best = distanceSquared;
                    }
                }

                if (winner < 0)
                    continue;

                item.MarkEaten();
                foodGains[winner] += item.Energy;
                creatures[winner].FoodEnergyEaten += item.Energy;
                LastFoodEaten++;
                agenda.Schedule(tick + World.Config.FoodRespawnTicks, FoodRespawn);
            }
        }

        private void ApplyEnergyAndAging(long tick, int count)
        {
            var config = World.Config;
            var creatures = World.Creatures;
            for (var i = 0; i < count; i++)
            {
                var creature = creatures[i];
                var action = actions[i];
                var effort = action.Thrust * action.Thrust + action.Strafe * action.Strafe;
                var body = creature.Body;
                var cost = config.TickDuration *
                           (body.MetabolismPerSecond + body.MovementCostPerSecond * effort + body.SensorCostPerSecond);
                var energy = creature.Energy + foodGains[i] - cost;
                creature.Energy = energy < 0f ? 0f : energy > body.MaxEnergy ? body.MaxEnergy : energy;
                creature.AgeTicks++;

                var cause = creature.Energy <= 0f ? DeathCause.Starvation
                    : creature.AgeTicks >= config.MaxAgeTicks ? DeathCause.OldAge
                    : DeathCause.None;
                if (cause == DeathCause.None)
                    continue;
                creature.DeathCause = cause;
                deaths.Add(new CreatureDeath(creature.Id, tick, cause));
            }
        }

        private void EnsureCapacity(int count)
        {
            if (observations.Length >= count)
                return;
            var size = Math.Max(count, observations.Length * 2);
            observations = new Observation[size];
            actions = new CreatureAction[size];
            foodGains = new float[size];
        }
    }
}
