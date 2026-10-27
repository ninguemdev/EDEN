using System;
using System.Linq;
using Eden.Domain;
using Eden.Evolution;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>Species assignment, shared fitness, exact division of children, champions, stagnation and resume.</summary>
    public class NeatSpeciationTests
    {
        private static readonly NeuralContract Contract = NeuralContract.Legacy;
        private static readonly Genome A = Minimal(1);
        private static readonly Genome B = Minimal(2);
        private static readonly Genome C = Minimal(3);

        [Test]
        public void Assign_GroupsCloseGenomesAndNumbersSpeciesInOrder()
        {
            var loose = new NeatSpeciation(Rules());
            Assert.That(loose.Assign(new[] { A, B, C }, 0), Is.EqualTo(new[] { 1, 1, 1 }),
                "random minimal networks differ only in weights: δ ≈ 0.27 < 3");

            var strict = new NeatSpeciation(Rules(threshold: 1e-6f));
            Assert.That(strict.Assign(new[] { A, B, A, C }, 0), Is.EqualTo(new[] { 1, 2, 1, 3 }));
            Assert.That(strict.Species.Select(s => s.Members.ToArray()),
                Is.EqualTo(new[] { new[] { 0, 2 }, new[] { 1 }, new[] { 3 } }));

            Assert.That(strict.Assign(new[] { C, C }, 1), Is.EqualTo(new[] { 3, 3 }), "kept species id, species 1 and 2 empty");
            Assert.That(strict.Species.Select(s => s.Id), Is.EqualTo(new[] { 3 }));
            Assert.That(strict.NextSpeciesId, Is.EqualTo(4));
        }

        [Test]
        public void Breed_DividesChildrenBySharedFitness_WithChampionsFirst()
        {
            var speciation = new NeatSpeciation(Rules(threshold: 1e-6f, championSize: 1));
            var population = new[] { A, A, B };
            speciation.Assign(population, 0);

            // Shifted scores [0, 2, 4]: species 1 mean 1, species 2 mean 4 → 10 children split 2 / 8.
            var children = speciation.Breed(population, new[] { 1f, 3f, 5f }, 0, 10, History(), Random(1));

            Assert.That(children, Has.Length.EqualTo(10));
            Assert.That(children.Count(c => c.Species == 1), Is.EqualTo(2));
            Assert.That(children.Count(c => c.Species == 2), Is.EqualTo(8));
            Assert.That(children[0].IsChampion, Is.True);
            Assert.That(children[0].FirstParent, Is.EqualTo(1), "the best member of species 1");
            Assert.That(children[0].Genome, Is.SameAs(population[1]), "copied unchanged");
            Assert.That(children[2].IsChampion && children[2].FirstParent == 2, Is.True);
            Assert.That(children.Count(c => c.IsChampion), Is.EqualTo(2));
        }

        [Test]
        public void Breed_UsesLargestRemainders_AndSizesWhenNoScoreIsPositive()
        {
            var speciation = new NeatSpeciation(Rules(threshold: 1e-6f));
            var population = new[] { A, B, C };
            speciation.Assign(population, 0);
            var equal = speciation.Breed(population, new[] { 2f, 2f, 2f }, 0, 10, History(), Random(2));
            Assert.That(Counts(equal), Is.EqualTo(new[] { 4, 3, 3 }), "equal shares: the extra child goes to the lowest id");

            speciation = new NeatSpeciation(Rules(threshold: 1e-6f));
            population = new[] { A, A, A, B };
            speciation.Assign(population, 0);
            var invalid = speciation.Breed(population, new[] { float.NaN, Fitness.Worst, float.NaN, Fitness.Worst }, 0, 8,
                History(), Random(3));
            Assert.That(Counts(invalid), Is.EqualTo(new[] { 6, 2 }), "no valid score: by species size");
        }

        [Test]
        public void Breed_CrossesTheFitterParentFirst()
        {
            var speciation = new NeatSpeciation(Rules(crossover: 1f, survival: 1f));
            var population = new[] { A, B, C };
            var fitness = new[] { 1f, 3f, 2f };
            speciation.Assign(population, 0);
            var children = speciation.Breed(population, fitness, 0, 30, History(), Random(4));

            foreach (var child in children)
            {
                Assert.That(child.SecondParent, Is.GreaterThanOrEqualTo(0));
                Assert.That(fitness[child.FirstParent], Is.GreaterThanOrEqualTo(fitness[child.SecondParent]));
                Assert.That(child.Genome.Neat, Is.Not.Null);
            }
        }

        [Test]
        public void StagnantSpecies_StopBreeding_ButTheBestIsProtected()
        {
            var speciation = new NeatSpeciation(Rules(threshold: 1e-6f, stagnation: 3, protectedSpecies: 1));
            // Shifted scores [0, 4, 2]: both species share 2 on average; species 1 has the best fitness (5).
            var population = new[] { A, A, B };
            var fitness = new[] { 1f, 5f, 3f };
            var history = History();
            for (var generation = 0; generation < 3; generation++)
            {
                speciation.Assign(population, generation);
                var children = speciation.Breed(population, fitness, generation, 6, history, Random((ulong)generation));
                Assert.That(Counts(children), Is.EqualTo(new[] { 3, 3 }), $"generation {generation}: equal shares");
            }

            speciation.Assign(population, 3);
            var last = speciation.Breed(population, fitness, 3, 6, history, Random(9));
            Assert.That(last.All(c => c.Species == 1), Is.True, "3 generations without improvement: only the best breeds");
            Assert.That(speciation.Species.Select(s => s.Id), Is.EqualTo(new[] { 1 }), "the stagnant species is gone");
            Assert.That(speciation.Species[0].LastImprovedGeneration, Is.EqualTo(0));
            Assert.That(speciation.Species[0].BestFitness, Is.EqualTo(5f));
        }

        [Test]
        public void Breed_IsReproducible_AndCaptureRestoreContinuesTheSame()
        {
            var rules = Rules(threshold: 0.2f);
            var population = Enumerable.Range(0, 12).Select(i => Minimal(10 + (ulong)i)).ToArray();
            var fitness = Enumerable.Range(0, 12).Select(i => (float)Math.Sin(i)).ToArray();

            NeatOffspring[] Run(NeatSpeciation speciation)
            {
                speciation.Assign(population, 0);
                return speciation.Breed(population, fitness, 0, 12, History(), Random(5));
            }

            var first = new NeatSpeciation(rules);
            var second = new NeatSpeciation(rules);
            var a = Run(first);
            var b = Run(second);
            for (var i = 0; i < a.Length; i++)
            {
                Assert.That(b[i].Genome.HasSameContent(a[i].Genome), Is.True);
                Assert.That(b[i].FirstParent, Is.EqualTo(a[i].FirstParent));
                Assert.That(b[i].SecondParent, Is.EqualTo(a[i].SecondParent));
            }

            var next = a.Select(c => c.Genome).ToArray();
            var restored = NeatSpeciation.Restore(rules, first.Capture(), first.NextSpeciesId);
            Assert.That(restored.Assign(next, 1), Is.EqualTo(first.Assign(next, 1)));
            Assert.That(restored.Species.Select(s => s.BestFitness), Is.EqualTo(first.Species.Select(s => s.BestFitness)));

            Assert.That(() => NeatSpeciation.Restore(rules, first.Capture(), 1), Throws.ArgumentException,
                "ids must be below the next id");
        }

        private static int[] Counts(NeatOffspring[] children) =>
            children.GroupBy(c => c.Species).OrderBy(g => g.Key).Select(g => g.Count()).ToArray();

        private static Genome Minimal(ulong seed) =>
            new Genome(NeatNetwork.CreateMinimal(Contract, new Pcg32(seed, 1), 1f));

        private static InnovationHistory History() => new InnovationHistory(Contract);

        private static GaussianSampler Random(ulong seed) => new GaussianSampler(new Pcg32(seed, 21));

        private static NeatRules Rules(float threshold = 3f, int championSize = 5, float crossover = 0.75f,
            float survival = 0.2f, int stagnation = 15, int protectedSpecies = 2) =>
            new NeatRules(new NeatSettings
            {
                CompatibilityThreshold = threshold, ChampionMinimumSize = championSize, CrossoverProbability = crossover,
                SurvivalFraction = survival, StagnationGenerations = stagnation, ProtectedSpecies = protectedSpecies
            });
    }
}
