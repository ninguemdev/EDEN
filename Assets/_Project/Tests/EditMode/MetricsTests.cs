using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Persistence;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class MetricsTests
    {
        [Test]
        public void Recorder_ClosesOneWindowPerSimulatedSecond()
        {
            var config = SimulationConfig.CreateDefault(4);
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(config));
            var controller = BrainController.Shared(config, new HeuristicBrain());
            var recorder = new MetricsRecorder(config, config.TicksPerSecond);
            var samples = 0;
            long eaten = 0;

            for (var t = 0; t < 100; t++)
            {
                simulation.Step(controller);
                eaten += simulation.LastFoodEaten;
                if (recorder.Record(simulation))
                {
                    samples++;
                    Assert.That(recorder.LastSample.Tick % config.TicksPerSecond, Is.EqualTo(0));
                    Assert.That(recorder.LastSample.Population, Is.EqualTo(simulation.World.Creatures.Count));
                    Assert.That(recorder.LastSample.FoodAvailable, Is.EqualTo(simulation.World.Food.Count));
                }
            }

            Assert.That(samples, Is.EqualTo(5));
            Assert.That(recorder.LastSample.SimulatedSeconds, Is.EqualTo(5.0));
            Assert.That(recorder.TotalFoodEaten, Is.EqualTo(eaten));
            Assert.That(recorder.LastSample.TotalFoodEaten, Is.EqualTo(eaten));
        }

        [Test]
        public void Recorder_CountsDeathsByCauseInTheirWindow()
        {
            var world = TestWorlds.Empty();
            world.AddCreature(new Float2(10f, 10f), 0f, 0.2f);
            world.AddCreature(new Float2(20f, 20f), 0f, 0.2f);
            var simulation = new WorldSimulation(world);
            var recorder = new MetricsRecorder(world.Config, 10);

            for (var t = 0; t < 10; t++)
            {
                simulation.Step(new ConstantController(CreatureAction.Idle));
                recorder.Record(simulation);
            }

            Assert.That(recorder.LastSample.StarvationDeathsInWindow, Is.EqualTo(2));
            Assert.That(recorder.LastSample.OldAgeDeathsInWindow, Is.EqualTo(0));
            Assert.That(recorder.LastSample.Population, Is.EqualTo(0));
            Assert.That(recorder.LastSample.MeanEnergy, Is.EqualTo(0f));
        }

        [Test]
        public void CsvRow_UsesInvariantNumbersUnderAnyCulture()
        {
            var sample = new MetricsSample(40, 2.0, 60, 120, 7, 1, 0, 43.21f, 55);
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
                Assert.That(MetricsCsvLog.FormatRow(sample), Is.EqualTo("40,2,60,120,7,1,0,43.21,55"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void CsvLog_WritesHeaderAndFlushedRows()
        {
            var directory = Path.Combine(Path.GetTempPath(), "eden-metrics-test-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "nested", "metrics.csv");
            try
            {
                using (var log = new MetricsCsvLog(path))
                {
                    log.Append(new MetricsSample(20, 1.0, 64, 128, 3, 0, 0, 49.9f, 3));
                    Assert.That(log.RowCount, Is.EqualTo(1));
                }

                var lines = File.ReadAllLines(path);
                Assert.That(lines, Is.EqualTo(new[] { MetricsCsvLog.Header, "20,1,64,128,3,0,0,49.9,3" }));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void RunInfo_DescribesTheRunWithInvariantValues()
        {
            var config = SimulationConfig.CreateDefault(12);
            var lines = RunInfo.Describe(config, "heuristic-v1", "0.1.0", new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc));

            Assert.That(lines, Does.Contain("seed=12"));
            Assert.That(lines, Does.Contain("brain=heuristic-v1"));
            Assert.That(lines, Does.Contain("evaluation_mode=shared_inspection"));
            Assert.That(lines, Does.Contain("created_utc=2026-10-23T12:00:00Z"));
            Assert.That(lines, Does.Contain("arena=50x50"));
        }

        [Test]
        public void Controller_ExposesTheHeldAction()
        {
            var controller = BrainController.Shared(SimulationConfig.CreateDefault(1), new HeuristicBrain());
            Assert.That(controller.TryGetLastAction(new CreatureId(1), out _), Is.False);

            var decided = controller.Decide(new CreatureId(1), default);

            Assert.That(controller.TryGetLastAction(new CreatureId(1), out var held), Is.True);
            Assert.That(held.Thrust, Is.EqualTo(decided.Thrust));
            Assert.That(held.Turn, Is.EqualTo(decided.Turn));
        }
    }
}
