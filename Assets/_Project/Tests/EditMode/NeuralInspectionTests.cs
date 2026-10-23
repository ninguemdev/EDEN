using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class NeuralInspectionTests
    {
        [Test]
        public void WatchedDecision_ReproducesTheHeldActionExactly()
        {
            var (simulation, controller) = NeuralRun(12);
            var watched = new CreatureId(3);
            controller.Watch(watched);

            for (var t = 0; t < 9; t++)
                simulation.Step(controller);

            Assert.That(controller.TryGetWatchedDecision(out var observation), Is.True);
            controller.TryGetBrain(watched, out var brain);
            controller.TryGetLastAction(watched, out var held);
            var snapshot = new NeuralSnapshot();
            snapshot.Capture(watched, (MlpBrain)brain, observation);

            Assert.That(snapshot.Action.Thrust, Is.EqualTo(held.Thrust));
            Assert.That(snapshot.Action.Turn, Is.EqualTo(held.Turn));
            Assert.That(snapshot.Action.Strafe, Is.EqualTo(held.Strafe));
            Assert.That(snapshot.Input(0), Is.EqualTo(observation.Energy));
            Assert.That(snapshot.Weight(100), Is.EqualTo(((MlpBrain)brain).GetParameter(100)));
        }

        [Test]
        public void Watch_RecordsOnlyTheWatchedCreature()
        {
            var config = SimulationConfig.CreateDefault(1);
            var controller = BrainController.Shared(config, new HeuristicBrain());
            var other = new Observation(0.2f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f);
            var mine = new Observation(0.7f, 0.1f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f);

            controller.Watch(new CreatureId(5));
            controller.Decide(new CreatureId(4), other);
            Assert.That(controller.TryGetWatchedDecision(out _), Is.False);

            controller.Decide(new CreatureId(5), mine);
            Assert.That(controller.TryGetWatchedDecision(out var recorded), Is.True);
            Assert.That(recorded.Energy, Is.EqualTo(0.7f));

            controller.Watch(null);
            Assert.That(controller.TryGetWatchedDecision(out _), Is.False);
        }

        [Test]
        public void Inspecting_DoesNotChangeTheSimulation()
        {
            var (plain, plainController) = NeuralRun(15);
            var (inspected, inspectedController) = NeuralRun(15);
            var watched = new CreatureId(7);
            inspectedController.Watch(watched);
            var snapshot = new NeuralSnapshot();

            for (var t = 0; t < 300; t++)
            {
                plain.Step(plainController);
                plainController.Forget(plain.LastDeaths);
                inspected.Step(inspectedController);
                if (inspectedController.TryGetBrain(watched, out var brain) &&
                    inspectedController.TryGetWatchedDecision(out var observation))
                    snapshot.Capture(watched, (MlpBrain)brain, observation);
                inspectedController.Forget(inspected.LastDeaths);
            }

            Assert.That(snapshot.Captures, Is.GreaterThan(0));
            Assert.That(inspected.ComputeFingerprint(), Is.EqualTo(plain.ComputeFingerprint()));
        }

        [Test]
        public void Capture_CopiesWeightsWhenTheBrainChanges()
        {
            var config = SimulationConfig.CreateDefault(2);
            var first = new MlpBrain(InitialGenomes.RandomFor(config, new CreatureId(1)));
            var second = new MlpBrain(InitialGenomes.RandomFor(config, new CreatureId(2)));
            var snapshot = new NeuralSnapshot();

            snapshot.Capture(new CreatureId(1), first, MlpBrainTests.Reference);
            Assert.That(snapshot.Weight(258), Is.EqualTo(first.GetParameter(258)));
            snapshot.Capture(new CreatureId(2), second, MlpBrainTests.Reference);
            Assert.That(snapshot.Weight(258), Is.EqualTo(second.GetParameter(258)));
            Assert.That(snapshot.Creature, Is.EqualTo(new CreatureId(2)));
            Assert.That(snapshot.Captures, Is.EqualTo(2));
        }

        private static (WorldSimulation, BrainController) NeuralRun(ulong seed)
        {
            var config = SimulationConfig.CreateDefault(seed);
            var controller = new BrainController(config, id => new MlpBrain(InitialGenomes.RandomFor(config, id)));
            return (new WorldSimulation(WorldFactory.CreateInitial(config)), controller);
        }
    }
}
