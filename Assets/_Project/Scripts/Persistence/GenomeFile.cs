using System;
using System.IO;
using System.Text;

namespace Eden.Persistence
{
    /// <summary>
    /// Files of one exported genome each (see <see cref="GenomeCodec"/>). Saving follows the same discipline as
    /// <see cref="CheckpointFile"/>: writes <c>&lt;path&gt;.tmp</c> in the same directory, reads it back and decodes
    /// it completely, and only then swaps it in; the file it replaces becomes <c>&lt;path&gt;.bak</c>. A pool is simply
    /// every genome file in one directory, with no manifest: curating it (adding or removing a genome) is an ordinary
    /// file operation.
    /// </summary>
    public static class GenomeFile
    {
        public const long MaxBytes = 4L * 1024 * 1024;

        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public static string BackupPath(string path) => path + ".bak";
        public static string TemporaryPath(string path) => path + ".tmp";

        public static void Save(string path, GenomeRecord record, string appVersion, DateTime savedUtc)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A file path is required.", nameof(path));

            string text;
            try
            {
                text = GenomeCodec.Encode(record, appVersion, savedUtc);
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException)
            {
                throw new CheckpointException("The genome cannot be saved: " + error.Message, error);
            }

            var temporary = TemporaryPath(path);
            try
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var bytes = Utf8.GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                Load(temporary);
                if (File.Exists(path))
                    File.Replace(temporary, path, BackupPath(path), true);
                else
                    File.Move(temporary, path);
            }
            catch (CheckpointException error)
            {
                TryDelete(temporary);
                throw new CheckpointException("The written genome failed its check and was discarded: " + error.Message,
                    error);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                                          error is NotSupportedException)
            {
                TryDelete(temporary);
                throw new CheckpointException($"Could not write {path}: {error.Message}", error);
            }
        }

        public static GenomeRecord Load(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A file path is required.", nameof(path));

            string text;
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists)
                    throw new CheckpointException($"There is no genome file at {path}.");
                if (file.Length > MaxBytes)
                    throw new CheckpointException(
                        $"{path} has {file.Length} bytes, more than the {MaxBytes} a genome file may have.");
                text = File.ReadAllText(path, Utf8);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                                          error is DecoderFallbackException)
            {
                throw new CheckpointException($"Could not read {path}: {error.Message}", error);
            }

            return GenomeCodec.Decode(text);
        }

        /// <summary>
        /// Every genome file (<c>*.json</c>) in <paramref name="directory"/>, in filename order (for determinism), as
        /// the pool an evolution can be seeded from. A genome exported without a known fitness of its own counts as 0
        /// when building that pool: it never outranks a pool member with a real score, but mixing both kinds in one
        /// pool means the scored ones dominate elite selection.
        /// </summary>
        public static GenomeRecord[] LoadPool(string directory) => LoadPool(directory, out _);

        /// <summary>As above, with each genome's file name (e.g. for a composition that names its genomes).</summary>
        public static GenomeRecord[] LoadPool(string directory, out string[] names)
        {
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("A directory path is required.", nameof(directory));
            if (!Directory.Exists(directory))
                throw new CheckpointException($"There is no genome pool directory at {directory}.");

            var paths = Directory.GetFiles(directory, "*.json");
            Array.Sort(paths, StringComparer.Ordinal);
            if (paths.Length == 0)
                throw new CheckpointException($"{directory} has no genome files (*.json).");

            var pool = new GenomeRecord[paths.Length];
            names = new string[paths.Length];
            for (var i = 0; i < pool.Length; i++)
            {
                pool[i] = Load(paths[i]);
                names[i] = Path.GetFileName(paths[i]);
            }

            return pool;
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                // A leftover temporary file is overwritten by the next save and never loaded.
            }
        }
    }
}
