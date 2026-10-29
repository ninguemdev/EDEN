using System;
using System.Linq;
using System.Threading;
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

        [TestCase(1, false, false, false)]
        [TestCase(4, false, false, false)]
        [TestCase(4, true, false, false)]
        [TestCase(4, true, true, false)]
        [TestCase(4, false, false, true)]
        public void Session_InChunks_ReproducesGenerationalEvolutionAndItsValidation(int workers, bool neat,
            bool eyes, bool plasticity)
        {
            var settings = Small(21);
            settings.Algorithm = neat ? EvolutionAlgorithm.Neat : EvolutionAlgorithm.Mlp;
            settings.Senses = eyes ? Senses.Eyes : Senses.Legacy;
            settings.EvolvingEyes = eyes;
            settings.Plasticity = plasticity;
            var reference = new GenerationalEvolution(settings);
            var session = new EvolutionSession(settings, 2, workers);
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
            for (var i = 0; i < reference.Population.Count; i++)
                Assert.That(session.Evolution.Population[i].HasSameContent(reference.Population[i]), Is.True,
                    "identical genomes after selection, crossover and mutation");

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
        public void Session_FromSeedPool_BeginsTrainingFromTheSeededPopulation()
        {
            var pool = InitialGenomes.Population(99, 3);
            var poolFitness = new[] { 1f, 2f, 3f };
            var session = EvolutionSession.FromSeedPool(Small(5), pool, poolFitness, 2);

            Assert.That(session.Phase, Is.EqualTo(EvolutionPhase.Training));
            Assert.That(session.Evolution.Generation, Is.Zero);
            Assert.That(session.Evolution.Population.Count, Is.EqualTo(6));
            Assert.That(session.CurrentIndividual, Is.Not.Null);
            foreach (var individual in session.Evolution.Individuals)
                Assert.That(individual.ParentIds, Is.Empty);
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
            Assert.That(ExperimentPreset.All.Select(p => p.Population), Is.EqualTo(new[] { 32, 64, 128, 1000 }));
            Assert.That(ExperimentPreset.All.Select(p => p.Food), Is.EqualTo(new[] { 64, 128, 256, 2000 }));

            var template = new SimulationSettings { MaxEnergy = 80f };
            var shared = ExperimentPreset.Minimal.SharedWorld(template, 42);
            Assert.That(shared.InitialPopulation, Is.EqualTo(32));
            Assert.That(shared.InitialFood, Is.EqualTo(64));
            Assert.That(shared.Seed, Is.EqualTo(42UL));
            Assert.That(shared.MaxEnergy, Is.EqualTo(80f), "other rules come from the template");
            Assert.That(template.InitialPopulation, Is.EqualTo(64), "the template is not changed");

            var evolution = ExperimentPreset.Thousand.Evolution(7);
            var proposed = new EvolutionSettings();
            Assert.That(evolution.PopulationSize, Is.EqualTo(1000));
            Assert.That(evolution.ExperimentSeed, Is.EqualTo(7UL));
            Assert.That(evolution.World.InitialFood, Is.EqualTo(proposed.World.InitialFood), "episodes score the same");
            Assert.That(evolution.EpisodeSeconds, Is.EqualTo(proposed.EpisodeSeconds));

            template.Reproduction = true;
            var largeWorld = ExperimentPreset.Thousand.SharedWorld(template, 42);
            var config = new SimulationConfig(largeWorld);
            Assert.That(WorldFactory.CreateInitial(config).Creatures.Count, Is.EqualTo(1000));
            Assert.That(config.PopulationCap, Is.EqualTo(1000));
            Assert.That(template.PopulationCap, Is.EqualTo(256), "the template is not changed");
            template.PopulationCap = 2000;
            Assert.That(ExperimentPreset.Thousand.SharedWorld(template, 42).PopulationCap, Is.EqualTo(2000),
                "a higher configured cap is preserved");
        }

        [TestCase(1)]
        [TestCase(37)]
        [TestCase(1024)]
        public void ParallelEvaluation_RespectsTickBudgetAndMatchesSequentialWithDeaths(int chunk)
        {
            var settings = Small(37);
            settings.World.MaxAgeSeconds = 3f;
            var evaluator = new IsolatedEvaluator(new EvaluationPlan(settings));
            var genomes = InitialGenomes.Population(37, 6);
            var reference = evaluator.TrainingJob(genomes, 0).RunToEnd();
            var parallel = evaluator.TrainingJob(genomes, 0);
            parallel.WorkerCount = 4;
            Assert.That(parallel.Advance(0), Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => parallel.Advance(-1));
            long ticks = 0;
            while (!parallel.IsComplete)
            {
                var ran = parallel.Advance(chunk);
                Assert.That(ran, Is.InRange(1, chunk));
                ticks += ran;
            }
            Assert.That(ticks, Is.EqualTo(reference.Episodes.Sum(e => e.SurvivedTicks)));
            AssertSameEvaluation(parallel.Result, reference);
        }

        [Test]
        public void ParallelEvaluation_ExecutesIndependentBrainsConcurrently()
        {
            using var rendezvous = new Barrier(2);
            var plan = new EvaluationPlan(Small(7));
            var job = new EvaluationJob(plan, new Func<IBrain>[]
                { () => new RendezvousBrain(rendezvous), () => new RendezvousBrain(rendezvous) },
                new[] { plan.TrainingSeed(0, 0) }, 0, EvaluationMode.Training) { WorkerCount = 2 };
            Assert.That(job.Advance(2), Is.EqualTo(2));
            Assert.That(job.Current.Tick, Is.EqualTo(1));
            Assert.That(job.Capture().Ahead[0].World.Tick, Is.EqualTo(1));
        }

        private sealed class RendezvousBrain : IBrain
        {
            private readonly Barrier rendezvous;
            private bool met;
            public RendezvousBrain(Barrier rendezvous) => this.rendezvous = rendezvous;
            public string Name => "parallel-test";
            public CreatureAction Evaluate(in Observation observation, BrainState state)
            {
                if (!met && !rendezvous.SignalAndWait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The other episode was not executing concurrently.");
                met = true;
                return default;
            }
        }

        private static void AdvanceUntil(EvolutionSession session, Func<bool> condition)
        {
            for (var i = 0; i < 100000 && !condition(); i++)
                session.Advance(50);
            Assert.That(condition(), Is.True);
        }

        private static void AssertSameEvaluation(GenerationEvaluation actual, GenerationEvaluation expected)
        {
            Assert.That(actual.Episodes, Is.EqualTo(expected.Episodes), "every ordered episode result is identical");
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
