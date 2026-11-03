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
        public static EvolutionSettings Settings(bool only = true, ActiveSearchOptions active = null)
        {
            var s = new EvolutionSettings { ExperimentSeed = 1, Algorithm = EvolutionAlgorithm.Neat,
                Senses = Senses.Eyes, EvolvingEyes = true, PopulationSize = 4, EliteCount = 1,
                TrainingSeedsPerGeneration = 2, ValidationSeedCount = 1 };
            RewardProfiles.UseActiveSearch(s, new TwoStageOptions(), active ?? new ActiveSearchOptions(), only);
            return s;
        }

        /// <summary>The rules version 1 options of a run started before exploration was scored by sight.</summary>
        private static ActiveSearchOptions EnteredCells() => new ActiveSearchOptions { RulesVersion = 1 };
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
            Assert.That(json, Does.Contain("\"schemaVersion\": 12"));
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

        // The parameters and hash of the user's own version 1 run (g750 of 20261030-173026-lab-seed1-stress).
        private static readonly RewardParameter[] PublishedVersion1 =
        {
            new RewardParameter("search_weight", 0.3), new RewardParameter("food_targets", 3),
            new RewardParameter("water_targets", 2), new RewardParameter("min_distance", 30),
            new RewardParameter("alpha", 0.5), new RewardParameter("beta", 0.30000001192092896),
            new RewardParameter("water_side", 2), new RewardParameter("max_arena", 200),
            new RewardParameter("feasible_fraction", 0.5), new RewardParameter("survival_weight", 0.5),
            new RewardParameter("replenish_weight", 0.3), new RewardParameter("reserves_weight", 0.2),
            new RewardParameter("active_rules_version", 1), new RewardParameter("mask_version", 1),
            new RewardParameter("frozen_needs", 0), new RewardParameter("food_respawn", 0),
            new RewardParameter("finish_all_targets", 0), new RewardParameter("active_CellSide", 2),
            new RewardParameter("active_Hysteresis", 0.2), new RewardParameter("active_ExplorationGoal", 200),
            new RewardParameter("active_WaterThreshold", 5), new RewardParameter("active_MaxEnergy", 160),
            new RewardParameter("active_InitialEnergy", 80), new RewardParameter("active_FoodEnergy", 30),
            new RewardParameter("active_Metabolism", 0.5), new RewardParameter("active_Movement", 0.5),
            new RewardParameter("active_Sensor", 0.1), new RewardParameter("active_Turn", 0.25),
            new RewardParameter("active_MaxHydration", 100), new RewardParameter("active_HydrationLoss", 1.25),
            new RewardParameter("active_DrinkRate", 20), new RewardParameter("active_MaxAge", 240)
        };

        [Test]
        public void PublishedVersion1ProfileRebuildsWithItsOwnHashAndComponents()
        {
            var profile = RewardProfiles.Create(RewardProfiles.ActiveSearchId, PublishedVersion1);
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.ParameterHash, Is.EqualTo("08c75ba590043126"));
            Assert.That(profile.Stages[0].Scoring, Is.EqualTo(StageScoring.ExploreCollectActive));
            Assert.That(profile.Stages[0].Components.Select(c => c.Name).ToList(), Has.No.Member("seen"));
            Assert.That(RewardProfiles.IsActive(profile), Is.True);
            Assert.That(RewardProfiles.IsSight(profile), Is.False);
            // The same options rebuilt in this build keep that list, parameter for parameter.
            var rebuilt = RewardProfiles.ActiveSearch(new TwoStageOptions { Beta = 0.30000001192092896 },
                EnteredCells(), true);
            Assert.That(rebuilt.Canonical(), Is.EqualTo(profile.Canonical()));
            Assert.That(rebuilt.ParameterHash, Is.EqualTo(profile.ParameterHash));
        }

        [Test]
        public void Version1ScoresByTheCellsEnteredAndKeepsItsSchema()
        {
            var settings = Settings(active: EnteredCells());
            Assert.That(settings.Profile.Id, Is.EqualTo(RewardProfiles.ActiveSearchId));
            Assert.That(ProfileChoice.Read(settings).Active.Sight, Is.False);
            var plan = new EvaluationPlan(settings);
            Assert.That(plan.Stages[0].ActiveSearch, Is.True);
            var episode = new IsolatedEpisode(plan, () => new StillBrain(), plan.TrainingSeeds(0)[0], 0, 0, 0,
                EvaluationMode.Training, eyes: EyeSet.Founder);
            episode.Advance(6);
            var a = episode.Tracker.Active;
            Assert.That(a.SeenUnique, Is.Zero); Assert.That(a.Sightings, Is.Zero); Assert.That(a.SightGoal, Is.Zero);
            Assert.That(a.Sight, Is.Zero); Assert.That(a.Exploration, Is.EqualTo(a.Travel));
            var session = new EvolutionSession(settings, 5, 2); session.Advance(367);
            var time = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            var json = CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 1, "neural", session.Capture(),
                null), "test", "test", time);
            Assert.That(json, Does.Contain("\"schemaVersion\": 9").And.Not.Contain("\"Seen\""));
            var resumed = EvolutionSession.Restore(CheckpointCodec.Decode(json, out _).Evolution);
            Assert.That(resumed.Job.Current.Tracker.Active.SightGoal, Is.Zero);
        }

        [Test]
        public void SightCreditsEveryCellTheEyeCouldReportAndNothingAnObstacleHides()
        {
            var plan = new EvaluationPlan(Settings());
            var episode = new IsolatedEpisode(plan, () => new StillBrain(), plan.TrainingSeeds(0)[0], 0, 0, 0,
                EvaluationMode.Training, eyes: EyeSet.Founder);
            var config = episode.Simulation.World.Config;
            var pillar = config.Obstacles[4]; // the middle pillar of the nine
            // Beside its near corner, looking along it: the pillar hides the cells behind it and not the ones below.
            episode.Creature.Position = new Float2(pillar.MinX - 1.5f, pillar.MinY - 1f);
            episode.Creature.Heading = 0f;
            var position = episode.Creature.Position;
            episode.Advance(1);
            var a = episode.Tracker.Active;
            var grid = new ExplorationGrid(config, episode.Layout.Founder, 2);
            var eye = EyeSet.Founder[0];
            var origin = Vision.EyePosition(config, episode.Creature, eye);
            var axisX = MathF.Cos(eye.Orientation);
            var axisY = MathF.Sin(eye.Orientation);
            var cosHalf = MathF.Cos(eye.Aperture * 0.5f);
            var range = episode.Creature.Body.SensorRange * eye.Range;
            var expected = new System.Collections.Generic.List<int>();
            var hidden = new System.Collections.Generic.List<int>();
            for (var cell = 0; cell < grid.Columns * grid.Rows; cell++)
            {
                if (!grid.Eligible(cell)) continue;
                if (!Vision.InCone(origin, axisX, axisY, cosHalf, range, grid.Centre(cell), out _)) continue;
                if (Vision.LineOfSight(config, origin, grid.Centre(cell))) expected.Add(cell);
                else hidden.Add(cell);
            }

            Assert.That(episode.Creature.Position, Is.EqualTo(position), "an idle creature does not move");
            Assert.That(expected, Is.Not.Empty, "the cone must reach cells the pillar does not hide");
            Assert.That(hidden, Is.Not.Empty, "the pillar must hide cells of the cone");
            Assert.That(a.Clone().Seen, Is.EqualTo(expected));
            Assert.That(a.SeenUnique, Is.EqualTo(expected.Count));
            Assert.That(a.SightGoal, Is.EqualTo((int)Math.Round(0.75 * a.Eligible)));
            // Looking again from the same place credits nothing new, and the repetitions lower the novelty.
            var sightings = a.Sightings;
            episode.Advance(4);
            Assert.That(a.SeenUnique, Is.EqualTo(expected.Count));
            Assert.That(a.Sightings, Is.GreaterThan(sightings));
            Assert.That(a.Novelty, Is.EqualTo(a.SeenUnique / (double)a.Sightings).Within(1e-12));
            Assert.That(a.Sight, Is.EqualTo(Math.Min(1.0, (double)expected.Count / a.SightGoal)).Within(1e-12));
            Assert.That(a.Exploration, Is.EqualTo(0.8 * a.Sight + 0.2 * a.Travel).Within(1e-12));
            episode.Advance(long.MaxValue);
            var components = plan.Profile.Stages[0].Components.Select(c => c.Name).ToList();
            var precise = episode.Result().PreciseComponents;
            Assert.That(precise[components.IndexOf("seen")], Is.EqualTo(a.SeenUnique));
            Assert.That(precise[components.IndexOf("sight_goal")], Is.EqualTo(a.SightGoal));
            Assert.That(precise[components.IndexOf("sightings")], Is.EqualTo(a.Sightings));
            Assert.That(precise[components.IndexOf("sight")], Is.EqualTo(a.Sight));
            Assert.That(precise[components.IndexOf("travel")], Is.EqualTo(a.Travel));
            Assert.That(precise[components.IndexOf("novelty")], Is.EqualTo(a.Novelty));
            Assert.That(precise[components.IndexOf("exploration")], Is.EqualTo(a.Exploration));
        }

        [Test]
        public void CrossingTheInteriorIsWorthMoreThanHuggingTheBorder()
        {
            var plan = new EvaluationPlan(Settings());
            ActiveSearchState Walk(float share)
            {
                var episode = new IsolatedEpisode(plan, () => new StillBrain(), plan.TrainingSeeds(0)[0], 0, 0, 0,
                    EvaluationMode.Training, eyes: EyeSet.Founder);
                var config = episode.Simulation.World.Config;
                var first = config.BodyRadius + 0.5f;
                var last = config.ArenaWidth - first;
                // The centre of the nearest row of cells, so the entry margin does not swallow the visits.
                var y = (MathF.Floor(config.ArenaHeight * share / 2f) + 0.5f) * 2f;
                const int steps = 48;
                for (var i = 0; i < steps; i++)
                {
                    episode.Creature.Position = new Float2(first + (last - first) * i / (steps - 1f), y);
                    episode.Advance(1);
                }

                return episode.Tracker.Active;
            }

            var border = Walk(0.02f);    // a creature that keeps to the wall
            var interior = Walk(0.375f); // the same path, between two rows of pillars
            UnityEngine.Debug.Log($"ACTIVE_SIGHT_WALK border={border.SeenUnique}/{border.Unique} " +
                                  $"interior={interior.SeenUnique}/{interior.Unique} goal={interior.SightGoal} " +
                                  $"travel_goal={interior.Goal} eligible={interior.Eligible} " +
                                  $"E={border.Exploration:0.0000}/{interior.Exploration:0.0000}");
            Assert.That(interior.SeenUnique, Is.GreaterThan(border.SeenUnique));
            Assert.That(interior.Sight, Is.GreaterThan(border.Sight));
            Assert.That(interior.Exploration, Is.GreaterThan(border.Exploration));
            Assert.That(interior.Score, Is.GreaterThan(border.Score));
        }

        [Test]
        public void SightStateResumesAndRefusesACellOutsideTheMask()
        {
            var original = new EvolutionSession(Settings(), 5, 2);
            original.Advance(400);
            var time = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            string Save(EvolutionSession session) => CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 1,
                "neural", session.Capture(), null), "test", "test", time);
            var saved = Save(original);
            Assert.That(saved, Does.Contain("\"sightGoal\"").And.Contain("\"sightings\""));
            var restored = EvolutionSession.Restore(CheckpointCodec.Decode(saved, out var info).Evolution);
            Assert.That(info.SchemaVersion, Is.EqualTo(12));
            Assert.That(restored.Job.Current.Tracker.Active.SeenUnique,
                Is.EqualTo(original.Job.Current.Tracker.Active.SeenUnique).And.GreaterThan(0));
            Assert.That(Save(restored), Is.EqualTo(saved));
            original.Advance(900);
            restored.Advance(900);
            Assert.That(Save(restored), Is.EqualTo(Save(original)));
            var first = saved.IndexOf("\"Seen\": [", StringComparison.Ordinal);
            Assert.That(first, Is.GreaterThan(0));
            var end = saved.IndexOf(']', first);
            var cells = saved.Substring(first, end - first);
            // The initial cell is never eligible, so a state that claims to have seen it is refused.
            var tampered = saved.Substring(0, first) +
                           cells.Substring(0, cells.LastIndexOf(',') + 1) + " 0" + saved.Substring(end);
            Assert.That(tampered, Is.Not.EqualTo(saved));
            Assert.That(() => CheckpointCodec.Decode(tampered, out _), Throws.Exception);
        }

        private static Float2 WaterPlace(Obstacle water) => new Float2(water.MinX - 0.6f, (water.MinY + water.MaxY) / 2);
        private sealed class StillBrain : IBrain
        {
            public string Name => "still-test";
            public CreatureAction Evaluate(in Observation observation, BrainState state) => CreatureAction.Idle;
        }
    }
}
