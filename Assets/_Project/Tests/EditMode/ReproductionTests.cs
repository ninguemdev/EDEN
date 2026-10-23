using System;
using System.Linq;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class ReproductionTests
    {
        [Test]
        public void Rules_UseProposedDefaultsAndRejectInvalidSettings()
        {
            var rules = new ReproductionRules(new EvolutionSettings());
            Assert.That(rules.PopulationSize, Is.EqualTo(64));
            Assert.That(rules.EliteCount, Is.EqualTo(4));
            Assert.That(rules.TournamentSize, Is.EqualTo(3));
            Assert.That(rules.MutationProbability, Is.EqualTo(0.05f));
            Assert.That(rules.MutationSigma, Is.EqualTo(0.10f));

            var error = Assert.Throws<ArgumentException>(() => new ReproductionRules(new EvolutionSettings
            {
                PopulationSize = 0, EliteCount = -1, TournamentSize = 0, MutationProbability = float.NaN,
                MutationSigma = float.PositiveInfinity
            }));
            Assert.That(error.Message, Does.Contain("PopulationSize").And.Contain("EliteCount").And.Contain("TournamentSize")
                .And.Contain("MutationProbability").And.Contain("MutationSigma"));
            Assert.Throws<ArgumentException>(() => new ReproductionRules(new EvolutionSettings { PopulationSize = 3, EliteCount = 4 }));
            Assert.Throws<ArgumentException>(() => new ReproductionRules(new EvolutionSettings { MutationProbability = 1.5f }));
            Assert.Throws<ArgumentException>(() => new ReproductionRules(new EvolutionSettings { MutationSigma = -0.1f }));
            Assert.Throws<ArgumentOutOfRangeException>(() => rules.MutationRandom(0));
        }

        [Test]
        public void Breed_KeepsTheConfiguredPopulationSize()
        {
            var parents = InitialGenomes.Population(3, 10);
            var fitness = Enumerable.Range(0, 10).Select(i => (float)i).ToArray();

            foreach (var size in new[] { 4, 10, 25 })
            {
                var rules = Rules(size, elites: 4);
                var children = Reproduction.Breed(rules, parents, fitness, 1);
                Assert.That(children, Has.Length.EqualTo(size));
                Assert.That(children.Count(c => c.IsElite), Is.EqualTo(4));
                Assert.That(children.Select(c => c.Genome), Is.All.Not.Null);
                Assert.That(children.Select(c => c.ParentIndex), Is.All.InRange(0, 9));
            }
        }

        [Test]
        public void Elites_AreTheBestUnchangedGenomesInRankOrder()
        {
            var parents = InitialGenomes.Population(4, 8);
            var fitness = new[] { 0.5f, 2f, Fitness.Worst, 2f, float.NaN, 1f, 0f, 3f };

            Assert.That(Reproduction.Rank(fitness), Is.EqualTo(new[] { 7, 1, 3, 5, 0, 6, 2, 4 }),
                "higher first, ties by lower index, NaN and -inf last");

            var children = Reproduction.Breed(Rules(8, elites: 4), parents, fitness, 1);
            Assert.That(children.Take(4).Select(c => c.ParentIndex), Is.EqualTo(new[] { 7, 1, 3, 5 }));
            for (var e = 0; e < 4; e++)
            {
                Assert.That(children[e].IsElite, Is.True);
                Assert.That(children[e].MutatedWeights, Is.Zero);
                Assert.That(children[e].Genome.HasSameContent(parents[children[e].ParentIndex]), Is.True);
            }

            Assert.That(children.Skip(4).Select(c => c.IsElite), Is.All.False);
        }

        [Test]
        public void ParentsAndElites_AreNotChangedThroughSharedMemory()
        {
            var parents = InitialGenomes.Population(5, 6);
            var before = parents.Select(p => p.CopyWeights()).ToArray();
            var fitness = new[] { 1f, 5f, 2f, 4f, 3f, 0f };

            var children = Reproduction.Breed(Rules(12, elites: 2, probability: 1f, sigma: 0.5f), parents, fitness, 1);

            for (var i = 0; i < parents.Length; i++)
                Assert.That(parents[i].CopyWeights(), Is.EqualTo(before[i]), $"parent {i} changed");
            foreach (var child in children.Where(c => !c.IsElite))
            {
                Assert.That(child.MutatedWeights, Is.EqualTo(NeuralWeights));
                Assert.That(child.Genome, Is.Not.SameAs(parents[child.ParentIndex]));
                Assert.That(child.Genome.HasSameContent(parents[child.ParentIndex]), Is.False);
            }

            // Changing what a child exposes never reaches its parent or the elites.
            var exposed = children[5].Genome.CopyWeights();
            exposed[0] = 2.5f;
            Assert.That(parents[children[5].ParentIndex].CopyWeights(), Is.EqualTo(before[children[5].ParentIndex]));
            Assert.That(children[0].Genome.CopyWeights(), Is.EqualTo(before[1]));
        }

        [Test]
        public void Mutation_KeepsWeightsFiniteAndWithinLimits()
        {
            var extreme = new float[NeuralWeights];
            for (var i = 0; i < extreme.Length; i++)
                extreme[i] = i % 2 == 0 ? Genome.MaxAbsWeight : -Genome.MaxAbsWeight;
            var parent = new Genome(NetworkTopology.Mlp12x16x3, extreme);
            var random = new GaussianSampler(new Pcg32(8, 3));

            var clamped = 0;
            for (var round = 0; round < 50; round++)
            {
                var child = Reproduction.Mutate(parent, 1f, 10f, random, out var mutated);
                Assert.That(mutated, Is.EqualTo(NeuralWeights));
                for (var i = 0; i < child.WeightCount; i++)
                {
                    var w = child.GetWeight(i);
                    Assert.That(float.IsNaN(w) || float.IsInfinity(w), Is.False);
                    Assert.That(w, Is.InRange(-Genome.MaxAbsWeight, Genome.MaxAbsWeight));
                    if (Math.Abs(w) == Genome.MaxAbsWeight)
                        clamped++;
                }
            }

            Assert.That(clamped, Is.GreaterThan(0), "the limit was actually exercised");
        }

        [Test]
        public void Mutation_MatchesTheConfiguredRateAndNoise()
        {
            var parent = new Genome(NetworkTopology.Mlp12x16x3, new float[NeuralWeights]);
            var random = new GaussianSampler(new Pcg32(12, 3));
            long changed = 0, total = 0;
            double sumSquares = 0;
            for (var round = 0; round < 800; round++)
            {
                var child = Reproduction.Mutate(parent, 0.05f, 0.10f, random, out var mutated);
                changed += mutated;
                total += NeuralWeights;
                for (var i = 0; i < child.WeightCount; i++)
                    sumSquares += child.GetWeight(i) * (double)child.GetWeight(i);
            }

            Assert.That((double)changed / total, Is.EqualTo(0.05).Within(0.003));
            Assert.That(Math.Sqrt(sumSquares / changed), Is.EqualTo(0.10).Within(0.004));

            var unchanged = Reproduction.Mutate(parent, 0f, 0.10f, random, out var none);
            Assert.That(none, Is.Zero);
            Assert.That(unchanged.HasSameContent(parent), Is.True);
        }

        [Test]
        public void Tournament_OfThreeFavorsTheBetterParents()
        {
            var parents = InitialGenomes.Population(6, 4);
            var fitness = new[] { 0f, 1f, 2f, 3f };
            var children = Reproduction.Breed(Rules(4000, elites: 0, probability: 0f), parents, fitness, 1);

            // With replacement, the k-th best of n wins with probability ((n-k+1)^3 - (n-k)^3) / n^3.
            var expected = new[] { 1 / 64.0, 7 / 64.0, 19 / 64.0, 37 / 64.0 };
            for (var p = 0; p < 4; p++)
            {
                var share = children.Count(c => c.ParentIndex == p) / 4000.0;
                Assert.That(share, Is.EqualTo(expected[p]).Within(0.025), $"parent {p}");
            }

            Assert.That(children.All(c => c.Genome.HasSameContent(parents[c.ParentIndex])), Is.True,
                "probability 0 copies the parent exactly");
        }

        [Test]
        public void Breed_IsDeterministicPerSeedAndGeneration()
        {
            var parents = InitialGenomes.Population(7, 8);
            var fitness = new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f };
            var rules = Rules(8, elites: 2, probability: 0.2f);

            var a = Reproduction.Breed(rules, parents, fitness, 3);
            var b = Reproduction.Breed(rules, parents, fitness, 3);
            var nextGeneration = Reproduction.Breed(rules, parents, fitness, 4);

            AssertSameOffspring(a, b);
            Assert.That(Enumerable.Range(2, 6).Any(i =>
                !a[i].Genome.HasSameContent(nextGeneration[i].Genome) || a[i].ParentIndex != nextGeneration[i].ParentIndex),
                Is.True, "each generation has its own stream");
        }

        [Test]
        public void Evolution_KeepsSizeReevaluatesElitesAndRepeatsWithTheSameSeed()
        {
            var first = new GenerationalEvolution(SmallExperiment(31));
            var second = new GenerationalEvolution(SmallExperiment(31));
            var other = new GenerationalEvolution(SmallExperiment(32));

            Assert.That(first.Generation, Is.Zero);
            Assert.That(first.Origins, Is.Empty);
            for (var g = 0; g < 3; g++)
            {
                var evaluation = first.Advance();
                var again = second.Advance();
                other.Advance();

                Assert.That(evaluation.Generation, Is.EqualTo(g));
                Assert.That(evaluation.Candidates.Select(c => c.MeanFitness), Is.EqualTo(again.Candidates.Select(c => c.MeanFitness)));
                Assert.That(first.Population.Count, Is.EqualTo(8));
                Assert.That(first.Generation, Is.EqualTo(g + 1));
                AssertSameOffspring(first.Origins.ToArray(), second.Origins.ToArray());

                // Elites carry no score: next generation evaluates them again on its own seeds.
                var elite = first.Origins[0];
                Assert.That(elite.IsElite && elite.ParentIndex == Reproduction.Rank(
                    evaluation.Candidates.Select(c => c.MeanFitness).ToArray())[0], Is.True);
            }

            var reevaluated = new IsolatedEvaluator(first.Plan).EvaluateTraining(first.Population, first.Generation);
            var advanced = first.Advance();
            Assert.That(advanced.Candidates.Select(c => c.MeanFitness), Is.EqualTo(reevaluated.Candidates.Select(c => c.MeanFitness)));
            Assert.That(Enumerable.Range(0, 8).Any(i => !first.Population[i].HasSameContent(other.Population[i])), Is.True);
        }

        [Test]
        public void Evolution_BreedsAFullPopulationWhenEveryoneDies()
        {
            var settings = SmallExperiment(40);
            settings.EpisodeSeconds = 60f;
            settings.World.InitialFood = 0;
            var evolution = new GenerationalEvolution(settings);

            var evaluation = evolution.Advance();

            Assert.That(evaluation.Candidates.Select(c => c.SurvivalRate), Is.All.EqualTo(0f));
            Assert.That(evaluation.Candidates.Select(c => c.MeanFitness), Is.All.EqualTo(0f));
            Assert.That(evolution.Population.Count, Is.EqualTo(8));
            Assert.That(evolution.Origins.Take(2).Select(o => o.ParentIndex), Is.EqualTo(new[] { 0, 1 }),
                "all tied: elites by index");
        }

        private const int NeuralWeights = 259;

        private static ReproductionRules Rules(int size, int elites, float probability = 0.05f, float sigma = 0.10f) =>
            new ReproductionRules(new EvolutionSettings
            {
                ExperimentSeed = 99, PopulationSize = size, EliteCount = elites, MutationProbability = probability,
                MutationSigma = sigma
            });

        private static EvolutionSettings SmallExperiment(ulong seed)
        {
            var settings = new EvolutionSettings
            {
                ExperimentSeed = seed, EpisodeSeconds = 10f, TrainingSeedsPerGeneration = 2, PopulationSize = 8,
                EliteCount = 2
            };
            settings.World.InitialFood = 64;
            return settings;
        }

        private static void AssertSameOffspring(Offspring[] actual, Offspring[] expected)
        {
            Assert.That(actual, Has.Length.EqualTo(expected.Length));
            for (var i = 0; i < actual.Length; i++)
            {
                Assert.That(actual[i].ParentIndex, Is.EqualTo(expected[i].ParentIndex));
                Assert.That(actual[i].IsElite, Is.EqualTo(expected[i].IsElite));
                Assert.That(actual[i].MutatedWeights, Is.EqualTo(expected[i].MutatedWeights));
                Assert.That(actual[i].Genome.HasSameContent(expected[i].Genome), Is.True, $"offspring {i}");
            }
        }
    }
}
