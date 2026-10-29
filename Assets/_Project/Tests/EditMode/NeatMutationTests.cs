using System;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>NEAT mutation: parents untouched, valid children, numbering shared within a generation, reproducibility.</summary>
    public class NeatMutationTests
    {
        private static readonly NeuralContract Contract = NeuralContract.Legacy;
        private static readonly int FirstOutput = NeatNetwork.OutputNodeId(Contract, 0);
        private static readonly int FirstHidden = NeatNetwork.FirstHiddenNodeId(Contract);

        [Test]
        public void Rules_RefuseImpossibleSettings()
        {
            Assert.That(new NeatRules(new NeatSettings()).AddNodeProbability, Is.EqualTo(0.03f));
            Assert.That(() => new NeatRules(new NeatSettings { AddNodeProbability = 1.5f }), Throws.ArgumentException);
            Assert.That(() => new NeatRules(new NeatSettings { WeightSigma = float.NaN }), Throws.ArgumentException);
            Assert.That(() => new NeatRules(new NeatSettings { InitialWeightLimit = 0f }), Throws.ArgumentException);
            Assert.That(() => new NeatRules(new NeatSettings { AddConnectionAttempts = 0 }), Throws.ArgumentException);
        }

        [Test]
        public void NothingHappens_WithZeroProbabilities_AndTheParentIsNeverChanged()
        {
            var parent = Minimal(1);
            var before = new Genome(new NeatNetwork(Contract, parent.Neat.Connections.ToArray()));
            var child = NeatMutation.Mutate(parent, Rules(), new InnovationHistory(Contract), Random(1), out var count);

            Assert.That(child.HasSameContent(parent), Is.True);
            Assert.That(count.MutatedWeights + count.AddedNodes + count.AddedConnections + count.ToggledConnections, Is.Zero);

            var mutated = NeatMutation.Mutate(parent, Rules(weights: 1f, addNode: 1f, addConnection: 1f, toggle: 1f),
                new InnovationHistory(Contract), Random(2), out _);
            Assert.That(mutated.HasSameContent(parent), Is.False);
            Assert.That(parent.HasSameContent(before), Is.True, "the parent keeps every gene");
        }

        [Test]
        public void Weights_ArePerturbedOrReplacedWithinTheirLimits()
        {
            var parent = Minimal(3);
            var perturbed = NeatMutation.Mutate(parent, Rules(weights: 1f), new InnovationHistory(Contract), Random(3),
                out var count);
            Assert.That(count.PerturbedWeights, Is.EqualTo(39));
            Assert.That(count.IsStructural, Is.False);
            for (var i = 0; i < 39; i++)
            {
                var gene = perturbed.Neat.Connections[i];
                Assert.That(gene.Innovation, Is.EqualTo(parent.Neat.Connections[i].Innovation));
                Assert.That(gene.Enabled, Is.True);
                Assert.That(Math.Abs(gene.Weight), Is.LessThanOrEqualTo(NeatNetwork.MaxAbsWeight));
            }

            Assert.That(perturbed.Neat.Connections.Select(c => c.Weight),
                Is.Not.EqualTo(parent.Neat.Connections.Select(c => c.Weight)));

            var replaced = NeatMutation.Mutate(parent, Rules(weights: 1f, perturb: 0f), new InnovationHistory(Contract),
                Random(4), out count);
            Assert.That(count.ReplacedWeights, Is.EqualTo(39));
            Assert.That(replaced.Neat.Connections.Select(c => c.Weight), Is.All.InRange(-1f, 1f));
        }

        [Test]
        public void AddNode_SplitsAnEnabledConnection()
        {
            var parent = Minimal(5);
            var history = new InnovationHistory(Contract);
            var child = NeatMutation.Mutate(parent, Rules(addNode: 1f), history, Random(5), out var count);

            Assert.That(count.AddedNodes, Is.EqualTo(1));
            var network = child.Neat;
            Assert.That(network.Connections.Count, Is.EqualTo(41));
            Assert.That(network.HiddenNodes, Is.EqualTo(new[] { FirstHidden }));
            var disabled = network.Connections.Single(c => !c.Enabled);
            var into = network.Connections.Single(c => c.To == FirstHidden);
            var outOf = network.Connections.Single(c => c.From == FirstHidden);
            Assert.That(into.From, Is.EqualTo(disabled.From));
            Assert.That(into.Weight, Is.EqualTo(1f));
            Assert.That(outOf.To, Is.EqualTo(disabled.To));
            Assert.That(outOf.Weight, Is.EqualTo(disabled.Weight));
            Assert.That(into.Innovation, Is.EqualTo(39));
            Assert.That(outOf.Innovation, Is.EqualTo(40));
            Assert.That(history.NextNodeId, Is.EqualTo(FirstHidden + 1));
        }

        [Test]
        public void SameSplitInOneGeneration_SharesNumbers_ALaterGenerationDoesNot()
        {
            // Two different genomes whose only enabled connection is innovation 4 (input 0 → output 1).
            var a = OnlyEnabled(4, Minimal(6));
            var b = OnlyEnabled(4, Minimal(7));
            var history = new InnovationHistory(Contract);

            var childA = NeatMutation.Mutate(a, Rules(addNode: 1f), history, Random(8), out _);
            var childB = NeatMutation.Mutate(b, Rules(addNode: 1f), history, Random(9), out _);
            Assert.That(childB.Neat.HiddenNodes, Is.EqualTo(childA.Neat.HiddenNodes));
            Assert.That(Innovations(childB), Is.EqualTo(Innovations(childA)));

            history.BeginGeneration(1);
            var later = NeatMutation.Mutate(b, Rules(addNode: 1f), history, Random(10), out _);
            Assert.That(later.Neat.HiddenNodes.Single(), Is.Not.EqualTo(childA.Neat.HiddenNodes.Single()));
            Assert.That(Innovations(later).Max(), Is.GreaterThan(Innovations(childA).Max()));
        }

        [Test]
        public void AddConnection_AddsANewAcyclicConnection_OrNothingWhenFull()
        {
            var history = new InnovationHistory(Contract);
            var minimal = Minimal(11);
            NeatMutation.Mutate(minimal, Rules(addConnection: 1f), history, Random(11), out var count);
            Assert.That(count.AddedConnections, Is.Zero, "every source already reaches every output");

            var withHidden = NeatMutation.Mutate(minimal, Rules(addNode: 1f), history, Random(12), out _);
            var grown = NeatMutation.Mutate(withHidden, Rules(addConnection: 1f), history, Random(13), out count);
            Assert.That(count.AddedConnections, Is.EqualTo(1));
            Assert.That(grown.Neat.Connections.Count, Is.EqualTo(withHidden.Neat.Connections.Count + 1));
            var added = grown.Neat.Connections.Last();
            Assert.That(added.Enabled, Is.True);
            Assert.That(added.From == FirstHidden || added.To == FirstHidden, Is.True, "only pairs with the hidden node are new");
            Assert.That(added.Innovation, Is.EqualTo(history.NextInnovation - 1));
        }

        [Test]
        public void Toggle_FlipsOneConnection()
        {
            var parent = Minimal(14);
            var child = NeatMutation.Mutate(parent, Rules(toggle: 1f), new InnovationHistory(Contract), Random(14),
                out var count);
            Assert.That(count.ToggledConnections, Is.EqualTo(1));
            Assert.That(child.Neat.EnabledConnectionCount, Is.EqualTo(38));
        }

        [Test]
        public void Mutation_IsReproducible()
        {
            var rules = Rules(weights: 0.8f, addNode: 0.5f, addConnection: 0.5f, toggle: 0.2f);
            Genome RunLineage(out InnovationHistory history)
            {
                history = new InnovationHistory(Contract);
                var genome = Minimal(15);
                var random = Random(15);
                for (var g = 1; g <= 30; g++)
                {
                    history.BeginGeneration(g);
                    genome = NeatMutation.Mutate(genome, rules, history, random, out _);
                }

                return genome;
            }

            var first = RunLineage(out var firstHistory);
            var second = RunLineage(out var secondHistory);
            Assert.That(second.HasSameContent(first), Is.True);
            Assert.That(secondHistory.NextInnovation, Is.EqualTo(firstHistory.NextInnovation));
            Assert.That(secondHistory.NextNodeId, Is.EqualTo(firstHistory.NextNodeId));
        }

        [Test]
        public void LongStructuralLineages_StayValidAndRunnable()
        {
            var rules = Rules(weights: 1f, addNode: 0.4f, addConnection: 0.6f, toggle: 0.3f);
            var history = new InnovationHistory(Contract);
            var random = Random(16);
            var genomes = Enumerable.Range(0, 8).Select(i => Minimal(100 + (ulong)i)).ToArray();
            var observation = new Observation(0.5f, 0.1f, 0.2f, -0.3f, 0.6f, 0.8f, 0.4f, 1f, 0.9f, 0.7f, 0.2f, 0.5f);
            for (var g = 1; g <= 60; g++)
            {
                history.BeginGeneration(g);
                for (var i = 0; i < genomes.Length; i++)
                    genomes[i] = NeatMutation.Mutate(genomes[i], rules, history, random, out _);
            }

            foreach (var genome in genomes)
            {
                // The constructor already refused any cycle or duplicate; the brain must still evaluate it.
                Assert.That(genome.Neat.HiddenNodes.Count, Is.GreaterThan(0));
                var action = new NeatBrain(genome).Evaluate(observation, null);
                Assert.That(float.IsNaN(action.Thrust) || float.IsNaN(action.Turn) || float.IsNaN(action.Strafe), Is.False);
            }
        }

        [Test]
        public void OnlyNeatGenomesMutateHere()
        {
            Assert.That(() => NeatMutation.Mutate(InitialGenomes.Population(1, 1)[0], Rules(),
                new InnovationHistory(Contract), Random(17), out _), Throws.ArgumentException);
        }

        private static Genome Minimal(ulong seed) =>
            new Genome(NeatNetwork.CreateMinimal(Contract, new Pcg32(seed, 1), 1f));

        private static Genome OnlyEnabled(int innovation, Genome genome) =>
            new Genome(new NeatNetwork(Contract,
                genome.Neat.Connections.Select(c => c.WithEnabled(c.Innovation == innovation)).ToArray()));

        private static int[] Innovations(Genome genome) => genome.Neat.Connections.Select(c => c.Innovation).ToArray();

        private static GaussianSampler Random(ulong seed) => new GaussianSampler(new Pcg32(seed, 9));

        private static NeatRules Rules(float weights = 0f, float perturb = 1f, float addNode = 0f, float addConnection = 0f,
            float toggle = 0f) =>
            new NeatRules(new NeatSettings
            {
                WeightMutationProbability = weights, WeightPerturbProbability = perturb, AddNodeProbability = addNode,
                AddConnectionProbability = addConnection, ToggleProbability = toggle
            });
    }
}
