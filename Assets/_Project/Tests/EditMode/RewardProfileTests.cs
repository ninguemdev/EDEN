using System;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>
    /// Versioned reward profiles: the legacy profile is recorded as before, invalid definitions are refused, and scores of
    /// another profile or parameter hash never rank genomes together.
    /// </summary>
    public class RewardProfileTests
    {
        private static readonly DateTime SavedAt = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        private static RewardProfile Food(double target) =>
            new RewardProfile("test-food-v1",
                new[] { new RewardStage("resources", 1, StageScoring.FoodEnergyPerSecond, "food_energy") },
                new[] { new RewardParameter("target", target) });

        [Test]
        public void Legacy_IsTheFoodProfileWithoutHashAndPassesScoresThrough()
        {
            var legacy = RewardProfile.Legacy;
            Assert.That(legacy.Id, Is.EqualTo(Fitness.Profile));
            Assert.That(legacy.IsLegacy);
            Assert.That(legacy.ParameterHash, Is.Null);
            Assert.That(legacy.Stages.Count, Is.EqualTo(1));
            Assert.That(legacy.Stages[0].Scoring, Is.EqualTo(StageScoring.FoodEnergyPerSecond));
            Assert.That(legacy.Stages[0].Weight, Is.EqualTo(1));
            Assert.That(new EvolutionSettings().Profile, Is.SameAs(legacy));

            foreach (var score in new[] { 0f, 0.1f, 1.2345679f, 3.4e38f, float.Epsilon })
                Assert.That(legacy.Combine(new[] { score }), Is.EqualTo(score));
            Assert.That(legacy.Combine(new[] { float.NaN }), Is.EqualTo(Fitness.Worst));
        }

        [Test]
        public void InvalidDefinitions_AreRefusedWithTheReason()
        {
            Assert.That(() => new RewardProfile("two-v1", new[] { StageOf("a", 0.5), StageOf("b", 0.4) }),
                Throws.ArgumentException.With.Message.Contains("add up to 1"));
            Assert.That(() => new RewardStage("a", double.NaN, StageScoring.FoodEnergyPerSecond),
                Throws.ArgumentException.With.Message.Contains("weight"));
            Assert.That(() => new RewardStage("a", double.PositiveInfinity, StageScoring.FoodEnergyPerSecond),
                Throws.ArgumentException.With.Message.Contains("weight"));
            Assert.That(() => new RewardStage("a", 0, StageScoring.FoodEnergyPerSecond),
                Throws.ArgumentException.With.Message.Contains("weight"));
            Assert.That(() => new RewardStage("a", 1, (StageScoring)99),
                Throws.ArgumentException.With.Message.Contains("unknown scoring"));
            Assert.That(() => new RewardStage("a", 1, StageScoring.FoodEnergyPerSecond, "collected_targets"),
                Throws.ArgumentException.With.Message.Contains("\"collected_targets\""));
            Assert.That(() => new RewardStage("a", 1, StageScoring.FoodEnergyPerSecond, "food_energy", "food_energy"),
                Throws.ArgumentException.With.Message.Contains("twice"));
            Assert.That(() => new RewardProfile("dup-v1", new[] { StageOf("a", 0.5), StageOf("a", 0.5) }),
                Throws.ArgumentException.With.Message.Contains("appears twice"));
            Assert.That(() => new RewardProfile("nan-v1", new[] { StageOf("a", 1) },
                    new[] { new RewardParameter("alpha", double.NaN) }),
                Throws.ArgumentException.With.Message.Contains("\"alpha\" must be finite"));
            Assert.That(() => new RewardProfile("inf-v1", new[] { StageOf("a", 1) },
                    new[] { new RewardParameter("alpha", double.NegativeInfinity) }),
                Throws.ArgumentException.With.Message.Contains("must be finite"));
            Assert.That(() => new RewardProfile("", new[] { StageOf("a", 1) }),
                Throws.ArgumentException.With.Message.Contains("id"));
            Assert.That(() => new RewardProfile(Fitness.Profile, new[] { StageOf("a", 1) }),
                Throws.ArgumentException.With.Message.Contains("legacy"));
            Assert.That(() => new RewardProfile("none-v1", Array.Empty<RewardStage>()),
                Throws.ArgumentException.With.Message.Contains("stage"));
            Assert.That(() => new RewardComponent("x", "energy", double.NaN, 1),
                Throws.ArgumentException.With.Message.Contains("finite"));
        }

        [Test]
        public void ParameterHash_FollowsTheDefinition()
        {
            Assert.That(Food(3).ParameterHash, Does.Match("^[0-9a-f]{16}$"));
            Assert.That(Food(3).ParameterHash, Is.EqualTo(Food(3).ParameterHash));
            Assert.That(Food(4).ParameterHash, Is.Not.EqualTo(Food(3).ParameterHash));

            var otherComponents = new RewardProfile("test-food-v1",
                new[] { new RewardStage("resources", 1, StageScoring.FoodEnergyPerSecond, "food_energy", "final_energy") },
                new[] { new RewardParameter("target", 3) });
            Assert.That(otherComponents.ParameterHash, Is.Not.EqualTo(Food(3).ParameterHash));
            Assert.That(Food(3).Matches("test-food-v1", Food(3).ParameterHash));
            Assert.That(Food(3).Matches("test-food-v1", Food(4).ParameterHash), Is.False);
        }

        [Test]
        public void Combine_WeighsStagesAndAnyInvalidStageIsWorst()
        {
            var profile = new RewardProfile("two-v1", new[] { StageOf("search", 0.3), StageOf("resources", 0.7) });
            Assert.That(profile.Combine(new[] { 1f, 0.5f }), Is.EqualTo(0.65f).Within(1e-6f));
            Assert.That(profile.Combine(new[] { 0f, 0f }), Is.EqualTo(0f));
            Assert.That(profile.Combine(new[] { float.NaN, 1f }), Is.EqualTo(Fitness.Worst));
            Assert.That(profile.Combine(new[] { 1f, Fitness.Worst }), Is.EqualTo(Fitness.Worst));
            Assert.That(() => profile.Combine(new[] { 1f }), Throws.ArgumentException);
        }

        [Test]
        public void Plan_RefusesSeveralStagesAndAMissingProfile()
        {
            var several = new EvolutionSettings
            {
                Profile = new RewardProfile("two-v1", new[] { StageOf("a", 0.5), StageOf("b", 0.5) })
            };
            Assert.That(() => new EvaluationPlan(several), Throws.ArgumentException.With.Message.Contains("2 stages"));
            Assert.That(() => new EvaluationPlan(new EvolutionSettings { Profile = null }),
                Throws.ArgumentException.With.Message.Contains("reward profile is required"));
            Assert.That(new EvaluationPlan(new EvolutionSettings { Profile = Food(3) }).Profile.ParameterHash,
                Is.EqualTo(Food(3).ParameterHash));
        }

        [Test]
        public void Records_NameTheProfileAndHashOnlyOutsideTheLegacyProfile()
        {
            var legacy = new ExperimentHeader(new EvolutionSettings(), 1, "0.1.0", SavedAt);
            var other = new ExperimentHeader(new EvolutionSettings { Profile = Food(3) }, 1, "0.1.0", SavedAt);

            Assert.That(legacy.Describe(), Does.Contain("fitness_profile=" + Fitness.Profile));
            Assert.That(string.Join("\n", legacy.Describe()), Does.Not.Contain("fitness_profile_hash").And.Not.Contain("reward_"));
            Assert.That(ExperimentJson.Experiment(legacy), Does.Not.Contain("fitnessProfileHash").And.Not.Contain("rewardProfile"));

            var lines = other.Describe();
            Assert.That(lines, Does.Contain("fitness_profile=test-food-v1"));
            Assert.That(lines, Does.Contain("fitness_profile_hash=" + Food(3).ParameterHash));
            Assert.That(lines, Does.Contain("reward_stage=resources;weight 1;scoring FoodEnergyPerSecond;components food_energy energy"));
            Assert.That(lines, Does.Contain("reward_parameters=target 3"));
            var json = ExperimentJson.Experiment(other);
            Assert.That(json, Does.Contain("\"fitnessProfile\": \"test-food-v1\""));
            Assert.That(json, Does.Contain("\"fitnessProfileHash\": \"" + Food(3).ParameterHash + "\""));
            Assert.That(other.ConfigHash, Is.Not.EqualTo(legacy.ConfigHash), "the profile is part of the definition");
            Assert.That(new ExperimentHeader(new EvolutionSettings { Profile = Food(4) }, 1, "0.1.0", SavedAt).ConfigHash,
                Is.Not.EqualTo(other.ConfigHash));
        }

        [Test]
        public void GenomeFiles_KeepTheHashAndAScoreOfAnotherHashDoesNotSeedAPool()
        {
            var genome = new Genome(NetworkTopology.Mlp12x16x3, new float[NetworkTopology.Mlp12x16x3.ParameterCount]);
            var scored = new GenomeRecord(genome, 0.8f, Food(3));
            Assert.That(scored.FitnessFor(Food(3)), Is.EqualTo(0.8f));
            Assert.That(scored.FitnessFor(Food(4)), Is.Null, "same id, other parameters");
            Assert.That(scored.FitnessFor(RewardProfile.Legacy), Is.Null);
            Assert.That(scored.FitnessFor("test-food-v1"), Is.Null, "an id alone means no hash");

            var text = GenomeCodec.Encode(scored, "0.1.0", SavedAt);
            Assert.That(text, Does.Contain("\"schemaVersion\": 3"));
            var decoded = GenomeCodec.Decode(text);
            Assert.That(decoded.FitnessProfileHash, Is.EqualTo(Food(3).ParameterHash));
            Assert.That(decoded.FitnessFor(Food(3)), Is.EqualTo(0.8f));
            Assert.That(decoded.FitnessFor(Food(4)), Is.Null);

            var legacy = new GenomeRecord(genome, 0.8f, RewardProfile.Legacy);
            var legacyText = GenomeCodec.Encode(legacy, "0.1.0", SavedAt);
            Assert.That(legacyText, Is.EqualTo(GenomeCodec.Encode(new GenomeRecord(genome, 0.8f), "0.1.0", SavedAt)));
            Assert.That(legacyText, Does.Contain("\"schemaVersion\": 2").And.Not.Contain("fitnessProfileHash"));
            Assert.That(GenomeCodec.Decode(legacyText).FitnessFor(RewardProfile.Legacy), Is.EqualTo(0.8f));

            Assert.That(() => GenomeCodec.Decode(text.Replace("\"schemaVersion\": 3", "\"schemaVersion\": 2")),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("schema 3"));
            Assert.That(() => GenomeCodec.Decode(text.Replace(Food(3).ParameterHash, "xyz")),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("hexadecimal"));
        }

        [Test]
        public void Checkpoints_RefuseRunsOfAnotherProfile()
        {
            var settings = new EvolutionSettings
            {
                ExperimentSeed = 3, EpisodeSeconds = 1f, TrainingSeedsPerGeneration = 1, ValidationSeedCount = 1,
                PopulationSize = 4, EliteCount = 1, TournamentSize = 2, Profile = Food(3)
            };
            var session = new EvolutionSession(settings, 1);
            session.Advance(5);
            var checkpoint = new Checkpoint("evolution", "minimal", 3, "neural", session.Capture(), null);
            Assert.That(() => CheckpointCodec.Encode(checkpoint, "test", "test", SavedAt),
                Throws.TypeOf<CheckpointException>().With.Message.Contains("\"test-food-v1\""));
        }

        private static RewardStage StageOf(string name, double weight) =>
            new RewardStage(name, weight, StageScoring.FoodEnergyPerSecond);
    }
}
