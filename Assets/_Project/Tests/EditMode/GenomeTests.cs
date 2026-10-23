using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class GenomeTests
    {
        private static readonly NetworkTopology Topology = NetworkTopology.Mlp12x16x3;

        [Test]
        public void Topology_MatchesTheNetworkLayout()
        {
            Assert.That(Topology.ParameterCount, Is.EqualTo(259));
            Assert.That(Topology.ParameterCount, Is.EqualTo(NeuralLayout.ParameterCount));
            Assert.That(Topology.Inputs, Is.EqualTo(NeuralLayout.Inputs));
            Assert.That(Topology.Hidden, Is.EqualTo(NeuralLayout.Hidden));
            Assert.That(Topology.Outputs, Is.EqualTo(NeuralLayout.Outputs));
            Assert.That(Topology.ToString(), Is.EqualTo("mlp-12-16-3"));
        }

        [Test]
        public void InvalidWeights_AreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new Genome(Topology, null));
            Assert.Throws<ArgumentException>(() => new Genome(Topology, new float[258]));
            Assert.Throws<ArgumentException>(() => new Genome(Topology, With(10, float.NaN)));
            Assert.Throws<ArgumentException>(() => new Genome(Topology, With(10, float.PositiveInfinity)));
            Assert.Throws<ArgumentException>(() => new Genome(Topology, With(10, 3.01f)));
            Assert.Throws<ArgumentException>(() => new Genome(Topology, With(10, -3.01f)));
            Assert.DoesNotThrow(() => new Genome(Topology, With(10, Genome.MaxAbsWeight)));
            Assert.DoesNotThrow(() => new Genome(Topology, With(10, -Genome.MaxAbsWeight)));
        }

        [Test]
        public void Weights_AreCopiedInAndOut()
        {
            var source = MlpBrainTests.ReferenceParameters();
            var genome = new Genome(Topology, source);
            var expected = (float[])source.Clone();

            source[0] = 2.5f;
            var copy = genome.CopyWeights();
            copy[1] = -2.5f;

            Assert.That(genome.CopyWeights(), Is.EqualTo(expected));
            Assert.That(genome.GetWeight(0), Is.EqualTo(expected[0]));
            Assert.That(genome.SchemaVersion, Is.EqualTo(Genome.CurrentSchemaVersion));
        }

        [Test]
        public void RandomGenomes_AreReproducibleBoundedAndPerStream()
        {
            var first = Genome.CreateRandom(Topology, new Pcg32(5, 6), 1f);
            var again = Genome.CreateRandom(Topology, new Pcg32(5, 6), 1f);
            var other = Genome.CreateRandom(Topology, new Pcg32(5, 7), 1f);

            Assert.That(first.HasSameContent(again), Is.True);
            Assert.That(first.HasSameContent(other), Is.False);
            Assert.That(first.CopyWeights().All(w => w >= -1f && w <= 1f), Is.True);
            Assert.That(first.CopyWeights().Distinct().Count(), Is.GreaterThan(250));
            Assert.Throws<ArgumentOutOfRangeException>(() => Genome.CreateRandom(Topology, new Pcg32(1, 1), 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => Genome.CreateRandom(Topology, new Pcg32(1, 1), 4f));
        }

        [Test]
        public void Genome_HoldsNoIndividualStateAndCannotBeModified()
        {
            var properties = typeof(Genome).GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var individual = new[] { "Energy", "Age", "Position", "Velocity", "Heading", "State" };

            Assert.That(properties.Where(p => individual.Any(word => p.Name.Contains(word))), Is.Empty);
            Assert.That(properties.Where(p => p.CanWrite), Is.Empty, "no public setters");
            Assert.That(typeof(Genome).GetFields(BindingFlags.Public | BindingFlags.Instance), Is.Empty);
        }

        [Test]
        public void InitialGenomes_DependOnlyOnSeedAndCreatureId()
        {
            var large = SimulationConfig.CreateDefault(40);
            var small = new SimulationConfig(new SimulationSettings { Seed = 40, InitialPopulation = 10 });
            var otherSeed = SimulationConfig.CreateDefault(41);
            var id = new CreatureId(5);

            Assert.That(InitialGenomes.RandomFor(large, id).HasSameContent(InitialGenomes.RandomFor(small, id)), Is.True);
            Assert.That(InitialGenomes.RandomFor(large, id).HasSameContent(InitialGenomes.RandomFor(large, new CreatureId(6))),
                Is.False);
            Assert.That(InitialGenomes.RandomFor(large, id).HasSameContent(InitialGenomes.RandomFor(otherSeed, id)), Is.False);
        }

        [Test]
        public void MlpBrain_IsBuiltFromAGenomeOfTheRightTopology()
        {
            var genome = InitialGenomes.RandomFor(SimulationConfig.CreateDefault(1), new CreatureId(1));
            var brain = new MlpBrain(genome);

            Assert.That(brain.Genome, Is.SameAs(genome));
            Assert.That(brain.GetParameter(17), Is.EqualTo(genome.GetWeight(17)));
            var wrong = new NetworkTopology(12, 8, 3);
            Assert.Throws<ArgumentException>(() => new MlpBrain(new Genome(wrong, new float[wrong.ParameterCount])));
            Assert.Throws<ArgumentNullException>(() => new MlpBrain((Genome)null));
        }

        [Test]
        public void CreaturesSharingAGenome_DoNotShareMutableState()
        {
            var config = SimulationConfig.CreateDefault(2);
            var genome = InitialGenomes.RandomFor(config, new CreatureId(1));
            var controller = new BrainController(config, _ => new MlpBrain(genome));
            var observationA = MlpBrainTests.Reference;
            var observationB = new Observation(0.1f, 0.9f, -0.5f, 0.5f, 0f, 0f, 0f, 0f, 0.2f, 1f, 1f, 0.4f);

            var actionA = controller.Decide(new CreatureId(1), observationA);
            var actionB = controller.Decide(new CreatureId(2), observationB);
            controller.TryGetBrain(new CreatureId(1), out var brainA);
            controller.TryGetBrain(new CreatureId(2), out var brainB);

            Assert.That(brainA, Is.Not.SameAs(brainB), "one brain instance per creature");
            AssertSameAction(actionA, new MlpBrain(genome).Evaluate(observationA, null));
            AssertSameAction(actionB, new MlpBrain(genome).Evaluate(observationB, null));
            Assert.That(((MlpBrain)brainA).Genome, Is.SameAs(genome), "the immutable genome itself can be shared");
        }

        [Test]
        public void Living_NeverChangesTheGenomes()
        {
            var config = SimulationConfig.CreateDefault(3);
            var genomes = new Dictionary<CreatureId, Genome>();
            var snapshots = new Dictionary<CreatureId, float[]>();
            var controller = new BrainController(config, id =>
            {
                var genome = InitialGenomes.RandomFor(config, id);
                genomes.Add(id, genome);
                snapshots.Add(id, genome.CopyWeights());
                return new MlpBrain(genome);
            });
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(config));

            for (var t = 0; t < 300; t++)
            {
                simulation.Step(controller);
                controller.Forget(simulation.LastDeaths);
            }

            Assert.That(genomes.Count, Is.EqualTo(config.InitialPopulation));
            foreach (var pair in genomes)
                Assert.That(pair.Value.CopyWeights(), Is.EqualTo(snapshots[pair.Key]), $"genome of {pair.Key}");
        }

        [Test]
        public void NeuralRuns_AreReproducible()
        {
            Assert.That(RunNeural(9), Is.EqualTo(RunNeural(9)));
            Assert.That(RunNeural(10), Is.Not.EqualTo(RunNeural(9)));
        }

        private static ulong RunNeural(ulong seed)
        {
            var config = SimulationConfig.CreateDefault(seed);
            var controller = new BrainController(config, id => new MlpBrain(InitialGenomes.RandomFor(config, id)));
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(config));
            for (var t = 0; t < 400; t++)
            {
                simulation.Step(controller);
                controller.Forget(simulation.LastDeaths);
            }

            return simulation.ComputeFingerprint();
        }

        private static float[] With(int index, float value)
        {
            var weights = new float[Topology.ParameterCount];
            weights[index] = value;
            return weights;
        }

        private static void AssertSameAction(CreatureAction actual, CreatureAction expected)
        {
            Assert.That(actual.Thrust, Is.EqualTo(expected.Thrust));
            Assert.That(actual.Turn, Is.EqualTo(expected.Turn));
            Assert.That(actual.Strafe, Is.EqualTo(expected.Strafe));
        }
    }
}
