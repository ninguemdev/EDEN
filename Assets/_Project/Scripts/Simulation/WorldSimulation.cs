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
        private const float FullTurn = 2f * MathF.PI;
        private static readonly SimulationEvent FoodRespawn = new SimulationEvent(SimulationEventKind.FoodRespawn);

        private readonly TickScheduler<SimulationEvent> agenda;
        private readonly List<ScheduledEvent<SimulationEvent>> dueEvents = new List<ScheduledEvent<SimulationEvent>>();
        private readonly List<CreatureDeath> deaths = new List<CreatureDeath>();
        private readonly List<CreatureBirth> births = new List<CreatureBirth>();
        private Observation[] observations = Array.Empty<Observation>();
        private float[][] eyeInputs = Array.Empty<float[]>();
        private CreatureAction[] actions = Array.Empty<CreatureAction>();
        private float[] foodGains = Array.Empty<float>();
        private readonly Contacts contacts = new Contacts();
        private EnergyFlows energy;

        public WorldSimulation(WorldState world)
            : this(world, new TickScheduler<SimulationEvent>())
        {
        }

        private WorldSimulation(WorldState world, TickScheduler<SimulationEvent> agenda)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            this.agenda = agenda;

            // Room for the most there can be (food is never created, only respawned; creatures are born only up to the
            // cap), so ticks do not allocate as lists grow; a new isolated episode is allocation-free from its first tick.
            var config = world.Config;
            var most = Math.Max(config.Reproduction ? config.PopulationCap : config.InitialPopulation, world.Creatures.Count);
            agenda.Reserve(config.InitialFood);
            dueEvents.Capacity = config.InitialFood;
            deaths.Capacity = most;
            if (config.Reproduction)
                births.Capacity = most;
            EnsureCapacity(most);
            if (config.SolidBodies)
                contacts.Reserve(most);
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

        /// <summary>Creatures born during the last tick, in id order (their parents' order).</summary>
        public IReadOnlyList<CreatureBirth> LastBirths => births;

        /// <summary>Births refused during the last tick because the population was at its cap; nobody paid for them.</summary>
        public int LastBirthsRefused { get; private set; }

        /// <summary>What children inherit; null gives every child its parent's body genes. Never saved: set it again.</summary>
        public IHeredity Heredity { get; set; }

        public int LastFoodEaten { get; private set; }

        /// <summary>Food items put back by the agenda in the last tick.</summary>
        public int LastFoodRespawned { get; private set; }

        /// <summary>Energy flows of the last tick; they explain exactly how the creatures' total energy changed.</summary>
        public EnergyFlows LastEnergy => energy;

        /// <summary>Pairs of solid bodies touching at the end of the last tick; always 0 without solid bodies.</summary>
        public int LastContacts { get; private set; }

        /// <summary>Observation values corrected by <see cref="ObservationLimits.Sanitize"/> in the last tick.</summary>
        public int LastCorrectedInputValues { get; private set; }

        /// <summary>Observation values corrected since the run started; stays 0 while sensors honor the contract.</summary>
        public long CorrectedInputValues { get; private set; }

        public ScheduledEvent<SimulationEvent>[] SnapshotAgenda() => agenda.SnapshotPending();

        /// <summary>When set, receives the cost of every tick; null (the default) measures nothing.</summary>
        public TickTimings Timings { get; set; }

        /// <summary>
        /// Runs one tick: agenda, observe all (checked against <see cref="ObservationLimits"/>), decide in id order,
        /// move (as solid bodies when the config says so, see <see cref="Contacts"/>), eat, energy/age/death, births
        /// (when the config allows reproduction), cleanup.
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
            births.Clear();
            LastBirthsRefused = 0;
            LastFoodEaten = 0;
            LastFoodRespawned = 0;
            LastContacts = 0;
            energy = default;
            LastCorrectedInputValues = 0;

            if (config.FoodShifts && tick == config.FoodShiftTicks)
                ShiftFood(tick);
            RunDueEvents(tick);

            var count = creatures.Count;
            EnsureCapacity(count);
            for (var i = 0; i < count; i++)
            {
                var observed = Sensors.Observe(World, creatures[i]);
                if (creatures[i].Eyes != null)
                    observed = Sensors.WithEyes(observed, World, creatures[i], EyeInputs(i));
                observations[i] = ObservationLimits.Sanitize(observed, out var corrected);
                LastCorrectedInputValues += corrected;
            }

            CorrectedInputValues += LastCorrectedInputValues;
            var sensed = timings != null ? TickTimings.Now() : 0L;
            for (var i = 0; i < count; i++)
                actions[i] = controller.Decide(creatures[i].Id, observations[i]);
            var decided = timings != null ? TickTimings.Now() : 0L;
            if (config.SolidBodies)
            {
                contacts.Move(creatures, actions, count, config);
                LastContacts = contacts.LastContacts;
            }
            else
            {
                for (var i = 0; i < count; i++)
                    Movement.Apply(creatures[i], actions[i], config);
            }

            ResolveFeeding(tick, count);
            ApplyEnergyAndAging(tick, count);
            if (config.Reproduction)
                ApplyBirths(tick, count);

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
                        World.AddFood(WorldFactory.RandomFoodPosition(World.SpawnRandom, World.Config, tick), World.Config.FoodEnergy);
                        LastFoodRespawned++;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown event kind {due.Payload.Kind}.");
                }
            }
        }

        // The changing world: every uneaten item moves, in id order, to a new place in the right half (spawn stream).
        private void ShiftFood(long tick)
        {
            var food = World.Food;
            for (var i = 0; i < food.Count; i++)
                food[i].MoveTo(WorldFactory.RandomFoodPosition(World.SpawnRandom, World.Config, tick));
        }

        // Each food goes to the closest creature within its own eat radius that can reach it (no obstacle between them,
        // see Vision.LineOfSight); equal distance goes to the lower id.
        private void ResolveFeeding(long tick, int count)
        {
            Array.Clear(foodGains, 0, count);
            var creatures = World.Creatures;
            var food = World.Food;
            var blocked = World.Config.Obstacles.Count > 0;

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
                    if (distanceSquared <= eatRadius * eatRadius && distanceSquared < best &&
                        (!blocked || Vision.LineOfSight(World.Config, creatures[c].Position, item.Position)))
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
                float cost;
                if (creature.Eyes == null)
                {
                    cost = config.TickDuration *
                           (body.MetabolismPerSecond + body.MovementCostPerSecond * effort + body.SensorCostPerSecond);
                }
                else
                {
                    // Eyes replace the circular sense and its cost, and turning costs a share of moving.
                    var eyeCost = EyeCost.PerSecond(body, creature.Eyes);
                    var turning = config.TurnCostFraction * action.Turn * action.Turn;
                    cost = config.TickDuration *
                           (body.MetabolismPerSecond + body.MovementCostPerSecond * (effort + turning) + eyeCost);
                    energy.EyeCosts += (double)config.TickDuration * eyeCost;
                    energy.TurnCosts += (double)config.TickDuration * body.MovementCostPerSecond * turning;
                }

                // Kept as one expression: splitting it would round the intermediate sum and change every run.
                var left = creature.Energy + foodGains[i] - cost;
                // A creature pays at most what it holds; food that does not fit is lost.
                energy.Food += foodGains[i];
                energy.Spent += left < 0f ? (double)creature.Energy + foodGains[i] : cost;
                if (left > body.MaxEnergy)
                    energy.Overflow += left - body.MaxEnergy;
                creature.Energy = left < 0f ? 0f : left > body.MaxEnergy ? body.MaxEnergy : left;
                creature.AgeTicks++;
                if (creature.ReproductionCooldownTicks > 0)
                    creature.ReproductionCooldownTicks--;

                var cause = creature.Energy <= 0f ? DeathCause.Starvation
                    : creature.AgeTicks >= config.MaxAgeTicks ? DeathCause.OldAge
                    : DeathCause.None;
                if (cause == DeathCause.None)
                    continue;
                creature.DeathCause = cause;
                energy.CarriedAwayByDeaths += creature.Energy;
                deaths.Add(new CreatureDeath(creature.Id, tick, cause));
            }
        }

        // Parents in id order, children appended (so id order holds). A parent must be alive, mature, out of cooldown and
        // hold the configured share of its capacity, and must keep some energy after paying. At the cap the birth is
        // refused before anything is drawn or paid. The child gets min(share of the parent's energy, its capacity);
        // the parent loses that plus the cost: no energy is created. Placement draws angle then heading from the spawn
        // stream.
        private void ApplyBirths(long tick, int count)
        {
            var config = World.Config;
            var creatures = World.Creatures;
            var alive = count - deaths.Count;
            for (var i = 0; i < count; i++)
            {
                var parent = creatures[i];
                if (!parent.IsAlive || parent.AgeTicks < config.MaturityTicks || parent.ReproductionCooldownTicks > 0 ||
                    parent.Energy < config.ReproductionEnergyFraction * parent.Body.MaxEnergy ||
                    parent.Energy * (1f - config.OffspringEnergyFraction) - config.ReproductionCost <= 0f)
                    continue;
                if (alive >= config.PopulationCap)
                {
                    LastBirthsRefused++;
                    continue;
                }

                var childId = new CreatureId(World.NextCreatureId);
                var genes = Heredity?.Conceive(parent, childId) ?? parent.Genes;
                var capacity = genes.IsNeutral ? World.ReferenceBody.MaxEnergy : CreatureBody.Express(config, genes).MaxEnergy;
                var transfer = Math.Min(parent.Energy * config.OffspringEnergyFraction, capacity);
                parent.Energy -= transfer + config.ReproductionCost;
                parent.ReproductionCooldownTicks = config.ReproductionCooldownTicks;
                energy.Transferred += transfer;
                energy.BirthCosts += config.ReproductionCost;

                var random = World.SpawnRandom;
                var angle = random.NextFloat() * FullTurn;
                var heading = random.NextFloat() * FullTurn;
                var x = Clamp(parent.Position.X + MathF.Cos(angle) * config.BirthDistance, config.ArenaWidth);
                var y = Clamp(parent.Position.Y + MathF.Sin(angle) * config.BirthDistance, config.ArenaHeight);
                var child = World.AddOffspring(parent, new Float2(x, y), heading, transfer, genes);
                if (Heredity != null)
                    child.Eyes = Heredity.EyesOf(childId);
                births.Add(new CreatureBirth(child.Id, parent.Id, tick));
                alive++;
            }
        }

        private static float Clamp(float value, float size) => value < 0f ? 0f : value > size ? size : value;

        private void EnsureCapacity(int count)
        {
            if (observations.Length >= count)
                return;
            var size = Math.Max(count, observations.Length * 2);
            observations = new Observation[size];
            actions = new CreatureAction[size];
            foodGains = new float[size];
            var buffers = new float[size][];
            Array.Copy(eyeInputs, buffers, eyeInputs.Length);
            eyeInputs = buffers;
        }

        // Scratch buffer for the eye inputs of the creature at index i this tick, created once per index.
        private float[] EyeInputs(int index) => eyeInputs[index] ??= new float[NeuralContract.Eyes.InputCount];
    }
}
