using System;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Persistence;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>
    /// Genome composition in the arena (P2.8): exact copies of each genome in free space, clones that share heredity and
    /// nothing else, frozen confrontation or mutating descendants with their lineage, refusals with a reason, the text
    /// that names a composition, and checkpoints.
    /// </summary>
    public class CompositionTests
    {
        private static readonly DateTime SavedAt = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void TwentyOfAAndFiveOfB_AreBornExactlySo_InFreeSpace()
        {
            var composition = new GenomeComposition(new[]
            {
                new CompositionEntry(MlpGenome(), 20, "a.json"), new CompositionEntry(NeatGenome(), 5, "b.json")
            }, true);
            var settings = Arena();
            composition.Apply(settings);
            var config = new SimulationConfig(settings);
            var world = composition.CreateWorld(config);

            Assert.That(world.Creatures.Count, Is.EqualTo(25));
            Assert.That(world.Creatures.Count(c => composition.EntryOf(c.Id) == 0), Is.EqualTo(20));
            Assert.That(world.Creatures.Count(c => composition.EntryOf(c.Id) == 1), Is.EqualTo(5));
            Assert.That(composition.EntryOf(new CreatureId(20)), Is.Zero);
            Assert.That(composition.EntryOf(new CreatureId(21)), Is.EqualTo(1));
            Assert.That(composition.CopyOf(new CreatureId(23)), Is.EqualTo(3));
            Assert.That(world.Creatures.Where(c => c.Id.Value > 20).All(c => c.Genes.Equals(NeatGenome().Body)), Is.True,
                "each copy has its genome's body");
            Assert.That(composition.Describe(), Is.EqualTo("a.json ×20 + b.json ×5 = 25"));

            foreach (var creature in world.Creatures)
            {
                foreach (var obstacle in config.Obstacles)
                    Assert.That(obstacle.DistanceTo(creature.Position), Is.GreaterThanOrEqualTo(creature.Body.Radius - 1e-4f));
                foreach (var other in world.Creatures.Where(o => o.Id.Value > creature.Id.Value))
                {
                    var dx = other.Position.X - creature.Position.X;
                    var dy = other.Position.Y - creature.Position.Y;
                    Assert.That(MathF.Sqrt(dx * dx + dy * dy),
                        Is.GreaterThanOrEqualTo(creature.Body.Radius + other.Body.Radius - 1e-4f), "nobody overlaps");
                }
            }
        }

        [Test]
        public void FrozenGenomes_NeverChange_AndClonesKeepTheirOwnState()
        {
            var a = MlpGenome();
            var composition = new GenomeComposition(new[] { new CompositionEntry(a, 6, "a") }, true);
            var settings = Arena();
            composition.Apply(settings);
            Assert.That(settings.Reproduction, Is.False, "frozen: no reproduction");
            var config = new SimulationConfig(settings);
            var simulation = new WorldSimulation(composition.CreateWorld(config));
            var genomes = composition.CreateGenomes(config);
            simulation.Heredity = genomes;
            var brains = new BrainController(config, id => GenomeBrains.Create(genomes.GenomeOf(id)));
            for (var i = 0; i < 200; i++)
                simulation.Step(brains);

            Assert.That(simulation.World.NextCreatureId, Is.EqualTo(7), "nobody is born");
            Assert.That(genomes.BornCount, Is.Zero);
            for (var id = 1; id <= 6; id++)
                Assert.That(genomes.GenomeOf(new CreatureId(id)), Is.SameAs(a), "the genome itself, unchanged");
            var positions = simulation.World.Creatures.Select(c => c.Position).Distinct().Count();
            Assert.That(positions, Is.EqualTo(simulation.World.Creatures.Count), "same heredity, separate lives");
        }

        [Test]
        public void WithDescendants_ChildrenMutate_AndTheGenealogyLeadsBackToTheirGenome()
        {
            var composition = new GenomeComposition(new[]
            {
                new CompositionEntry(MlpGenome(), 3, "a"), new CompositionEntry(NeatGenome(), 3, "b")
            }, false);
            var settings = Arena();
            settings.InitialEnergy = 100f;
            settings.MaturitySeconds = 0.5f;
            settings.ReproductionEnergyFraction = 0.5f;
            settings.ReproductionCooldownSeconds = 1f;
            settings.BirthMutationProbability = 1f;
            composition.Apply(settings);
            Assert.That(settings.Reproduction, Is.True);
            var config = new SimulationConfig(settings);
            Assert.That(composition.ExpressedBodyGenes, Is.EqualTo(BodyGeneSet.Size | BodyGeneSet.Speed),
                "only the genes some founder expresses mutate");
            var simulation = new WorldSimulation(composition.CreateWorld(config));
            var genomes = composition.CreateGenomes(config);
            simulation.Heredity = genomes;
            var genealogy = new Genealogy(simulation.World);
            var brains = new BrainController(config, id => GenomeBrains.Create(genomes.GenomeOf(id)));
            for (var i = 0; i < 100 && simulation.World.NextCreatureId <= 7; i++)
            {
                simulation.Step(brains);
                genealogy.Record(simulation);
            }

            var child = simulation.World.Creatures.First(c => c.Parent.HasValue);
            var line = genealogy.Ancestry(child.Id);
            var founder = line[line.Count - 1].Creature;
            Assert.That(founder.Value, Is.InRange(1, 6));
            var parentGenome = composition.GenomeOf(founder);
            Assert.That(genomes.GenomeOf(child.Id).HasSameContent(parentGenome), Is.False, "the child's genome mutated");
            Assert.That(genomes.GenomeOf(child.Id).Network, Is.EqualTo(parentGenome.Network), "of its founder's kind");
            Assert.That(composition.GenomeOf(founder), Is.SameAs(parentGenome), "founders keep theirs");
        }

        [Test]
        public void GenomesThatCannotSenseTheWorld_AreRefusedWithAReason()
        {
            var eyes = new Genome(NeatNetwork.CreateMinimal(NeuralContract.Eyes, new Pcg32(4, 1), 1f), BodyGenes.Neutral,
                EyeSet.Founder);
            var thirsty = new Genome(NeatNetwork.CreateMinimal(NeuralContract.EyesWater, new Pcg32(5, 1), 1f),
                BodyGenes.Neutral, EyeSet.Founder);
            var composition = new GenomeComposition(new[]
            {
                new CompositionEntry(MlpGenome(), 2, "legacy"), new CompositionEntry(eyes, 2, "eyes"),
                new CompositionEntry(thirsty, 2, "thirsty")
            }, true);

            var wet = Arena();
            wet.Water = WaterLayouts.Create(WaterLayout.Pond, wet);
            composition.Apply(wet);
            var refusals = composition.Refusals(new SimulationConfig(wet));
            Assert.That(refusals, Has.Count.EqualTo(1).And.Some.StartsWith("eyes: sees with eyes-v1"));
            Assert.That(() => composition.CreateWorld(new SimulationConfig(wet)),
                Throws.ArgumentException.With.Message.Contains("needs eyes-water-v1 (it has water)"));

            var dry = Arena();
            composition.Apply(dry);
            Assert.That(composition.Refusals(new SimulationConfig(dry)), Has.Count.EqualTo(1)
                .And.Some.StartsWith("thirsty: sees with eyes-water-v1"));

            var fine = new GenomeComposition(new[] { new CompositionEntry(MlpGenome(), 2, "a"),
                new CompositionEntry(eyes, 2, "eyes") }, false);
            var settings = Arena();
            fine.Apply(settings);
            var world = fine.CreateWorld(new SimulationConfig(settings));
            Assert.That(world.Creatures.Count(c => c.Eyes != null), Is.EqualTo(2), "eyes come with their genome");
            settings.Reproduction = false;
            Assert.That(() => fine.CreateWorld(new SimulationConfig(settings)),
                Throws.ArgumentException.With.Message.Contains("Descendants need reproduction"));
            Assert.That(() => new CompositionEntry(MlpGenome(), 0, "x"), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void TheText_NamesGenomesByFileOrRank_AndRefusesWhatItCannotRead()
        {
            var names = new[] { "alpha.json", "beta.json", "gamma.json" };
            var genomes = new[] { MlpGenome(1), MlpGenome(2), NeatGenome() };
            var fitness = new[] { 1f, 3f, 2f };
            var composition = GenomeComposition.Parse("alpha*20; #1×5\n gamma", names, genomes, fitness, true);
            Assert.That(composition.Entries.Select(e => (e.Source, e.Count)),
                Is.EqualTo(new[] { ("alpha.json", 20), ("beta.json", 5), ("gamma.json", 1) }));
            Assert.That(composition.Entries[1].Genome, Is.SameAs(genomes[1]), "#1 is the highest fitness");
            Assert.That(composition.Total, Is.EqualTo(26));

            var everyone = GenomeComposition.Parse("  ", names, genomes, fitness, false);
            Assert.That(everyone.Entries.Select(e => e.Count), Is.EqualTo(new[] { 1, 1, 1 }));
            Assert.That(everyone.Frozen, Is.False);

            Assert.That(() => GenomeComposition.Parse("delta*2", names, genomes, fitness, true),
                Throws.ArgumentException.With.Message.Contains("no genome named delta"));
            Assert.That(() => GenomeComposition.Parse("alpha*0", names, genomes, fitness, true),
                Throws.ArgumentException.With.Message.Contains("whole number from 1"));
            Assert.That(() => GenomeComposition.Parse("#4", names, genomes, fitness, true),
                Throws.ArgumentException.With.Message.Contains("#1 to #3"));
            Assert.That(() => GenomeComposition.Parse("alpha*5000", names, genomes, fitness, true),
                Throws.ArgumentException.With.Message.Contains("exceed the limit"));
        }

        [Test]
        public void AWorldByComposition_ResumesExactly_FromACheckpoint()
        {
            var composition = new GenomeComposition(new[]
            {
                new CompositionEntry(MlpGenome(), 4, "a.json"), new CompositionEntry(NeatGenome(), 2, "b.json")
            }, false);
            var settings = Arena();
            settings.MaturitySeconds = 1f;
            settings.ReproductionEnergyFraction = 0.3f;
            composition.Apply(settings);
            var config = new SimulationConfig(settings);
            var original = new WorldSimulation(composition.CreateWorld(config));
            var genomes = composition.CreateGenomes(config);
            original.Heredity = genomes;
            var brains = new BrainController(config, id => GenomeBrains.Create(genomes.GenomeOf(id)));
            for (var i = 0; i < 60; i++)
                original.Step(brains);

            var text = CheckpointCodec.Encode(new Checkpoint("shared", "minimal", 1, "composition", null,
                new SharedWorldSnapshot(settings, original.Capture(), brains.Capture(), genomes.Capture(), null,
                    composition)), "0.1.0", "test", SavedAt);
            Assert.That(text, Does.Contain("\"composition\"").And.Contain("\"source\": \"b.json\""));
            var loaded = CheckpointCodec.Decode(text, out _).SharedWorld;
            var restoredComposition = loaded.Composition;
            Assert.That(restoredComposition.Frozen, Is.False);
            Assert.That(restoredComposition.Entries.Select(e => (e.Source, e.Count)),
                Is.EqualTo(new[] { ("a.json", 4), ("b.json", 2) }));
            Assert.That(restoredComposition.Entries[1].Genome.HasSameContent(NeatGenome()), Is.True);

            var restoredConfig = new SimulationConfig(loaded.Settings);
            var restored = WorldSimulation.Restore(restoredConfig, loaded.World);
            var restoredGenomes = restoredComposition.CreateGenomes(restoredConfig);
            restoredGenomes.Restore(loaded.BornGenomes, restored.World);
            restored.Heredity = restoredGenomes;
            var restoredBrains = new BrainController(restoredConfig,
                id => GenomeBrains.Create(restoredGenomes.GenomeOf(id)));
            restoredBrains.Restore(loaded.Brains);
            for (var i = 0; i < 80; i++)
            {
                original.Step(brains);
                restored.Step(restoredBrains);
            }

            Assert.That(restored.ComputeFingerprint(), Is.EqualTo(original.ComputeFingerprint()));

            var plain = CheckpointCodec.Encode(new Checkpoint("shared", "minimal", 1, "heuristic", null,
                new SharedWorldSnapshot(new SimulationSettings { InitialPopulation = 2 },
                    new WorldSimulation(WorldFactory.CreateInitial(new SimulationConfig(
                        new SimulationSettings { InitialPopulation = 2 }))).Capture(), Array.Empty<BrainSlotState>())),
                "0.1.0", "test", SavedAt);
            Assert.That(plain, Does.Not.Contain("composition"));
        }

        private static SimulationSettings Arena()
        {
            var settings = new SimulationSettings { Seed = 9, InitialFood = 40, SolidBodies = true };
            settings.Obstacles = ObstacleLayouts.Create(ObstacleLayout.Pillars, settings);
            return settings;
        }

        private static Genome MlpGenome(ulong seed = 1) =>
            InitialGenomes.RandomFor(SimulationConfig.CreateDefault(seed), new CreatureId(1));

        private static Genome NeatGenome() =>
            new Genome(NeatNetwork.CreateMinimal(NeuralContract.Legacy, new Pcg32(3, 1), 1f),
                new BodyGenes(0.5f, -0.25f, 0f, 0f));
    }
}
