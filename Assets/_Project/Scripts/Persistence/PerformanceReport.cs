using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Eden.Application;

namespace Eden.Persistence
{
    /// <summary>
    /// A measured lab run: where it ran (hardware, build, display), what ran (mode, preset, brain, speed), the frame and
    /// tick costs and the memory and garbage collector figures. Values the runtime cannot provide (for example
    /// allocation counters outside Development builds) are null and reported as unavailable, never as zero.
    /// </summary>
    public sealed class BenchmarkRecord
    {
        public BenchmarkRecord(IReadOnlyList<KeyValuePair<string, string>> environment,
            IReadOnlyList<KeyValuePair<string, string>> scenario, PerformanceRecorder recorder)
        {
            Environment = environment ?? throw new ArgumentNullException(nameof(environment));
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            Recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        }

        public IReadOnlyList<KeyValuePair<string, string>> Environment { get; }
        public IReadOnlyList<KeyValuePair<string, string>> Scenario { get; }
        public PerformanceRecorder Recorder { get; }

        /// <summary>Ticks per second the run asked for (simulated rate × requested speed); null at maximum speed.</summary>
        public double? RequestedTicksPerSecond { get; set; }

        public int PopulationAtStart { get; set; }
        public int PopulationAtEnd { get; set; }

        /// <summary>Garbage collections during the measurement.</summary>
        public long? GcCollections { get; set; }

        /// <summary>Managed allocations made while simulating ticks (Development builds only).</summary>
        public long? SimulationAllocations { get; set; }

        /// <summary>Managed bytes allocated by whole frames, simulation, view and HUD (Development builds only).</summary>
        public long? FrameAllocatedBytes { get; set; }

        /// <summary>Isolated episodes finished during the measurement (evolution mode).</summary>
        public long? Episodes { get; set; }

        public long? ManagedHeapUsedStart { get; set; }
        public long? ManagedHeapUsedEnd { get; set; }
        public long? TotalReservedEnd { get; set; }

        /// <summary>Memory the engine has allocated (native and managed), at the end.</summary>
        public long? TotalAllocatedEnd { get; set; }
        public long? WorkingSetEnd { get; set; }
        public long? PeakWorkingSet { get; set; }
    }

    /// <summary>Writes a <see cref="BenchmarkRecord"/> as <c>performance.json</c> and a plain <c>performance.txt</c>.</summary>
    public static class PerformanceReport
    {
        public const string Format = "eden-performance";
        public const int SchemaVersion = 1;

        public static void Write(string directory, BenchmarkRecord record)
        {
            if (string.IsNullOrEmpty(directory))
                throw new ArgumentException("A directory is required.", nameof(directory));
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            Directory.CreateDirectory(directory);
            var encoding = new UTF8Encoding(false);
            File.WriteAllText(Path.Combine(directory, "performance.json"), Json(record), encoding);
            File.WriteAllText(Path.Combine(directory, "performance.txt"), string.Join("\n", Describe(record)) + "\n",
                encoding);
        }

        public static string Json(BenchmarkRecord record)
        {
            var r = record.Recorder;
            var root = JsonValue.NewObject()
                .Add("format", JsonValue.Of(Format))
                .Add("schemaVersion", JsonValue.Of(SchemaVersion))
                .Add("environment", Pairs(record.Environment))
                .Add("scenario", Pairs(record.Scenario))
                .Add("measuredSeconds", Number(r.ElapsedSeconds))
                .Add("frames", JsonValue.Of(r.FrameCount))
                .Add("framesPerSecond", Number(r.FramesPerSecond))
                .Add("frameMs", Histogram(r.Frames))
                .Add("simulationMsPerFrame", Histogram(r.SimulationPerFrame))
                .Add("ticks", JsonValue.Of(r.TickCount))
                .Add("ticksPerSecond", Number(r.TicksPerSecond))
                .Add("requestedTicksPerSecond", Optional(record.RequestedTicksPerSecond))
                .Add("meanTickMs", Number(r.MeanTickMilliseconds))
                .Add("timedTicks", JsonValue.Of(r.Ticks.Count))
                .Add("tickMs", Histogram(r.Ticks))
                .Add("sensorsMs", Histogram(r.Sensors))
                .Add("decisionsMs", Histogram(r.Decisions))
                .Add("meanCreatures", Number(r.MeanCreatures))
                .Add("populationAtStart", JsonValue.Of(record.PopulationAtStart))
                .Add("populationAtEnd", JsonValue.Of(record.PopulationAtEnd))
                .Add("episodes", Optional(record.Episodes))
                .Add("memory", JsonValue.NewObject()
                    .Add("gcCollections", Optional(record.GcCollections))
                    .Add("simulationAllocations", Optional(record.SimulationAllocations))
                    .Add("simulationAllocationsPerCreatureTick", Optional(AllocationsPerCreatureTick(record)))
                    .Add("simulationAllocationsPerEpisode", Optional(AllocationsPerEpisode(record)))
                    .Add("frameAllocatedBytes", Optional(record.FrameAllocatedBytes))
                    .Add("managedHeapUsedStart", Optional(record.ManagedHeapUsedStart))
                    .Add("managedHeapUsedEnd", Optional(record.ManagedHeapUsedEnd))
                    .Add("totalReservedEnd", Optional(record.TotalReservedEnd))
                    .Add("totalAllocatedEnd", Optional(record.TotalAllocatedEnd))
                    .Add("workingSetEnd", Optional(record.WorkingSetEnd))
                    .Add("peakWorkingSet", Optional(record.PeakWorkingSet)));
            return JsonText.Write(root);
        }

