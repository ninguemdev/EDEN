using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Eden.Application;
using Eden.Persistence;
using Eden.Simulation;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace Eden.Presentation
{
    /// <summary>
    /// Measures a lab run in the player: after a warm-up, records frame time, simulation time and ticks per frame and,
    /// in the shared world, the cost of every tick, then writes <see cref="PerformanceReport"/> files with the hardware
    /// and settings it ran on. Managed allocations made while simulating are counted with the profiler's GC.Alloc
    /// recorder, checked first against an allocation of its own; where the runtime does not offer it (release builds)
    /// the report says so instead of claiming zero.
    /// </summary>
    internal sealed class LabBenchmark : IDisposable
    {
        private readonly PerformanceRecorder recorder = new PerformanceRecorder();
        private readonly Stopwatch simulationWatch = new Stopwatch();
        private readonly double warmupSeconds;
        private readonly double measureSeconds;
        private readonly string output;
        private Recorder allocations;
        private ProfilerRecorder frameAllocated;
        private double warmedSeconds;
        private long frameTicks;
        private long allocationCount;
        private long frameBytes;
        private int gcAtStart;
        private long heapAtStart;
        private int populationAtStart;
        private long episodesAtStart;

        public LabBenchmark(double warmupSeconds, double measureSeconds, string output)
        {
            if (warmupSeconds < 0 || measureSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(measureSeconds), "Needs a positive measurement time.");
            this.warmupSeconds = warmupSeconds;
            this.measureSeconds = measureSeconds;
            this.output = output;
        }

        /// <summary>Assigned to the shared world's simulation so every tick reports its cost.</summary>
        public TickTimings Timings { get; } = new TickTimings();

        public bool IsMeasuring { get; private set; }
        public bool IsFinished { get; private set; }

        /// <summary>Where the report was written; null until the measurement ends.</summary>
        public string Directory { get; private set; }

        /// <summary>-eden-benchmark SECONDS [-eden-benchmark-warmup SECONDS] [-eden-benchmark-output DIR]; null without it.</summary>
        public static LabBenchmark FromCommandLine(string[] arguments)
        {
            double? seconds = null;
            var warmup = 5.0;
            string output = null;
            for (var i = 0; i + 1 < arguments.Length; i++)
            {
                var value = arguments[i + 1];
                switch (arguments[i])
                {
                    case "-eden-benchmark" when TryParse(value, out var parsed):
                        seconds = parsed;
                        break;
                    case "-eden-benchmark-warmup" when TryParse(value, out var parsed):
                        warmup = parsed;
                        break;
                    case "-eden-benchmark-output":
                        output = value;
                        break;
                }
            }

            return seconds.HasValue && seconds.Value > 0 ? new LabBenchmark(warmup, seconds.Value, output) : null;
        }

        public void BeginSimulation()
        {
            if (IsMeasuring && allocations != null)
                allocations.enabled = true;
            simulationWatch.Restart();
        }

        public void EndSimulation(long ticks)
        {
            simulationWatch.Stop();
            if (IsMeasuring && allocations != null)
            {
                allocations.enabled = false;
                allocationCount += allocations.sampleBlockCount;
            }

            frameTicks = ticks;
        }

        public void RecordTick()
        {
            if (IsMeasuring)
                recorder.AddTick(Timings);
        }

        /// <summary>Closes the frame; returns true on the frame the measurement ends and the report is written.</summary>
        public bool EndFrame(double frameSeconds, LabRunner runner)
        {
            if (IsFinished)
                return false;
            if (!IsMeasuring)
            {
                warmedSeconds += frameSeconds;
                if (warmedSeconds >= warmupSeconds)
                    Begin(runner);
                return false;
            }

            recorder.AddFrame(frameSeconds, frameTicks, simulationWatch.Elapsed.TotalMilliseconds);
            if (frameAllocated.Valid)
                frameBytes += frameAllocated.LastValue;
            if (recorder.ElapsedSeconds < measureSeconds)
                return false;

            Finish(runner);
            return true;
        }

        public void Dispose()
        {
            if (frameAllocated.Valid)
                frameAllocated.Dispose();
            if (allocations != null)
                allocations.enabled = false;
        }

        private void Begin(LabRunner runner)
        {
            IsMeasuring = true;
            allocations = ProbeAllocationRecorder();
            frameAllocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            gcAtStart = GC.CollectionCount(0);
            heapAtStart = Profiler.GetMonoUsedSizeLong();
            populationAtStart = runner.Simulation.World.Creatures.Count;
            episodesAtStart = runner.Evolution?.EpisodesRun ?? 0;
        }

        // The GC.Alloc recorder exists only where the profiler runs (Development builds, the Editor). It is trusted only
        // if it sees an allocation made on purpose, so a silent recorder never reads as "no allocations".
        private static Recorder ProbeAllocationRecorder()
        {
            var recorder = Recorder.Get("GC.Alloc");
            if (recorder == null || !recorder.isValid)
                return null;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            var probe = new object[4];
            recorder.enabled = false;
            GC.KeepAlive(probe);
            return recorder.sampleBlockCount > 0 ? recorder : null;
        }

        private void Finish(LabRunner runner)
        {
            IsMeasuring = false;
            IsFinished = true;
            var simulated = runner.Simulation.World.Config.TicksPerSecond;
            var record = new BenchmarkRecord(Environment(), Scenario(runner), recorder)
            {
                RequestedTicksPerSecond = runner.Clock.IsUnpaced ? (double?)null : simulated * runner.Clock.RequestedSpeed,
                PopulationAtStart = populationAtStart,
                PopulationAtEnd = runner.Simulation.World.Creatures.Count,
                Episodes = runner.Mode == LabMode.IsolatedEvolution
                    ? runner.Evolution.EpisodesRun - episodesAtStart
                    : (long?)null,
                GcCollections = GC.CollectionCount(0) - gcAtStart,
                SimulationAllocations = allocations != null ? allocationCount : (long?)null,
                FrameAllocatedBytes = frameAllocated.Valid ? frameBytes : (long?)null,
                ManagedHeapUsedStart = heapAtStart > 0 ? heapAtStart : (long?)null,
                ManagedHeapUsedEnd = Positive(Profiler.GetMonoUsedSizeLong()),
                TotalReservedEnd = Positive(Profiler.GetTotalReservedMemoryLong()),
                TotalAllocatedEnd = Positive(Profiler.GetTotalAllocatedMemoryLong())
            };
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    // Mono may report 0 here; that is unavailable, not an empty process.
                    record.WorkingSetEnd = Positive(process.WorkingSet64);
                    record.PeakWorkingSet = Positive(process.PeakWorkingSet64);
                }
            }
            catch (Exception error) when (error is InvalidOperationException || error is NotSupportedException ||
                                          error is PlatformNotSupportedException)
            {
                // Reported as unavailable.
            }

            Dispose();
            Directory = output ?? DefaultDirectory(runner);
            try
            {
                PerformanceReport.Write(Directory, record);
                Debug.Log("EDEN benchmark finished: output=" + Directory);
                foreach (var line in PerformanceReport.Describe(record))
                    Debug.Log("EDEN benchmark | " + line);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogError($"EDEN: benchmark report not written ({error.Message})");
            }
        }

        private static IReadOnlyList<KeyValuePair<string, string>> Environment()
        {
            var culture = CultureInfo.InvariantCulture;
            var resolution = Screen.currentResolution;
            return new[]
            {
                Pair("build", UnityEngine.Application.isEditor ? "editor" : Debug.isDebugBuild ? "development" : "release"),
                Pair("app_version", UnityEngine.Application.version),
                Pair("unity_version", UnityEngine.Application.unityVersion),
                Pair("platform", UnityEngine.Application.platform.ToString()),
#if ENABLE_IL2CPP
                Pair("scripting_backend", "il2cpp"),
#else
                Pair("scripting_backend", "mono"),
#endif
                Pair("os", SystemInfo.operatingSystem),
                Pair("cpu", SystemInfo.processorType),
                Pair("cpu_logical_cores", SystemInfo.processorCount.ToString(culture)),
                Pair("cpu_frequency_mhz", SystemInfo.processorFrequency.ToString(culture)),
                Pair("ram_mb", SystemInfo.systemMemorySize.ToString(culture)),
                Pair("gpu", SystemInfo.graphicsDeviceName),
                Pair("gpu_memory_mb", SystemInfo.graphicsMemorySize.ToString(culture)),
                Pair("graphics_api", SystemInfo.graphicsDeviceType.ToString()),
                Pair("window", Screen.width.ToString(culture) + "x" + Screen.height.ToString(culture) + " " +
                               Screen.fullScreenMode),
                Pair("display", resolution.width.ToString(culture) + "x" + resolution.height.ToString(culture) + " @ " +
                                resolution.refreshRateRatio.value.ToString("0.##", culture) + " Hz"),
                Pair("quality", QualitySettings.names[QualitySettings.GetQualityLevel()]),
                Pair("vsync_count", QualitySettings.vSyncCount.ToString(culture)),
                Pair("target_frame_rate", UnityEngine.Application.targetFrameRate.ToString(culture)),
                Pair("measured_utc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", culture))
            };
        }

        private IReadOnlyList<KeyValuePair<string, string>> Scenario(LabRunner runner)
        {
            var culture = CultureInfo.InvariantCulture;
            var config = runner.Simulation.World.Config;
            return new[]
            {
                Pair("mode", runner.Mode == LabMode.IsolatedEvolution ? "evolution" : "shared"),
                Pair("preset", runner.Preset.Name),
                Pair("brain", runner.Mode == LabMode.IsolatedEvolution ? "mlp (evolução)" : runner.BrainName),
                Pair("seed", runner.Seed.ToString(culture)),
                Pair("speed", runner.Clock.IsUnpaced ? "max" : runner.Clock.RequestedSpeed.ToString("0.###", culture) + "x"),
                Pair("simulated_ticks_per_second", runner.Simulation.World.Config.TicksPerSecond.ToString(culture)),
                Pair("arena", config.ArenaWidth.ToString(culture) + "x" + config.ArenaHeight.ToString(culture) +
                              (runner.Scenario.Describe() is string scenario ? " (" + scenario + ")" : string.Empty)),
                Pair("food", config.InitialFood.ToString(culture)),
                Pair("bodies", config.SolidBodies ? "solid" : "points"),
                Pair("obstacles", config.Obstacles.Count.ToString(culture)),
                Pair("water_sources", config.Water.Count.ToString(culture)),
                Pair("max_speed_budget_ms", (LabRunner.MaxSpeedBudgetSeconds * 1000).ToString("0.#", culture)),
                Pair("warmup_seconds", warmupSeconds.ToString("0.###", culture)),
                Pair("selected_creature", runner.Selected.HasValue ? runner.Selected.Value.Value.ToString(culture) : "none"),
                Pair("metrics_logs", "off"),
                Pair("input", "ignored while measuring")
            };
        }

        private static string DefaultDirectory(LabRunner runner)
        {
            var mode = runner.Mode == LabMode.IsolatedEvolution ? "evolution" : "shared";
            var speed = runner.Clock.IsUnpaced ? "max" : "1x";
            var build = UnityEngine.Application.isEditor ? "editor" : Debug.isDebugBuild ? "development" : "release";
            return Path.Combine(UnityEngine.Application.persistentDataPath, "Benchmarks",
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{mode}-{runner.Preset.Name}-{speed}-{build}");
        }

        private static long? Positive(long value) => value > 0 ? value : (long?)null;

        private static KeyValuePair<string, string> Pair(string key, string value) =>
            new KeyValuePair<string, string>(key, value ?? string.Empty);

        private static bool TryParse(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= 0;
    }
}
