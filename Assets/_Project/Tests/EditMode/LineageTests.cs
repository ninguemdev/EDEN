using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class LineageTests
    {
        [Test]
        public void Founders_GetAscendingIdsWithoutParents()
        {
            var lineage = new Lineage();
            var founders = lineage.Founders(InitialGenomes.Population(1, 5));

            Assert.That(founders.Select(f => f.Id), Is.EqualTo(new long[] { 1, 2, 3, 4, 5 }));
            Assert.That(founders.Select(f => f.ParentIds.Count), Is.All.Zero);
            Assert.That(founders.Select(f => f.BirthGeneration), Is.All.Zero);
            Assert.That(lineage.NextId, Is.EqualTo(6));
            Assert.That(lineage.Ancestry(3), Is.EqualTo(new long[] { 3 }));
        }

        [Test]
        public void Descend_GivesNewIdsToChildrenAndKeepsElites()
        {
            var lineage = new Lineage();
            var parents = lineage.Founders(InitialGenomes.Population(2, 4));
            var offspring = new[]
            {
                new Offspring(parents[2].Genome, 2, true, 0),
                new Offspring(Child(parents[2].Genome), 2, false, 3),
                new Offspring(Child(parents[0].Genome), 0, false, 1),
                new Offspring(Child(parents[2].Genome), 2, false, 2)
            };

            var children = lineage.Descend(parents, offspring, 1);

            Assert.That(children[0], Is.SameAs(parents[2]), "an elite is the same individual");
            Assert.That(children.Skip(1).Select(c => c.Id), Is.EqualTo(new long[] { 5, 6, 7 }));
            Assert.That(children.Skip(1).Select(c => c.ParentIds.Single()), Is.EqualTo(new long[] { 3, 1, 3 }));
            Assert.That(children.Skip(1).Select(c => c.BirthGeneration), Is.All.EqualTo(1));
            Assert.That(children.Skip(1).Select(c => c.MutatedWeights), Is.EqualTo(new[] { 3, 1, 2 }));
            Assert.That(children[1].Genome, Is.SameAs(offspring[1].Genome));
            Assert.That(lineage.ParentIds(6), Is.EqualTo(new long[] { 1 }));
            Assert.That(lineage.BirthGeneration(7), Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => new Lineage().Descend(parents, offspring, 1),
                "parents must come from the same lineage");
        }

        [Test]
        public void Evolution_IdsAreUniqueAndAncestryIsTraceableFromAnyGenome()
        {
            var evolution = new GenerationalEvolution(SmallExperiment(51));
            var seen = new Dictionary<long, Genome>();
            for (var g = 0; g < 4; g++)
            {
                var report = evolution.Advance();
                Assert.That(report.Individuals.Select(i => i.Id).Distinct().Count(), Is.EqualTo(report.Individuals.Count),
                    "no id twice in a generation");
                foreach (var individual in report.Individuals)
                {
                    if (seen.TryGetValue(individual.Id, out var genome))
                        Assert.That(individual.Genome.HasSameContent(genome), Is.True, "an id always names the same genome");
                    else
                        seen.Add(individual.Id, individual.Genome);
                }
            }

            Assert.That(evolution.Lineage.Count, Is.EqualTo(8 + 4 * 6), "8 founders, then 6 children per generation");
            foreach (var individual in evolution.Individuals)
            {
                var chain = evolution.Lineage.Ancestry(individual.Id);
                Assert.That(chain[0], Is.EqualTo(individual.Id));
                Assert.That(evolution.Lineage.BirthGeneration(chain[chain.Count - 1]), Is.Zero, "ends at a founder");
                for (var i = 1; i < chain.Count; i++)
                    Assert.That(evolution.Lineage.BirthGeneration(chain[i]),
                        Is.LessThan(evolution.Lineage.BirthGeneration(chain[i - 1])), "parents are older");
            }

            for (var i = 0; i < evolution.Origins.Count; i++)
            {
                var origin = evolution.Origins[i];
                var individual = evolution.Individuals[i];
                Assert.That(individual.Genome, Is.SameAs(origin.Genome));
                if (!origin.IsElite)
                    Assert.That(individual.BirthGeneration, Is.EqualTo(evolution.Generation));
            }
        }

        [Test]
        public void SameSeed_GivesSameIdsAndParents()
        {
            var a = new GenerationalEvolution(SmallExperiment(52));
            var b = new GenerationalEvolution(SmallExperiment(52));
            for (var g = 0; g < 3; g++)
            {
                var ra = a.Advance();
                var rb = b.Advance();
                Assert.That(EvolutionCsvLog.FormatGeneration(ra.Summary), Is.EqualTo(EvolutionCsvLog.FormatGeneration(rb.Summary)));
                for (var c = 0; c < ra.Individuals.Count; c++)
                    Assert.That(EvolutionCsvLog.FormatIndividual(ra, c), Is.EqualTo(EvolutionCsvLog.FormatIndividual(rb, c)));
            }
        }

        [Test]
        public void Summary_ReportsFitnessDistributionAndOrigins()
        {
            var lineage = new Lineage();
            var founders = lineage.Founders(InitialGenomes.Population(3, 5));
            var fitness = new[] { 0.5f, 2f, Fitness.Worst, 1f, 4f };
            var summary = new GenerationSummary(Evaluation(0, fitness), founders);

            Assert.That(summary.PopulationSize, Is.EqualTo(5));
            Assert.That(summary.ValidCount, Is.EqualTo(4));
            Assert.That(summary.Best, Is.EqualTo(4f));
            Assert.That(summary.UpperQuartile, Is.EqualTo(2f));
            Assert.That(summary.Median, Is.EqualTo(1f));
            Assert.That(summary.LowerQuartile, Is.EqualTo(0.5f));
            Assert.That(summary.Worst, Is.EqualTo(Fitness.Worst));
            Assert.That(summary.MeanFitness, Is.EqualTo(1.875f), "mean of the valid values only");
            Assert.That(summary.BestIndividualId, Is.EqualTo(5));
            Assert.That(summary.NewIndividuals, Is.EqualTo(5));
            Assert.That(summary.Elites, Is.Zero);
            Assert.That(summary.DistinctParents, Is.Zero);

            var even = new GenerationSummary(Evaluation(0, new[] { 1f, 2f, 3f, 4f }), founders.Take(4).ToArray());
            Assert.That(even.Median, Is.EqualTo(2.5f));
            Assert.That(even.LowerQuartile, Is.EqualTo(1.75f));
            Assert.That(even.UpperQuartile, Is.EqualTo(3.25f));

            var children = lineage.Descend(founders, new[]
            {
                new Offspring(founders[4].Genome, 4, true, 0),
                new Offspring(Child(founders[4].Genome), 4, false, 4),
                new Offspring(Child(founders[1].Genome), 1, false, 2),
                new Offspring(Child(founders[4].Genome), 4, false, 6)
            }, 1);
            var next = new GenerationSummary(Evaluation(1, new[] { 1f, 1f, 1f, 1f }), children);
            Assert.That(next.Elites, Is.EqualTo(1));
            Assert.That(next.NewIndividuals, Is.EqualTo(3));
            Assert.That(next.DistinctParents, Is.EqualTo(2));
            Assert.That(next.MeanMutatedWeights, Is.EqualTo(4f));
            Assert.That(next.BestIndividualId, Is.EqualTo(5), "ties go to the first candidate");
        }

        [Test]
        public void Diversity_IsZeroForClonesAndMatchesAKnownSpread()
        {
            var zero = new float[NeuralWeights];
            var plus = Enumerable.Repeat(1f, NeuralWeights).ToArray();
            var minus = Enumerable.Repeat(-1f, NeuralWeights).ToArray();
            var lineage = new Lineage();

            var clones = lineage.Founders(new[] { Weights(zero), Weights(zero), Weights(zero) });
            var flat = new GenerationSummary(Evaluation(0, new[] { 0f, 0f, 0f }), clones);
            Assert.That(flat.WeightStdDev, Is.Zero);
            Assert.That(flat.MeanDistanceToCentroid, Is.Zero);

            var spread = lineage.Founders(new[] { Weights(plus), Weights(minus) });
            var summary = new GenerationSummary(Evaluation(0, new[] { 0f, 0f }), spread);
            Assert.That(summary.WeightStdDev, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(summary.MeanDistanceToCentroid, Is.EqualTo((float)Math.Sqrt(NeuralWeights)).Within(1e-4f));
        }

        [Test]
        public void Diversity_ShrinksUnderSelectionWithoutMutation()
        {
            var settings = SmallExperiment(53);
            settings.MutationProbability = 0f;
            var evolution = new GenerationalEvolution(settings);

            var first = evolution.Advance().Summary;
            GenerationSummary last = null;
            for (var g = 0; g < 5; g++)
                last = evolution.Advance().Summary;

            Assert.That(last.WeightStdDev, Is.LessThan(first.WeightStdDev));
            Assert.That(last.MeanMutatedWeights, Is.Zero);
        }

        [Test]
        public void Csv_IsInvariantAndJoinsGenerationsIndividualsAndEpisodes()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            var directory = Path.Combine(Path.GetTempPath(), "eden-evolution-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
                var evolution = new GenerationalEvolution(SmallExperiment(54));
                var header = new ExperimentHeader(SmallExperiment(54), 0, "0.1.0",
                    new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc));
                using (var log = new EvolutionCsvLog(directory, header))
                {
                    log.Append(evolution.Advance());
                    log.Append(evolution.Advance());
                    Assert.That(log.GenerationCount, Is.EqualTo(2));
                }

                var experiment = File.ReadAllLines(Path.Combine(directory, "experiment.txt"));
                Assert.That(experiment, Does.Contain("experiment_seed=54").And.Contain("mutation_sigma=0.1")
                    .And.Contain("crossover=off").And.Contain("created_utc=2026-10-23T12:00:00Z"));

                var generations = File.ReadAllLines(Path.Combine(directory, "generations.csv"));
                Assert.That(generations, Has.Length.EqualTo(3));
                Assert.That(generations[0], Is.EqualTo(EvolutionCsvLog.GenerationsHeader));
                Assert.That(generations[1], Does.StartWith("0,training,8,8,"));
                Assert.That(generations[2], Does.StartWith("1,training,8,8,"));

                var individuals = File.ReadAllLines(Path.Combine(directory, "individuals.csv"));
                Assert.That(individuals, Has.Length.EqualTo(1 + 16));
                Assert.That(individuals[1], Does.StartWith("0,training,0,1,,0,0,0,1,"), "founder: no parents, not elite");
                var secondGeneration = individuals.Skip(9).Select(l => l.Split(',')).ToArray();
                Assert.That(secondGeneration.Count(f => f[6] == "1"), Is.EqualTo(2), "two elites carried over");
                Assert.That(secondGeneration.Where(f => f[6] == "0").Select(f => f[4]), Is.All.Not.Empty, "children name a parent");

                var episodes = File.ReadAllLines(Path.Combine(directory, "episodes.csv"));
                Assert.That(episodes, Has.Length.EqualTo(1 + 2 * 8 * 2));
                AssertColumns(generations, EvolutionCsvLog.GenerationsHeader);
                AssertColumns(individuals, EvolutionCsvLog.IndividualsHeader);
                AssertColumns(episodes, EvaluationCsv.Header);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        private const int NeuralWeights = 259;

        private static void AssertColumns(string[] lines, string header)
        {
            var columns = header.Split(',').Length;
            foreach (var line in lines)
            {
                Assert.That(line.Split(',').Length, Is.EqualTo(columns), line);
                Assert.That(line, Does.Not.Contain("∞").And.Not.Contain("NaN"));
            }
        }

        private static Genome Weights(float[] weights) => new Genome(NetworkTopology.Mlp12x16x3, weights);

        private static Genome Child(Genome parent) =>
            Reproduction.Mutate(parent, 1f, 0.1f, new GaussianSampler(new Pcg32(parent.GetWeight(0) > 0 ? 1UL : 2UL, 3)), out _);

        private static GenerationEvaluation Evaluation(int generation, float[] fitness)
        {
            var candidates = new CandidateEvaluation[fitness.Length];
            var episodes = new List<EpisodeResult>();
            for (var c = 0; c < fitness.Length; c++)
            {
                var food = float.IsInfinity(fitness[c]) ? float.NaN : fitness[c] * 120f;
                var episode = new EpisodeResult(EvaluationMode.Training, generation, c, 0, 1, 2400, 120f, food, 2400,
                    true, 50f);
                episodes.Add(episode);
                candidates[c] = new CandidateEvaluation(c, new[] { episode }, 20);
            }

            return new GenerationEvaluation(generation, EvaluationMode.Training, new ulong[] { 1 }, episodes, candidates);
        }

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
    }
}
