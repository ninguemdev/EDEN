using System;
using Eden.Brains;
using Eden.Domain;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>NEAT genomes: node ids from the contract, the minimal network, every validity rule and the MLP kept apart.</summary>
    public class NeatNetworkTests
    {
        private static readonly NeuralContract Contract = NeuralContract.Legacy;
        private static readonly int FirstOutput = NeatNetwork.OutputNodeId(Contract, 0);
        private static readonly int FirstHidden = NeatNetwork.FirstHiddenNodeId(Contract);

        [Test]
        public void NodeIds_FollowTheContract()
        {
            Assert.That(NeatNetwork.BiasNodeId, Is.EqualTo(0));
            Assert.That(NeatNetwork.InputNodeId(0), Is.EqualTo(1));
            Assert.That(NeatNetwork.InputNodeId(11), Is.EqualTo(12));
            Assert.That(FirstOutput, Is.EqualTo(13));
            Assert.That(NeatNetwork.OutputNodeId(Contract, 2), Is.EqualTo(15));
            Assert.That(FirstHidden, Is.EqualTo(16));
            Assert.That(NeatNetwork.MinimalConnectionCount(Contract), Is.EqualTo(39), "(12 inputs + bias) × 3 outputs");
        }

        [Test]
        public void CreateMinimal_ConnectsBiasAndInputsToEveryOutput()
        {
            var network = NeatNetwork.CreateMinimal(Contract, new Pcg32(5, 1), 1f);

            Assert.That(network.Connections.Count, Is.EqualTo(39));
            Assert.That(network.HiddenNodes, Is.Empty);
            Assert.That(network.EnabledConnectionCount, Is.EqualTo(39));
            for (var i = 0; i < network.Connections.Count; i++)
            {
                var gene = network.Connections[i];
                Assert.That(gene.Innovation, Is.EqualTo(i));
                Assert.That(gene.From, Is.EqualTo(i / 3), "source 0 = bias, 1 + i = input i");
                Assert.That(gene.To, Is.EqualTo(FirstOutput + i % 3));
                Assert.That(gene.Weight, Is.InRange(-1f, 1f));
                Assert.That(NeatNetwork.MinimalInnovation(Contract, gene.From, gene.To), Is.EqualTo(gene.Innovation));
            }

            Assert.That(network.KindOf(0), Is.EqualTo(NeatNodeKind.Bias));
            Assert.That(network.KindOf(5), Is.EqualTo(NeatNodeKind.Input));
            Assert.That(network.KindOf(14), Is.EqualTo(NeatNodeKind.Output));
            Assert.That(() => network.KindOf(FirstHidden), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(NeatNetwork.MinimalInnovation(Contract, FirstHidden, FirstOutput), Is.EqualTo(-1));
            Assert.That(NeatNetwork.MinimalInnovation(Contract, 1, 2), Is.EqualTo(-1), "input to input");
        }

        [Test]
        public void CreateMinimal_IsReproducibleFromTheSeed()
        {
            var a = NeatNetwork.CreateMinimal(Contract, new Pcg32(9, 3), 1f);
            var b = NeatNetwork.CreateMinimal(Contract, new Pcg32(9, 3), 1f);
            var c = NeatNetwork.CreateMinimal(Contract, new Pcg32(10, 3), 1f);
            Assert.That(a.HasSameContent(b), Is.True);
            Assert.That(a.HasSameContent(c), Is.False);
        }

        [Test]
        public void Network_KeepsConnectionsInInnovationOrderAndFindsHiddenNodes()
        {
            var hidden = FirstHidden + 4;
            var network = new NeatNetwork(Contract, new[]
            {
                new NeatConnection(41, hidden, FirstOutput, 0.5f, true),
                new NeatConnection(40, 3, hidden, 1f, true),
                new NeatConnection(9, 3, FirstOutput, -0.25f, false)
            });

            Assert.That(network.Connections[0].Innovation, Is.EqualTo(9));
            Assert.That(network.Connections[2].Innovation, Is.EqualTo(41));
            Assert.That(network.HiddenNodes, Is.EqualTo(new[] { hidden }));
            Assert.That(network.KindOf(hidden), Is.EqualTo(NeatNodeKind.Hidden));
            Assert.That(network.ContainsNode(hidden), Is.True);
            Assert.That(network.ContainsNode(hidden + 1), Is.False);
            Assert.That(network.EnabledConnectionCount, Is.EqualTo(2));
            Assert.That(network.HasConnection(3, hidden), Is.True);
            Assert.That(network.HasConnection(hidden, 3), Is.False);
            Assert.That(network.IndexOfInnovation(40), Is.EqualTo(1));
            Assert.That(network.IndexOfInnovation(10), Is.LessThan(0));
            Assert.That(network.WouldCreateCycle(hidden, 3), Is.True, "3 already reaches the hidden node");
            Assert.That(network.WouldCreateCycle(3, FirstOutput + 1), Is.False);
            Assert.That(network.WouldCreateCycle(hidden, hidden), Is.True);
        }

        [Test]
        public void Network_RefusesEveryBrokenRule()
        {
            var h1 = FirstHidden;
            var h2 = FirstHidden + 1;
            Assert.That(() => Build(new NeatConnection(-1, 1, FirstOutput, 0f, true)), Throws.ArgumentException,
                "negative innovation");
            Assert.That(() => Build(new NeatConnection(1, 1, FirstOutput, 0f, true),
                new NeatConnection(1, 2, FirstOutput, 0f, true)), Throws.ArgumentException, "repeated innovation");
            Assert.That(() => Build(new NeatConnection(1, FirstOutput, h1, 0f, true)), Throws.ArgumentException,
                "from an output");
            Assert.That(() => Build(new NeatConnection(1, 1, 2, 0f, true)), Throws.ArgumentException, "into an input");
            Assert.That(() => Build(new NeatConnection(1, 1, 0, 0f, true)), Throws.ArgumentException, "into the bias");
            Assert.That(() => Build(new NeatConnection(1, h1, h1, 0f, true)), Throws.ArgumentException, "self loop");
            Assert.That(() => Build(new NeatConnection(1, 1, FirstOutput, 0f, true),
                new NeatConnection(2, 1, FirstOutput, 0f, false)), Throws.ArgumentException, "same pair twice");
            Assert.That(() => Build(new NeatConnection(1, 1, FirstOutput, 3.5f, true)), Throws.ArgumentException,
                "weight out of range");
            Assert.That(() => Build(new NeatConnection(1, 1, FirstOutput, float.NaN, true)), Throws.ArgumentException,
                "NaN weight");
            Assert.That(() => Build(new NeatConnection(1, h1, h2, 0f, true), new NeatConnection(2, h2, h1, 0f, false)),
                Throws.ArgumentException.With.Message.Contains("cycle"), "a disabled connection still closes a cycle");
            Assert.That(() => new NeatNetwork(null, Array.Empty<NeatConnection>()), Throws.ArgumentNullException);
            Assert.That(Build().Connections, Is.Empty, "an empty network is valid: every output reads 0");
        }

        [Test]
        public void NeatGenome_IsAnotherFormatThanTheMlp()
        {
            var network = NeatNetwork.CreateMinimal(Contract, new Pcg32(2, 2), 1f);
            var genome = new Genome(network, new BodyGenes(0.5f, 0f, 0f, 0f));

            Assert.That(genome.Network, Is.EqualTo(NeatNetwork.Kind));
            Assert.That(genome.Neat, Is.SameAs(network));
            Assert.That(genome.Contract, Is.SameAs(Contract));
            Assert.That(genome.Plasticity.IsFixed, Is.True);
            Assert.That(() => genome.Topology, Throws.InvalidOperationException);
            Assert.That(() => genome.WeightCount, Throws.InvalidOperationException);
            Assert.That(() => genome.CopyWeights(), Throws.InvalidOperationException);
            Assert.That(() => genome.WithPlasticity(new PlasticityGenes(0.5f, 0.1f)), Throws.InvalidOperationException);
            Assert.That(genome.WithPlasticity(PlasticityGenes.Fixed), Is.SameAs(genome));

            var otherBody = genome.WithBody(BodyGenes.Neutral);
            Assert.That(otherBody.Neat, Is.SameAs(network));
            Assert.That(otherBody.HasSameContent(genome), Is.False);
            Assert.That(otherBody.HasSameContent(new Genome(network)), Is.True);

            var mlp = new Genome(NetworkTopology.Mlp12x16x3, new float[NetworkTopology.Mlp12x16x3.ParameterCount]);
            Assert.That(mlp.Neat, Is.Null);
            Assert.That(mlp.HasSameContent(new Genome(network)), Is.False);
            Assert.That(new Genome(network).HasSameContent(mlp), Is.False);
            Assert.That(() => new MlpBrain(new Genome(network)), Throws.ArgumentException.With.Message.Contains("neat"));
        }

        private static NeatNetwork Build(params NeatConnection[] connections) => new NeatNetwork(Contract, connections);
    }
}
