using System;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class CurriculumTests
    {
        private static EpisodeResult Outcome(int food, bool alive, float useful, float approach = 0, int seed = 0) =>
            new EpisodeResult(EvaluationMode.Training, 0, 0, seed, (ulong)seed, 100, 5,
                food * 10, alive ? 100 : 50, alive, alive ? 10 : 0, true, food, useful, approach);

        [Test]
        public void Competence_PrioritizesSustainableFeedingAndNeverExchangesFoodForApproach()
        {
            float Score(EpisodeResult result) => CompetenceFitness.Score(new[] { result });
            Assert.That(Score(Outcome(1, true, 10)), Is.GreaterThan(Score(Outcome(50, false, 500))));
            Assert.That(Score(Outcome(1, false, 10)), Is.GreaterThan(Score(Outcome(0, true, 0, 1))));
            Assert.That(Score(Outcome(2, true, 20)), Is.GreaterThan(Score(Outcome(4, true, 10))));
            var maximum = Score(Outcome(100, true, 1000, 1));
            Assert.That(maximum, Is.LessThan(16777216));
            Assert.That(maximum + 1, Is.GreaterThan(maximum), "all ranking digits retain integer precision");
        }

        [Test]
        public void Gate_RequiresTwentyConsecutiveSuccessfulGenerationsAndRejectsValidation()
        {
            var settings = new EvolutionSettings { CurriculumEnabled = true };
            GenerationEvaluation Evaluation(bool success, EvaluationMode mode = EvaluationMode.Training)
            {
                var episodes = Enumerable.Range(0, 3).Select(s => Outcome(success ? 1 : 0, true, success ? 10 : 0, seed: s)).ToArray();
                return new GenerationEvaluation(0, mode, new ulong[] { 0, 1, 2 }, episodes,
                    new[] { new CandidateEvaluation(0, episodes, 20) });
            }
            for (var i = 0; i < 19; i++) Curriculum.Observe(settings, Evaluation(true));
            Assert.That(settings.CurriculumStage, Is.Zero);
            Curriculum.Observe(settings, Evaluation(false));
            Assert.That(settings.CurriculumStreak, Is.Zero);
            for (var i = 0; i < 20; i++) Curriculum.Observe(settings, Evaluation(true));
            Assert.That(settings.CurriculumStage, Is.EqualTo(1));
            Assert.That(settings.CurriculumStreak, Is.Zero);
            Assert.Throws<ArgumentException>(() => Curriculum.Observe(settings, Evaluation(true, EvaluationMode.Validation)));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void EachStage_KeepsFinalValidationAndRehearsesPreviousCompetence(int stage)
        {
            var settings = new EvolutionSettings { CurriculumEnabled = true, CurriculumStage = stage };
            var plan = new EvaluationPlan(settings);
            var validation = plan.ForEpisode(EvaluationMode.Validation, 0);
            Assert.That(validation.EpisodeSeconds, Is.EqualTo(120));
            Assert.That(validation.EpisodeConfig(0).InitialFood, Is.EqualTo(settings.World.InitialFood));
            Assert.That(plan.ForEpisode(EvaluationMode.Training, 0).CurriculumStage, Is.EqualTo(Math.Max(0, stage - 1)));
            Assert.That(plan.ForEpisode(EvaluationMode.Training, 1).CurriculumStage, Is.EqualTo(stage));
        }

        [TestCase(0, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(2, false)]
        [TestCase(3, true)]
        [TestCase(4, true)]
        public void ParallelCheckpoint_ResumesCurriculumAndProducesIdenticalResults(int stage, bool neat)
        {
            var settings = new EvolutionSettings
            {
                CurriculumEnabled = true, CurriculumStage = stage, CurriculumStreak = 7,
                PopulationSize = 4, EliteCount = 1, TrainingSeedsPerGeneration = 3,
                ValidationSeedCount = 2, EpisodeSeconds = 2,
                Algorithm = neat ? EvolutionAlgorithm.Neat : EvolutionAlgorithm.Mlp
            };
            if (neat)
            {
                settings.Senses = Senses.Eyes;
                settings.EvolvingEyes = true;
                settings.ExpressedBodyGenes = BodyGeneSet.All;
            }
            var original = new EvolutionSession(settings, evaluationWorkers: 4);
            original.Advance(23);
            string Encode(EvolutionSession session) => CheckpointCodec.Encode(
                new Checkpoint("evolution", "minimal", 0, "neural", session.Capture(), null),
                "test", "test", new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc));
            var text = Encode(original);
            var restored = EvolutionSession.Restore(CheckpointCodec.Decode(text, out _).Evolution);
            Assert.That(Encode(restored), Is.EqualTo(text));
            for (var i = 0; i < 30; i++)
            {
                original.Advance(137);
                restored.Advance(137);
            }
            Assert.That(Encode(restored), Is.EqualTo(Encode(original)));
        }

        [Test]
        public void Promotion_ChangesTrainingPlanAndPreservesPopulationAndValidationArena()
        {
            var settings = new EvolutionSettings
            {
                CurriculumEnabled = true, CurriculumStreak = 19, PopulationSize = 4, EliteCount = 1,
                Algorithm = EvolutionAlgorithm.Neat
            };
            var evolution = new GenerationalEvolution(settings);
            var seeds = evolution.Plan.TrainingSeeds(0);
            var episodes = Enumerable.Range(0, 4).SelectMany(c => Enumerable.Range(0, seeds.Length).Select(s =>
                new EpisodeResult(EvaluationMode.Training, 0, c, s, seeds[s], 300, 15, 10, 300, true, 10,
                    true, 1, 10))).ToArray();
            var report = evolution.Advance(GenerationEvaluation.FromEpisodes(0, EvaluationMode.Training, seeds, episodes, 20));
            Assert.That(evolution.Settings.CurriculumStage, Is.EqualTo(1));
            Assert.That(evolution.Plan.CurriculumStage, Is.EqualTo(1));
            Assert.That(evolution.Population.Count, Is.EqualTo(4));
            Assert.That(evolution.BeginTraining().Current.EpisodeSeconds, Is.EqualTo(15), "first seed rehearses interception");
            Assert.That(evolution.Plan.ForEpisode(EvaluationMode.Training, 1).EpisodeSeconds, Is.EqualTo(20));
            Assert.That(evolution.BeginValidation(report).Current.EpisodeSeconds, Is.EqualTo(120));
        }

        [Test]
        public void Gate_DoesNotPromoteWhenPreviousSkillIsLost()
        {
            var settings = new EvolutionSettings { CurriculumEnabled = true, CurriculumStage = 1, CurriculumStreak = 19 };
            var episodes = Enumerable.Range(0, 5).Select(s => Outcome(s == 0 ? 0 : 1, true, s == 0 ? 0 : 10, seed: s)).ToArray();
            Curriculum.Observe(settings, new GenerationEvaluation(0, EvaluationMode.Training,
                new ulong[] { 0, 1, 2, 3, 4 }, episodes, new[] { new CandidateEvaluation(0, episodes, 20) }));
            Assert.That(settings.CurriculumStage, Is.EqualTo(1));
            Assert.That(settings.CurriculumStreak, Is.Zero);
        }
    }
}
