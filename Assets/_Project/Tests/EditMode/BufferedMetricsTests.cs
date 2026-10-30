using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Eden.Application;
using Eden.Evolution;
using Eden.Persistence;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class BufferedMetricsTests
    {
        private string directory;
        [SetUp] public void SetUp() => directory = Path.Combine(Path.GetTempPath(), "eden-buffered-" + Guid.NewGuid().ToString("N"));
        [TearDown] public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        private static EvolutionSettings Settings() => new EvolutionSettings
        {
            ExperimentSeed = 8, PopulationSize = 4, EliteCount = 1, TrainingSeedsPerGeneration = 1,
            ValidationSeedCount = 1, EpisodeSeconds = 0.1f
        };
        private static ExperimentHeader Header(EvolutionSettings settings) => new ExperimentHeader(settings, 5, "test", DateTime.UtcNow);
        [Test]
        public void SegmentsAreClosedVerifiedAndDoNotOverwriteExistingExperiments()
        {
            var settings = Settings(); var evolution = new GenerationalEvolution(settings); var report = evolution.Advance();
            using (var writer = new GenerationSegmentWriter(directory, Header(settings))) writer.Append(report);
            var segments = GenerationSegmentWriter.VerifiedSegments(directory);
            Assert.That(segments.Length, Is.EqualTo(1));
            Assert.That(File.ReadAllLines(Path.Combine(segments[0], "episodes.csv")).Length, Is.EqualTo(5));
            Assert.That(File.Exists(Path.Combine(segments[0], "champion.json")), Is.True);
            Directory.CreateDirectory(Path.Combine(directory, "segments", "g000001-training.partial"));
            Assert.That(GenerationSegmentWriter.VerifiedSegments(directory).Length, Is.EqualTo(1));
            Assert.Throws<IOException>(() => new GenerationSegmentWriter(directory, Header(settings)));
            File.AppendAllText(Path.Combine(segments[0], "episodes.csv"), "corruption");
            Assert.Throws<IOException>(() => GenerationSegmentWriter.VerifiedSegments(directory));
        }
        [Test]
        public void BackgroundQueueDrainsInOrderAndSurfacesFailure()
        {
            var settings = Settings(); var evolution = new GenerationalEvolution(settings);
            using (var writer = new BufferedEvolutionLog(directory, Header(settings), 1))
            {
                for (var generation = 0; generation < 4; generation++)
                {
                    var report = evolution.Advance();
                    while (!writer.TryAppend(report)) { Assert.That(writer.Error, Is.Null); Thread.Sleep(1); }
                    Assert.That(writer.PendingCount, Is.LessThanOrEqualTo(2), "one queued and one in flight");
                }
                writer.Drain();
                Assert.That(writer.IsIdle, Is.True);
                Assert.That(GenerationSegmentWriter.VerifiedSegments(directory).Length, Is.EqualTo(4));
                Assert.That(writer.LastCommittedSegment, Is.EqualTo("g000003-training"));
                var duplicate = evolution.Advance(); Assert.That(writer.TryAppend(duplicate), Is.True); writer.Drain();
                Assert.That(writer.TryAppend(duplicate), Is.True);
                Assert.That(SpinWait.SpinUntil(() => writer.Error != null, 10000), Is.True);
                Assert.Throws<IOException>(() => writer.Drain()); Assert.That(writer.TryAppend(duplicate), Is.False);
            }
        }
        [Test]
        public void ActiveMetricsAreSparseAndCompatibilityExportRetainsExactStageRows()
        {
            var settings = ActiveSearchTests.Settings(); var report = new GenerationalEvolution(settings).Advance();
            using (var writer = new BufferedEvolutionLog(directory, Header(settings))) { Assert.That(writer.TryAppend(report), Is.True); writer.Drain(); }
            var segment = GenerationSegmentWriter.VerifiedSegments(directory).Single();
            var rows = File.ReadAllLines(Path.Combine(segment, "episodes.csv"));
            Assert.That(rows[0].Split(',').Length, Is.LessThan(50));
            Assert.That(rows.Length, Is.EqualTo(report.Evaluation.Episodes.Count + 1));
            var targetRows = File.ReadAllLines(Path.Combine(segment, "targets.csv"));
            Assert.That(targetRows.Length - 1, Is.LessThanOrEqualTo(report.Evaluation.Episodes.Count * 5));
            var output = Path.Combine(directory, "flat"); MetricsCompatibilityExporter.Export(directory, output);
            Assert.That(File.ReadAllLines(Path.Combine(output, "stages.csv")).Skip(1),
                Is.EqualTo(report.Evaluation.Episodes.Select(e => StageCsv.FormatRow(settings.Profile, e))));
            Assert.That(File.ReadAllLines(Path.Combine(output, "episodes.csv")).Skip(1),
                Is.EqualTo(report.Evaluation.Episodes.Select(e => EvaluationCsv.FormatRow(e))));
        }
        [Test]
        public void LegacyFormatterKeepsThePublishedColumnLayout()
        {
            var profile = RewardProfiles.BalancedResources(true);
            var values = new float[] { 0.5f, 0.25f, 0.75f, 0.125f, 123.5f, 4.5f, 6 };
            var result = new EpisodeResult(EvaluationMode.Training, 3, 5, 1, 17, 2400, 120,
                30, 1600, false, 20, values, score: 0.5f);
            Assert.That(StageCsv.FormatRow(profile, result),
                Is.EqualTo("training,3,5,1,0,resources,17,1,0.5,0.25,0.5,0.25,0.75,0.125,123.5,4.5,6"));
        }
        [Test]
        public void CheckpointFollowsCommittedMetricsAndPreservesTargetDetectionAudit()
        {
            var settings = ActiveSearchTests.Settings(); var session = new EvolutionSession(settings);
            while (session.LastTraining == null) session.Advance(50);
            var hash = settings.Profile.ParameterHash;
            var snapshot = new Checkpoint("evolution", "minimal", 1, "neural", session.Capture(), null);
            var checkpointPath = Path.Combine(directory, "checkpoints", "save.json");
            using (var writer = new BufferedEvolutionLog(directory, Header(settings)))
            {
                Assert.That(writer.TryAppend(session.LastTraining), Is.True);
                var task = writer.TrySaveCheckpoint(checkpointPath, snapshot, "test", "test", DateTime.UtcNow);
                Assert.That(task, Is.Not.Null); task.GetAwaiter().GetResult(); writer.Drain();
                Assert.That(GenerationSegmentWriter.VerifiedSegments(directory).Length, Is.EqualTo(1));
            }
            var loaded = CheckpointFile.Load(checkpointPath, out var info);
            Assert.That(info.SchemaVersion, Is.EqualTo(10));
            var cursor = MetricsCheckpointCursor.LoadMatching(checkpointPath, info.StateHash);
            Assert.That(cursor, Is.Not.Null); Assert.That(cursor.ThroughSegment, Is.EqualTo("g000000-training"));
            Assert.That(MetricsCheckpointCursor.LoadMatching(checkpointPath, info.StateHash + 1), Is.Null);
            Assert.That(loaded.Evolution.Settings.Profile.ParameterHash, Is.EqualTo(hash));
            var restored = EvolutionSession.Restore(loaded.Evolution);
            Assert.That(restored.LastTraining.Evaluation.Episodes[0].DetectedTargets,
                Is.EqualTo(session.LastTraining.Evaluation.Episodes[0].DetectedTargets));
        }

        [Test]
        public void ReplayUsesEvaluatedRankingAndDoesNotChangeTrainingOrRng()
        {
            var settings = Settings(); var session = new EvolutionSession(settings);
            while (session.LastTraining == null) session.Advance(1);
            var report = session.LastTraining; var replay = new RankedReplay(session.Plan);
            replay.Offer(report); replay.Configure(3, 1); replay.BeginLatest();
            var champion = report.Evaluation.Candidates.OrderByDescending(c => c.MeanFitness)
                .ThenBy(c => report.Individuals[c.Candidate].Id).First();
            Assert.That(replay.Individual.Id, Is.EqualTo(report.Individuals[champion.Candidate].Id));
            var before = session.Job.Current.Simulation.ComputeFingerprint(); var ticks = session.TotalTicks;
            replay.Advance(2400); replay.Next(); replay.Advance(2400);
            Assert.That(session.TotalTicks, Is.EqualTo(ticks));
            Assert.That(session.Job.Current.Simulation.ComputeFingerprint(), Is.EqualTo(before));
        }
        [Test]
        public void CheckpointSnapshotCanSerializeWhileTrainingContinues()
        {
            var settings = ActiveSearchTests.Settings(); var session = new EvolutionSession(settings, 5, 2);
            session.Advance(273);
            var snapshot = new Checkpoint("evolution", "minimal", 1, "neural", session.Capture(), null);
            var time = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
            var expected = CheckpointCodec.Encode(snapshot, "test", "test", time);
            var saving = System.Threading.Tasks.Task.Run(() => CheckpointCodec.Encode(snapshot, "test", "test", time));
            session.Advance(12000);
            Assert.That(saving.GetAwaiter().GetResult(), Is.EqualTo(expected));
            var restored = EvolutionSession.Restore(CheckpointCodec.Decode(expected, out _).Evolution);
            Assert.That(restored.TotalTicks, Is.EqualTo(273));
            Assert.That(session.TotalTicks, Is.EqualTo(12273));
        }

        [Test]
        public void ExecutionGateStopsAfterAReportAndCanResumeWithoutChangingResults()
        {
            var settings = Settings(); var session = new EvolutionSession(settings); var stop = false;
            session.CanAdvance = () => !stop; session.ReportCompleted += _ => stop = true;
            session.Advance(100000);
            Assert.That(session.Evolution.Generation, Is.EqualTo(1));
            Assert.That(session.Job.Current.Tick, Is.EqualTo(0));
            var ticks = session.TotalTicks; session.Advance(500); Assert.That(session.TotalTicks, Is.EqualTo(ticks));
            stop = false; session.Advance(500);
            Assert.That(session.TotalTicks, Is.GreaterThan(ticks));
        }
    }
}
