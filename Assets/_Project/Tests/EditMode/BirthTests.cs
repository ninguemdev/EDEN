using System;
using System.Collections.Generic;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using Eden.Simulation;
using NUnit.Framework;
using static Eden.Tests.EditMode.ResumeChecks;

namespace Eden.Tests.EditMode
{
    /// <summary>Local asexual reproduction in a shared world: who may give birth, energy, cap, heredity and resumption.</summary>
    public class BirthTests
    {
        private static readonly CreatureAction Idle = CreatureAction.Idle;

        [Test]
        public void Config_KeepsReproductionOffByDefault_AndValidatesIt()
        {
            Assert.That(SimulationConfig.CreateDefault(1).Reproduction, Is.False);
            Assert.That(() => new SimulationConfig(new SimulationSettings { Reproduction = true, PopulationCap = 10 }),
                Throws.ArgumentException.With.Message.Contains("PopulationCap"), "below the initial population");
            Assert.That(() => new SimulationConfig(new SimulationSettings { OffspringEnergyFraction = 1f }),
                Throws.ArgumentException.With.Message.Contains("OffspringEnergyFraction"));
            Assert.That(() => new SimulationConfig(new SimulationSettings { ReproductionCost = -1f }), Throws.ArgumentException);
            Assert.That(() => new SimulationConfig(new SimulationSettings { MaturitySeconds = 0f }), Throws.ArgumentException);
            var config = Rules(s => { s.MaturitySeconds = 1f; s.ReproductionCooldownSeconds = 2f; });
            Assert.That(config.MaturityTicks, Is.EqualTo(20));
            Assert.That(config.ReproductionCooldownTicks, Is.EqualTo(40));

            var evolution = new EvolutionSettings();
            evolution.World.Reproduction = true;
            Assert.That(new EvaluationPlan(evolution).EpisodeConfig(3).Reproduction, Is.False, "isolated episodes never do");
        }

        [Test]
        public void Births_NeedMaturityEnergyAndAnEndedCooldown_AndCreateNoEnergy()
        {
            var world = new WorldState(Rules(s => { s.MaturitySeconds = 1f; s.ReproductionCooldownSeconds = 2f; }));
            var config = world.Config;
            var rich = world.AddCreature(new Float2(10f, 10f), 0f, 80f);
            var poor = world.AddCreature(new Float2(20f, 10f), 0f, 60f);
            var other = world.AddCreature(new Float2(30f, 10f), 0f, 90f);
            var simulation = new WorldSimulation(world);
            var idle = new ConstantController(Idle);

            for (var t = 0; t < config.MaturityTicks - 1; t++)
            {
                simulation.Step(idle);
                Assert.That(simulation.LastBirths, Is.Empty, "not mature yet");
            }

            var before = TotalEnergy(world);
            simulation.Step(idle);
            Assert.That(simulation.LastBirths.Select(b => (b.Child.Value, b.Parent.Value)), Is.EqualTo(new[] { (4, 1), (5, 3) }),
                "parents in id order; the one below 70% of its capacity waits");
            Assert.That(TotalEnergy(world), Is.EqualTo(before - 2f * config.ReproductionCost).Within(1e-4f), "no energy created");
            Assert.That(rich.Energy, Is.EqualTo(80f - 32f - 5f));
            Assert.That(poor.Energy, Is.EqualTo(60f));
            var child = world.Creatures[3];
            Assert.That(child.Energy, Is.EqualTo(32f), "40% of the parent's energy");
            Assert.That(child.Parent, Is.EqualTo(rich.Id));
            Assert.That(child.Generation, Is.EqualTo(1));
            Assert.That(child.AgeTicks, Is.Zero);
            Assert.That(rich.ReproductionCooldownTicks, Is.EqualTo(config.ReproductionCooldownTicks));
            Assert.That(other.Energy, Is.EqualTo(90f - 36f - 5f));

            rich.Energy = 100f;
            for (var t = 0; t < config.ReproductionCooldownTicks - 1; t++)
            {
                simulation.Step(idle);
                Assert.That(simulation.LastBirths.Any(b => b.Parent == rich.Id), Is.False, "cooldown");
            }

            simulation.Step(idle);
            Assert.That(simulation.LastBirths.Single().Parent, Is.EqualTo(rich.Id), "again once the cooldown ends");
            Assert.That(simulation.LastBirths.Single().Child.Value, Is.EqualTo(6), "ids are never reused");
            Assert.That(world.Creatures.Last().Generation, Is.EqualTo(1));
        }

