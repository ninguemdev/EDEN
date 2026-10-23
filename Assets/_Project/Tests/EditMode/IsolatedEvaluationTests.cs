using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class IsolatedEvaluationTests
    {
        [Test]
        public void Plan_UsesProposedDefaultsAndAlwaysOneCreature()
        {
            var settings = new EvolutionSettings { ExperimentSeed = 4 };
            settings.World.InitialPopulation = 64;
            var plan = new EvaluationPlan(settings);

            Assert.That(plan.EpisodeSeconds, Is.EqualTo(120f));
            Assert.That(plan.EpisodeTicks, Is.EqualTo(2400));
            Assert.That(plan.TrainingSeedsPerGeneration, Is.EqualTo(3));
            var episode = plan.EpisodeConfig(99);
            Assert.That(episode.InitialPopulation, Is.EqualTo(1));
            Assert.That(episode.InitialFood, Is.EqualTo(32));
            Assert.That(episode.Seed, Is.EqualTo(99UL));
            Assert.That(settings.World.InitialPopulation, Is.EqualTo(64), "the caller's settings are not modified");
        }

        [Test]
        public void Plan_RejectsInvalidSettings()
        {
            var error = Assert.Throws<ArgumentException>(() =>
                new EvaluationPlan(new EvolutionSettings { EpisodeSeconds = 0f, TrainingSeedsPerGeneration = 0 }));
            Assert.That(error.Message, Does.Contain("EpisodeSeconds").And.Contain("TrainingSeedsPerGeneration"));

            var badWorld = new EvolutionSettings();
            badWorld.World.ArenaWidth = float.NaN;
            Assert.Throws<ArgumentException>(() => new EvaluationPlan(badWorld));
            Assert.Throws<ArgumentNullException>(() => new EvaluationPlan(null));
        }

        [Test]
        public void TrainingSeeds_FollowTheSchemeAndDifferPerGeneration()
        {
            var plan = new EvaluationPlan(new EvolutionSettings { ExperimentSeed = 2026 });
            var root = SplitMix64.Output(2026, 0x74726169UL);
            var expected = SplitMix64.Output(SplitMix64.Output(root, 6), 2);

            Assert.That(plan.TrainingSeed(5, 1), Is.EqualTo(expected));
            Assert.That(plan.TrainingSeeds(0), Is.EqualTo(plan.TrainingSeeds(0)));
            Assert.That(plan.TrainingSeeds(0), Is.Not.EqualTo(plan.TrainingSeeds(1)));
            Assert.That(plan.TrainingSeeds(0).Distinct().Count(), Is.EqualTo(3));
            var other = new EvaluationPlan(new EvolutionSettings { ExperimentSeed = 2027 });
            Assert.That(other.TrainingSeeds(0), Is.Not.EqualTo(plan.TrainingSeeds(0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => plan.TrainingSeed(0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => plan.TrainingSeed(-1, 0));
        }

        [Test]
        public void Fitness_DividesByTheConfiguredDurationAndRanksInvalidLast()
        {
            Assert.That(Fitness.FromFood(60f, 120f), Is.EqualTo(0.5f));
            Assert.That(Fitness.FromFood(0f, 120f), Is.EqualTo(0f));
            Assert.That(Fitness.FromFood(float.NaN, 120f), Is.EqualTo(Fitness.Worst));
            Assert.That(Fitness.FromFood(-1f, 120f), Is.EqualTo(Fitness.Worst));
            Assert.That(Fitness.FromFood(10f, 0f), Is.EqualTo(Fitness.Worst));
            Assert.That(Fitness.IsValid(Fitness.Worst), Is.False);
            Assert.That(Fitness.Worst, Is.LessThan(0f));
        }

        [Test]
        public void TrainingEvaluation_IsReproducibleAndKeepsEveryGenomeSeedResult()
        {
            var plan = new EvaluationPlan(new EvolutionSettings { ExperimentSeed = 11 });
            var genomes = Enumerable.Range(1, 4).Select(i => RandomGenome(11, i)).ToArray();

            var first = new IsolatedEvaluator(plan).EvaluateTraining(genomes, 0);
            var second = new IsolatedEvaluator(plan).EvaluateTraining(genomes, 0);

            Assert.That(first.Mode, Is.EqualTo(EvaluationMode.Training));
            Assert.That(first.Seeds, Is.EqualTo(plan.TrainingSeeds(0)));
            Assert.That(first.Episodes, Has.Count.EqualTo(12));
            for (var i = 0; i < first.Episodes.Count; i++)
            {
                Assert.That(first.Episodes[i].Candidate, Is.EqualTo(i / 3), "candidate-major order");
                Assert.That(first.Episodes[i].SeedIndex, Is.EqualTo(i % 3));
                AssertSameOutcome(first.Episodes[i], second.Episodes[i]);
            }

            Assert.That(first.Candidates.Select(c => c.Episodes.Count), Is.All.EqualTo(3));
            Assert.That(first.Candidates.Select(c => c.MeanFitness), Is.EqualTo(second.Candidates.Select(c => c.MeanFitness)));
        }

        [Test]
        public void IdenticalCandidates_MeetIdenticalConditions()
        {
            var plan = new EvaluationPlan(new EvolutionSettings { ExperimentSeed = 12 });
            var genome = RandomGenome(12, 3);

            var evaluation = new IsolatedEvaluator(plan).EvaluateTraining(new[] { genome, genome }, 2);

            for (var s = 0; s < plan.TrainingSeedsPerGeneration; s++)
                AssertSameOutcome(evaluation.Candidates[0].Episodes[s], evaluation.Candidates[1].Episodes[s]);
        }

        [Test]
        public void Death_EndsTheEpisodeButNotItsConfiguredDuration()
        {
            var settings = new EvolutionSettings { ExperimentSeed = 13 };
            settings.World.InitialFood = 0;
            var plan = new EvaluationPlan(settings);

            var result = new IsolatedEvaluator(plan).RunEpisode(() => new IdleBrain(), plan.TrainingSeed(0, 0), 0, 0, 0,
                EvaluationMode.Training);

            Assert.That(result.Survived, Is.False);
            Assert.That(result.SurvivedTicks, Is.EqualTo(800), "50 energy at 0.0625 per tick");
            Assert.That(result.EpisodeTicks, Is.EqualTo(2400));
            Assert.That(result.FoodEnergy, Is.EqualTo(0f));
            Assert.That(result.Fitness, Is.EqualTo(0f));
            Assert.That(result.FinalEnergy, Is.EqualTo(0f));
        }

        [Test]
        public void CandidateAggregate_KeepsMetricsSeparateAndPropagatesInvalidResults()
        {
            var a = Episode(0, foodEnergy: 120f, survivedTicks: 2400, survived: true, finalEnergy: 60f);
            var b = Episode(1, foodEnergy: 0f, survivedTicks: 800, survived: false, finalEnergy: 0f);
            var aggregate = new CandidateEvaluation(0, new[] { a, b }, 20);

            Assert.That(aggregate.MeanFitness, Is.EqualTo(0.5f), "(1.0 + 0.0) / 2");
            Assert.That(aggregate.MeanFoodEnergy, Is.EqualTo(60f));
            Assert.That(aggregate.SurvivalRate, Is.EqualTo(0.5f));
            Assert.That(aggregate.MeanSurvivedSeconds, Is.EqualTo(80f), "(120 s + 40 s) / 2");
            Assert.That(aggregate.MeanFinalEnergy, Is.EqualTo(30f));

            var invalid = Episode(2, foodEnergy: float.NaN, survivedTicks: 2400, survived: true, finalEnergy: 50f);
            Assert.That(new CandidateEvaluation(0, new[] { a, invalid }, 20).MeanFitness, Is.EqualTo(Fitness.Worst));
        }

        [Test]
        public void Heuristic_BeatsRandom_InIsolatedEpisodes()
        {
            var plan = new EvaluationPlan(new EvolutionSettings { ExperimentSeed = 21 });
            var seeds = plan.TrainingSeeds(0);
            var candidates = new Func<IBrain>[]
            {
                () => new RandomBrain(),
                () => new HeuristicBrain(),
                () => new MlpBrain(RandomGenome(21, 1))
            };

            var evaluation = new IsolatedEvaluator(plan).Evaluate(candidates, seeds, 0, EvaluationMode.Training);
            var names = new[] { "random", "heuristic", "random-genome mlp" };
            for (var c = 0; c < candidates.Length; c++)
            {
                var result = evaluation.Candidates[c];
                TestContext.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}: fitness={1:0.####} food={2:0.#} survival={3:0.##} survived_s={4:0.#}",
                    names[c], result.MeanFitness, result.MeanFoodEnergy, result.SurvivalRate, result.MeanSurvivedSeconds));
            }

            Assert.That(evaluation.Candidates[1].MeanFitness, Is.GreaterThan(evaluation.Candidates[0].MeanFitness));
        }

        [Test]
        public void ReportRows_AreInvariantAndNameTheEvaluationMode()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-BR");
                var row = EvaluationCsv.FormatRow(Episode(1, 61.5f, 2400, true, 42.25f));
                Assert.That(row, Is.EqualTo("training,3,0,1,77,1,0.5125,61.5,1,2400,2400,42.25"));

                var invalidRow = EvaluationCsv.FormatRow(Episode(0, float.NaN, 10, false, 0f));
                Assert.That(invalidRow, Does.StartWith("training,3,0,0,77,0,,"));
                Assert.That(EvaluationCsv.Header.Split(',').Length, Is.EqualTo(row.Split(',').Length));
                Assert.That(EvaluationCsv.ModeName(EvaluationMode.Validation), Is.EqualTo("validation"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        private static Genome RandomGenome(ulong seed, int id) =>
            InitialGenomes.RandomFor(SimulationConfig.CreateDefault(seed), new CreatureId(id));

        private static EpisodeResult Episode(int seedIndex, float foodEnergy, long survivedTicks, bool survived,
            float finalEnergy) =>
            new EpisodeResult(EvaluationMode.Training, 3, 0, seedIndex, 77, 2400, 120f, foodEnergy, survivedTicks,
                survived, finalEnergy);

        private static void AssertSameOutcome(EpisodeResult actual, EpisodeResult expected)
        {
            Assert.That(actual.EpisodeSeed, Is.EqualTo(expected.EpisodeSeed));
            Assert.That(actual.FoodEnergy, Is.EqualTo(expected.FoodEnergy));
            Assert.That(actual.Fitness, Is.EqualTo(expected.Fitness));
            Assert.That(actual.SurvivedTicks, Is.EqualTo(expected.SurvivedTicks));
            Assert.That(actual.Survived, Is.EqualTo(expected.Survived));
            Assert.That(actual.FinalEnergy, Is.EqualTo(expected.FinalEnergy));
        }

        private sealed class IdleBrain : IBrain
        {
            public string Name => "idle";
            public CreatureAction Evaluate(in Observation observation, BrainState state) => CreatureAction.Idle;
        }
    }
}
