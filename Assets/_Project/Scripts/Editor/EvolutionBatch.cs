using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using UnityEditor;
using UnityEngine;

namespace Eden.Editor
{
    /// <summary>
    /// Runs generational experiments in isolated episodes without a scene and exports them to
    /// persistentDataPath/EvolutionRuns/&lt;UTC&gt;-… (or -eden-output DIR). From the command line:
    /// Unity.exe -batchmode -projectPath . -executeMethod Eden.Editor.EvolutionBatch.RunFromCommandLine
    ///   [-eden-seed N] [-eden-generations N] [-eden-food N] [-eden-body GENES] [-eden-output DIR] -logFile -
    /// Unity.exe -batchmode -projectPath . -executeMethod Eden.Editor.EvolutionBatch.RunBatteryFromCommandLine
    ///   [-eden-seed N] [-eden-runs N] [-eden-generations N] [-eden-validation-interval N] [-eden-food N]
    ///   [-eden-output DIR] -logFile -
    /// Unity.exe -batchmode -projectPath . -executeMethod Eden.Editor.EvolutionBatch.ComparePresetsFromCommandLine
    ///   [-eden-presets minimal,standard,stress] and the battery options -logFile -
    /// -eden-body takes the expressed body genes: none (the fixed body, default), all, or a comma list of
    /// size, speed, sensor, efficiency; the battery commands accept it too.
    /// Settings not given keep the proposed defaults of <see cref="EvolutionSettings"/> and <see cref="BatterySettings"/>.
    /// </summary>
    public static class EvolutionBatch
    {
        private const int DefaultGenerations = 20;

        [MenuItem("EDEN/Evolution/Run 20 generations (seed 1)")]
        public static void RunFromMenu()
        {
            Run(new EvolutionSettings { ExperimentSeed = 1 }, DefaultGenerations, null);
        }

        public static void RunFromCommandLine()
        {
            RunAndExit(() =>
            {
                var settings = new EvolutionSettings { ExperimentSeed = 1 };
                var battery = new BatterySettings { Generations = DefaultGenerations };
                var output = ReadArguments(settings, battery);
                Run(settings, battery.Generations, output);
            });
        }

        public static void RunBatteryFromCommandLine()
        {
            RunAndExit(() =>
            {
                var settings = new EvolutionSettings { ExperimentSeed = 1 };
                var battery = new BatterySettings();
                var output = ReadArguments(settings, battery);
                RunBattery(settings, battery, output);
            });
        }

        public static void ComparePresetsFromCommandLine()
        {
            RunAndExit(() =>
            {
                var settings = new EvolutionSettings { ExperimentSeed = 1 };
                var battery = new BatterySettings();
                var output = ReadArguments(settings, battery);
                ComparePresets(settings, battery, ReadPresets(), output);
            });
        }

