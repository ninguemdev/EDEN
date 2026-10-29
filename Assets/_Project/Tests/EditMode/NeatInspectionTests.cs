using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>What the lab shows of a NEAT network, and NEAT children in a shared world.</summary>
    public class NeatInspectionTests
    {
        private static readonly NeuralContract Contract = NeuralContract.Legacy;
        private static readonly int Turn = NeatNetwork.OutputNodeId(Contract, 1);
        private static readonly int H0 = NeatNetwork.FirstHiddenNodeId(Contract);

        private static readonly Observation Sample =
            new Observation(0.8f, 0.25f, 0.1f, -0.2f, 0.6f, 0.8f, 0.3f, 1f, 0.9f, 0.4f, 0.2f, 0.7f);

        [Test]
        public void Snapshot_PlacesNodesByDepth_AndMatchesTheBrain()
        {
            // input 0 → H0+3 → H0 → turn, plus a disabled input 1 → turn: H0+3 is column 1, H0 column 2, outputs 3.
            var brain = new NeatBrain(new Genome(new NeatNetwork(Contract, new[]
            {
                new NeatConnection(7, 2, Turn, 0.4f, false),
                new NeatConnection(50, 1, H0 + 3, 1.2f, true),
                new NeatConnection(51, H0 + 3, H0, -0.9f, true),
                new NeatConnection(52, H0, Turn, 1.5f, true)
            })));
            var snapshot = new NeatSnapshot();
            snapshot.Capture(new CreatureId(4), brain, Sample);

            Assert.That(snapshot.NodeCount, Is.EqualTo(brain.NodeIds.Count));
            Assert.That(snapshot.ColumnCount, Is.EqualTo(4));
            for (var s = 0; s < snapshot.NodeCount; s++)
            {
                var id = snapshot.NodeId(s);
                var expected = id == H0 + 3 ? 1 : id == H0 ? 2 : snapshot.Kind(s) == NeatNodeKind.Output ? 3 : 0;
                Assert.That(snapshot.Column(s), Is.EqualTo(expected), $"node {id}");
            }

            var values = new float[brain.NodeIds.Count];
            brain.ComputeActivations(Sample, values);
            for (var s = 0; s < values.Length; s++)
                Assert.That(snapshot.Value(s), Is.EqualTo(values[s]));
            var action = brain.Evaluate(Sample, null);
            Assert.That(snapshot.Action.Turn, Is.EqualTo(action.Turn));
            Assert.That(snapshot.ConnectionCount, Is.EqualTo(4));
            Assert.That(snapshot.Connection(0).Enabled, Is.False);

            var version = snapshot.StructureVersion;
            snapshot.Capture(new CreatureId(4), brain, Sample);
            Assert.That(snapshot.StructureVersion, Is.EqualTo(version), "the same brain keeps its layout");
            snapshot.Capture(new CreatureId(5), new NeatBrain(new Genome(NeatNetwork.CreateMinimal(Contract, new Pcg32(1, 1), 1f))),
                Sample);
            Assert.That(snapshot.StructureVersion, Is.GreaterThan(version));
            Assert.That(snapshot.ColumnCount, Is.EqualTo(2), "a minimal network: inputs and outputs");
        }

        [Test]
        public void MutateWeights_KeepsTheStructure()
        {
            var parent = new Genome(NeatNetwork.CreateMinimal(Contract, new Pcg32(2, 1), 1f), new BodyGenes(0.5f, 0f, 0f, 0f));
            var child = NeatMutation.MutateWeights(parent, 1f, 0.5f, BodyGeneSet.Size, new GaussianSampler(new Pcg32(3, 1)),
                out var mutated);
            Assert.That(mutated, Is.EqualTo(39));
            Assert.That(child.Neat.Connections.Select(c => (c.Innovation, c.From, c.To, c.Enabled)),
                Is.EqualTo(parent.Neat.Connections.Select(c => (c.Innovation, c.From, c.To, c.Enabled))));
            Assert.That(child.Neat.Connections.All(c => System.Math.Abs(c.Weight) <= NeatNetwork.MaxAbsWeight), Is.True);
            Assert.That(child.Body[BodyGene.Size], Is.Not.EqualTo(0.5f));

            var same = NeatMutation.MutateWeights(parent, 0f, 0.5f, BodyGeneSet.Size, new GaussianSampler(new Pcg32(3, 1)),
                out mutated);
            Assert.That(same.HasSameContent(parent), Is.True);
            Assert.That(mutated, Is.Zero);
        }

        [Test]
        public void SharedWorld_NeatChildrenInheritTheStructure()
        {
            var settings = new SimulationSettings { InitialPopulation = 2, Reproduction = true, Seed = 5 };
            var config = new SimulationConfig(settings);
            var founders = new[]
            {
                new Genome(NeatNetwork.CreateMinimal(Contract, new Pcg32(4, 1), 1f)),
                new Genome(NeatNetwork.CreateMinimal(Contract, new Pcg32(5, 1), 1f))
            };
            var genomes = new SharedGenomes(config, id => founders[id.Value - 1], BodyGeneSet.None);
            var world = WorldFactory.CreateInitial(config);

            genomes.Conceive(world.Creatures[0], new CreatureId(3));
            var child = genomes.GenomeOf(new CreatureId(3));
            Assert.That(child.Network, Is.EqualTo(NeatNetwork.Kind));
            Assert.That(child.Neat.Connections.Select(c => c.Innovation),
                Is.EqualTo(founders[0].Neat.Connections.Select(c => c.Innovation)));
            Assert.That(GenomeBrains.Create(child), Is.TypeOf<NeatBrain>());
        }
    }
}