        [Test]
        public void AtTheCap_BirthsAreRefusedWithoutCost_AndNobodyIsRemoved()
        {
            var world = new WorldState(Rules(s => { s.PopulationCap = 3; s.MaxAgeSeconds = 60f; }));
            var config = world.Config;
            var parents = Enumerable.Range(0, 3).Select(i => world.AddCreature(new Float2(10f + 10f * i, 10f), 0f, 80f)).ToArray();
            foreach (var parent in parents)
                parent.AgeTicks = config.MaturityTicks;
            var simulation = new WorldSimulation(world);
            var idle = new ConstantController(Idle);

            simulation.Step(idle);
            Assert.That(simulation.LastBirths, Is.Empty);
            Assert.That(simulation.LastBirthsRefused, Is.EqualTo(3));
            Assert.That(parents.Select(p => p.Energy), Is.All.EqualTo(80f), "a refused birth costs nothing");
            Assert.That(parents.Select(p => p.ReproductionCooldownTicks), Is.All.Zero);
            Assert.That(world.Creatures.Count, Is.EqualTo(3), "nobody is discarded to make room");

            parents[0].AgeTicks = config.MaxAgeTicks - 1;
            simulation.Step(idle);
            Assert.That(simulation.LastDeaths.Single().Creature, Is.EqualTo(parents[0].Id));
            Assert.That(simulation.LastBirths.Single().Parent, Is.EqualTo(parents[1].Id), "the room left by a death");
            Assert.That(simulation.LastBirthsRefused, Is.EqualTo(1));
            Assert.That(world.Creatures.Count, Is.EqualTo(3));
        }

        [Test]
        public void Children_AreBornNearTheirParent_InsideTheArena()
        {
            var world = new WorldState(Rules(s => { s.ReproductionCooldownSeconds = 0f; s.BirthDistance = 2f; }));
            var config = world.Config;
            var corner = world.AddCreature(new Float2(0f, 0f), 0f, 100f);
            corner.AgeTicks = config.MaturityTicks;
            var simulation = new WorldSimulation(world);
            for (var i = 0; i < 30; i++)
            {
                corner.Energy = 100f;
                simulation.Step(new ConstantController(Idle));
                var child = world.Creatures.Last();
                Assert.That(child.Parent, Is.EqualTo(corner.Id));
                Assert.That(child.Position.X, Is.InRange(0f, config.ArenaWidth));
                Assert.That(child.Position.Y, Is.InRange(0f, config.ArenaHeight));
                var dx = child.Position.X - corner.Position.X;
                var dy = child.Position.Y - corner.Position.Y;
                Assert.That(Math.Sqrt(dx * dx + dy * dy), Is.LessThanOrEqualTo(config.BirthDistance + 1e-4));
            }

            Assert.That(world.Creatures.Select(c => c.Id.Value), Is.Ordered.And.Unique);
        }

        [Test]
        public void Heredity_GivesTheChildsGenes_AndWithoutItTheChildCopiesItsParent()
        {
            var world = new WorldState(Rules());
            var genes = new BodyGenes(0.5f, -0.25f, 0f, 0.75f);
            var parent = world.AddCreature(new Float2(10f, 10f), 0f, 100f, genes);
            parent.AgeTicks = world.Config.MaturityTicks;
            var simulation = new WorldSimulation(world);
            simulation.Step(new ConstantController(Idle));
            var copy = world.Creatures.Last();
            Assert.That(copy.Genes, Is.EqualTo(genes));
            Assert.That(copy.Body, Is.EqualTo(CreatureBody.Express(world.Config, genes)));

            var config = Rules(s => { s.InitialPopulation = 2; s.BirthMutationProbability = 1f; s.BirthMutationSigma = 0.3f; });
            var founders = InitialGenomes.Population(9, 2, BodyGeneSet.All);
            SharedGenomes Fresh() => new SharedGenomes(config, id => founders[id.Value - 1], BodyGeneSet.Sensor);
            var mother = new CreatureState(new CreatureId(2), Float2.Zero, 0f, 50f, founders[1].Body.Masked(BodyGeneSet.Sensor),
                CreatureBody.Express(config, founders[1].Body.Masked(BodyGeneSet.Sensor)));
            var first = Fresh();
            var expressed = first.Conceive(mother, new CreatureId(7));
            var again = Fresh();
            again.Conceive(mother, new CreatureId(3));
            again.Conceive(mother, new CreatureId(7));
            Assert.That(again.GenomeOf(new CreatureId(7)).HasSameContent(first.GenomeOf(new CreatureId(7))), Is.True,
                "a child's draws depend on its own id only");
            Assert.That(first.GenomeOf(new CreatureId(7)).HasSameContent(founders[1]), Is.False, "mutated");
            Assert.That(first.GenomeOf(new CreatureId(2)), Is.SameAs(founders[1]), "the parent is untouched");
            Assert.That(expressed, Is.EqualTo(first.GenomeOf(new CreatureId(7)).Body.Masked(BodyGeneSet.Sensor)));
            Assert.That(first.GenomeOf(new CreatureId(7)).Body.Size, Is.EqualTo(founders[1].Body.Size), "only expressed genes mutate");
            Assert.That(() => first.Conceive(mother, new CreatureId(7)), Throws.InvalidOperationException, "once per child");

            first.Forget(new[] { new CreatureDeath(new CreatureId(7), 5, DeathCause.Starvation) });
            Assert.That(first.BornCount, Is.Zero);
            Assert.That(() => first.GenomeOf(new CreatureId(7)), Throws.InvalidOperationException);
        }

