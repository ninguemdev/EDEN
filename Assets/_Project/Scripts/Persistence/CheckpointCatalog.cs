using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Eden.Persistence
{
    /// <summary>A rebuildable, bounded header listing. Listing never restores a world or trusts a cached state.
    /// Entries are candidates; CheckpointFile validates the selected file completely before it can be applied.</summary>
    public sealed class CheckpointCatalogEntry
    {
        public CheckpointCatalogEntry() { }
        public CheckpointCatalogEntry(string path, string name, DateTime savedUtc, string details)
        { Path = path; Name = name; SavedUtc = savedUtc; Details = details; }
        public string Path { get; internal set; }
        public string Name { get; internal set; }
        public DateTime SavedUtc { get; internal set; }
        public long Bytes { get; internal set; }
        public string Details { get; internal set; }
        public bool CanLoad { get; internal set; }
        public bool Recovery => Path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
        public override string ToString() => Name + " · " + SavedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss.fff") + (Recovery ? " · recuperação" : "");
    }

    public static class CheckpointCatalog
    {
        private const int HeaderLimit = 131072;
        public static string NewPath(string directory, string displayName = null)
        {
            // The display name is not a path and never enters the experiment's RNG/configuration.
            var label = new StringBuilder();
            foreach (var c in displayName ?? "checkpoint")
                if (label.Length < 48 && (char.IsLetterOrDigit(c) || c == '-' || c == '_')) label.Append(c);
                else if (label.Length < 48 && char.IsWhiteSpace(c)) label.Append('-');
            return System.IO.Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture)
                + "-" + label + "-" + Guid.NewGuid().ToString("N") + ".json");
        }

        public static CheckpointCatalogEntry[] List(string directory)
        {
            var entries = new List<CheckpointCatalogEntry>();
            if (!Directory.Exists(directory)) return entries.ToArray();
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var file = System.IO.Path.GetFileName(path);
                if (!(file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase)) ||
                    file.EndsWith(".metrics.json", StringComparison.OrdinalIgnoreCase)) continue;
                entries.Add(ReadHeader(path));
            }
            entries.Sort((a, b) => { var time = b.SavedUtc.CompareTo(a.SavedUtc); return time != 0 ? time : string.CompareOrdinal(a.Path, b.Path); });
            return entries.ToArray();
        }

        public static CheckpointCatalogEntry ReadHeader(string path)
        {
            var entry = new CheckpointCatalogEntry { Path = System.IO.Path.GetFullPath(path), Name = System.IO.Path.GetFileNameWithoutExtension(path) };
            entry.Name = Regex.Replace(Regex.Replace(entry.Name, "^[0-9]{8}T[0-9]+Z-", ""), "-[a-f0-9]{32}$", "");
            try
            {
                var file = new FileInfo(path); entry.Bytes = file.Length; entry.SavedUtc = file.LastWriteTimeUtc;
                if (entry.Bytes > CheckpointFile.MaxBytes) throw new IOException("Arquivo acima do limite de 64 MiB.");
                var chars = new char[(int)Math.Min(HeaderLimit, entry.Bytes)];
                int read;
                using (var reader = new StreamReader(path, Encoding.UTF8, true)) read = reader.ReadBlock(chars, 0, chars.Length);
                var text = new string(chars, 0, read);
                var fields = new Dictionary<string, string>();
                foreach (Match match in Regex.Matches(text, "\"([A-Za-z][A-Za-z0-9]*)\"\\s*:\\s*(?:\"([^\"]*)\"|(-?[0-9]+))"))
                    if (!fields.ContainsKey(match.Groups[1].Value)) fields.Add(match.Groups[1].Value,
                        match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value);
                string Field(string key) => fields.TryGetValue(key, out var value) ? value : "";
                if (Field("format") != "eden-checkpoint") throw new IOException("Não é um checkpoint EDEN.");
                if (!int.TryParse(Field("schemaVersion"), out var schema) || schema < 1 || schema > CheckpointCodec.SchemaVersion)
                    throw new IOException("Versão do checkpoint incompatível.");
                if (DateTime.TryParse(Field("savedUtc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var utc)) entry.SavedUtc = utc.ToUniversalTime();
                entry.CanLoad = true;
                var algorithm = Field("algorithm"); var senses = Field("senses");
                var profile = Regex.Match(text, "\\\"rewardProfile\\\"\\s*:\\s*\\{\\s*\\\"id\\\"\\s*:\\s*\\\"([^\\\"]*)").Groups[1].Value;
                var progress = "Geração/fase: consulte ao carregar (arquivo legado sem índice).";
                var catalog = path + ".catalog";
                if (File.Exists(catalog) && new FileInfo(catalog).Length < 4096)
                {
                    var lines = File.ReadAllLines(catalog, Encoding.UTF8);
                    if (lines.Length >= 2 && lines[0] == Field("stateHash")) progress = lines[1];
                }
                var population = Field("populationSize"); if (population.Length == 0) population = Field("initialPopulation");
                var brain = Field("mode") == "evolution" ? algorithm.Length == 0 ? "MLP" : algorithm : Field("brain");
                entry.Details = $"{Field("mode")} · seed {Field("seed")} · população {population}\n" +
                    $"{brain} · {(senses.Length == 0 ? "sentidos legados" : senses)} · {Field("arenaWidth")} × {Field("arenaHeight")}\n" +
                    $"Perfil: {(profile.Length == 0 ? "food-energy-per-second-v1" : profile)}\n" +
                    $"{progress}\nSchema {schema} · {entry.Bytes / 1048576.0:0.00} MiB\nCabeçalho legível; integridade conferida ao carregar.\n{entry.Path}";
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            { entry.CanLoad = false; entry.Details = error.Message + "\n" + entry.Path; }
            return entry;
        }

        public static string Import(string source, string directory)
        {
            // Validate before copying. Import never changes or removes the source.
            CheckpointFile.Load(source, out _);
            Directory.CreateDirectory(directory);
            var destination = NewPath(directory, System.IO.Path.GetFileNameWithoutExtension(source));
            var temporary = destination + ".tmp";
            File.Copy(source, temporary, false);
            var state = CheckpointFile.Load(temporary, out var info);
            File.Move(temporary, destination);
            Record(destination, state, info);
            return destination;
        }

        internal static void Record(string path, Checkpoint checkpoint, CheckpointInfo info)
        {
            // Optional index: losing it never invalidates or removes the already verified checkpoint.
            try
            {
                var evolution = checkpoint.Evolution;
                var progress = evolution == null ? "Arena · tick " + checkpoint.SharedWorld.World.Tick :
                    $"Geração {evolution.Generation} · fase {evolution.Phase}";
                var destination = path + ".catalog"; var temporary = destination + ".tmp";
                File.WriteAllLines(temporary, new[] { CheckpointCodec.Hex(info.StateHash), progress }, new UTF8Encoding(false));
                if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
        }
    }
}
