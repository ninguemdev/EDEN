using System.Globalization;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>
    /// Preliminary sanity baseline (default config, 5 seeds, 60 simulated seconds): the hand-written heuristic
    /// must gather more food than random actions. Numbers are written to the test output for the record;
    /// they are not performance or evolution claims.
    /// </summary>
    public class BaselineTests
    {
        private const int Ticks = 1200;
        private static readonly ulong[] Seeds = { 1, 2, 3, 4, 5 };

        [Test]
        public void Heuristic_GathersMoreFoodThanRandom()
        {
            long randomFood = 0, heuristicFood = 0;
            int randomSurvivors = 0, heuristicSurvivors = 0;

            foreach (var seed in Seeds)
            {
                var random = Run(seed, new RandomBrain());
                var heuristic = Run(seed, new HeuristicBrain());
                randomFood += random.FoodEaten;
                heuristicFood += heuristic.FoodEaten;
                randomSurvivors += random.Survivors;
                heuristicSurvivors += heuristic.Survivors;
                TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "seed={0} random(food={1}, survivors={2}) heuristic(food={3}, survivors={4})",
                    seed, random.FoodEaten, random.Survivors, heuristic.FoodEaten, heuristic.Survivors));
            }

            TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "TOTAL over {0} seeds x {1} ticks: random(food={2}, survivors={3}) heuristic(food={4}, survivors={5})",
                Seeds.Length, Ticks, randomFood, randomSurvivors, heuristicFood, heuristicSurvivors));

            Assert.That(heuristicFood, Is.GreaterThan(randomFood));
        }

        [Test]
        public void RandomBaseline_IsReproducible()
        {
            Assert.That(Run(7, new RandomBrain()).Fingerprint, Is.EqualTo(Run(7, new RandomBrain()).Fingerprint));
        }

        private static RunResult Run(ulong seed, IBrain brain)
        {
            var config = SimulationConfig.CreateDefault(seed);
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(config));
            var controller = BrainController.Shared(config, brain);
            var food = 0L;

            for (var t = 0; t < Ticks; t++)
            {
                simulation.Step(controller);
                food += simulation.LastFoodEaten;
                controller.Forget(simulation.LastDeaths);
            }

            Assert.That(controller.TrackedCreatures, Is.EqualTo(simulation.World.Creatures.Count));
            return new RunResult(food, simulation.World.Creatures.Count, simulation.ComputeFingerprint());
        }

        private readonly struct RunResult
        {
            public RunResult(long foodEaten, int survivors, ulong fingerprint)
            {
                FoodEaten = foodEaten;
                Survivors = survivors;
                Fingerprint = fingerprint;
            }

            public long FoodEaten { get; }
            public int Survivors { get; }
            public ulong Fingerprint { get; }
        }
    }
}
