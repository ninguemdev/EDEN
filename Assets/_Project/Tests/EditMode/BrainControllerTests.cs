using System;
using System.Collections.Generic;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class BrainControllerTests
    {
        [Test]
        public void Decisions_HappenEveryIntervalAndActionsAreHeld()
        {
            var config = new SimulationConfig(new SimulationSettings { DecisionIntervalTicks = 3 });
            var brain = new CountingBrain();
            var controller = BrainController.Shared(config, brain);
            var creature = new CreatureId(1);

            var thrusts = new List<float>();
            for (var tick = 0; tick < 7; tick++)
                thrusts.Add(controller.Decide(creature, default).Thrust);

            Assert.That(brain.Evaluations, Is.EqualTo(3), "ticks 0, 3 and 6");
            Assert.That(thrusts, Is.EqualTo(new[] { 0.1f, 0.1f, 0.1f, 0.2f, 0.2f, 0.2f, 0.3f }));
        }

        [Test]
        public void EachCreature_GetsItsBrainOnceAndItsOwnRandomStream()
        {
            var config = SimulationConfig.CreateDefault(99);
            var requested = new List<CreatureId>();
            var recorder = new StreamRecordingBrain();
            var controller = new BrainController(config, id =>
            {
                requested.Add(id);
                return recorder;
            });

            controller.Decide(new CreatureId(4), default);
            controller.Decide(new CreatureId(9), default);
            controller.Decide(new CreatureId(4), default);

            Assert.That(requested, Is.EqualTo(new[] { new CreatureId(4), new CreatureId(9) }));
            Assert.That(recorder.SeenStates[0],
                Is.EqualTo(SeedDerivation.CreateCreatureStream(99, RandomStream.Brain, new CreatureId(4)).State));
            Assert.That(recorder.SeenStates[1],
                Is.EqualTo(SeedDerivation.CreateCreatureStream(99, RandomStream.Brain, new CreatureId(9)).State));
        }

        [Test]
        public void Forget_DropsDeadCreatures()
        {
            var controller = BrainController.Shared(SimulationConfig.CreateDefault(1), new HeuristicBrain());
            controller.Decide(new CreatureId(1), default);
            controller.Decide(new CreatureId(2), default);

            controller.Forget(new[] { new CreatureDeath(new CreatureId(1), 0, DeathCause.Starvation) });

            Assert.That(controller.TrackedCreatures, Is.EqualTo(1));
            Assert.That(controller.TryGetBrain(new CreatureId(1), out _), Is.False);
            Assert.That(controller.TryGetBrain(new CreatureId(2), out var brain), Is.True);
            Assert.That(brain.Name, Is.EqualTo(HeuristicBrain.BrainName));
        }

        [Test]
        public void MissingBrain_IsReported()
        {
            var controller = new BrainController(SimulationConfig.CreateDefault(1), _ => null);
            Assert.Throws<InvalidOperationException>(() => controller.Decide(new CreatureId(1), default));
        }

        private sealed class CountingBrain : IBrain
        {
            public int Evaluations;
            public string Name => "counting";

            public CreatureAction Evaluate(in Observation observation, BrainState state)
            {
                Evaluations++;
                return new CreatureAction(Evaluations / 10f, 0f, 0f);
            }
        }

        private sealed class StreamRecordingBrain : IBrain
        {
            public readonly List<Pcg32State> SeenStates = new List<Pcg32State>();
            public string Name => "recording";

            public CreatureAction Evaluate(in Observation observation, BrainState state)
            {
                SeenStates.Add(state.Random.State);
                return CreatureAction.Idle;
            }
        }
    }
}
