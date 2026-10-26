using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Simulation;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Eden.Tests.EditMode
{
    /// <summary>Steady-state ticks allocate nothing (docs/07: zero bytes per creature per tick after warm-up).</summary>
    public class PerformanceTests
    {
        [TestCase("neural")]
        [TestCase("heuristic")]
        [TestCase("random")]
        public void SharedWorld_TicksDoNotAllocateAfterWarmup(string brain)
        {
            // Full initial energy, so nobody starves within the 360 ticks below (100 energy, at most 5.25/s), and a
            // 15 s life, so every creature dies of old age at tick 300, inside the measured ticks.
            var config = new SimulationConfig(new SimulationSettings
            {
                Seed = 3, InitialPopulation = 64, InitialFood = 128, InitialEnergy = 100f, MaxAgeSeconds = 15f
            });
            var world = new WorldSimulation(WorldFactory.CreateInitial(config));
            var brains = brain == "neural"
                ? new BrainController(config, id => new MlpBrain(InitialGenomes.RandomFor(config, id)))
                : BrainController.Shared(config, brain == "random" ? (IBrain)new RandomBrain() : new HeuristicBrain());
            var metrics = new MetricsRecorder(config, config.TicksPerSecond);
            void Tick()
            {
                world.Step(brains);
                brains.Forget(world.LastDeaths);
                metrics.Record(world);
            }

            for (var i = 0; i < 120; i++)
                Tick();
            var eatenBefore = metrics.TotalFoodEaten;
            var nextFoodBefore = world.World.NextFoodId;
            Assert.That(world.World.Creatures.Count, Is.EqualTo(64), "nobody died before the measured ticks");

            Assert.That(() =>
            {
                for (var i = 0; i < 240; i++)
                    Tick();
            }, Is.Not.AllocatingGCMemory());

            Assert.That(world.World.Creatures.Count, Is.Zero, "the first deaths happened while measured");
            Assert.That(metrics.TotalFoodEaten, Is.GreaterThan(eatenBefore), "food was eaten while measured");
            Assert.That(world.World.NextFoodId, Is.GreaterThan(nextFoodBefore), "and respawned into reused items");
            var food = world.World.Food;
            Assert.That(food.Select(f => f.Id.Value), Is.Ordered.And.Unique);
            Assert.That(food.Any(f => f.IsEaten), Is.False);
            Assert.That(food.Distinct().Count(), Is.EqualTo(food.Count), "an item is in the world once");
        }

        [TestCase("heuristic")]
        [TestCase("neural")]
        public void IsolatedEpisode_TicksDoNotAllocateAfterWarmup(string brain)
        {
            var plan = new EvaluationPlan(new EvolutionSettings { ExperimentSeed = 4 });
            var genome = InitialGenomes.Population(4, 1)[0];
            var episode = new IsolatedEpisode(plan,
                () => brain == "neural" ? (IBrain)new MlpBrain(genome) : new HeuristicBrain(),
                plan.TrainingSeeds(0)[0], 0, 0, 0, EvaluationMode.Training);
            episode.Advance(20);

            // 150 ticks keep even a creature that spends at full effort alive (episode energy 50, at most 5.25/s).
            Assert.That(() => { episode.Advance(150); }, Is.Not.AllocatingGCMemory());
            Assert.That(episode.Tick, Is.EqualTo(170), "the creature lived through the measured ticks");
        }
    }
}
