using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    /// <summary>Checkpoint files: exact JSON round trip, refusal of bad files and replacement with a backup.</summary>
    public class CheckpointTests
    {
        private static readonly DateTime SavedAt = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ParallelCheckpoint_PreservesEveryWorldAndResumesExactly(bool neat, bool eyes)
        {
            var settings = Small(42);
            settings.Algorithm = neat ? EvolutionAlgorithm.Neat : EvolutionAlgorithm.Mlp;
            settings.Senses = eyes ? Senses.Eyes : Senses.Legacy;
            settings.EvolvingEyes = eyes;
            foreach (var phase in new[] { EvolutionPhase.Training, EvolutionPhase.Baselines, EvolutionPhase.Validation })
            {
                var original = new EvolutionSession(settings, 2, evaluationWorkers: 4);
                AdvanceUntil(original, () => original.Phase == phase, 13);
                original.Advance(17);
                var saved = original.Capture();
                Assert.That(saved.Job.Ahead.Count, Is.GreaterThan(0));
                var checkpoint = new Checkpoint("evolution", "minimal", 42, "neural", saved, null);
                var text = CheckpointCodec.Encode(checkpoint, "test", "test", SavedAt);
                var decoded = CheckpointCodec.Decode(text, out var info);
                Assert.That(info.SchemaVersion, Is.EqualTo(7));
                Assert.That(CheckpointCodec.Encode(decoded, "test", "test", SavedAt), Is.EqualTo(text));
                var restored = EvolutionSession.Restore(decoded.Evolution);
                Assert.That(restored.EvaluationWorkers, Is.EqualTo(4));
                Assert.That(restored.Job.WorkerCount, Is.EqualTo(4));
                AssertSameSession(restored, original);
                for (var i = 0; i < 30; i++)
                {
                    original.Advance(113);
                    restored.Advance(113);
                }
                var originalText = CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 42, "neural",
                    original.Capture(), null), "test", "test", SavedAt);
                var restoredText = CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 42, "neural",
                    restored.Capture(), null), "test", "test", SavedAt);
                Assert.That(restoredText, Is.EqualTo(originalText), "all worlds, brains and lineage resume exactly");
            }
        }

        [Test]
        public void ParallelCheckpoint_KeepsAnEpisodeThatFinishedAheadOfTheDisplayedWorld()
        {
            var session = new EvolutionSession(Small(42), 2, evaluationWorkers: 4);
            var expected = new IsolatedEvaluator(session.Plan).TrainingJob(session.Evolution.Population, 0).RunToEnd();
            session.Advance(4);
            var saved = session.Capture();
            var ahead = saved.Job.Ahead.ToArray();
            ahead[0] = new EvaluationAhead(expected.Episodes[1]);
            var progress = new EvaluationProgress(saved.Job.Completed, saved.Job.CurrentWorld, saved.Job.CurrentBrains,
                4, ahead);
            var snapshot = new EvolutionSnapshot(saved.Settings, saved.ValidationInterval, saved.Generation,
                saved.Population, saved.Lineage, saved.LastTrainingIndividuals, saved.LastTrainingEpisodes,
                saved.BaselineEpisodes, saved.Training, saved.Validation,
                saved.TotalTicks + expected.Episodes[1].SurvivedTicks - 1, saved.Phase, progress, saved.Neat);
            var text = CheckpointCodec.Encode(new Checkpoint("evolution", "minimal", 42, "neural", snapshot, null),
                "test", "test", SavedAt);
            var decoded = CheckpointCodec.Decode(text, out _);
            Assert.That(decoded.Evolution.Job.Ahead[0].Completed, Is.EqualTo(expected.Episodes[1]));
            var restored = EvolutionSession.Restore(decoded.Evolution);
            Assert.That(restored.Job.RunToEnd().Episodes, Is.EqualTo(expected.Episodes));

            ahead[0] = new EvaluationAhead(expected.Episodes[2]);
            var invalid = new EvaluationProgress(saved.Job.Completed, saved.Job.CurrentWorld, saved.Job.CurrentBrains, 4, ahead);
            var job = new IsolatedEvaluator(session.Plan).TrainingJob(session.Evolution.Population, 0);
            Assert.Throws<ArgumentException>(() => job.Resume(invalid));
            Assert.That(job.Current.Tick, Is.Zero, "an invalid resume leaves the job unchanged");
        }

        [Test]
        public void Codec_RoundTrip_IsExactUnderAnotherCulture()
        {
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
                var checkpoint = Sample(out var session, out var world, out var brains);
                var text = CheckpointCodec.Encode(checkpoint, "0.1.0", "test", SavedAt, out var written);

                Assert.That(text, Does.Contain("\"maxTurnRate\": 3.1415927,"), "invariant decimal point");
                var decoded = CheckpointCodec.Decode(text, out var read);
                Assert.That(CheckpointCodec.Encode(decoded, "0.1.0", "test", SavedAt), Is.EqualTo(text));
                Assert.That(read.ConfigHash, Is.EqualTo(written.ConfigHash));
                Assert.That(read.StateHash, Is.EqualTo(written.StateHash));
                Assert.That(read.SavedUtc, Is.EqualTo(SavedAt));
                Assert.That(decoded.Mode, Is.EqualTo("shared"));

                var restoredSession = EvolutionSession.Restore(decoded.Evolution);
                AdvanceUntil(session, () => session.Training.Count >= 4, 97);
                CatchUp(restoredSession, session);
                AssertSameSession(restoredSession, session);

                var config = new SimulationConfig(decoded.SharedWorld.Settings);
                var restoredWorld = WorldSimulation.Restore(config, decoded.SharedWorld.World);
                var restoredBrains = BrainController.Shared(config, new RandomBrain());
                restoredBrains.Restore(decoded.SharedWorld.Brains);
                Run(world, brains, 250);
                Run(restoredWorld, restoredBrains, 250);
                Assert.That(restoredWorld.ComputeFingerprint(), Is.EqualTo(world.ComputeFingerprint()));
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        }

        [Test]
        public void Codec_Floats_RoundTripBitExactly()
        {
            var settings = SharedSettings();
            var values = new[]
            {
                (position: new Float2(0.1f, 1f / 3f), velocity: new Float2(-0f, float.MaxValue), heading: 6.283185f,
                    energy: float.Epsilon, eaten: 123456.79f),
                (position: new Float2(float.Epsilon, 49.99999f), velocity: new Float2(1e-40f, -float.MaxValue),
                    heading: 0f, energy: 99.99999f, eaten: 0f),
                (position: new Float2(0f, settings.ArenaHeight), velocity: new Float2(-1.17549435e-38f, 7f),
                    heading: 1e-7f, energy: 33.3333321f, eaten: 1e-30f)
            };
            var body = CreatureBody.Reference(new SimulationConfig(settings));
            var creatures = values.Select((v, i) => new CreatureState(new CreatureId(i + 1), v.position, v.heading, v.energy,
                BodyGenes.Neutral, body)
            {
                Velocity = v.velocity, AgeTicks = i * 7, FoodEnergyEaten = v.eaten
            }).ToArray();
            var saved = new SimulationSnapshot(12, 4, 1, new Pcg32State(ulong.MaxValue, 1), new Pcg32State(0, ulong.MaxValue),
                creatures, Array.Empty<FoodState>(), Array.Empty<ScheduledEvent<SimulationEvent>>(), 0, 0);
            var checkpoint = new Checkpoint("shared", "minimal", 7, "random", null,
                new SharedWorldSnapshot(settings, saved, Array.Empty<BrainSlotState>()));

            var culture = CultureInfo.CurrentCulture;
            SimulationSnapshot read;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                read = CheckpointCodec.Decode(CheckpointCodec.Encode(checkpoint, "0.1.0", "test", SavedAt), out _)
                    .SharedWorld.World;
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }

            for (var i = 0; i < creatures.Length; i++)
            {
                var expected = creatures[i];
                var actual = read.Creatures[i];
                Assert.That(Bits(actual.Position.X), Is.EqualTo(Bits(expected.Position.X)));
                Assert.That(Bits(actual.Position.Y), Is.EqualTo(Bits(expected.Position.Y)));
                Assert.That(Bits(actual.Velocity.X), Is.EqualTo(Bits(expected.Velocity.X)), "-0 and subnormals included");
                Assert.That(Bits(actual.Velocity.Y), Is.EqualTo(Bits(expected.Velocity.Y)));
                Assert.That(Bits(actual.Heading), Is.EqualTo(Bits(expected.Heading)));
                Assert.That(Bits(actual.Energy), Is.EqualTo(Bits(expected.Energy)));
                Assert.That(Bits(actual.FoodEnergyEaten), Is.EqualTo(Bits(expected.FoodEnergyEaten)));
                Assert.That(actual.AgeTicks, Is.EqualTo(expected.AgeTicks));
            }

            Assert.That(read.WorldRandom, Is.EqualTo(saved.WorldRandom));
            Assert.That(read.SpawnRandom, Is.EqualTo(saved.SpawnRandom));
        }

        [Test]
        public void Codec_RefusesCorruptedForeignAndNewerFiles()
        {
            var text = CheckpointCodec.Encode(Sample(out _, out _, out _), "0.1.0", "test", SavedAt);

            AssertRefused(text.Substring(0, text.Length / 2), "Invalid JSON");
            AssertRefused("not json", "Invalid JSON");
            AssertRefused("{\"format\": \"something-else\"}", "Not an EDEN checkpoint");
            AssertRefused(text.Replace("\"schemaVersion\": 5,", $"\"schemaVersion\": {CheckpointCodec.SchemaVersion + 1},"), "newer");
            AssertRefused(text.Replace(Pcg32.AlgorithmName, "xorshift128"), "random");
            AssertRefused(ChangeFirstWeight(text, "0.5"), "State hash mismatch");
            AssertRefused(text.Replace("\"maxSpeed\": 5,", "\"maxSpeed\": 6,"), "Config hash mismatch");

            // Edited files whose hashes were recomputed still have to make sense.
            AssertRefused(Rehash(ChangeFirstWeight(text, "7")), "Invalid checkpoint content");
            AssertRefused(Rehash(Regex.Replace(text, "\"weights\": \\[[^,\\]]+,", "\"weights\": [", RegexOptions.None,
                TimeSpan.FromSeconds(1))), "259 weights");
            AssertRefused(Rehash(ReplaceFirst(text, "\"energy\": [0-9.]+", "\"energy\": \"NaN\"")), "expected number");
            AssertRefused(Rehash(ReplaceFirst(text, "\"energy\": [0-9.]+", "\"energy\": 1e999")), "not a finite float");
            AssertRefused(Rehash(text.Replace("\"phase\": ", "\"note\": 1,\n    \"phase\": ")), "does not read back exactly");
            AssertRefused(Rehash(ReplaceFirst(text, "\"tick\": [0-9]+", "\"tick\": -1")), "integer in");
            AssertRefused(Rehash(ReplaceFirst(text, "\"population\": \\[[0-9]+,", "\"population\": [")),
                "Invalid checkpoint content");
        }

        [Test]
        public void File_SaveSwapsInWithABackup_AndFailuresLeaveFilesUntouched()
        {
            var directory = Path.Combine(Path.GetTempPath(), "eden-checkpoint-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "lab.json");
            try
            {
                var first = Sample(out var session, out var world, out var brains);
                CheckpointFile.Save(path, first, "0.1.0", "test", SavedAt);
                Assert.That(File.Exists(path), Is.True);
                Assert.That(File.Exists(CheckpointFile.BackupPath(path)), Is.False);
                Assert.That(File.Exists(CheckpointFile.TemporaryPath(path)), Is.False);
                var firstText = File.ReadAllText(path);

                session.Advance(500);
                Run(world, brains, 40);
                var second = new Checkpoint("shared", "minimal", 3, "random", session.Capture(),
                    new SharedWorldSnapshot(SharedSettings(), world.Capture(), brains.Capture()));
                var info = CheckpointFile.Save(path, second, "0.1.0", "test", SavedAt);
                var secondText = File.ReadAllText(path);
                Assert.That(File.ReadAllText(CheckpointFile.BackupPath(path)), Is.EqualTo(firstText));
                Assert.That(CheckpointFile.Load(path, out var loaded).Evolution.TotalTicks, Is.EqualTo(session.TotalTicks));
                Assert.That(loaded.StateHash, Is.EqualTo(info.StateHash));

                var dead = world.Capture();
                dead.Creatures[0].DeathCause = DeathCause.Starvation;
                var invalid = new Checkpoint("shared", "minimal", 3, "heuristic", null,
                    new SharedWorldSnapshot(SharedSettings(), dead, brains.Capture()));
                Assert.That(() => CheckpointFile.Save(path, invalid, "0.1.0", "test", SavedAt),
                    Throws.TypeOf<CheckpointException>());
                Assert.That(File.ReadAllText(path), Is.EqualTo(secondText));
                Assert.That(File.ReadAllText(CheckpointFile.BackupPath(path)), Is.EqualTo(firstText));
                Assert.That(File.Exists(CheckpointFile.TemporaryPath(path)), Is.False);

                File.WriteAllText(path, secondText.Substring(0, secondText.Length - 40));
                Assert.That(() => CheckpointFile.Load(path, out _), Throws.TypeOf<CheckpointException>());
                var fallback = CheckpointFile.LoadOrBackup(path, out _, out var problem);
                Assert.That(problem, Does.Contain("Invalid JSON"));
                Assert.That(fallback.Evolution.TotalTicks, Is.EqualTo(first.Evolution.TotalTicks), "the backup");

                File.WriteAllText(path, secondText.Replace("\"schemaVersion\": 5,", "\"schemaVersion\": 99,"));
                Assert.That(() => CheckpointFile.Load(path, out _),
                    Throws.TypeOf<CheckpointException>().With.Message.Contains("newer"));
                Assert.That(() => CheckpointFile.Load(Path.Combine(directory, "missing.json"), out _),
                    Throws.TypeOf<CheckpointException>());
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        // An evolution saved during its validation phase and a shared world with stochastic brains.
        private static Checkpoint Sample(out EvolutionSession session, out WorldSimulation world, out BrainController brains)
        {
            var evolution = new EvolutionSession(Small(13), 2);
            AdvanceUntil(evolution, () => evolution.Phase == EvolutionPhase.Validation && evolution.Validation.Count == 1);
            evolution.Advance(31);
            session = evolution;

            var config = SharedConfig();
            world = new WorldSimulation(WorldFactory.CreateInitial(config));
            brains = BrainController.Shared(config, new RandomBrain());
            Run(world, brains, 61);
            return new Checkpoint("shared", "minimal", 13, "random", session.Capture(),
                new SharedWorldSnapshot(SharedSettings(), world.Capture(), brains.Capture()));
        }

        private static void AssertRefused(string text, string reason)
        {
            Assert.That(() => CheckpointCodec.Decode(text, out _),
                Throws.TypeOf<CheckpointException>().With.Message.Contains(reason));
        }

        private static string ChangeFirstWeight(string text, string value) =>
            ReplaceFirst(text, "\"weights\": \\[[^,\\]]+", "\"weights\": [" + value);

        private static string ReplaceFirst(string text, string pattern, string replacement) =>
            new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1)).Replace(text, replacement, 1);

        // What someone editing the file by hand and fixing up the hashes would produce.
        private static string Rehash(string text)
        {
            var root = JsonText.Parse(text);
            var config = CheckpointCodec.Hash(root["config"].ToCompactString()).ToString("x16");
            var state = CheckpointCodec.Hash(root["state"].ToCompactString()).ToString("x16");
            text = ReplaceFirst(text, "\"configHash\": \"[0-9a-f]{16}\"", $"\"configHash\": \"{config}\"");
            return ReplaceFirst(text, "\"stateHash\": \"[0-9a-f]{16}\"", $"\"stateHash\": \"{state}\"");
        }
    }
}