        private static void Run(EvolutionSettings settings, int generations, string output)
        {
            if (generations < 1)
                throw new ArgumentOutOfRangeException(nameof(generations), "At least one generation is required.");

            var evolution = new GenerationalEvolution(settings);
            var directory = output ?? DefaultDirectory($"seed{settings.ExperimentSeed}");
            var header = new ExperimentHeader(settings, 0, UnityEngine.Application.version, DateTime.UtcNow);
            var started = DateTime.UtcNow;

            using (var log = new EvolutionCsvLog(directory, header))
            {
                for (var g = 0; g < generations; g++)
                {
                    var report = evolution.Advance();
                    log.Append(report);
                    LogSummary(0, report.Summary);
                }
            }

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "EDEN evolution finished: generations={0}, seconds={1:0.0}, output={2}",
                generations, (DateTime.UtcNow - started).TotalSeconds, directory));
        }

        private static void RunBattery(EvolutionSettings settings, BatterySettings battery, string output)
        {
            var runner = new EvolutionBattery(settings, battery);
            var directory = output ?? DefaultDirectory($"battery-seed{settings.ExperimentSeed}x{battery.Runs}");
            var logs = new EvolutionCsvLog[battery.Runs];
            try
            {
                var result = runner.Run((run, report) =>
                {
                    logs[run] ??= RunLog(directory, run, settings.WithExperimentSeed(runner.RunSeed(run)), battery, null);
                    logs[run].Append(report);
                    LogSummary(run, report.Summary);
                });

                BatteryReport.Write(directory, result);
                foreach (var line in BatteryReport.Describe(result))
                    Debug.Log("EDEN battery | " + line);
                Debug.Log("EDEN battery finished: output=" + directory);
            }
            finally
            {
                foreach (var log in logs)
                    log?.Dispose();
            }
        }

        private static void ComparePresets(EvolutionSettings settings, BatterySettings battery,
            IReadOnlyList<ExperimentPreset> presets, string output)
        {
            var comparison = new PresetComparison(settings, battery, presets);
            var directory = output ?? DefaultDirectory($"presets-seed{settings.ExperimentSeed}x{battery.Runs}");
            var logs = new Dictionary<(string, int), EvolutionCsvLog>();
            try
            {
                var result = comparison.Run((preset, run, report) =>
                {
                    if (!logs.TryGetValue((preset.Name, run), out var log))
                    {
                        var runSettings = comparison.Settings(preset).WithExperimentSeed(settings.ExperimentSeed + (ulong)run);
                        log = RunLog(Path.Combine(directory, preset.Name), run, runSettings, battery, preset.Name);
                        logs.Add((preset.Name, run), log);
                    }

                    log.Append(report);
                    LogSummary(run, report.Summary);
                });

                PresetComparisonReport.Write(directory, comparison, result, UnityEngine.Application.version, DateTime.UtcNow);
                foreach (var line in PresetComparisonReport.Describe(result))
                    Debug.Log("EDEN presets | " + line);
                Debug.Log("EDEN preset comparison finished: output=" + directory);
            }
            finally
            {
                foreach (var log in logs.Values)
                    log.Dispose();
            }
        }

        private static EvolutionCsvLog RunLog(string directory, int run, EvolutionSettings runSettings,
            BatterySettings battery, string preset) =>
            new EvolutionCsvLog(Path.Combine(directory, $"run{run}-seed{runSettings.ExperimentSeed}"),
                new ExperimentHeader(runSettings, battery.ValidationInterval, UnityEngine.Application.version,
                    DateTime.UtcNow, preset));

        private static IReadOnlyList<ExperimentPreset> ReadPresets()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] != "-eden-presets")
                    continue;
                var presets = new List<ExperimentPreset>();
                foreach (var name in args[i + 1].Split(','))
                {
                    var preset = ExperimentPreset.All.FirstOrDefault(p => p.Name == name.Trim()) ??
                                 throw new ArgumentException($"Unknown preset \"{name}\".");
                    presets.Add(preset);
                }

                return presets;
            }

            return ExperimentPreset.All;
        }

        private static string ReadArguments(EvolutionSettings settings, BatterySettings battery)
        {
            string output = null;
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
            {
                var value = args[i + 1];
                switch (args[i])
                {
                    case "-eden-seed":
                        settings.ExperimentSeed = ulong.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
                        break;
                    case "-eden-generations":
                        battery.Generations = Integer(value);
                        break;
                    case "-eden-runs":
                        battery.Runs = Integer(value);
                        break;
                    case "-eden-validation-interval":
                        battery.ValidationInterval = Integer(value);
                        break;
                    case "-eden-food":
                        settings.World.InitialFood = Integer(value);
                        break;
                    case "-eden-body":
                        settings.ExpressedBodyGenes = BodyGeneSetOf(value);
                        break;
                    case "-eden-output":
                        output = value;
                        break;
                }
            }

            return output;
        }

        private static void RunAndExit(Action run)
        {
            try
            {
                run();
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void LogSummary(int run, GenerationSummary summary)
        {
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "EDEN evolution run={0} gen={1} mode={2} best={3:0.###} median={4:0.###} lower_quartile={5:0.###} " +
                "survival={6:0.###} weight_std={7:0.####} distinct_parents={8}",
                run, summary.Generation, EvaluationCsv.ModeName(summary.Mode), summary.Best, summary.Median,
                summary.LowerQuartile, summary.MeanSurvivalRate, summary.WeightStdDev, summary.DistinctParents));
        }

        private static int Integer(string value) => int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);

        private static BodyGeneSet BodyGeneSetOf(string value)
        {
            if (value == "none")
                return BodyGeneSet.None;
            if (value == "all")
                return BodyGeneSet.All;
            var set = BodyGeneSet.None;
            foreach (var name in value.Split(','))
            {
                if (!Enum.TryParse(name.Trim(), true, out BodyGene gene) || !Enum.IsDefined(typeof(BodyGene), gene))
                    throw new ArgumentException($"Unknown body gene \"{name}\" (size, speed, sensor, efficiency).");
                set |= (BodyGeneSet)(1 << (int)gene);
            }

            return set;
        }

        private static string DefaultDirectory(string name) =>
            Path.Combine(UnityEngine.Application.persistentDataPath, "EvolutionRuns", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{name}");
    }
}
