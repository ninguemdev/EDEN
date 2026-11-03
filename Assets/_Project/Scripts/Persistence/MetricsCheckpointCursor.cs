using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Eden.Persistence
{
    /// <summary>Optional receipt linking a checkpoint state hash to the last committed metrics segment.
    /// A stale/missing receipt is unavailable, never evidence that later segments belong to the resumed timeline.</summary>
    public sealed class MetricsCheckpointCursor
    {
        public string DirectoryPath { get; private set; }
        public string ThroughSegment { get; private set; }
        public string StateHash { get; private set; }
        public string ExperimentHash { get; private set; }
        public static string ReceiptPath(string checkpointPath) => checkpointPath + ".metrics.json";
        internal static void Write(string checkpointPath, CheckpointInfo info, string directory, string through, ulong experimentHash)
        {
            var cursor = new MetricsCheckpointCursor { DirectoryPath = Path.GetFullPath(directory), ThroughSegment = through ?? "",
                StateHash = CheckpointCodec.Hex(info.StateHash), ExperimentHash = CheckpointCodec.Hex(experimentHash) };
            var path = ReceiptPath(checkpointPath); var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonText.Write(cursor.ToJson()), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null, true); else File.Move(temporary, path);
        }
        public static MetricsCheckpointCursor LoadMatching(string checkpointPath, ulong stateHash)
        {
            var path = ReceiptPath(checkpointPath);
            if (!File.Exists(path)) return null;
            try
            {
                var value = JsonText.Parse(File.ReadAllText(path));
                if (value["schemaVersion"].Int() != 1 || value["checkpointStateHash"].String() != CheckpointCodec.Hex(stateHash)) return null;
                return new MetricsCheckpointCursor { DirectoryPath = value["evolutionDirectory"].String(),
                    ThroughSegment = value["throughSegment"].String(), StateHash = value["checkpointStateHash"].String(),
                    ExperimentHash = value["experimentHash"].String() };
            }
            catch (Exception error) when (error is IOException || error is CheckpointException) { return null; }
        }
        internal JsonValue ToJson() => JsonValue.NewObject().Add("schemaVersion", JsonValue.Of(1))
            .Add("checkpointStateHash", JsonValue.Of(StateHash)).Add("experimentHash", JsonValue.Of(ExperimentHash))
            .Add("evolutionDirectory", JsonValue.Of(DirectoryPath)).Add("throughSegment", JsonValue.Of(ThroughSegment));
    }
}
