using System;
using System.IO;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Persistence;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>Genealogy of a shared world with chained births, window metrics of births and an explicit extinction.</summary>
    public class GenealogyTests
    {
        private string root;

        [SetUp]
        public void CreateDirectory()
        {
            root = Path.Combine(Path.GetTempPath(), "eden-genealogy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void DeleteDirectory()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void ChainedBirths_AreFollowedBackToTheFounder_AndForwardToTheDescendants()
        {
            var (simulation, genealogy) = Chain();
            var world = simulation.World;
            Run(simulation, genealogy, 100);

            // 100 → 50 → 25 → 12.5 → 6.25: each creature reproduces once (long cooldown) while it holds 10 or more.
            Assert.That(world.Creatures.Select(c => c.Generation), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            var last = world.Creatures.Last();
            Assert.That(genealogy.Ancestry(last.Id).Select(e => e.Creature.Value), Is.EqualTo(new[] { 5, 4, 3, 2, 1 }));
            Assert.That(genealogy.Ancestry(last.Id).Select(e => e.Generation), Is.EqualTo(new[] { 4, 3, 2, 1, 0 }));
            Assert.That(genealogy.Descendants(new CreatureId(1), out var living), Is.EqualTo(4));
            Assert.That(living, Is.EqualTo(4));
            foreach (var creature in world.Creatures)
            {
                Assert.That(genealogy.TryGet(creature.Id, out var entry), Is.True);
                Assert.That(entry.Parent, Is.EqualTo(creature.Parent));
                Assert.That(entry.Generation, Is.EqualTo(creature.Generation));
                Assert.That(entry.IsAlive, Is.True);
            }

            var second = world.Creatures[1];
            second.AgeTicks = world.Config.MaxAgeTicks - 1;
            Run(simulation, genealogy, 1);
            Assert.That(genealogy.TryGet(second.Id, out var dead), Is.True, "the dead stay in the genealogy");
            Assert.That(dead.IsAlive, Is.False);
            Assert.That(dead.DeathCause, Is.EqualTo(DeathCause.OldAge));
            Assert.That(dead.DeathTick, Is.EqualTo(world.Tick - 1));
            Assert.That(genealogy.Ancestry(last.Id).Select(e => e.Creature.Value), Is.EqualTo(new[] { 5, 4, 3, 2, 1 }),
                "a line goes through dead ancestors");
            Assert.That(genealogy.Descendants(new CreatureId(1), out living), Is.EqualTo(4));
            Assert.That(living, Is.EqualTo(3));
            Assert.That(genealogy.Descendants(last.Id, out _), Is.Zero);
        }

        [Test]
        public void Genealogy_RestoresFromItsCapture_AndRefusesOneThatDoesNotMatchTheWorld()
        {
            var (simulation, genealogy) = Chain();
            Run(simulation, genealogy, 100);
            simulation.World.Creatures[1].AgeTicks = simulation.World.Config.MaxAgeTicks - 1;
            Run(simulation, genealogy, 1);
            var saved = genealogy.Capture();
            var world = simulation.World;

            var restored = Genealogy.Restore(saved, world);
            Assert.That(restored.Capture(), Is.EqualTo(saved));
            Assert.That(restored.Descendants(new CreatureId(1), out var living), Is.EqualTo(4));
            Assert.That(living, Is.EqualTo(3));

            var wrongGeneration = saved.ToArray();
            var e = wrongGeneration[2];
            wrongGeneration[2] = new GenealogyEntry(e.Creature, e.Parent, e.Generation + 1, e.BirthTick, e.DeathTick, e.DeathCause);
            Assert.That(() => Genealogy.Restore(wrongGeneration, world), Throws.ArgumentException);
            var missing = saved.Where(x => x.Creature.Value != 3).ToArray();
            Assert.That(() => Genealogy.Restore(missing, world), Throws.ArgumentException);
            var deadButAlive = saved.ToArray();
            var living4 = deadButAlive[3];
            deadButAlive[3] = living4.Died(world.Tick - 1, DeathCause.Starvation);
            Assert.That(() => Genealogy.Restore(deadButAlive, world), Throws.ArgumentException, "the world says it lives");
        }

        [Test]
        public void Metrics_CountBirthsPerWindow_AndCloseAtExtinction()
        {
            var settings = new SimulationSettings
            {
                Seed = 6, InitialPopulation = 8, InitialFood = 0, MaxAgeSeconds = 8f, Reproduction = true,
                MaturitySeconds = 1f, ReproductionEnergyFraction = 0.3f, ReproductionCooldownSeconds = 100f
            };
            var config = new SimulationConfig(settings);
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(config));
            var genealogy = new Genealogy(simulation.World);
            var metrics = new MetricsRecorder(config, config.TicksPerSecond);
            var brains = BrainController.Shared(config, new HeuristicBrain());
            var path = Path.Combine(root, "genealogy.csv");
            var samples = new System.Collections.Generic.List<MetricsSample>();
            using (var log = new GenealogyCsvLog(path, genealogy))
            {
                while (!simulation.IsExtinct)
                {
                    simulation.Step(brains);
                    brains.Forget(simulation.LastDeaths);
                    genealogy.Record(simulation);
                    log.Append(simulation, genealogy);
                    if (metrics.Record(simulation))
                        samples.Add(metrics.LastSample);
                    if (simulation.IsExtinct)
                        log.AppendExtinction(metrics.ExtinctionTick.Value);
                }
            }

            Assert.That(samples.Sum(s => s.BirthsInWindow), Is.EqualTo(simulation.World.NextCreatureId - 1 - 8));
            Assert.That(samples.Sum(s => s.BirthsInWindow), Is.GreaterThan(0));
            Assert.That(samples.Max(s => s.MaxGeneration), Is.EqualTo(1), "children of founders, cooldown too long for more");
            var final = samples.Last();
            Assert.That(final.Extinct, Is.True);
            Assert.That(final.Population, Is.Zero);
            Assert.That(samples.Take(samples.Count - 1).Any(s => s.Extinct), Is.False);
            Assert.That(metrics.ExtinctionTick, Is.EqualTo(simulation.World.Tick - 1), "the window closed at once");
            Assert.That(genealogy.Entries.All(entry => !entry.IsAlive), Is.True);
            Assert.That(genealogy.Count, Is.EqualTo(simulation.World.NextCreatureId - 1));

            var rows = File.ReadAllLines(path);
            Assert.That(rows[0], Is.EqualTo(GenealogyCsvLog.Header));
            Assert.That(rows.Skip(1).Take(8).All(r => r.StartsWith("0,founder,")), Is.True);
            Assert.That(rows.Count(r => r.Contains(",birth,")), Is.EqualTo(genealogy.Count - 8));
            Assert.That(rows.Count(r => r.Contains(",death,")), Is.EqualTo(genealogy.Count));
            Assert.That(rows.Last(), Is.EqualTo($"{metrics.ExtinctionTick},extinction,,,,"));
            Assert.That(rows.Skip(1).Select(r => long.Parse(r.Split(',')[0])), Is.Ordered, "events in tick order");

            // A log opened for a restored run writes the saved history first, in the same order.
            var replay = Path.Combine(root, "replay.csv");
            using (new GenealogyCsvLog(replay, Genealogy.Restore(genealogy.Capture(), simulation.World)))
            {
            }

            Assert.That(File.ReadAllLines(replay), Is.EqualTo(rows.Take(rows.Length - 1)));
            Assert.That(MetricsCsvLog.FormatRow(final), Does.EndWith(",1"));
        }

        [Test]
        public void Checkpoint_KeepsTheGenealogy_AndRefusesOneThatContradictsTheWorld()
        {
            var settings = new SimulationSettings
            {
                Seed = 8, InitialPopulation = 6, InitialFood = 40, MaxAgeSeconds = 10f, Reproduction = true,
                MaturitySeconds = 1f, ReproductionEnergyFraction = 0.15f, OffspringEnergyFraction = 0.5f,
                ReproductionCooldownSeconds = 2f, PopulationCap = 20
            };
            var config = new SimulationConfig(settings);
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(config));
            var genealogy = new Genealogy(simulation.World);
            var brains = BrainController.Shared(config, new HeuristicBrain());
            for (var t = 0; t < 260 && !simulation.IsExtinct; t++)
            {
                simulation.Step(brains);
                brains.Forget(simulation.LastDeaths);
                genealogy.Record(simulation);
            }

            Assert.That(genealogy.Entries.Any(e => !e.IsAlive), Is.True, "deaths to keep");
            Assert.That(genealogy.Entries.Any(e => e.Generation >= 2), Is.True, "chained births");
            var checkpoint = new Checkpoint("shared", "minimal", 8, "heuristic", null,
                new SharedWorldSnapshot(settings, simulation.Capture(), brains.Capture(), null, genealogy.Capture()));
            var text = CheckpointCodec.Encode(checkpoint, "0.1.0", "test", new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc));
            var decoded = CheckpointCodec.Decode(text, out _).SharedWorld;
            Assert.That(decoded.Genealogy, Is.EqualTo(genealogy.Capture()));

            var living = genealogy.Entries.First(e => e.IsAlive);
            var wrong = new Checkpoint("shared", "minimal", 8, "heuristic", null,
                new SharedWorldSnapshot(settings, simulation.Capture(), brains.Capture(), null,
                    genealogy.Capture().Where(e => e.Creature != living.Creature).ToArray()));
            var wrongText = CheckpointCodec.Encode(wrong, "0.1.0", "test", new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc));
            Assert.That(() => CheckpointCodec.Decode(wrongText, out _),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("Invalid checkpoint content"));
        }

        // One founder with 100 energy and no costs; maturity 1 s, 10 energy needed, half handed over, one birth each.
        private static (WorldSimulation, Genealogy) Chain()
        {
            var config = new SimulationConfig(new SimulationSettings
            {
                Seed = 2, InitialPopulation = 0, InitialFood = 0, MetabolismPerSecond = 0f, MovementCostPerSecond = 0f,
                SensorCostPerSecond = 0f, MaxAgeSeconds = 600f, Reproduction = true, MaturitySeconds = 1f,
                ReproductionEnergyFraction = 0.1f, OffspringEnergyFraction = 0.5f, ReproductionCost = 0f,
                ReproductionCooldownSeconds = 1000f
            });
            var world = new WorldState(config);
            world.AddCreature(new Float2(25f, 25f), 0f, 100f);
            var simulation = new WorldSimulation(world);
            return (simulation, new Genealogy(world));
        }

        private static void Run(WorldSimulation simulation, Genealogy genealogy, int ticks)
        {
            var idle = new ConstantController(CreatureAction.Idle);
            for (var i = 0; i < ticks; i++)
            {
                simulation.Step(idle);
                genealogy.Record(simulation);
            }
        }
    }
}
