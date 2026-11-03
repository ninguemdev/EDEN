using System;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using Eden.Simulation;
using NUnit.Framework;
using static Eden.Tests.EditMode.ResumeChecks;

namespace Eden.Tests.EditMode
{
    /// <summary>
    /// The two-stage search profile: search rules, target layout, path distances, the stage scores, and runs that resume
    /// exactly from progress and from checkpoints.
    /// </summary>
    public class TwoStageProfileTests
    {
        private static readonly DateTime SavedAt = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void FrozenNeeds_KeepEnergyAndHydrationAndMeasureWhatWouldBeCharged()
        {
            var settings = new SimulationSettings { InitialPopulation = 1, InitialFood = 0, SolidBodies = true };
            settings.Water = WaterLayouts.Create(WaterLayout.Pond, settings);
            var config = new SimulationConfig(settings);
            var frozen = new WorldSimulation(WorldFactory.CreateInitial(config)) { FrozenNeeds = true };
            var normal = new WorldSimulation(WorldFactory.CreateInitial(config));
            var creature = frozen.World.Creatures[0];
            var energy = creature.Energy;
            var hydration = creature.Hydration;
            var controller = new BrainController(config, _ => new HeuristicBrain());
            var other = new BrainController(config, _ => new HeuristicBrain());
            for (var tick = 0; tick < 200; tick++)
            {
                frozen.Step(controller);
                normal.Step(other);
                Assert.That(creature.Energy, Is.EqualTo(energy));
                Assert.That(creature.Hydration, Is.EqualTo(hydration));
                if (tick == 0)
                    Assert.That(frozen.LastEnergy.Uncharged, Is.EqualTo(normal.LastEnergy.Charged).Within(1e-6),
                        "the cost not charged is what the same first tick charges");
            }

            Assert.That(creature.AgeTicks, Is.EqualTo(200));
            Assert.That(frozen.LastEnergy.Charged, Is.EqualTo(0));
        }

        [Test]
        public void FreeSpaceDistance_GoesAroundAWall()
        {
            var wall = new Obstacle(24f, 0f, 26f, 40f);
            var space = new FreeSpace(50f, 50f, 0.5f, new[] { wall });
            var field = space.DistanceField(new[] { space.CellAt(new Float2(10f, 10f)) });
            var across = space.DistanceAt(field, new Float2(40f, 10f));
            Assert.That(across, Is.GreaterThan(60f), "around the wall, not through it (30 straight)");
            var open = space.DistanceAt(field, new Float2(10f, 30f));
            Assert.That(open, Is.EqualTo(20f).Within(1f));
        }

        [TestCase(ObstacleLayout.Pillars)]
        [TestCase(ObstacleLayout.Corridor)]
        public void Targets_AreApartAlongThePathAndInTheFoundersRegion(ObstacleLayout layout)
        {
            var settings = TwoStage(3, o => o.SearchObstacles = layout);
            var plan = new EvaluationPlan(settings);
            var search = plan.Stages[1].Search;
            var min = search.Targets.MinDistance;
            for (var seed = 1UL; seed <= 30; seed++)
            {
                var placed = search.Layout(seed);
                Assert.That(placed.TargetCount, Is.EqualTo(5));
                Assert.That(placed.Water.Count, Is.EqualTo(2));
                for (var t = 0; t < placed.TargetCount; t++)
                {
                    Assert.That(placed.DistanceTo(t, placed.Founder), Is.GreaterThanOrEqualTo(min * 0.95f),
                        $"seed {seed}: target {t} from the founder");
                    for (var u = 0; u < t; u++)
                        Assert.That(placed.DistanceTo(u, placed.TargetPoint(t)), Is.GreaterThanOrEqualTo(min * 0.9f),
                            $"seed {seed}: targets {u} and {t}");
                }
            }

            Assert.That(search.ArenaSide, Is.GreaterThan(50f), "six points 30 apart do not fit 50 × 50");
        }

        [Test]
        public void SearchScore_RewardsReachingAboveApproachingAndSpeedOnlyInItsShare()
        {
            var none = StageScores.Search(5, 0.5, 0.3, new float[0], 0);
            var halfWay = StageScores.Search(5, 0.5, 0.3, new float[0], 1);
            var one = StageScores.Search(5, 0.5, 0.3, new[] { 0f }, 0);
            Assert.That(none, Is.EqualTo(0f));
            Assert.That(halfWay, Is.LessThan(one), "reaching beats any approach");
            var fast = StageScores.Search(5, 0.5, 0.3, new[] { 1f, 1f, 1f, 1f, 1f }, 0);
            var slow = StageScores.Search(5, 0.5, 0.3, new[] { 0f, 0f, 0f, 0f, 0f }, 0);
            Assert.That(fast, Is.EqualTo(1f));
            Assert.That(slow, Is.EqualTo(0.7f).Within(1e-6f), "time takes only the β share");
            Assert.That(StageScores.Approach(10, 4), Is.EqualTo(0.6f).Within(1e-6f));
            Assert.That(StageScores.Approach(10, 12), Is.EqualTo(0f), "moving away never counts");
        }

