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
            }

            slot.TicksUntilDecision--;
            return slot.Action;
        }

        public bool TryGetBrain(CreatureId creature, out IBrain brain)
        {
            var found = slots.TryGetValue(creature, out var slot);
            brain = found ? slot.Brain : null;
            return found;
        }

        /// <summary>Drops brains of creatures that died, e.g. with <see cref="WorldSimulation.LastDeaths"/>.</summary>
        public void Forget(IReadOnlyList<CreatureDeath> deaths)
        {
            for (var i = 0; i < deaths.Count; i++)
                slots.Remove(deaths[i].Creature);
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
}
