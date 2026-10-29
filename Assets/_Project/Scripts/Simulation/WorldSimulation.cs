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
        private const float BirthGap = 1e-3f;
        private static readonly SimulationEvent FoodRespawn = new SimulationEvent(SimulationEventKind.FoodRespawn);

        private readonly TickScheduler<SimulationEvent> agenda;
        private readonly List<ScheduledEvent<SimulationEvent>> dueEvents = new List<ScheduledEvent<SimulationEvent>>();
        private readonly List<CreatureDeath> deaths = new List<CreatureDeath>();
        private readonly List<CreatureBirth> births = new List<CreatureBirth>();
        private readonly List<BirthRefusal> refusals = new List<BirthRefusal>();
        private Observation[] observations = Array.Empty<Observation>();
        private float[][] eyeInputs = Array.Empty<float[]>();
        private CreatureAction[] actions = Array.Empty<CreatureAction>();
        private float[] foodGains = Array.Empty<float>();
        private float[] drunk = Array.Empty<float>();
        private float[] thirst = Array.Empty<float>();
        private readonly Contacts contacts = new Contacts();
        private EnergyFlows energy;
        private HydrationFlows hydration;

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
            {
                births.Capacity = most;
                refusals.Capacity = most;
            }

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
                saved.SpawnRandom, saved.Creatures, saved.Food, saved.WaterReserves);
            var agenda = TickScheduler<SimulationEvent>.Restore(saved.Agenda, saved.NextEventSequence, saved.Tick - 1);
            return new WorldSimulation(world, agenda) { CorrectedInputValues = saved.CorrectedInputValues };
        }

        /// <summary>Detached copy of the state between ticks; later steps never change it.</summary>
        public SimulationSnapshot Capture() =>
            new SimulationSnapshot(World.Tick, World.NextCreatureId, World.NextFoodId, World.WorldRandom.State,
                World.SpawnRandom.State, World.Creatures, World.Food, agenda.SnapshotPending(), agenda.NextSequence,
                CorrectedInputValues, World.WaterReserves);

        public WorldState World { get; }
        public bool IsExtinct => World.Creatures.Count == 0;

        /// <summary>Creatures that died during the last tick, in id order.</summary>
        public IReadOnlyList<CreatureDeath> LastDeaths => deaths;

        /// <summary>Creatures born during the last tick, in id order (their parents' order).</summary>
        public IReadOnlyList<CreatureBirth> LastBirths => births;

        /// <summary>Births refused during the last tick because the population was at its cap; nobody paid for them.</summary>
        public int LastBirthsRefused { get; private set; }

        /// <summary>
        /// Births refused during the last tick because no room was found near the parent (solid bodies); nobody paid for
        /// them and nobody was placed far away instead.
        /// </summary>
        public int LastBirthsBlocked { get; private set; }

        /// <summary>
        /// Every birth refused during the last tick, cap and room alike, with its parent and reason, in the parents' id
        /// order. Only a record: nothing in the world depends on it.
        /// </summary>
        public IReadOnlyList<BirthRefusal> LastRefusedBirths => refusals;

        /// <summary>What children inherit; null gives every child its parent's body genes. Never saved: set it again.</summary>
        public IHeredity Heredity { get; set; }

        public int LastFoodEaten { get; private set; }

        /// <summary>Food items put back by the agenda in the last tick.</summary>
        public int LastFoodRespawned { get; private set; }

        /// <summary>
        /// Respawns due in the last tick that found no free place (solid bodies) and wait for the next tick, kept in the
        /// agenda: food is neither lost nor multiplied.
        /// </summary>
        public int LastFoodRespawnsDelayed { get; private set; }

        /// <summary>Energy flows of the last tick; they explain exactly how the creatures' total energy changed.</summary>
        public EnergyFlows LastEnergy => energy;

        /// <summary>Water flows of the last tick (all zero without water); see <see cref="HydrationFlows"/>.</summary>
        public HydrationFlows LastHydration => hydration;

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
        /// move (as solid bodies when the config says so, see <see cref="Contacts"/>), eat, drink (with water),
        /// energy/hydration/age/death, births (when the config allows reproduction), cleanup.
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
            refusals.Clear();
            LastBirthsRefused = 0;
            LastBirthsBlocked = 0;
            LastFoodEaten = 0;
            LastFoodRespawned = 0;
            LastFoodRespawnsDelayed = 0;
            LastContacts = 0;
            energy = default;
            hydration = default;
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
            if (config.HasWater)
                ResolveDrinking(count);
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
                    case SimulationEventKind.FoodRespawn when !World.Config.SolidBodies:
                        World.AddFood(WorldFactory.RandomFoodPosition(World.SpawnRandom, World.Config, tick), World.Config.FoodEnergy);
                        LastFoodRespawned++;
                        break;
                    case SimulationEventKind.FoodRespawn:
                        if (Placement.TryPlaceFood(World.SpawnRandom, World.Config, World.Creatures, tick, out var place))
                        {
                            World.AddFood(place, World.Config.FoodEnergy);
                            LastFoodRespawned++;
                        }
                        else
                        {
                            agenda.Schedule(tick + 1, FoodRespawn);
                            LastFoodRespawnsDelayed++;
                        }

                        break;
                    default:
                        throw new InvalidOperationException($"Unknown event kind {due.Payload.Kind}.");
                }
            }
        }

        // The changing world: every uneaten item moves, in id order, to a new place in the right half (spawn stream).
        // With solid bodies an item that finds no free place there stays where it is.
        private void ShiftFood(long tick)
        {
            var food = World.Food;
            for (var i = 0; i < food.Count; i++)
            {
                if (!World.Config.SolidBodies)
                    food[i].MoveTo(WorldFactory.RandomFoodPosition(World.SpawnRandom, World.Config, tick));
                else if (Placement.TryPlaceFood(World.SpawnRandom, World.Config, World.Creatures, tick, out var place))
                    food[i].MoveTo(place);
            }
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

        // Each source in order: a limited one first recharges, up to its capacity. Then every creature whose eating reach
        // touches the source's edge, with no obstacle in between, asks for what it can still take this tick: up to the
        // drink rate (less what it drank elsewhere this tick), never beyond full. A source holding enough gives everyone
        // what it asked; otherwise each gets the same fraction of its ask (what the source holds over all that was asked),
        // so the split depends on neither ids nor order. Water gives no energy.
        private void ResolveDrinking(int count)
        {
            var config = World.Config;
            var creatures = World.Creatures;
            var water = config.Water;
            var perTick = config.DrinkRatePerSecond * config.TickDuration;
            Array.Clear(drunk, 0, count);
            for (var s = 0; s < water.Count; s++)
            {
                var source = water[s];
                var reserve = World.WaterReserves[s];
                if (!source.Inexhaustible)
                {
                    var recharged = Math.Min(source.Capacity, reserve + source.RechargePerSecond * config.TickDuration);
                    hydration.Recharged += recharged - reserve;
                    reserve = recharged;
                }

                var asked = 0.0;
                for (var c = 0; c < count; c++)
                {
                    var creature = creatures[c];
                    thirst[c] = Reaches(config, creature, source.Area)
                        ? Math.Min(perTick - drunk[c], config.MaxHydration - creature.Hydration)
                        : 0f;
                    if (thirst[c] > 0f)
                        asked += thirst[c];
                }

                if (asked > 0.0)
                {
                    var share = source.Inexhaustible || reserve >= asked ? 1f : (float)(reserve / asked);
                    var given = 0.0;
                    for (var c = 0; c < count; c++)
                    {
                        if (!(thirst[c] > 0f))
                            continue;
                        var creature = creatures[c];
                        var before = creature.Hydration;
                        creature.Hydration = Math.Min(config.MaxHydration, before + thirst[c] * share);
                        drunk[c] += creature.Hydration - before;
                        given += creature.Hydration - before;
                    }

                    hydration.Drunk += given;
                    if (source.Inexhaustible)
                    {
                        hydration.DrunkFromInexhaustible += given;
                    }
                    else
                    {
                        var left = Math.Max(0f, (float)(reserve - given));
                        hydration.TakenFromSources += reserve - left;
                        reserve = left;
                    }
                }

                World.SetWaterReserve(s, reserve);
            }
        }

        // The edge of the water within the creature's eating reach, with no obstacle between its centre and that point.
        private static bool Reaches(SimulationConfig config, CreatureState creature, Obstacle area)
        {
            var position = creature.Position;
            if (area.DistanceTo(position) > creature.Body.EatRadius)
                return false;
            var nearest = new Float2(Math.Min(area.MaxX, Math.Max(area.MinX, position.X)),
                Math.Min(area.MaxY, Math.Max(area.MinY, position.Y)));
            return config.Obstacles.Count == 0 || Vision.LineOfSight(config, position, nearest);
        }

        private void ApplyEnergyAndAging(long tick, int count)
        {
            var config = World.Config;
            var creatures = World.Creatures;
            var hasWater = config.HasWater;
            var thirstPerTick = config.HydrationLossPerSecond * config.TickDuration;
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
                if (hasWater)
                {
                    // Time takes water whatever the creature does; it cannot lose more than it holds.
                    var held = creature.Hydration;
                    var remaining = held - thirstPerTick;
                    hydration.Lost += remaining < 0f ? held : thirstPerTick;
                    creature.Hydration = remaining < 0f ? 0f : remaining;
                }

                var cause = creature.Energy <= 0f ? DeathCause.Starvation
                    : hasWater && creature.Hydration <= 0f ? DeathCause.Dehydration
                    : creature.AgeTicks >= config.MaxAgeTicks ? DeathCause.OldAge
                    : DeathCause.None;
                if (cause == DeathCause.None)
                    continue;
                creature.DeathCause = cause;
                energy.CarriedAwayByDeaths += creature.Energy;
                if (hasWater)
                    hydration.CarriedAwayByDeaths += creature.Hydration;
                deaths.Add(new CreatureDeath(creature.Id, tick, cause));
            }
        }

        // Parents in id order, children appended (so id order holds). A parent must be alive, mature, out of cooldown and
        // hold the configured share of its capacity, and must keep some energy after paying. At the cap the birth is
        // refused before anything is drawn or paid. The child gets min(share of the parent's energy, its capacity);
        // the parent loses that plus the cost: no energy is created. With water the child also gets the same share of the
        // parent's hydration, which the parent loses. Placement draws angle then heading from the spawn
        // stream. With solid bodies the child is conceived first, a place for its own disc is looked for next to the
        // parent and only then is anything paid; without room the birth is refused for free and the child forgotten.
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
                    refusals.Add(new BirthRefusal(parent.Id, tick, BirthRefusalReason.PopulationCap));
                    continue;
                }

                var childId = new CreatureId(World.NextCreatureId);
                var genes = Heredity?.Conceive(parent, childId) ?? parent.Genes;
                if (config.SolidBodies)
                {
                    if (!SolidBirth(tick, parent, childId, genes))
                    {
                        Heredity?.Cancel(childId);
                        LastBirthsBlocked++;
                        refusals.Add(new BirthRefusal(parent.Id, tick, BirthRefusalReason.NoRoom));
                        continue;
                    }

                    alive++;
                    continue;
                }

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
                if (config.HasWater)
                    ShareHydration(parent, child);
                births.Add(new CreatureBirth(child.Id, parent.Id, tick));
                alive++;
            }
        }

        // Up to BirthAttempts directions around the parent, the child's disc just clear of the parent's (or at the birth
        // distance if farther); the first where it fits wins, then the heading is drawn and the birth is paid. The small
        // gap keeps rounding from reading a touching child as overlapping its parent.
        private bool SolidBirth(long tick, CreatureState parent, CreatureId childId, BodyGenes genes)
        {
            var config = World.Config;
            var body = genes.IsNeutral ? World.ReferenceBody : CreatureBody.Express(config, genes);
            var distance = Math.Max(config.BirthDistance, parent.Body.Radius + body.Radius + BirthGap);
            var random = World.SpawnRandom;
            var found = false;
            var place = default(Float2);
            for (var attempt = 0; attempt < Placement.BirthAttempts && !found; attempt++)
            {
                var angle = random.NextFloat() * FullTurn;
                place = new Float2(parent.Position.X + MathF.Cos(angle) * distance,
                    parent.Position.Y + MathF.Sin(angle) * distance);
                found = Placement.BodyFits(config, World.Creatures, place, body.Radius);
            }

            if (!found)
                return false;
            var heading = random.NextFloat() * FullTurn;
            var transfer = Math.Min(parent.Energy * config.OffspringEnergyFraction, body.MaxEnergy);
            parent.Energy -= transfer + config.ReproductionCost;
            parent.ReproductionCooldownTicks = config.ReproductionCooldownTicks;
            energy.Transferred += transfer;
            energy.BirthCosts += config.ReproductionCost;
            var child = World.AddOffspring(parent, place, heading, transfer, genes);
            if (Heredity != null)
                child.Eyes = Heredity.EyesOf(childId);
            if (config.HasWater)
                ShareHydration(parent, child);
            births.Add(new CreatureBirth(child.Id, parent.Id, tick));
            return true;
        }

        // The child's hydration is the parent's share for offspring, taken from the parent: no water is created.
        private void ShareHydration(CreatureState parent, CreatureState child)
        {
            var share = parent.Hydration * World.Config.OffspringEnergyFraction;
            parent.Hydration -= share;
            child.Hydration = share;
            hydration.Transferred += share;
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
            drunk = new float[size];
            thirst = new float[size];
            var buffers = new float[size][];
            Array.Copy(eyeInputs, buffers, eyeInputs.Length);
            eyeInputs = buffers;
        }

        // Scratch buffer for the eye inputs of the creature at index i this tick, created once per index.
        private float[] EyeInputs(int index) => eyeInputs[index] ??= new float[NeuralContract.Eyes.InputCount];
    }
}