        /// <summary>Plain summary in Portuguese.</summary>
        public static IReadOnlyList<string> Describe(BenchmarkRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            var r = record.Recorder;
            var lines = new List<string> { "EDEN — medição de desempenho", string.Empty, "Ambiente:" };
            foreach (var pair in record.Environment)
                lines.Add("  " + pair.Key + ": " + pair.Value);
            lines.Add("Cenário:");
            foreach (var pair in record.Scenario)
                lines.Add("  " + pair.Key + ": " + pair.Value);
            lines.Add(string.Empty);
            lines.Add(Text("Medição: {0} s, {1} quadros, {2} ticks", F(r.ElapsedSeconds, "0.0"), r.FrameCount, r.TickCount));
            lines.Add(Text("Quadros: {0} FPS · tempo de quadro mediana {1} ms, p95 {2} ms, máx. {3} ms",
                F(r.FramesPerSecond, "0.0"), Ms(r.Frames.Median), Ms(r.Frames.P95), Ms(r.Frames.Max)));
            lines.Add(Text("Simulação por quadro: mediana {0} ms, p95 {1} ms", Ms(r.SimulationPerFrame.Median),
                Ms(r.SimulationPerFrame.P95)));
            lines.Add(record.RequestedTicksPerSecond.HasValue
                ? Text("Ticks: {0}/s efetivos de {1}/s pedidos · {2} ms por tick em média", F(r.TicksPerSecond, "0.0"),
                    F(record.RequestedTicksPerSecond.Value, "0.0"), Ms(r.MeanTickMilliseconds))
                : Text("Ticks: {0}/s efetivos na velocidade máxima · {1} ms por tick em média", F(r.TicksPerSecond, "0"),
                    Ms(r.MeanTickMilliseconds)));
            if (r.Ticks.Count > 0)
            {
                lines.Add(Text("Por tick ({0} ticks medidos, {1} criaturas em média): total mediana {2} ms, p95 {3} ms · " +
                               "sensores mediana {4} ms, p95 {5} ms · decisão (inferência) mediana {6} ms, p95 {7} ms",
                    r.Ticks.Count, F(r.MeanCreatures, "0.0"), Ms(r.Ticks.Median), Ms(r.Ticks.P95), Ms(r.Sensors.Median),
                    Ms(r.Sensors.P95), Ms(r.Decisions.Median), Ms(r.Decisions.P95)));
            }

            lines.Add(Text("População: {0} no início, {1} no fim", record.PopulationAtStart, record.PopulationAtEnd));
            if (record.Episodes.HasValue)
                lines.Add(Text("Episódios isolados concluídos: {0} · alocações por episódio {1}", record.Episodes.Value,
                    AllocationsPerEpisode(record)?.ToString("0.##", CultureInfo.InvariantCulture) ?? "indisponível"));
            lines.Add(Text("Memória: coletas de lixo {0} · alocações durante os ticks {1} ({2} por criatura-tick) · " +
                           "bytes alocados nos quadros {3}",
                Value(record.GcCollections), Value(record.SimulationAllocations),
                AllocationsPerCreatureTick(record)?.ToString("0.######", CultureInfo.InvariantCulture) ?? "indisponível",
                Value(record.FrameAllocatedBytes)));
            lines.Add(Text("Heap gerenciado {0} → {1} B · engine alocado {2} B, reservado {3} B · working set {4} B (pico {5} B)",
                Value(record.ManagedHeapUsedStart), Value(record.ManagedHeapUsedEnd), Value(record.TotalAllocatedEnd),
                Value(record.TotalReservedEnd), Value(record.WorkingSetEnd), Value(record.PeakWorkingSet)));
            return lines;
        }

        private static double? AllocationsPerCreatureTick(BenchmarkRecord record)
        {
            var creatureTicks = record.Recorder.CreatureTicks;
            return record.SimulationAllocations.HasValue && creatureTicks > 0
                ? (double)record.SimulationAllocations.Value / creatureTicks
                : (double?)null;
        }

        private static double? AllocationsPerEpisode(BenchmarkRecord record) =>
            record.SimulationAllocations.HasValue && record.Episodes.HasValue && record.Episodes.Value > 0
                ? (double)record.SimulationAllocations.Value / record.Episodes.Value
                : (double?)null;

        private static JsonValue Histogram(DurationHistogram histogram) =>
            JsonValue.NewObject()
                .Add("count", JsonValue.Of(histogram.Count))
                .Add("mean", Number(histogram.Mean))
                .Add("median", Number(histogram.Median))
                .Add("p95", Number(histogram.P95))
                .Add("max", Number(histogram.Max));

        private static JsonValue Pairs(IReadOnlyList<KeyValuePair<string, string>> pairs)
        {
            var value = JsonValue.NewObject();
            foreach (var pair in pairs)
                value.Add(pair.Key, JsonValue.Of(pair.Value ?? string.Empty));
            return value;
        }

        private static JsonValue Number(double value) => JsonValue.OfAnyFloat((float)value);
        private static JsonValue Optional(long? value) => value.HasValue ? JsonValue.Of(value.Value) : JsonValue.Null();
        private static JsonValue Optional(double? value) => value.HasValue ? Number(value.Value) : JsonValue.Null();
        private static string Value(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "indisponível";
        private static string Ms(double value) => F(value, "0.####");
        private static string F(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

        private static string Text(string format, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, format, args);
    }
}
