using System;
using System.Collections.Generic;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>
    /// Evaluation plans of several stages: seeds tagged per stage, episodes run candidate → stage → seed, stage means
    /// combined by the profile weights, and runs that do not depend on how ticks are split or where they resume.
    /// </summary>
    public class StagedEvaluationTests
    {
        private static readonly DateTime SavedAt = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        private static readonly RewardProfile Halves = new RewardProfile("halves-v1", new[]
        {
            new RewardStage("first", 0.5, StageScoring.FoodEnergyPerSecond, "food_energy"),
            new RewardStage("second", 0.5, StageScoring.FoodEnergyPerSecond, "food_energy")
        });

        [Test]
        public void SingleStage_KeepsTheLegacySeedsAndLengths()
        {
            var plan = new EvaluationPlan(Small(2026));
            var root = SplitMix64.Output(2026, 0x74726169UL);
            var validationRoot = SplitMix64.Output(1, 0x76616C69UL);

            Assert.That(plan.Stages.Count, Is.EqualTo(1));
            Assert.That(plan.SeedScheme, Is.EqualTo(EvaluationPlan.SeedSchemeVersion));
            Assert.That(plan.TrainingSeeds(4), Is.EqualTo(new[]
            {
                SplitMix64.Output(SplitMix64.Output(root, 5), 1), SplitMix64.Output(SplitMix64.Output(root, 5), 2)
            }));
            Assert.That(plan.ValidationSeeds(), Is.EqualTo(Enumerable.Range(1, 3)
                .Select(i => SplitMix64.Output(validationRoot, (ulong)i)).ToArray()));
            Assert.That(plan.Stages[0].EpisodeTicks, Is.EqualTo(plan.EpisodeTicks));
            Assert.That(plan.Stages[0].Name, Is.EqualTo("episode"));
            Assert.That(plan.StageOfSeeds(EvaluationMode.Training, 7), Is.EqualTo(new int[7]),
                "a single stage runs any seeds, as before stages");
        }

        [Test]
        public void TwoStages_TagTheirSeedsAndListThemStageAfterStage()
        {
            var plan = new EvaluationPlan(TwoStages(2026));
            var single = new EvaluationPlan(Small(2026));
            var root = SplitMix64.Output(2026, 0x74726169UL);
            var secondRoot = SplitMix64.Output(root, 0x73746700UL + 1);

            Assert.That(plan.SeedScheme, Is.EqualTo(EvaluationPlan.StagedSeedSchemeVersion));
            Assert.That(plan.TrainingSeedsPerGeneration, Is.EqualTo(5));
            Assert.That(plan.ValidationSeedCount, Is.EqualTo(5));
            var seeds = plan.TrainingSeeds(3);
            Assert.That(seeds[2], Is.EqualTo(SplitMix64.Output(SplitMix64.Output(secondRoot, 4), 1)));
            Assert.That(seeds.Distinct().Count(), Is.EqualTo(5));
            Assert.That(seeds.Intersect(single.TrainingSeeds(3)), Is.Empty, "the stage tag changes the first stage too");
            Assert.That(plan.ValidationSeeds().Distinct().Count(), Is.EqualTo(5));
            Assert.That(plan.ValidationSeeds().Intersect(single.ValidationSeeds()), Is.Empty);
            Assert.That(new EvaluationPlan(TwoStages(2026)).TrainingSeeds(3), Is.EqualTo(seeds));
            Assert.That(plan.StageOfSeeds(EvaluationMode.Training, 5), Is.EqualTo(new[] { 0, 0, 1, 1, 1 }));
            Assert.That(plan.StageOfSeeds(EvaluationMode.Validation, 5), Is.EqualTo(new[] { 0, 0, 0, 1, 1 }));
            Assert.That(() => plan.StageOfSeeds(EvaluationMode.Training, 4),
                Throws.ArgumentException.With.Message.Contains("on 5 seeds"));
            Assert.That(plan.Stages[1].EpisodeTicks, Is.EqualTo(160));
            Assert.That(plan.Stages[1].EpisodeConfig(9).InitialFood, Is.EqualTo(8));
            Assert.That(plan.Stages[1].EpisodeConfig(9).InitialPopulation, Is.EqualTo(1));
        }

        [Test]
        public void TwoStages_RunCandidateStageSeedAndCombineTheStageMeans()
        {
            var plan = new EvaluationPlan(TwoStages(41));
            var genomes = InitialGenomes.Population(41, 3);
            var evaluation = new IsolatedEvaluator(plan).EvaluateTraining(genomes, 0);
            var seeds = plan.TrainingSeeds(0);

            Assert.That(evaluation.Episodes.Count, Is.EqualTo(15));
            for (var i = 0; i < evaluation.Episodes.Count; i++)
            {
                var episode = evaluation.Episodes[i];
                Assert.That(episode.Candidate, Is.EqualTo(i / 5));
                Assert.That(episode.SeedIndex, Is.EqualTo(i % 5));
                Assert.That(episode.EpisodeSeed, Is.EqualTo(seeds[i % 5]));
                Assert.That(episode.Stage, Is.EqualTo(i % 5 < 2 ? 0 : 1));
                Assert.That(episode.EpisodeTicks, Is.EqualTo(plan.Stages[episode.Stage].EpisodeTicks));
            }

            var alone = new IsolatedEpisode(plan, () => GenomeBrains.Create(genomes[1]), seeds[3], 1, 3, 0,
                EvaluationMode.Training, genomes[1].Body, genomes[1].Eyes, 1);
            alone.Advance(long.MaxValue);
            Assert.That(alone.Result().FoodEnergy, Is.EqualTo(evaluation.Episodes[8].FoodEnergy),
                "a stage episode is the episode of that stage's world");

            foreach (var candidate in evaluation.Candidates)
            {
                var first = (float)(candidate.Episodes.Take(2).Sum(e => (double)e.Fitness) / 2);
                var second = (float)(candidate.Episodes.Skip(2).Sum(e => (double)e.Fitness) / 3);
                Assert.That(candidate.StageFitness, Is.EqualTo(new[] { first, second }));
                Assert.That(candidate.MeanFitness, Is.EqualTo(Halves.Combine(new[] { first, second })));
            }

            Assert.That(evaluation.Candidates.Select(c => c.StageFitness[1]),
                Is.Not.EqualTo(evaluation.Candidates.Select(c => c.StageFitness[0])), "the stages are different worlds");
        }

        [Test]
        public void TwoStages_GiveTheSameResultHoweverTicksAreSplit()
        {
            var plan = new EvaluationPlan(TwoStages(7));
            var genomes = InitialGenomes.Population(7, 3);
            var continuous = new IsolatedEvaluator(plan).EvaluateTraining(genomes, 0);

            foreach (var (step, workers) in new[] { (1L, 1), (37L, 1), (333L, 1), (1L, 3), (53L, 4) })
            {
                var job = new IsolatedEvaluator(plan).TrainingJob(genomes, 0);
                job.WorkerCount = workers;
                while (!job.IsComplete)
                    job.Advance(step);
                AssertSame(job.Result, continuous);
            }
        }

        [Test]
        public void TwoStages_ResumedInsideEitherStage_MatchTheContinuousRun()
        {
            var plan = new EvaluationPlan(TwoStages(13));
            var genomes = InitialGenomes.Population(13, 2);
            var continuous = new IsolatedEvaluator(plan).EvaluateTraining(genomes, 0);
            var first = plan.Stages[0].EpisodeTicks;
            var second = plan.Stages[1].EpisodeTicks;

            // Inside the first stage of candidate 0, inside its second stage, and inside the second stage of candidate 1.
            var perCandidate = 2 * first + 3 * second;
            foreach (var workers in new[] { 1, 3 })
            {
                foreach (var ticks in new[] { first / 2, 2 * first + second + 11, 2 * perCandidate - second / 2 })
                {
                    var job = new IsolatedEvaluator(plan).TrainingJob(genomes, 0);
                    job.WorkerCount = workers;
                    var ran = 0L;
                    while (ran < ticks && !job.IsComplete)
                        ran += job.Advance(Math.Min(7, ticks - ran));
                    Assert.That(job.IsComplete, Is.False);
                    var progress = job.Capture();

                    var resumed = new IsolatedEvaluator(plan).TrainingJob(genomes, 0);
                    resumed.Resume(progress);
                    AssertSame(resumed.RunToEnd(), continuous);
                }
            }

            var stopped = new IsolatedEvaluator(plan).TrainingJob(genomes, 0);
            stopped.Advance(2 * first + 3);
            Assert.That(stopped.Current.Stage, Is.EqualTo(1));
            var saved = stopped.Capture();
            var single = new IsolatedEvaluator(new EvaluationPlan(Small(13))).TrainingJob(genomes, 0);
            Assert.That(() => single.Resume(saved), Throws.ArgumentException);
        }

        [Test]
        public void Validation_RunsEveryStageAndASessionBreedsOnTheCombinedScore()
        {
            var settings = TwoStages(5);
            settings.PopulationSize = 4;
            settings.EliteCount = 1;
            var plan = new EvaluationPlan(settings);
            var validation = new IsolatedEvaluator(plan).EvaluateValidation(InitialGenomes.Population(5, 2), 0);
            Assert.That(validation.Episodes.Select(e => e.Stage), Is.EqualTo(new[] { 0, 0, 0, 1, 1, 0, 0, 0, 1, 1 }));

            GenerationReport Run()
            {
                var session = new EvolutionSession(TwoStages(5, s => { s.PopulationSize = 4; s.EliteCount = 1; }), 1);
                while (session.Evolution.Generation < 2)
                    session.Advance(500);
                return session.LastTraining;
            }

            var once = Run();
            var again = Run();
            Assert.That(once.Evaluation.Episodes.Count, Is.EqualTo(4 * 5));
            AssertSame(again.Evaluation, once.Evaluation);
        }

        [Test]
        public void Plan_RefusesStagesThatDoNotFitWithTheReason()
        {
            Assert.That(() => new EvaluationPlan(new EvolutionSettings { Profile = Halves }),
                Throws.ArgumentException.With.Message.Contains("has 2 stages and the settings describe 1"));
            Assert.That(() => new EvaluationPlan(TwoStages(1, s => s.Profile = RewardProfile.Legacy)),
                Throws.ArgumentException.With.Message.Contains("has 1 stages and the settings describe 2"));
            Assert.That(() => new EvaluationPlan(TwoStages(1, s => s.FurtherStages[0].EpisodeSeconds = 0f)),
                Throws.ArgumentException.With.Message.Contains("stage 2 EpisodeSeconds"));
            Assert.That(() => new EvaluationPlan(TwoStages(1, s => s.FurtherStages[0].ValidationSeedCount = 0)),
                Throws.ArgumentException.With.Message.Contains("stage 2 ValidationSeedCount"));
            Assert.That(() => new EvaluationPlan(TwoStages(1, s => s.FurtherStages[0] = null)),
                Throws.ArgumentException.With.Message.Contains("stage 2 needs"));
            Assert.That(() => new EvaluationPlan(TwoStages(1, s => s.FurtherStages[0].World.TicksPerSecond = 10)),
                Throws.ArgumentException.With.Message.Contains("ticks per second"));

            var dryThenWet = TwoStages(1, s =>
            {
                s.Algorithm = EvolutionAlgorithm.Neat;
                s.Senses = Senses.Eyes;
                var wet = s.FurtherStages[0].World;
                wet.SolidBodies = true;
                wet.Water = WaterLayouts.Create(WaterLayout.Pond, wet);
            });
            Assert.That(() => new EvaluationPlan(dryThenWet), Throws.ArgumentException.With.Message
                .Contains("stage 2 reads the neural contract eyes-water-v1 and stage 1 reads eyes-v1"));

            var bothWet = dryThenWet.WithExperimentSeed(1);
            bothWet.World.SolidBodies = true;
            bothWet.World.Water = WaterLayouts.Create(WaterLayout.Pond, bothWet.World);
            Assert.That(new EvaluationPlan(bothWet).Contract, Is.SameAs(NeuralContract.EyesWater));
        }

        [Test]
        public void FurtherStages_ArePartOfTheDefinitionAndCopied()
        {
            var settings = TwoStages(3);
            var header = new ExperimentHeader(settings, 1, "0.1.0", SavedAt);
            Assert.That(header.Describe(), Does.Contain("seed_scheme=2"));
            Assert.That(new ExperimentHeader(new EvolutionSettings(), 1, "0.1.0", SavedAt).Describe(),
                Does.Contain("seed_scheme=1"));

            var otherWorld = TwoStages(3, s => s.FurtherStages[0].World.InitialFood = 9);
            Assert.That(new ExperimentHeader(otherWorld, 1, "0.1.0", SavedAt).ConfigHash, Is.Not.EqualTo(header.ConfigHash));

            var copy = settings.WithExperimentSeed(4);
            copy.FurtherStages[0].World.InitialFood = 99;
            Assert.That(settings.FurtherStages[0].World.InitialFood, Is.EqualTo(8), "copies do not share stages");
        }

        private static EvolutionSettings Small(ulong seed)
        {
            var settings = new EvolutionSettings
            {
                ExperimentSeed = seed, EpisodeSeconds = 6f, TrainingSeedsPerGeneration = 2, ValidationSeedCount = 3,
                PopulationSize = 6, EliteCount = 2
            };
            settings.World.InitialFood = 64;
            return settings;
        }

        // Two trivial stages: the legacy scoring on two different worlds, weighted 0.5 / 0.5.
        private static EvolutionSettings TwoStages(ulong seed, Action<EvolutionSettings> change = null)
        {
            var settings = Small(seed);
            settings.Profile = Halves;
            var second = new StageSettings { EpisodeSeconds = 8f, TrainingSeedsPerGeneration = 3, ValidationSeedCount = 2 };
            second.World.InitialFood = 8;
            settings.FurtherStages = new List<StageSettings> { second };
            change?.Invoke(settings);
            return settings;
        }

        private static void AssertSame(GenerationEvaluation actual, GenerationEvaluation expected)
        {
            Assert.That(actual.Episodes.Count, Is.EqualTo(expected.Episodes.Count));
            for (var i = 0; i < expected.Episodes.Count; i++)
            {
                var a = actual.Episodes[i];
                var e = expected.Episodes[i];
                Assert.That((a.Candidate, a.SeedIndex, a.Stage, a.EpisodeSeed, a.EpisodeTicks),
                    Is.EqualTo((e.Candidate, e.SeedIndex, e.Stage, e.EpisodeSeed, e.EpisodeTicks)));
                Assert.That((a.FoodEnergy, a.Fitness, a.SurvivedTicks, a.Survived, a.FinalEnergy),
                    Is.EqualTo((e.FoodEnergy, e.Fitness, e.SurvivedTicks, e.Survived, e.FinalEnergy)));
            }

            for (var c = 0; c < expected.Candidates.Count; c++)
            {
                Assert.That(actual.Candidates[c].MeanFitness, Is.EqualTo(expected.Candidates[c].MeanFitness));
                Assert.That(actual.Candidates[c].StageFitness, Is.EqualTo(expected.Candidates[c].StageFitness));
            }
        }
    }
}
