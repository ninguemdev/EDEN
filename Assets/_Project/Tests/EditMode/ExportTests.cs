using System;
using System.Globalization;
using System.IO;
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
    /// Experiment exports (JSON definition and summary, units) and runs resumed from checkpoint files compared with
    /// uninterrupted ones.
    /// </summary>
    public class ExportTests
    {
        private static readonly DateTime CreatedAt = new DateTime(2026, 10, 26, 15, 0, 0, DateTimeKind.Utc);
        private static readonly string[] CsvFiles = { "generations.csv", "individuals.csv", "episodes.csv" };

        private string root;

        [SetUp]
        public void CreateDirectory()
        {
            root = Path.Combine(Path.GetTempPath(), "eden-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void DeleteDirectory()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }

        [Test]
        public void Json_StatesTheSettingsTheirHashTheUnitsAndASummary()
        {
            var culture = CultureInfo.CurrentCulture;
            EvolutionSession session;
            ExperimentHeader header;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
                session = new EvolutionSession(Small(12), 1);
                using (var log = Log(Path.Combine(root, "run"), session, null))
                {
                    header = log.Header;
                    AdvanceUntil(session, () => session.Validation.Count >= 2);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }

            var experiment = ReadJson("run", "experiment.json");
            Assert.That(experiment["format"].String(), Is.EqualTo(ExperimentJson.ExperimentFormat));
            Assert.That(experiment["evaluationMode"].String(), Is.EqualTo("isolated"));
            Assert.That(experiment["fitnessUnit"].String(), Is.EqualTo(ExperimentJson.FitnessUnit));
            Assert.That(experiment["preset"].String(), Is.EqualTo("minimal"));
            Assert.That(experiment["settings"]["populationSize"].Int(), Is.EqualTo(6));
            Assert.That(experiment["configHash"].String(), Is.EqualTo(CheckpointCodec.Hex(header.ConfigHash)));
            Assert.That(experiment["resumedFrom"].IsNull, Is.True);
            Assert.That(File.ReadAllLines(Path.Combine(root, "run", "experiment.txt")),
                Does.Contain("config_hash=" + CheckpointCodec.Hex(header.ConfigHash)).And.Contain("validation_interval=1"));

            var headers = new[] { EvolutionCsvLog.GenerationsHeader, EvolutionCsvLog.IndividualsHeader, EvaluationCsv.Header };
            for (var f = 0; f < CsvFiles.Length; f++)
            {
                var units = experiment["columns"][CsvFiles[f]];
                Assert.That(units.Members.Select(m => m.Key), Is.EqualTo(headers[f].Split(',')), "every column has a unit");
                Assert.That(units.Members.Select(m => m.Value.String()), Is.All.Not.Empty);
            }

            var same = new ExperimentHeader(Small(12), 1, "another version", CreatedAt.AddDays(3), "standard");
            Assert.That(same.ConfigHash, Is.EqualTo(header.ConfigHash), "the hash covers the definition only");
            var bigger = Small(12);
            bigger.PopulationSize = 7;
            Assert.That(new ExperimentHeader(bigger, 1, "0.1.0", CreatedAt).ConfigHash, Is.Not.EqualTo(header.ConfigHash));
            Assert.That(new ExperimentHeader(Small(12), 2, "0.1.0", CreatedAt).ConfigHash, Is.Not.EqualTo(header.ConfigHash));

            var summary = ReadJson("run", "summary.json");
            var training = summary["training"];
            var validation = summary["validation"];
            Assert.That(summary["resumed"].Boolean(), Is.False);
            Assert.That(training["generations"].Int(), Is.EqualTo(session.Training.Count));
            Assert.That(training["episodes"].Long(), Is.EqualTo(session.Training.Count * 6L * 2));
            Assert.That(training["last"]["generation"].Int(), Is.EqualTo(session.Training.Count - 1));
            Assert.That(Bits(training["last"]["median"].AnyFloat()), Is.EqualTo(Bits(session.Training.Last().Median)));
            Assert.That(training["best"]["fitness"].AnyFloat(), Is.EqualTo(session.Training.Max(t => t.Best)));
            Assert.That(validation["generations"].Int(), Is.EqualTo(2));
            Assert.That(validation["episodes"].Long(), Is.EqualTo(2 * 6L * 3), "reference brains are not exported");
            var champion = validation["champion"];
            Assert.That(champion["generation"].Int(), Is.EqualTo(1));
            Assert.That(champion["individualId"].Long(), Is.EqualTo(session.Validation[1].ChampionId));
            Assert.That(Bits(champion["fitness"].AnyFloat()), Is.EqualTo(Bits(session.Validation[1].ChampionFitness)));
        }

        [TestCase(EvolutionPhase.Training)]
        [TestCase(EvolutionPhase.Validation)]
        public void ResumedFromAFile_MidEvaluation_ExportsAddUpToTheUninterruptedRun(EvolutionPhase phase)
        {
            // N ticks, saved to a file in the middle of an episode with part of the evaluation done.
            var original = new EvolutionSession(Small(37), 2);
            var path = Path.Combine(root, "lab.json");
            CheckpointInfo saved;
            int completedAtSave;
            long episodeTickAtSave;
            using (Log(Path.Combine(root, "before"), original, null))
            {
                AdvanceUntil(original, () => original.Phase == phase && original.Evolution.Generation >= 1 &&
                                             original.Job.CompletedEpisodes > 0);
                original.Advance(37);
                Assert.That(original.Phase, Is.EqualTo(phase));
                Assert.That(original.Job.Current.Tick, Is.GreaterThan(0), "saved in the middle of an episode");
                completedAtSave = original.Job.CompletedEpisodes;
                episodeTickAtSave = original.Job.Current.Tick;
                saved = CheckpointFile.Save(path, new Checkpoint("evolution", "minimal", 37, "neural", original.Capture(), null),
                    "0.1.0", "test", CreatedAt);
            }

            var n = original.TotalTicks;
            var loaded = CheckpointFile.Load(path, out var loadedInfo);
            var resumed = EvolutionSession.Restore(loaded.Evolution);
            var uninterrupted = new EvolutionSession(Small(37), 2);
            using (Log(Path.Combine(root, "after"), resumed, new ResumeOrigin(path, loadedInfo, loaded.Evolution)))
            using (Log(Path.Combine(root, "whole"), uninterrupted, null))
            {
                AdvanceUntil(uninterrupted, () => uninterrupted.Training.Count >= 5, 113);
                var m = uninterrupted.TotalTicks - n;
                Assert.That(resumed.Advance(m), Is.EqualTo(m));
                AssertSameSession(resumed, uninterrupted);
                Assert.That(resumed.Job.Current.Simulation.ComputeFingerprint(),
                    Is.EqualTo(uninterrupted.Job.Current.Simulation.ComputeFingerprint()), "N + save/load + M = N + M");
            }

            foreach (var file in CsvFiles)
            {
                var before = Rows("before", file);
                var after = Rows("after", file);
                Assert.That(after, Is.Not.Empty, file);
                Assert.That(before.Concat(after), Is.EqualTo(Rows("whole", file)), file);
            }

            var episodes = Rows("before", "episodes.csv").Concat(Rows("after", "episodes.csv"))
                .Select(row => string.Join(",", row.Split(',').Take(4))).ToArray();
            Assert.That(episodes, Is.Unique, "no episode written twice (mode, generation, candidate, seed)");

            var from = ReadJson("after", "experiment.json")["resumedFrom"];
            Assert.That(from["checkpoint"].String(), Is.EqualTo(path));
            Assert.That(from["stateHash"].String(), Is.EqualTo(CheckpointCodec.Hex(saved.StateHash)));
            Assert.That(from["phase"].String(), Is.EqualTo(phase == EvolutionPhase.Training ? "training" : "validation"));
            Assert.That(from["totalTicks"].Long(), Is.EqualTo(n));
            Assert.That(from["restoredEpisodes"].Int(), Is.EqualTo(completedAtSave));
            Assert.That(from["episodeTick"].Long(), Is.EqualTo(episodeTickAtSave));
            Assert.That(from["evaluationGeneration"].Int(),
                Is.EqualTo(phase == EvolutionPhase.Training ? loaded.Evolution.Generation : loaded.Evolution.Generation - 1));
            Assert.That(ReadJson("after", "summary.json")["resumed"].Boolean(), Is.True);
            Assert.That(ReadJson("after", "experiment.json")["configHash"].String(),
                Is.EqualTo(ReadJson("whole", "experiment.json")["configHash"].String()));
        }

        [Test]
        public void SharedWorld_SavedToAFileAndLoaded_ContinuesLikeTheUninterruptedRun()
        {
            var config = SharedConfig();
            var world = new WorldSimulation(WorldFactory.CreateInitial(config));
            var brains = BrainController.Shared(config, new RandomBrain());
            Run(world, brains, 173);
            var path = Path.Combine(root, "shared.json");
            CheckpointFile.Save(path, new Checkpoint("shared", "minimal", 5, "random", null,
                new SharedWorldSnapshot(SharedSettings(), world.Capture(), brains.Capture())), "0.1.0", "test", CreatedAt);
            Run(world, brains, 311);

            var loaded = CheckpointFile.Load(path, out _).SharedWorld;
            var restoredConfig = new SimulationConfig(loaded.Settings);
            var restored = WorldSimulation.Restore(restoredConfig, loaded.World);
            var restoredBrains = BrainController.Shared(restoredConfig, new RandomBrain());
            restoredBrains.Restore(loaded.Brains);
            Run(restored, restoredBrains, 311);

            Assert.That(restored.World.Tick, Is.EqualTo(world.World.Tick));
            Assert.That(restored.ComputeFingerprint(), Is.EqualTo(world.ComputeFingerprint()));
        }

        private static EvolutionCsvLog Log(string directory, EvolutionSession session, ResumeOrigin resumedFrom)
        {
            var log = new EvolutionCsvLog(directory, new ExperimentHeader(session.Evolution.Settings,
                session.ValidationInterval, "0.1.0", CreatedAt, "minimal", resumedFrom));
            session.ReportCompleted += log.Append;
            return log;
        }

        private string[] Rows(string run, string file)
        {
            var lines = File.ReadAllLines(Path.Combine(root, run, file));
            return lines.Skip(1).ToArray();
        }

        private JsonValue ReadJson(string run, string file) =>
            JsonText.Parse(File.ReadAllText(Path.Combine(root, run, file)));
    }
}
