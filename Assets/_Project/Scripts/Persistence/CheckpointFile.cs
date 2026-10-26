using System;
using System.IO;
using System.Text;

namespace Eden.Persistence
{
    /// <summary>
    /// Checkpoint files. Saving writes <c>&lt;path&gt;.tmp</c> in the same directory, reads it back and decodes it
    /// completely (<see cref="CheckpointCodec.Decode"/>), and only then swaps it in; the checkpoint it replaces, which
    /// passed the same check when it was saved, becomes <c>&lt;path&gt;.bak</c>. A failure at any step leaves the
    /// existing checkpoint and backup as they were. Loading refuses files above <see cref="MaxBytes"/> before reading
    /// them. Every problem is reported as a <see cref="CheckpointException"/>.
    /// </summary>
    public static class CheckpointFile
    {
        public const long MaxBytes = 64L * 1024 * 1024;

        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public static string BackupPath(string path) => path + ".bak";
        public static string TemporaryPath(string path) => path + ".tmp";

        public static CheckpointInfo Save(string path, Checkpoint checkpoint, string appVersion, string runtime,
            DateTime savedUtc)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A file path is required.", nameof(path));
            if (checkpoint == null)
                throw new ArgumentNullException(nameof(checkpoint));

            string text;
            CheckpointInfo info;
            try
            {
                text = CheckpointCodec.Encode(checkpoint, appVersion, runtime, savedUtc, out info);
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException)
            {
                throw new CheckpointException("The run cannot be saved: " + error.Message, error);
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

                Load(temporary, out _);
                if (File.Exists(path))
                    File.Replace(temporary, path, BackupPath(path), true);
                else
                    File.Move(temporary, path);
                return info;
            }
            catch (CheckpointException error)
            {
                TryDelete(temporary);
                throw new CheckpointException("The written checkpoint failed its check and was discarded: " + error.Message,
                    error);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                                          error is NotSupportedException)
            {
                TryDelete(temporary);
                throw new CheckpointException($"Could not write {path}: {error.Message}", error);
            }
        }

        public static Checkpoint Load(string path, out CheckpointInfo info)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A file path is required.", nameof(path));

            string text;
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists)
                    throw new CheckpointException($"There is no checkpoint at {path}.");
                if (file.Length > MaxBytes)
                    throw new CheckpointException($"{path} has {file.Length} bytes, more than the {MaxBytes} a checkpoint may have.");
                text = File.ReadAllText(path, Utf8);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                                          error is DecoderFallbackException)
            {
                throw new CheckpointException($"Could not read {path}: {error.Message}", error);
            }

            return CheckpointCodec.Decode(text, out info);
        }

        /// <summary>
        /// The checkpoint at <paramref name="path"/>, or its backup when the checkpoint itself cannot be used; then
        /// <paramref name="problem"/> says what was wrong with it (null otherwise). Fails only if neither is usable.
        /// </summary>
        public static Checkpoint LoadOrBackup(string path, out CheckpointInfo info, out string problem)
        {
            try
            {
                problem = null;
                return Load(path, out info);
            }
            catch (CheckpointException error)
            {
                problem = error.Message;
                if (!File.Exists(BackupPath(path)))
                    throw;
                try
                {
                    return Load(BackupPath(path), out info);
                }
                catch (CheckpointException backupError)
                {
                    throw new CheckpointException($"{error.Message} The backup is not usable either: {backupError.Message}",
                        backupError);
                }
            }
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
