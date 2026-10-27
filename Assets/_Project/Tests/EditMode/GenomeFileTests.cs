using System;
using System.IO;
using Eden.Domain;
using Eden.Persistence;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>Genome files on disk: atomic save with a backup, refusal of bad files, and loading a pool directory.</summary>
    public class GenomeFileTests
    {
        private static readonly DateTime SavedAt = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Save_SwapsInWithABackup_AndFailuresLeaveFilesUntouched()
        {
            var directory = Path.Combine(Path.GetTempPath(), "eden-genome-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "genome.json");
            try
            {
                var first = new GenomeRecord(SampleGenome(1));
                GenomeFile.Save(path, first, "0.1.0", SavedAt);
                Assert.That(File.Exists(path), Is.True);
                Assert.That(File.Exists(GenomeFile.BackupPath(path)), Is.False);
                Assert.That(File.Exists(GenomeFile.TemporaryPath(path)), Is.False);
                var firstText = File.ReadAllText(path);

                var second = new GenomeRecord(SampleGenome(2), 1.5f);
                GenomeFile.Save(path, second, "0.1.0", SavedAt);
                var secondText = File.ReadAllText(path);
                Assert.That(File.ReadAllText(GenomeFile.BackupPath(path)), Is.EqualTo(firstText));
                Assert.That(GenomeFile.Load(path).Fitness, Is.EqualTo(1.5f));

                File.WriteAllText(path, secondText.Substring(0, secondText.Length - 10));
                Assert.That(() => GenomeFile.Load(path), Throws.TypeOf<CheckpointException>());
                Assert.That(() => GenomeFile.Load(Path.Combine(directory, "missing.json")),
                    Throws.TypeOf<CheckpointException>());
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void LoadPool_ReadsEveryFileInFilenameOrder_AndDefaultsMissingFitnessToZero()
        {
            var directory = Path.Combine(Path.GetTempPath(), "eden-genome-pool-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory);
                GenomeFile.Save(Path.Combine(directory, "b.json"), new GenomeRecord(SampleGenome(2), 2f), "0.1.0", SavedAt);
                GenomeFile.Save(Path.Combine(directory, "a.json"), new GenomeRecord(SampleGenome(1)), "0.1.0", SavedAt);
                GenomeFile.Save(Path.Combine(directory, "c.json"), new GenomeRecord(SampleGenome(3), 3f), "0.1.0", SavedAt);

                var pool = GenomeFile.LoadPool(directory);
                Assert.That(pool, Has.Length.EqualTo(3));
                Assert.That(pool[0].Genome.HasSameContent(SampleGenome(1)), Is.True, "filename order, not save order");
                Assert.That(pool[0].Fitness, Is.Null);
                Assert.That(pool[1].Fitness, Is.EqualTo(2f));
                Assert.That(pool[2].Fitness, Is.EqualTo(3f));
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, true);
            }
        }

        [Test]
        public void LoadPool_RejectsAnEmptyOrMissingDirectory()
        {
            var empty = Path.Combine(Path.GetTempPath(), "eden-genome-empty-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(empty);
                Assert.That(() => GenomeFile.LoadPool(empty), Throws.TypeOf<CheckpointException>());
                Assert.That(() => GenomeFile.LoadPool(Path.Combine(empty, "missing")),
                    Throws.TypeOf<CheckpointException>());
            }
            finally
            {
                if (Directory.Exists(empty))
                    Directory.Delete(empty, true);
            }
        }

        private static Genome SampleGenome(int seed)
        {
            var weights = new float[NetworkTopology.Mlp12x16x3.ParameterCount];
            for (var i = 0; i < weights.Length; i++)
                weights[i] = (float)Math.Sin(seed * 0.37 + i * 0.1) * 2f;
            return new Genome(NetworkTopology.Mlp12x16x3, weights);
        }
    }
}
