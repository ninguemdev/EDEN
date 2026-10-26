using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using Eden.Simulation;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Eden.Tests.EditMode
{
    /// <summary>
    /// Steady-state ticks allocate nothing (docs/07: zero bytes per creature per tick after warm-up), tick timings do
    /// not change the run, and the performance figures and report are computed and written as stated.
    /// </summary>
    public class PerformanceTests
    {
        [TestCase("neural")]
        [TestCase("heuristic")]
        [TestCase("random")]
        public void SharedWorld_TicksDoNotAllocateAfterWarmup(string brain)
        {
            WarmUpEveryTickPath();

            // Full initial energy, so nobody starves within the 360 ticks below (100 energy, at most 5.25/s), and a
            // 15 s life, so every creature dies of old age at tick 300, inside the measured ticks.
            var config = new SimulationConfig(new SimulationSettings
            {
                Seed = 3, InitialPopulation = 64, InitialFood = 128, InitialEnergy = 100f, MaxAgeSeconds = 15f
            });
            var world = new WorldSimulation(WorldFactory.CreateInitial(config)) { Timings = new TickTimings() };
            var brains = brain == "neural"
                ? new BrainController(config, id => new MlpBrain(InitialGenomes.RandomFor(config, id)))
                : BrainController.Shared(config, brain == "random" ? (IBrain)new RandomBrain() : new HeuristicBrain());
            var metrics = new MetricsRecorder(config, config.TicksPerSecond);
            var recorder = new PerformanceRecorder();
            void Tick()
            {
                world.Step(brains);
                brains.Forget(world.LastDeaths);
                metrics.Record(world);
                recorder.AddTick(world.Timings);
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
            WarmUpEveryTickPath();
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

        [Test]
        public void Session_CountsTheEpisodesItRuns()
        {
            var session = new EvolutionSession(ResumeChecks.Small(5), 100);
            var training = session.Job;
            while (session.Job == training)
                session.Advance(7);

            Assert.That(session.EpisodesRun, Is.EqualTo(6 * 2 + session.Job.CompletedEpisodes),
                "generation 0 on its 2 training seeds, then the reference brains so far");
        }

        [Test]
        public void Timings_DoNotChangeTheRun()
        {
            var config = new SimulationConfig(new SimulationSettings { Seed = 8, InitialPopulation = 32, InitialFood = 64 });
            var timed = new WorldSimulation(WorldFactory.CreateInitial(config)) { Timings = new TickTimings() };
            var plain = new WorldSimulation(WorldFactory.CreateInitial(config));
            var timedBrains = BrainController.Shared(config, new HeuristicBrain());
            var plainBrains = BrainController.Shared(config, new HeuristicBrain());
            for (var i = 0; i < 300; i++)
            {
                timed.Step(timedBrains);
                timedBrains.Forget(timed.LastDeaths);
                plain.Step(plainBrains);
                plainBrains.Forget(plain.LastDeaths);
            }

            Assert.That(timed.ComputeFingerprint(), Is.EqualTo(plain.ComputeFingerprint()));
            var t = timed.Timings;
            Assert.That(t.Creatures, Is.GreaterThan(0));
            Assert.That(t.SensorsMilliseconds, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(t.DecisionsMilliseconds, Is.GreaterThanOrEqualTo(0.0));
            Assert.That(t.TotalMilliseconds, Is.GreaterThanOrEqualTo(t.SensorsMilliseconds + t.DecisionsMilliseconds));
        }

        [Test]
        public void Histogram_QuantilesAreWithinTheBinResolution()
        {
            var histogram = new DurationHistogram();
            Assert.That(histogram.Median, Is.Zero, "no samples");
            for (var i = 1; i <= 1000; i++)
                histogram.Add(i * 0.01);

            Assert.That(histogram.Count, Is.EqualTo(1000));
            Assert.That(histogram.Mean, Is.EqualTo(5.005).Within(1e-9));
            Assert.That(histogram.Max, Is.EqualTo(10.0));
            Assert.That(histogram.Median, Is.EqualTo(5.0).Within(5.0 * 0.012));
            Assert.That(histogram.P95, Is.EqualTo(9.5).Within(9.5 * 0.012));
            Assert.That(histogram.Quantile(1.0), Is.LessThanOrEqualTo(10.0));

            histogram.Add(double.NaN);
            histogram.Add(1e9);
            Assert.That(histogram.Count, Is.EqualTo(1002), "out-of-range values are still counted");
        }

        [Test]
        public void Recorder_AndReport_StateRatesAndWhatIsUnavailable()
        {
            var recorder = new PerformanceRecorder();
            for (var i = 0; i < 60; i++)
                recorder.AddFrame(1.0 / 60.0, 2, 0.5);
            Assert.That(recorder.FramesPerSecond, Is.EqualTo(60.0).Within(1e-6));
            Assert.That(recorder.TicksPerSecond, Is.EqualTo(120.0).Within(1e-6));
            Assert.That(recorder.MeanTickMilliseconds, Is.EqualTo(0.25).Within(1e-9));

            var record = new BenchmarkRecord(new[] { new KeyValuePair<string, string>("build", "release") },
                new[] { new KeyValuePair<string, string>("preset", "minimal") }, recorder)
            {
                RequestedTicksPerSecond = 20, GcCollections = 0, PopulationAtStart = 32, PopulationAtEnd = 30
            };
            var json = JsonText.Parse(PerformanceReport.Json(record));
            Assert.That(json["environment"]["build"].String(), Is.EqualTo("release"));
            Assert.That(json["framesPerSecond"].AnyFloat(), Is.EqualTo(60f).Within(1e-3f));
            Assert.That(json["memory"]["gcCollections"].Long(), Is.Zero);
            Assert.That(json["memory"]["simulationAllocations"].IsNull, Is.True, "unavailable, not zero");
            Assert.That(json["memory"]["simulationAllocationsPerCreatureTick"].IsNull, Is.True);

            var text = string.Join("\n", PerformanceReport.Describe(record));
            StringAssert.Contains("alocações durante os ticks indisponível", text);
            StringAssert.Contains("120.0/s efetivos de 20.0/s pedidos", text);

            var directory = Path.Combine(Path.GetTempPath(), "eden-performance-" + Guid.NewGuid().ToString("N"));
            try
            {
                PerformanceReport.Write(directory, record);
                Assert.That(File.Exists(Path.Combine(directory, "performance.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(directory, "performance.txt")), Is.True);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        // Under Mono the first execution of a method can allocate once (JIT work), e.g. the first death of the process.
        // That is warm-up, not a per-tick cost: a throwaway world with short lives runs every tick path first, so the
        // measurements below see steady-state ticks even when a test runs alone in a fresh domain.
        private static void WarmUpEveryTickPath()
        {
            var config = new SimulationConfig(new SimulationSettings
            {
                Seed = 1, InitialPopulation = 8, InitialFood = 64, MaxAgeSeconds = 6f, FoodRespawnSeconds = 0.5f
            });
            var world = new WorldSimulation(WorldFactory.CreateInitial(config)) { Timings = new TickTimings() };
            var brains = new BrainController(config, id => id.Value % 2 == 0
                ? (IBrain)new MlpBrain(InitialGenomes.RandomFor(config, id))
                : new HeuristicBrain());
            var metrics = new MetricsRecorder(config, config.TicksPerSecond);
            var recorder = new PerformanceRecorder();
            for (var i = 0; i < 160 && !world.IsExtinct; i++)
            {
                world.Step(brains);
                brains.Forget(world.LastDeaths);
                metrics.Record(world);
                recorder.AddTick(world.Timings);
            }

            Assert.That(world.IsExtinct, Is.True, "the warm-up reached the deaths");
        }
    }
}
