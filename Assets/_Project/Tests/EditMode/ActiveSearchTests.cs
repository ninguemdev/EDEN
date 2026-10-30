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
    public class ActiveSearchTests
    {
        public static EvolutionSettings Settings(bool only = true)
        {
            var s = new EvolutionSettings { ExperimentSeed = 1, Algorithm = EvolutionAlgorithm.Neat,
                Senses = Senses.Eyes, EvolvingEyes = true, PopulationSize = 4, EliteCount = 1,
                TrainingSeedsPerGeneration = 2, ValidationSeedCount = 1 };
            RewardProfiles.UseActiveSearch(s, new TwoStageOptions(), new ActiveSearchOptions(), only);
            return s;
        }
        [TestCase(0, 0, 0, 0)]
        [TestCase(1, 1, 1, 1)]
        [TestCase(1, 0, 0, 0.1857142857)]
        [TestCase(0, 0, 1, 0.3)]
        public void Formula(double e, double d, double c, double expected) =>
            Assert.That(StageScores.ExploreCollect(e, d, c), Is.EqualTo(expected).Within(1e-7));
        [Test]
        public void FormulaRejectsInvalid()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => StageScores.ExploreCollect(double.NaN, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => StageScores.ExploreCollect(0, double.PositiveInfinity, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => StageScores.ExploreCollect(0, 0, -1));
        }
        [TestCase(123.45678f)]
        [TestCase(0.123456789f)]
        public void LegacyStageCsvKeepsSinglePrecisionFormatting(float amount)
        {
            var profile = RewardProfiles.BalancedResources(true);
            var index = profile.Stages[0].Components.ToList().FindIndex(c => c.Name == "absorbed_energy");
            var values = new float[profile.Stages[0].Components.Count]; values[index] = amount;
            var episode = new EpisodeResult(EvaluationMode.Training, 0, 0, 0, 1, 2400, 120, 0, 2400, true, 50,
                values, score: 0.5f);
            Assert.That(StageCsv.FormatRow(profile, episode).Split(',')[10 + index],
                Is.EqualTo(amount.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)));
        }

        [Test]
        public void LogicalSeedsAndProfilesRoundTrip()
        {
            var single = new EvaluationPlan(Settings()); var two = new EvaluationPlan(Settings(false));
            Assert.That(single.Stages.Count, Is.EqualTo(1)); Assert.That(single.Stages[0].Reward.Weight, Is.EqualTo(1));
            Assert.That(single.SeedScheme, Is.EqualTo(3)); Assert.That(two.SeedScheme, Is.EqualTo(3));
            for (var g = 0; g < 3; g++)
                Assert.That(single.TrainingSeeds(g), Is.EqualTo(two.TrainingSeeds(g).Skip(2)));
            Assert.That(single.ValidationSeeds(), Is.EqualTo(two.ValidationSeeds().Skip(1)));
            Assert.That(single.Stages[0].Search.Layout(single.TrainingSeeds(0)[0]).Founder,
                Is.EqualTo(two.Stages[1].Search.Layout(two.TrainingSeeds(0)[2]).Founder));
            foreach (var only in new[] { true, false })
            {
                var s = Settings(only); var read = ProfileChoice.Read(s);
                Assert.That(read.Kind, Is.EqualTo(only ? ProfileKind.SearchActive : ProfileKind.TwoStageActive));
                Assert.That(RewardProfiles.Create(s.Profile.Id, s.Profile.Parameters).ParameterHash, Is.EqualTo(s.Profile.ParameterHash));
                var copy = new EvolutionSettings { Algorithm = EvolutionAlgorithm.Neat, Senses = Senses.Eyes };
                read.Apply(copy, null); Assert.That(copy.Profile.ParameterHash, Is.EqualTo(s.Profile.ParameterHash));
            }
            var invalid = Settings(); invalid.World.InitialEnergy += 1;
            Assert.Throws<ArgumentException>(() => new EvaluationPlan(invalid));
        }
        [Test]
        public void ActualConsumptionAndNoRespawn_EvenOnFatalTick()
        {
            var s = Settings(); var plan = new EvaluationPlan(s);
            var e = new IsolatedEpisode(plan, () => new StillBrain(), plan.TrainingSeeds(0)[0], 0, 0, 0,
                EvaluationMode.Training, eyes: EyeSet.Founder);
            var c = e.Creature; var config = e.Simulation.World.Config;
            Assert.That(e.Simulation.FrozenNeeds, Is.False); Assert.That(e.Simulation.RespawnFood, Is.False);
            c.Position = e.Layout.Food[0]; var before = c.Energy; e.Advance(1);
            Assert.That(c.Energy, Is.GreaterThan(before)); Assert.That(e.Tracker.Collected, Does.Contain(0));
            Assert.That(e.Simulation.World.Food.Any(f => f.Id.Value == 1), Is.False);
            Assert.That(e.Simulation.SnapshotAgenda(), Is.Empty);
            c.Position = WaterPlace(e.Layout.Water[0]); c.Hydration = config.MaxHydration;
            e.Advance(1); Assert.That(e.Tracker.Active.Collection, Is.EqualTo(0.2));
            c.Hydration = 50; e.Advance(10);
            Assert.That(e.Tracker.Active.WaterUseful[0], Is.GreaterThanOrEqualTo(5));
            Assert.That(c.Hydration, Is.GreaterThan(50));
            var collected = e.Tracker.Collected.Count; e.Advance(10); Assert.That(e.Tracker.Collected.Count, Is.EqualTo(collected));
            c.Position = e.Layout.Food[1]; c.Hydration = 0.01f; e.Advance(1);
            Assert.That(c.IsAlive, Is.False); Assert.That(e.Tracker.Collected, Does.Contain(1));
            Assert.That(e.Tracker.AbsorbedEnergy, Is.GreaterThan(0)); Assert.That(e.Result().Fitness, Is.InRange(0f, 1f));
        }
        [Test]
        public void UsefulWaterCrossingThresholdOnFatalTickIsRetained()
        {
            var plan = new EvaluationPlan(Settings());
            var episode = new IsolatedEpisode(plan, () => new StillBrain(), plan.TrainingSeeds(0)[0],
                0, 0, 0, EvaluationMode.Training, eyes: EyeSet.Founder);
            episode.Creature.Position = WaterPlace(episode.Layout.Water[0]);
            episode.Creature.Hydration = 40;
            episode.Advance(4);
            Assert.That(episode.Tracker.Active.WaterUseful[0], Is.LessThan(5));
            episode.Creature.Energy = 0.001f; episode.Advance(1);
            Assert.That(episode.Creature.DeathCause, Is.EqualTo(DeathCause.Starvation));
            Assert.That(episode.Tracker.Collected, Does.Contain(episode.Layout.Food.Count));
            Assert.That(episode.Tracker.Active.WaterUseful[0], Is.GreaterThanOrEqualTo(5));
            Assert.That(episode.Result().PreciseComponents, Is.Not.Null);
        }

        [Test]
        public void NoImmortalityAndNoEarlyFinishAfterCollectingEverything()
        {
            var p = new EvaluationPlan(Settings());
            var e = new IsolatedEpisode(p, () => new StillBrain(), p.TrainingSeeds(0)[0], 0, 0, 0, EvaluationMode.Training,
                eyes: EyeSet.Founder);
            foreach (var food in e.Layout.Food) { e.Creature.Position = food; e.Advance(1); }
            for (var i = 0; i < e.Layout.Water.Count; i++)
            {
                e.Creature.Position = WaterPlace(e.Layout.Water[i]); e.Creature.Hydration = 40; e.Advance(10);
            }
            Assert.That(e.Tracker.Collected.Count, Is.EqualTo(5)); Assert.That(e.IsFinished, Is.False);
            e.Creature.Position = e.Layout.Founder; e.Advance(long.MaxValue);
            Assert.That(e.Creature.DeathCause, Is.EqualTo(DeathCause.Dehydration));
            Assert.That(e.Tick, Is.LessThan(p.EpisodeTicks)); Assert.That(e.Tracker.Active.Collection, Is.EqualTo(1));
        }
        [Test]
        public void CoverageHysteresisUniqueVisitsAndPartialEdge()
        {
            var p = new EvaluationPlan(Settings());
            var e = new IsolatedEpisode(p, () => new StillBrain(), p.TrainingSeeds(0)[0], 0, 0, 0, EvaluationMode.Training,
                eyes: EyeSet.Founder);
            var grid = new ExplorationGrid(e.Layout.Config, e.Layout.Founder, 2);
            var cell = Enumerable.Range(0, grid.Columns * grid.Rows).First(i => grid.Eligible(i) && i % grid.Columns > 1);
            var centre = grid.Centre(cell);
            e.Creature.Position = new Float2(cell % grid.Columns * 2 + 0.1f, centre.Y); e.Advance(1);
            Assert.That(e.Tracker.Active.Unique, Is.Zero);
            e.Creature.Position = centre; e.Advance(1); Assert.That(e.Tracker.Active.Unique, Is.EqualTo(1));
            e.Advance(4); Assert.That(e.Tracker.Active.Unique, Is.EqualTo(1));
            var last = grid.CellAt(new Float2(e.Layout.Config.ArenaWidth, e.Layout.Config.ArenaHeight));
            Assert.That(last, Is.EqualTo(grid.Columns * grid.Rows - 1));
            e.Advance(long.MaxValue);
            var index = p.Profile.Stages[0].Components.ToList().FindIndex(component => component.Name == "coverage");
            Assert.That(e.Result().PreciseComponents[index], Is.EqualTo((double)e.Tracker.Active.Unique / e.Tracker.Active.Eligible));
            Assert.That(e.Result().PreciseComponents[index], Is.GreaterThan(0).And.LessThan(1));
        }
        [Test]
        public void InstrumentedEyesAreBitIdenticalAndSelectionIdsAreExact()
        {
            var s = new SimulationSettings { InitialPopulation = 1, InitialFood = 0, SolidBodies = true };
            s.Water = WaterLayouts.Create(WaterLayout.Pond, s); var w = WorldFactory.CreateInitial(new SimulationConfig(s));
            var c = w.Creatures[0]; c.Position = new Float2(10, 10); c.Heading = 0; c.Eyes = EyeSet.Founder;
            var a = w.AddFood(new Float2(12, 10), 5); w.AddFood(new Float2(14, 10), 5);
            var plain = new float[EyeChannels.WithWater]; var observed = new float[plain.Length];
            var ids = new System.Collections.Generic.List<int>();
            Vision.See(w, c, c.Eyes, 0, plain, 0, true);
            Vision.See(w, c, c.Eyes, 0, observed, 0, true, (id, target, water) => { if (!water) ids.Add(target); });
            Assert.That(observed, Is.EqualTo(plain)); Assert.That(ids, Is.EqualTo(new[] { 1 }));
        }
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void CheckpointAndWorkersAreDeterministic(int workers)
        {
            var settings = Settings(); var original = new EvolutionSession(settings, 5, workers);
            original.Advance(367);
            var checkpoint = new Checkpoint("evolution", "minimal", 1, "neural", original.Capture(), null);
            var time = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            var json = CheckpointCodec.Encode(checkpoint, "test", "test", time);
            Assert.That(json, Does.Contain("\"schemaVersion\": 9"));
            var resumed = EvolutionSession.Restore(CheckpointCodec.Decode(json, out _).Evolution);
            Assert.That(resumed.Job.Current.Simulation.RespawnFood, Is.False);
            Assert.That(resumed.Job.Current.Simulation.ComputeFingerprint(), Is.EqualTo(original.Job.Current.Simulation.ComputeFingerprint()));
            for (var i = 0; i < 10; i++) { original.Advance(23); resumed.Advance(23); }
            Assert.That(CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 1, "neural", resumed.Capture(), null), "test", "test", time),
                Is.EqualTo(CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 1, "neural", original.Capture(), null), "test", "test", time)));
            var plan = new EvaluationPlan(settings); var genomes = InitialGenomes.NeatPopulation(1, 2, BodyGeneSet.None, 1, plan.Contract);
            var reference = new IsolatedEvaluator(plan).TrainingJob(genomes, 0).RunToEnd();
            var job = new IsolatedEvaluator(plan).TrainingJob(genomes, 0); job.WorkerCount = workers;
            while (!job.IsComplete) job.Advance(17);
            Assert.That(job.Result.Episodes.Select(e => e.Fitness), Is.EqualTo(reference.Episodes.Select(e => e.Fitness)));
            Assert.That(job.Result.Episodes.Select(e => string.Join(",", e.Components)), Is.EqualTo(reference.Episodes.Select(e => string.Join(",", e.Components))));
        }
        [TestCase(1UL)]
        [TestCase(2UL)]
        [TestCase(3UL)]
        public void DiagnosticRouteDemonstratesRealPhysiologicalFeasibility(ulong seed)
        {
            var p = new EvaluationPlan(Settings());
            foreach (var eyes in new[] { EyeSet.Founder, ActiveSearchDiagnostics.MaximumEyes() })
            {
                var result = ActiveSearchDiagnostics.Run(p.Stages[0], p.Profile, seed, eyes);
                UnityEngine.Debug.Log("ACTIVE_SEARCH_DIAGNOSTIC " + result);
                Assert.That(result.Feasible, Is.True, result + ";" + result.Route);
                Assert.That(result.Seconds, Is.GreaterThanOrEqualTo(120));
                Assert.That(result.Visited, Is.GreaterThan(50), "exploration remains possible after collecting");
            }
        }

        [Test]
        public void CheckpointPreservesDoubleBitsAtTheGeneration750Regression()
        {
            // The maintenance total of candidate 113 / seed 1 at g749 triggered Mono's R formatter bug.
            foreach (var bits in new[] { 4628509548862323434L, 4628509548862323435L, 4628509548862323436L })
            {
                var original = new EvolutionSession(Settings());
                original.Job.Current.Tracker.Active.Maintenance = BitConverter.Int64BitsToDouble(bits);
                var checkpoint = new Checkpoint("evolution", "minimal", 1, "neural", original.Capture(), null);
                var time = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
                var text = CheckpointCodec.Encode(checkpoint, "test", "test", time);
                var decoded = CheckpointCodec.Decode(text, out _);
                Assert.That(BitConverter.DoubleToInt64Bits(decoded.Evolution.Job.CurrentTracker.Active.Maintenance),
                    Is.EqualTo(bits), "double bits " + bits);
                Assert.That(CheckpointCodec.Encode(decoded, "test", "test", time), Is.EqualTo(text));
                var restored = EvolutionSession.Restore(decoded.Evolution);
                original.Advance(50); restored.Advance(50);
                Assert.That(CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 1, "neural", restored.Capture(), null), "test", "test", time),
                    Is.EqualTo(CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 1, "neural", original.Capture(), null), "test", "test", time)));
            }
        }

        [Test]
        public void ResumeAroundConsumptionThresholdAndCompletedEpisodes()
        {
            foreach (var waterTicks in new[] { 0, 1, 4, 5, 7 })
            {
                var original = new EvolutionSession(Settings(), 5, 2);
                var episode = original.Job.Current;
                episode.Creature.Position = episode.Layout.Food[0]; original.Advance(1);
                episode.Creature.Position = WaterPlace(episode.Layout.Water[0]); episode.Creature.Hydration = 40;
                if (waterTicks > 0) original.Advance(waterTicks);
                var time = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
                string Save(EvolutionSession session) => CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 1,
                    "neural", session.Capture(), null), "test", "test", time);
                var saved = Save(original);
                var restored = EvolutionSession.Restore(CheckpointCodec.Decode(saved, out _).Evolution);
                Assert.That(Save(restored), Is.EqualTo(saved));
                original.Advance(12000); restored.Advance(12000);
                Assert.That(Save(restored), Is.EqualTo(Save(original)));
            }
        }

        private static Float2 WaterPlace(Obstacle water) => new Float2(water.MinX - 0.6f, (water.MinY + water.MaxY) / 2);
        private sealed class StillBrain : IBrain
        {
            public string Name => "still-test";
            public CreatureAction Evaluate(in Observation observation, BrainState state) => CreatureAction.Idle;
        }
    }
}
