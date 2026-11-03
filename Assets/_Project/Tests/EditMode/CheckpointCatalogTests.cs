using System;
using System.IO;
using System.Linq;
using Eden.Application;
using Eden.Evolution;
using Eden.Persistence;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class CheckpointCatalogTests
    {
        private string directory;
        [SetUp] public void Setup() => directory = Path.Combine(Path.GetTempPath(), "eden-catalog-" + Guid.NewGuid().ToString("N"));
        [TearDown] public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        private static Checkpoint Snapshot(EvolutionSession session) => new Checkpoint("evolution", "minimal", 1, "neural", session.Capture(), null);
        private static EvolutionSession Session() => new EvolutionSession(new EvolutionSettings { PopulationSize = 4, EliteCount = 1, EpisodeSeconds = 0.1f }, 5, 2);
        [Test]
        public void SavesABCAndReopenedCatalogLoadsBAndAddsDWithoutChangingExistingFiles()
        {
            var session = Session(); var paths = new string[3]; var contents = new string[3];
            for (var i = 0; i < 3; i++)
            {
                session.Advance(3); paths[i] = CheckpointCatalog.NewPath(directory, "Teste / fora");
                CheckpointFile.Save(paths[i], Snapshot(session), "test", "test", DateTime.UtcNow); contents[i] = File.ReadAllText(paths[i]);
            }
            var reopened = CheckpointCatalog.List(directory);
            Assert.That(reopened.Length, Is.EqualTo(3)); Assert.That(reopened.All(e => e.CanLoad), Is.True);
            StringAssert.Contains("Geração", reopened[0].Details);
            var loaded = EvolutionSession.Restore(CheckpointFile.Load(paths[1], out _).Evolution); Assert.That(loaded.TotalTicks, Is.EqualTo(6));
            loaded.Advance(3); CheckpointFile.Save(CheckpointCatalog.NewPath(directory, "D"), Snapshot(loaded), "test", "test", DateTime.UtcNow);
            Assert.That(CheckpointCatalog.List(directory).Length, Is.EqualTo(4));
            for (var i = 0; i < 3; i++) Assert.That(File.ReadAllText(paths[i]), Is.EqualTo(contents[i]));
        }
        [Test]
        public void CatalogSeparatesRecoveryIgnoresTemporaryAndReceiptsAndKeepsInvalidEntries()
        {
            Directory.CreateDirectory(directory); var path = Path.Combine(directory, "lab.json");
            CheckpointFile.Save(path, Snapshot(Session()), "test", "test", DateTime.UtcNow);
            File.Copy(path, path + ".bak"); File.WriteAllText(path + ".tmp", "not a file"); File.WriteAllText(path + ".metrics.json", "{}");
            File.WriteAllText(Path.Combine(directory, "broken.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "future.json"), "{\"format\":\"eden-checkpoint\",\"schemaVersion\":999}");
            var entries = CheckpointCatalog.List(directory);
            Assert.That(entries.Length, Is.EqualTo(4)); Assert.That(entries.Count(e => e.Recovery), Is.EqualTo(1));
            Assert.That(entries.Count(e => !e.CanLoad), Is.EqualTo(2));
            Assert.Throws<CheckpointException>(() => CheckpointFile.Load(Path.Combine(directory, "broken.json"), out _));
            Assert.That(CheckpointCatalog.ReadHeader(path).CanLoad, Is.True);
        }
        [Test]
        public void ImportValidatesBeforeCopyAndNeverChangesSource()
        {
            var path = Path.Combine(directory, "source.json"); CheckpointFile.Save(path, Snapshot(Session()), "test", "test", DateTime.UtcNow);
            var text = File.ReadAllText(path); var destination = CheckpointCatalog.Import(path, Path.Combine(directory, "imported"));
            Assert.That(File.ReadAllText(destination), Is.EqualTo(text)); Assert.That(File.ReadAllText(path), Is.EqualTo(text));
            File.WriteAllText(path, "{}"); Assert.Throws<CheckpointException>(() => CheckpointCatalog.Import(path, Path.Combine(directory, "refused")));
            Assert.That(Directory.Exists(Path.Combine(directory, "refused")), Is.False);
        }
    }
}
