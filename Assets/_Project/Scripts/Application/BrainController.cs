using System;
using System.Collections.Generic;
using Eden.Brains;
using Eden.Domain;
using Eden.Simulation;

namespace Eden.Application
{
    /// <summary>
    /// Connects the simulation to per-creature brains. Each creature gets a brain from the factory and a fresh
    /// <see cref="BrainState"/> with its own random stream the first time it acts. Brains decide every
    /// <see cref="SimulationConfig.DecisionIntervalTicks"/> ticks, starting on the creature's first tick, and the
    /// last action is held in between.
    /// </summary>
    public sealed class BrainController : ICreatureController
    {
        private readonly Dictionary<CreatureId, Slot> slots = new Dictionary<CreatureId, Slot>();
        private readonly Func<CreatureId, IBrain> brainFor;
        private readonly ulong runSeed;
        private readonly int decisionInterval;
        private CreatureId? watched;
        private Observation watchedObservation;
        private float[] watchedEyes;
        private bool hasWatchedDecision;

        public BrainController(SimulationConfig config, Func<CreatureId, IBrain> brainFor)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            this.brainFor = brainFor ?? throw new ArgumentNullException(nameof(brainFor));
            runSeed = config.Seed;
            decisionInterval = config.DecisionIntervalTicks;
        }

        /// <summary>Same brain instance for every creature; per-creature state still lives in each BrainState.</summary>
        public static BrainController Shared(SimulationConfig config, IBrain brain)
        {
            if (brain == null)
                throw new ArgumentNullException(nameof(brain));
            return new BrainController(config, _ => brain);
        }

        public int TrackedCreatures => slots.Count;

        public CreatureAction Decide(CreatureId creature, in Observation observation)
        {
            if (!slots.TryGetValue(creature, out var slot))
            {
                var brain = brainFor(creature) ??
                            throw new InvalidOperationException($"No brain was provided for {creature}.");
                var state = new BrainState(SeedDerivation.CreateCreatureStream(runSeed, RandomStream.Brain, creature));
                slot = new Slot(brain, state);
                slots.Add(creature, slot);
            }

            if (slot.TicksUntilDecision == 0)
            {
                slot.Action = slot.Brain.Evaluate(observation, slot.State);
                slot.TicksUntilDecision = decisionInterval;
                if (watched.HasValue && watched.Value == creature)
                {
                    watchedObservation = KeepEyes(observation);
                    hasWatchedDecision = true;
                }
            }

            slot.TicksUntilDecision--;
            return slot.Action;
        }

        /// <summary>The creature's own brain state (random stream, lifetime weight changes), for inspection.</summary>
        public bool TryGetState(CreatureId creature, out BrainState state)
        {
            var found = slots.TryGetValue(creature, out var slot);
            state = found ? slot.State : null;
            return found;
        }

        public bool TryGetBrain(CreatureId creature, out IBrain brain)
        {
            var found = slots.TryGetValue(creature, out var slot);
            brain = found ? slot.Brain : null;
            return found;
        }

        /// <summary>
        /// Keeps the observation of the next decisions of one creature (e.g. the selected one) for inspection.
        /// Only that creature is recorded, so watching costs nothing for the rest of the population; null stops.
        /// </summary>
        public void Watch(CreatureId? creature)
        {
            watched = creature;
            hasWatchedDecision = false;
        }

        // Eye inputs live in the simulation's buffer for one tick; the watched decision keeps its own copy.
        private Observation KeepEyes(in Observation observation)
        {
            var eyes = observation.EyeInputs;
            if (eyes == null)
                return observation;
            if (watchedEyes == null || watchedEyes.Length != eyes.Length)
                watchedEyes = new float[eyes.Length];
            Array.Copy(eyes, watchedEyes, eyes.Length);
            return new Observation(observation.Energy, observation.Age, observation.VelocityForward,
                observation.VelocityLeft, observation.FoodSin, observation.FoodCos, observation.FoodProximity,
                observation.FoodVisible, observation.WallFront, observation.WallRight, observation.WallBack,
                observation.WallLeft, watchedEyes);
        }

        /// <summary>
        /// Observation the watched creature's brain used for its current held action, if it decided since (its eye
        /// inputs are a copy that later ticks do not change).
        /// </summary>
        public bool TryGetWatchedDecision(out Observation observation)
        {
            observation = watchedObservation;
            return hasWatchedDecision;
        }

        /// <summary>Action currently held for the creature (the result of its last decision).</summary>
        public bool TryGetLastAction(CreatureId creature, out CreatureAction action)
        {
            var found = slots.TryGetValue(creature, out var slot);
            action = found ? slot.Action : CreatureAction.Idle;
            return found;
        }

