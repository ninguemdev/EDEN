using System;
using System.Collections.Generic;
using Eden.Domain;
using Eden.Simulation;

namespace Eden.Tests.EditMode
{
    internal static class TestWorlds
    {
        /// <summary>Default rules in an arena without initial creatures or food, for hand-placed scenarios.</summary>
        public static WorldState Empty(Action<SimulationSettings> configure = null)
        {
            var settings = new SimulationSettings { Seed = 11, InitialPopulation = 0, InitialFood = 0 };
            configure?.Invoke(settings);
            return new WorldState(new SimulationConfig(settings));
        }
    }

    internal sealed class ConstantController : ICreatureController
    {
        private readonly CreatureAction action;

        public ConstantController(CreatureAction action)
        {
            this.action = action;
        }

        public CreatureAction Decide(CreatureId creature, in Observation observation) => action;
    }

    /// <summary>Deterministic stand-in for a brain: steer to visible food, otherwise wander in a circle.</summary>
    internal sealed class SeekFoodController : ICreatureController
    {
        public CreatureAction Decide(CreatureId creature, in Observation observation)
        {
            if (observation.FoodVisible > 0f)
                return new CreatureAction(observation.FoodCos > 0f ? 1f : 0.2f, observation.FoodSin * 3f, 0f);
            return new CreatureAction(0.6f, 0.4f, 0f);
        }
    }

    internal sealed class RecordingController : ICreatureController
    {
        public readonly List<CreatureId> Calls = new List<CreatureId>();

        public CreatureAction Decide(CreatureId creature, in Observation observation)
        {
            Calls.Add(creature);
            return CreatureAction.Idle;
        }
    }
}
