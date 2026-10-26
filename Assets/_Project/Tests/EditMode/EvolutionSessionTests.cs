using System;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class EvolutionSessionTests
    {
        [Test]
        public void EvaluationJob_AdvancedInSmallChunks_MatchesOneCall()
        {
            var plan = new EvaluationPlan(Small(5));
            var genomes = InitialGenomes.Population(5, 4);
            var evaluator = new IsolatedEvaluator(plan);
            var whole = evaluator.EvaluateTraining(genomes, 0);

            var job = evaluator.TrainingJob(genomes, 0);
            var ticks = 0L;
            var switches = 0;
            var shown = job.Current;
            while (!job.IsComplete)
            {
                var ran = job.Advance(37);
                Assert.That(ran, Is.InRange(0, 37));
                ticks += ran;
                if (job.Current != shown)
                {
                    switches++;
                    shown = job.Current;
                }
            }

            Assert.That(job.Current, Is.Null);
            Assert.That(job.CompletedEpisodes, Is.EqualTo(job.TotalEpisodes).And.EqualTo(8));
            Assert.That(switches, Is.EqualTo(8), "one new episode per finished one, then none");
            Assert.That(ticks, Is.LessThanOrEqualTo(8 * plan.EpisodeTicks));
            Assert.That(job.Advance(100), Is.Zero, "a complete job runs nothing");
            AssertSameEvaluation(job.Result, whole);
        }

        [Test]
        public void IsolatedEpisode_StopsAtTheDurationOrAtDeath()
        {
            var plan = new EvaluationPlan(Small(5));
            var episode = new IsolatedEpisode(plan, () => new HeuristicBrain(), plan.TrainingSeed(0, 0), 0, 0, 0,
                EvaluationMode.Training);

            Assert.That(episode.IsFinished, Is.False);
            Assert.Throws<InvalidOperationException>(() => episode.Result());
            Assert.That(episode.Advance(5), Is.EqualTo(5));
            Assert.That(episode.Tick, Is.EqualTo(5));
            episode.Advance(long.MaxValue);
            Assert.That(episode.IsFinished, Is.True);
            Assert.That(episode.Tick, Is.EqualTo(plan.EpisodeTicks).Or.EqualTo(episode.Creature.AgeTicks));

            var reference = new IsolatedEvaluator(plan).RunEpisode(() => new HeuristicBrain(), plan.TrainingSeed(0, 0), 0, 0, 0,
                EvaluationMode.Training);
            Assert.That(episode.Result().FoodEnergy, Is.EqualTo(reference.FoodEnergy));
            Assert.That(episode.Result().SurvivedTicks, Is.EqualTo(reference.SurvivedTicks));
        }

        [Test]
        public void Session_InChunks_ReproducesGenerationalEvolutionAndItsValidation()
        {
            var reference = new GenerationalEvolution(Small(21));
            var session = new EvolutionSession(Small(21), 2);
            var ran = 0L;
            while (session.Training.Count < 4)
                ran += session.Advance(113);

            Assert.That(session.TotalTicks, Is.EqualTo(ran));
            for (var g = 0; g < 4; g++)
            {
                var expected = reference.Advance();
                var actual = session.Training[g];
                Assert.That(actual.Generation, Is.EqualTo(g));
                Assert.That(actual.Mode, Is.EqualTo(EvaluationMode.Training));
                Assert.That(actual.Best, Is.EqualTo(expected.Summary.Best));
                Assert.That(actual.Median, Is.EqualTo(expected.Summary.Median));
                Assert.That(actual.LowerQuartile, Is.EqualTo(expected.Summary.LowerQuartile));
                Assert.That(actual.BestIndividualId, Is.EqualTo(expected.Summary.BestIndividualId));
                Assert.That(actual.WeightStdDev, Is.EqualTo(expected.Summary.WeightStdDev));
                if (g % 2 != 0)
                    continue;

                var validation = reference.Validate(expected);
                var point = session.Validation[g / 2];
                Assert.That(point.Generation, Is.EqualTo(g));
                Assert.That(point.Validation.Median, Is.EqualTo(validation.Summary.Median));
                Assert.That(point.ChampionId, Is.EqualTo(expected.Summary.BestIndividualId));
            }

            Assert.That(session.Evolution.Individuals.Select(i => i.Id),
                Is.EqualTo(reference.Individuals.Select(i => i.Id)), "validation never changes who is bred");
            Assert.That(session.Evolution.Lineage.NextId, Is.EqualTo(reference.Lineage.NextId));

            var baselines = new IsolatedEvaluator(reference.Plan).Evaluate(
                new Func<IBrain>[] { () => new RandomBrain(), () => new HeuristicBrain() },
                reference.Plan.ValidationSeeds(), 0, EvaluationMode.Validation);
            Assert.That(session.RandomBaseline.MeanFitness, Is.EqualTo(baselines.Candidates[0].MeanFitness));
            Assert.That(session.HeuristicBaseline.MeanFitness, Is.EqualTo(baselines.Candidates[1].MeanFitness));
        }

        [Test]
        public void Session_MovesThroughTrainingBaselinesAndValidation()
        {
            var session = new EvolutionSession(Small(3), 2);
            var reports = 0;
            session.ReportCompleted += _ => reports++;

            Assert.That(session.Phase, Is.EqualTo(EvolutionPhase.Training));
            Assert.That(session.Job.Generation, Is.EqualTo(0));
            Assert.That(session.CurrentIndividual.Id, Is.EqualTo(1));
            Assert.That(session.CurrentReference, Is.Null);

            AdvanceUntil(session, () => session.Phase != EvolutionPhase.Training);
            Assert.That(session.Phase, Is.EqualTo(EvolutionPhase.Baselines));
            Assert.That(session.Training.Count, Is.EqualTo(1));
            Assert.That(reports, Is.EqualTo(1));
            Assert.That(session.CurrentIndividual, Is.Null);
            Assert.That(session.CurrentReference, Is.EqualTo(RandomBrain.BrainName));
            Assert.That(session.Job.SeedCount, Is.EqualTo(session.Plan.ValidationSeedCount));

            AdvanceUntil(session, () => session.Phase != EvolutionPhase.Baselines);
            Assert.That(session.Phase, Is.EqualTo(EvolutionPhase.Validation));
            Assert.That(session.HeuristicBaseline, Is.Not.Null);
            Assert.That(session.Job.Generation, Is.EqualTo(0), "validates the generation just trained");
            Assert.That(session.CurrentIndividual.BirthGeneration, Is.EqualTo(0));

            AdvanceUntil(session, () => session.Phase != EvolutionPhase.Validation);
            Assert.That(session.Validation.Count, Is.EqualTo(1));
            Assert.That(reports, Is.EqualTo(2));
            Assert.That(session.Phase, Is.EqualTo(EvolutionPhase.Training));
            Assert.That(session.Job.Generation, Is.EqualTo(1));

            AdvanceUntil(session, () => session.Training.Count == 2);
            Assert.That(session.Phase, Is.EqualTo(EvolutionPhase.Training), "generation 1 is not validated");
            Assert.That(session.Job.Generation, Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EvolutionSession(Small(3), 0));
        }

        [Test]
        public void Evolution_RejectsEvaluationsOfAnotherGenerationOrMode()
        {
            var evolution = new GenerationalEvolution(Small(9));
            var job = evolution.BeginTraining();
            var evaluation = job.RunToEnd();
            var validationOfWrongReport = new IsolatedEvaluator(evolution.Plan)
                .EvaluateValidation(evolution.Population, 0);
            Assert.Throws<ArgumentException>(() => evolution.Advance(validationOfWrongReport));

            var report = evolution.Advance(evaluation);
            Assert.That(evolution.Generation, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => evolution.Advance(evaluation), "generation 0 again");
            Assert.Throws<ArgumentNullException>(() => evolution.Advance(null));

            Assert.Throws<ArgumentException>(() => evolution.Validate(report, evaluation), "training is not validation");
            var validation = evolution.BeginValidation(report).RunToEnd();
            Assert.That(evolution.Validate(report, validation).Summary.Median,
                Is.EqualTo(evolution.Validate(report).Summary.Median));
        }

        [Test]
        public void Presets_SizeTheSharedWorldAndThePopulationOnly()
        {
            Assert.That(ExperimentPreset.All.Select(p => p.Population), Is.EqualTo(new[] { 32, 64, 128 }));
            Assert.That(ExperimentPreset.All.Select(p => p.Food), Is.EqualTo(new[] { 64, 128, 256 }));

            var template = new SimulationSettings { MaxEnergy = 80f };
            var shared = ExperimentPreset.Minimal.SharedWorld(template, 42);
            Assert.That(shared.InitialPopulation, Is.EqualTo(32));
            Assert.That(shared.InitialFood, Is.EqualTo(64));
            Assert.That(shared.Seed, Is.EqualTo(42UL));
            Assert.That(shared.MaxEnergy, Is.EqualTo(80f), "other rules come from the template");
            Assert.That(template.InitialPopulation, Is.EqualTo(64), "the template is not changed");

            var evolution = ExperimentPreset.Stress.Evolution(7);
            var proposed = new EvolutionSettings();
            Assert.That(evolution.PopulationSize, Is.EqualTo(128));
            Assert.That(evolution.ExperimentSeed, Is.EqualTo(7UL));
            Assert.That(evolution.World.InitialFood, Is.EqualTo(proposed.World.InitialFood), "episodes score the same");
            Assert.That(evolution.EpisodeSeconds, Is.EqualTo(proposed.EpisodeSeconds));
        }

        private static void AdvanceUntil(EvolutionSession session, Func<bool> condition)
        {
            for (var i = 0; i < 100000 && !condition(); i++)
                session.Advance(50);
            Assert.That(condition(), Is.True);
        }

        private static void AssertSameEvaluation(GenerationEvaluation actual, GenerationEvaluation expected)
        {
            Assert.That(actual.Generation, Is.EqualTo(expected.Generation));
            Assert.That(actual.Mode, Is.EqualTo(expected.Mode));
            Assert.That(actual.Seeds, Is.EqualTo(expected.Seeds));
            Assert.That(actual.Episodes.Count, Is.EqualTo(expected.Episodes.Count));
            for (var i = 0; i < expected.Episodes.Count; i++)
            {
                Assert.That(actual.Episodes[i].Candidate, Is.EqualTo(expected.Episodes[i].Candidate));
                Assert.That(actual.Episodes[i].SeedIndex, Is.EqualTo(expected.Episodes[i].SeedIndex));
                Assert.That(actual.Episodes[i].FoodEnergy, Is.EqualTo(expected.Episodes[i].FoodEnergy));
                Assert.That(actual.Episodes[i].SurvivedTicks, Is.EqualTo(expected.Episodes[i].SurvivedTicks));
                Assert.That(actual.Episodes[i].FinalEnergy, Is.EqualTo(expected.Episodes[i].FinalEnergy));
            }

            for (var c = 0; c < expected.Candidates.Count; c++)
                Assert.That(actual.Candidates[c].MeanFitness, Is.EqualTo(expected.Candidates[c].MeanFitness));
        }

        private static EvolutionSettings Small(ulong seed)
        {
            var settings = new EvolutionSettings
            {
                ExperimentSeed = seed, EpisodeSeconds = 10f, TrainingSeedsPerGeneration = 2, ValidationSeedCount = 3,
                PopulationSize = 6, EliteCount = 2
            };
            settings.World.InitialFood = 64;
            return settings;
        }
    }
}