        [Test]
        public void ResourcesScore_IsInZeroToOneAndWasteNeverRaisesIt()
        {
            float Score(double survival, double eaten, double drunk, double reserves) =>
                StageScores.BalancedResources(0.5, 0.3, 0.2, survival, StageScores.Replenished(eaten, 100),
                    StageScores.Replenished(drunk, 100), reserves);

            Assert.That(Score(1, 100, 100, 1), Is.EqualTo(1f));
            Assert.That(Score(0.2, 10, 0, 0), Is.LessThan(Score(1, 10, 0, 0.5)), "dying early scores less");
            Assert.That(Score(1, 1000, 0, 0.5), Is.EqualTo(Score(1, 100, 0, 0.5)), "food beyond the need adds nothing");
            Assert.That(Score(1, 1000, 0, 0.5), Is.LessThan(Score(1, 100, 100, 0.5)), "food does not make up for water");
            Assert.That(Score(0, 0, 0, 0), Is.EqualTo(0f));
        }

        [Test]
        public void TwoStageEvaluation_ScoresInRangeAndResumesInsideTheSearchStage()
        {
            var plan = new EvaluationPlan(TwoStage(5));
            var genomes = InitialGenomes.NeatPopulation(5, 2, BodyGeneSet.None, 1f, plan.Contract);
            var continuous = new IsolatedEvaluator(plan).EvaluateTraining(genomes, 0);
            foreach (var episode in continuous.Episodes)
            {
                Assert.That(episode.HasScore);
                Assert.That(episode.Fitness, Is.InRange(0f, 1f));
                Assert.That(episode.Components.Count, Is.EqualTo(plan.Stages[episode.Stage].Reward.Components.Count));
            }

            foreach (var candidate in continuous.Candidates)
                Assert.That(candidate.MeanFitness, Is.EqualTo(plan.Profile.Combine(candidate.StageFitness)));

            var resources = 2 * (int)plan.Stages[0].EpisodeTicks;
            foreach (var workers in new[] { 1, 3 })
            {
                var job = new IsolatedEvaluator(plan).TrainingJob(genomes, 0);
                job.WorkerCount = workers;
                var ran = 0L;
                while (ran < resources + 37 && !job.IsComplete)
                    ran += job.Advance(Math.Min(11, resources + 37 - ran));
                var progress = job.Capture();
                var resumed = new IsolatedEvaluator(plan).TrainingJob(genomes, 0);
                resumed.Resume(progress);
                var result = resumed.RunToEnd();
                Assert.That(result.Episodes.Select(e => (e.Stage, e.Fitness, e.LegacyFitness)),
                    Is.EqualTo(continuous.Episodes.Select(e => (e.Stage, e.Fitness, e.LegacyFitness))));
                Assert.That(result.Episodes.Select(e => string.Join(",", e.Components)),
                    Is.EqualTo(continuous.Episodes.Select(e => string.Join(",", e.Components))));
            }
        }

        [TestCase(1)]
        [TestCase(3)]
        public void TwoStageSession_SavedInEitherStage_ResumesExactly(int workers)
        {
            foreach (var searchStage in new[] { false, true })
            {
                var original = new EvolutionSession(TwoStage(9), 1, workers);
                AdvanceUntil(original, () => original.Phase == EvolutionPhase.Training &&
                                             original.Job.Current.Stage == (searchStage ? 1 : 0) &&
                                             original.Job.Current.Tick > 20);
                var text = CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 9, "neural", original.Capture(), null),
                    "0.1.0", "test", SavedAt);
                Assert.That(text, Does.Contain("\"schemaVersion\": 8,").And.Contain(RewardProfiles.TwoStageSearchId));
                var restored = EvolutionSession.Restore(CheckpointCodec.Decode(text, out _).Evolution);
                AssertSameSession(restored, original);
                AdvanceUntil(original, () => original.Training.Count >= 2, 97);
                AdvanceUntil(restored, () => restored.Training.Count >= 2, 31);
                CatchUp(restored, original);
                AssertSameSession(restored, original);
            }
        }

        [Test]
        public void ProfileChoice_RefusesInvalidValuesAndRecordsTheStages()
        {
            var choice = new ProfileChoice();
            Assert.That(() => choice.TryRead("-eden-profile", "both"), Throws.ArgumentException);
            Assert.That(() => choice.TryRead("-eden-stage1-seconds", "x"), Throws.ArgumentException);
            Assert.That(choice.TryRead("-eden-seed", "3"), Is.False);
            Assert.That(choice.TryRead("-eden-profile", "two-stage"), Is.True);
            var settings = TwoStage(2);
            var header = new ExperimentHeader(settings, 1, "0.1.0", SavedAt);
            var lines = header.Describe();
            Assert.That(lines.Count(l => l.StartsWith("stage=", StringComparison.Ordinal)), Is.EqualTo(2));
            Assert.That(lines, Has.Some.Contains("rules search").And.Some.Contains("arena_used"));
            Assert.That(ProfileChoice.Read(settings).Kind, Is.EqualTo(ProfileKind.TwoStage));
        }

        // NEAT with eyes, a short resources stage with water and a short search stage.
        private static EvolutionSettings TwoStage(ulong seed, Action<TwoStageOptions> change = null)
        {
            var settings = new EvolutionSettings
            {
                ExperimentSeed = seed, EpisodeSeconds = 4f, TrainingSeedsPerGeneration = 2, ValidationSeedCount = 1,
                PopulationSize = 4, EliteCount = 1, TournamentSize = 2, Algorithm = EvolutionAlgorithm.Neat,
                Senses = Senses.Eyes
            };
            var options = new TwoStageOptions
            {
                SearchSeconds = 60f, FeasibleFraction = 1, SearchTrainingSeeds = 2, SearchValidationSeeds = 1
            };
            change?.Invoke(options);
            RewardProfiles.UseTwoStageSearch(settings, options);
            return settings;
        }
    }
}