        [Test]
        public void SharedWorldWithBirths_IsReproducible_AndResumesExactlyFromACheckpoint()
        {
            var settings = Shared();
            var config = new SimulationConfig(settings);
            var (world, brains, genomes) = NeuralWorld(config);
            Run(world, brains, genomes, 180);
            Assert.That(world.World.NextCreatureId - 1, Is.GreaterThan(config.InitialPopulation), "children were born");
            Assert.That(world.World.Creatures.Any(c => c.Generation >= 1), Is.True);

            var (twin, twinBrains, twinGenomes) = NeuralWorld(config);
            Run(twin, twinBrains, twinGenomes, 180);
            Assert.That(twin.ComputeFingerprint(), Is.EqualTo(world.ComputeFingerprint()), "same seed, same run");

            var checkpoint = new Checkpoint("shared", "minimal", settings.Seed, "neural", null,
                new SharedWorldSnapshot(settings, world.Capture(), brains.Capture(), genomes.Capture()));
            var text = CheckpointCodec.Encode(checkpoint, "0.1.0", "test", new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc));
            Assert.That(text, Does.Contain("\"reproduction\": {").And.Contain("\"bornGenomes\": [").And.Contain("\"parent\": "));
            var saved = CheckpointCodec.Decode(text, out _).SharedWorld;
            Assert.That(saved.Settings.Reproduction, Is.True);

            var restoredConfig = new SimulationConfig(saved.Settings);
            var restored = WorldSimulation.Restore(restoredConfig, saved.World);
            var restoredGenomes = new SharedGenomes(restoredConfig, id => InitialGenomes.RandomFor(restoredConfig, id),
                BodyGeneSet.None);
            restoredGenomes.Restore(saved.BornGenomes, restored.World);
            restored.Heredity = restoredGenomes;
            var restoredBrains = new BrainController(restoredConfig, id => new MlpBrain(restoredGenomes.GenomeOf(id)));
            restoredBrains.Restore(saved.Brains);

            Run(world, brains, genomes, 300);
            Run(restored, restoredBrains, restoredGenomes, 300);
            Assert.That(restored.ComputeFingerprint(), Is.EqualTo(world.ComputeFingerprint()));
            Assert.That(restoredGenomes.Capture().Select(g => g.Creature), Is.EqualTo(genomes.Capture().Select(g => g.Creature)));

            Assert.That(() => new SharedGenomes(restoredConfig, id => null, BodyGeneSet.None)
                    .Restore(saved.BornGenomes.Skip(1).ToArray(), WorldSimulation.Restore(restoredConfig, saved.World).World),
                Throws.ArgumentException, "every living child needs its genome");
        }

        [Test]
        public void WithoutFood_APopulationThatReproducesStillDiesOut_WithNoReplacement()
        {
            var settings = Shared();
            settings.InitialFood = 0;
            settings.MaxAgeSeconds = 30f;
            var config = new SimulationConfig(settings);
            var (world, brains, genomes) = NeuralWorld(config);
            for (var t = 0; t < 20 * 200 && !world.IsExtinct; t++)
                Run(world, brains, genomes, 1);
            Assert.That(world.IsExtinct, Is.True);
            Assert.That(world.World.NextCreatureId - 1, Is.GreaterThan(config.InitialPopulation), "births happened before");
        }

        private static SimulationConfig Rules(Action<SimulationSettings> configure = null)
        {
            var settings = new SimulationSettings
            {
                Seed = 4, InitialPopulation = 0, InitialFood = 0, Reproduction = true, MetabolismPerSecond = 0f,
                MovementCostPerSecond = 0f, SensorCostPerSecond = 0f, MaturitySeconds = 1f, MaxAgeSeconds = 600f
            };
            configure?.Invoke(settings);
            return new SimulationConfig(settings);
        }

        // Mature after 2 s with most of the initial energy (50 of 100) still above the 30% threshold: births come quickly.
        private static SimulationSettings Shared() =>
            new SimulationSettings
            {
                Seed = 21, InitialPopulation = 12, InitialFood = 60, FoodRespawnSeconds = 2f, MaxAgeSeconds = 40f,
                Reproduction = true, PopulationCap = 30, MaturitySeconds = 2f, ReproductionEnergyFraction = 0.3f,
                ReproductionCooldownSeconds = 3f
            };

        private static (WorldSimulation, BrainController, SharedGenomes) NeuralWorld(SimulationConfig config)
        {
            var world = new WorldSimulation(WorldFactory.CreateInitial(config));
            var genomes = new SharedGenomes(config, id => InitialGenomes.RandomFor(config, id), BodyGeneSet.None);
            world.Heredity = genomes;
            return (world, new BrainController(config, id => new MlpBrain(genomes.GenomeOf(id))), genomes);
        }

        private static void Run(WorldSimulation world, BrainController brains, SharedGenomes genomes, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                world.Step(brains);
                brains.Forget(world.LastDeaths);
                genomes.Forget(world.LastDeaths);
            }
        }

        private static float TotalEnergy(WorldState world) => world.Creatures.Sum(c => c.Energy);
    }
}