        /// <summary>Drops brains of creatures that died, e.g. with <see cref="WorldSimulation.LastDeaths"/>.</summary>
        public void Forget(IReadOnlyList<CreatureDeath> deaths)
        {
            for (var i = 0; i < deaths.Count; i++)
                slots.Remove(deaths[i].Creature);
        }

        /// <summary>Per-creature state of every tracked creature, in id order (brains themselves come from the factory).</summary>
        public BrainSlotState[] Capture()
        {
            var saved = new BrainSlotState[slots.Count];
            var i = 0;
            foreach (var pair in slots)
                saved[i++] = new BrainSlotState(pair.Key, pair.Value.Brain.Name, pair.Value.State.Random.State,
                    pair.Value.Action, pair.Value.TicksUntilDecision, pair.Value.State.Plastic);
            Array.Sort(saved, (a, b) => a.Creature.Value.CompareTo(b.Creature.Value));
            return saved;
        }

        /// <summary>
        /// Gives a controller that has not decided yet the saved per-creature state. Each creature's brain comes from
        /// the factory again and must have the saved name; everything is checked before any slot is set.
        /// </summary>
        public void Restore(IReadOnlyList<BrainSlotState> saved)
        {
            if (saved == null)
                throw new ArgumentNullException(nameof(saved));
            if (slots.Count > 0)
                throw new InvalidOperationException("Only a controller that has not decided yet can be restored.");

            var restored = new Dictionary<CreatureId, Slot>(saved.Count);
            foreach (var state in saved)
            {
                if (restored.ContainsKey(state.Creature))
                    throw new ArgumentException($"{state.Creature} is saved twice.", nameof(saved));
                if (state.TicksUntilDecision < 0 || state.TicksUntilDecision >= decisionInterval)
                    throw new ArgumentException($"{state.Creature}: ticks until decision {state.TicksUntilDecision} " +
                                                $"is outside [0, {decisionInterval}).", nameof(saved));
                var brain = brainFor(state.Creature) ??
                            throw new ArgumentException($"No brain was provided for {state.Creature}.", nameof(saved));
                if (brain.Name != state.BrainName)
                    throw new ArgumentException($"{state.Creature} was saved with brain {state.BrainName}, " +
                                                $"the factory gives {brain.Name}.", nameof(saved));
                var brainState = new BrainState(Pcg32.FromState(state.Random));
                if (state.Plastic != null)
                {
                    if (!(brain is MlpBrain mlp) || !mlp.Plasticity.IsPlastic || !state.Plastic.IsValid())
                        throw new ArgumentException($"{state.Creature} was saved with lifetime weight changes its brain " +
                                                    "cannot hold, or with impossible values.", nameof(saved));
                    brainState.EnsurePlastic().CopyFrom(state.Plastic);
                }

                restored.Add(state.Creature, new Slot(brain, brainState)
                {
                    Action = state.Action, TicksUntilDecision = state.TicksUntilDecision
                });
            }

            foreach (var pair in restored)
                slots.Add(pair.Key, pair.Value);
        }

        private sealed class Slot
        {
            public Slot(IBrain brain, BrainState state)
            {
                Brain = brain;
                State = state;
            }

            public IBrain Brain { get; }
            public BrainState State { get; }
            public CreatureAction Action { get; set; }
            public int TicksUntilDecision { get; set; }
        }
    }

    /// <summary>
    /// What a <see cref="BrainController"/> keeps for one creature between ticks: which brain it uses (by name), the
    /// creature's own random stream, the held action, how many ticks remain until its next decision and, for a plastic
    /// network, what it learned so far in this life (a copy, detached from the live state).
    /// </summary>
    public readonly struct BrainSlotState
    {
        public BrainSlotState(CreatureId creature, string brainName, Pcg32State random, CreatureAction action,
            int ticksUntilDecision, PlasticMemory plastic = null)
        {
            Creature = creature;
            BrainName = brainName ?? throw new ArgumentNullException(nameof(brainName));
            Random = random;
            Action = action;
            TicksUntilDecision = ticksUntilDecision;
            if (plastic == null)
            {
                Plastic = null;
            }
            else
            {
                Plastic = new PlasticMemory();
                Plastic.CopyFrom(plastic);
            }
        }

        public CreatureId Creature { get; }
        public string BrainName { get; }
        public Pcg32State Random { get; }
        public CreatureAction Action { get; }
        public int TicksUntilDecision { get; }

        /// <summary>Lifetime weight changes of a plastic network; null for any other brain.</summary>
        public PlasticMemory Plastic { get; }
    }
}
