using System.Linq;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>Crossover aligned by innovation, inherited disabled genes and the compatibility distance.</summary>
    public class NeatCrossoverTests
    {
        private static readonly NeuralContract Contract = NeuralContract.Legacy;
        private static readonly int Out0 = NeatNetwork.OutputNodeId(Contract, 0);
        private static readonly int Out1 = NeatNetwork.OutputNodeId(Contract, 1);
        private static readonly int H0 = NeatNetwork.FirstHiddenNodeId(Contract);

        [Test]
        public void IdenticalParents_GiveTheSameGenome()
        {
            var parent = new Genome(NeatNetwork.CreateMinimal(Contract, new Pcg32(1, 1), 1f), new BodyGenes(0.5f, 0f, 0f, 0f));
            var child = NeatCrossover.Cross(parent, parent, Rules(), new Pcg32(2, 1));
            Assert.That(child.HasSameContent(parent), Is.True);
            Assert.That(NeatCrossover.Distance(parent, parent, Rules()), Is.EqualTo(0f));
        }

        [Test]
        public void Child_HasTheFitterParentsStructure_WithMatchingWeightsFromEither()
        {
            // Fitter: 1 → out0 (#3), 2 → out0 (#6) split by H0 (#39, #40). Other: 1 → out0 (#3), 2 → out0 (#6),
            // 3 → out1 (#10), 1 → out1 (#41).
            var fitter = Build(
                new NeatConnection(3, 1, Out0, 0.1f, true),
                new NeatConnection(6, 2, Out0, 0.2f, false),
                new NeatConnection(39, 2, H0, 1f, true),
                new NeatConnection(40, H0, Out0, 0.2f, true));
            var other = Build(
                new NeatConnection(3, 1, Out0, -0.9f, true),
                new NeatConnection(6, 2, Out0, -0.8f, true),
                new NeatConnection(10, 3, Out1, 0.7f, true),
                new NeatConnection(41, 1, Out1, 0.6f, true));

            var fromOther = 0;
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var child = NeatCrossover.Cross(fitter, other, Rules(), new Pcg32(seed, 3)).Neat;
                Assert.That(child.Connections.Select(c => c.Innovation), Is.EqualTo(new[] { 3, 6, 39, 40 }));
                Assert.That(child.Connections[0].Weight, Is.EqualTo(0.1f).Or.EqualTo(-0.9f));
                Assert.That(child.Connections[1].Weight, Is.EqualTo(0.2f).Or.EqualTo(-0.8f));
                Assert.That(child.Connections[2].Weight, Is.EqualTo(1f), "disjoint: from the fitter parent");
                Assert.That(child.Connections[3].Weight, Is.EqualTo(0.2f), "excess: from the fitter parent");
                if (child.Connections[0].Weight < 0f)
                    fromOther++;
            }

            Assert.That(fromOther, Is.InRange(5, 35), "matching genes come from either parent");
        }

        [Test]
        public void DisabledInEitherParent_StaysDisabledWithTheConfiguredChance()
        {
            var fitter = Build(new NeatConnection(3, 1, Out0, 0.1f, true));
            var other = Build(new NeatConnection(3, 1, Out0, 0.5f, false));

            var always = Rules(disabled: 1f);
            var never = Rules(disabled: 0f);
            for (ulong seed = 1; seed <= 20; seed++)
            {
                Assert.That(NeatCrossover.Cross(fitter, other, always, new Pcg32(seed, 4)).Neat.Connections[0].Enabled,
                    Is.False);
                Assert.That(NeatCrossover.Cross(other, fitter, never, new Pcg32(seed, 4)).Neat.Connections[0].Enabled,
                    Is.True);
            }
        }

        [Test]
        public void Cross_IsReproducible_AndRefusesMlpGenomes()
        {
            var a = new Genome(NeatNetwork.CreateMinimal(Contract, new Pcg32(5, 1), 1f));
            var b = new Genome(NeatNetwork.CreateMinimal(Contract, new Pcg32(6, 1), 1f));
            Assert.That(NeatCrossover.Cross(a, b, Rules(), new Pcg32(7, 1))
                .HasSameContent(NeatCrossover.Cross(a, b, Rules(), new Pcg32(7, 1))), Is.True);
            var mlp = InitialGenomes.Population(1, 1)[0];
            Assert.That(() => NeatCrossover.Cross(a, mlp, Rules(), new Pcg32(7, 1)), Throws.ArgumentException);
            Assert.That(() => NeatCrossover.Distance(mlp, a, Rules()), Throws.ArgumentException);
        }

        [Test]
        public void Distance_MatchesAHandCount()
        {
            // a: #1 #2 #4 #8; b: #1 #3 #4 #9 #10. Matching 1, 4; disjoint 2, 3, 8 (8 < b's max 10); excess 9, 10.
            var a = Build(
                new NeatConnection(1, 0, Out0, 0.5f, true),
                new NeatConnection(2, 0, Out1, 0f, true),
                new NeatConnection(4, 1, Out1, -1f, true),
                new NeatConnection(8, 2, Out1, 0f, true));
            var b = Build(
                new NeatConnection(1, 0, Out0, -0.5f, true),
                new NeatConnection(3, 1, Out0, 0f, true),
                new NeatConnection(4, 1, Out1, 1f, false),
                new NeatConnection(9, 3, Out0, 0f, true),
                new NeatConnection(10, 3, Out1, 0f, true));

            // Small genomes (< 20 connections): N = 1. W̄ = (1 + 2) / 2 = 1.5.
            var small = Rules(excess: 1f, disjoint: 2f, weight: 0.4f);
            Assert.That(NeatCrossover.Distance(a, b, small), Is.EqualTo(1f * 2 + 2f * 3 + 0.4f * 1.5f).Within(1e-5f));
            Assert.That(NeatCrossover.Distance(b, a, small), Is.EqualTo(NeatCrossover.Distance(a, b, small)), "symmetric");

            // Normalized: N = 5, the larger count.
            var normalized = Rules(excess: 1f, disjoint: 2f, weight: 0.4f, threshold: 0);
            Assert.That(NeatCrossover.Distance(a, b, normalized),
                Is.EqualTo(1f * 2 / 5 + 2f * 3 / 5 + 0.4f * 1.5f).Within(1e-5f));
        }

        private static Genome Build(params NeatConnection[] connections) =>
            new Genome(new NeatNetwork(Contract, connections));

        private static NeatRules Rules(float disabled = 0.75f, float excess = 1f, float disjoint = 1f, float weight = 0.4f,
            int threshold = 20) =>
            new NeatRules(new NeatSettings
            {
                DisabledInheritProbability = disabled, ExcessCoefficient = excess, DisjointCoefficient = disjoint,
                WeightDifferenceCoefficient = weight, NormalizationThreshold = threshold
            });
    }
}
