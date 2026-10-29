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
    ///   [-eden-seed N] [-eden-generations N] [-eden-food N] [-eden-body GENES] [-eden-output DIR]
    ///   [-eden-seed-genomes DIR] [-eden-export-top N DIR] -logFile -
    /// Unity.exe -batchmode -projectPath . -executeMethod Eden.Editor.EvolutionBatch.RunBatteryFromCommandLine
    ///   [-eden-seed N] [-eden-runs N] [-eden-generations N] [-eden-validation-interval N] [-eden-food N]
    ///   [-eden-output DIR] -logFile -
    /// Unity.exe -batchmode -projectPath . -executeMethod Eden.Editor.EvolutionBatch.ComparePresetsFromCommandLine
    ///   [-eden-presets minimal,standard,stress] and the battery options -logFile -
    /// Unity.exe -batchmode -projectPath . -executeMethod Eden.Editor.EvolutionBatch.AblateBodyFromCommandLine
    ///   [-eden-variants ablation|extended|fixed,all,no-size,only-sensor,…] and the battery options -logFile -
    /// Unity.exe -batchmode -projectPath . -executeMethod Eden.Editor.EvolutionBatch.ComparePlasticityFromCommandLine
    ///   [-eden-food-shift SECONDS (default 60)] and the battery options -logFile -
    /// -eden-body takes the expressed body genes: none (the fixed body, default), all, or a comma list of
    /// size, speed, sensor, efficiency; -eden-plasticity on|off turns lifetime plasticity on (default off). The battery
    /// commands accept both. -eden-algorithm mlp|neat chooses how generations are bred (default mlp, the legacy
    /// control); a NEAT run only takes a pool of NEAT genomes, which are renumbered on import. -eden-senses legacy|eyes
    /// chooses how creatures sense (eyes need NEAT; default legacy).
    /// -eden-seed-genomes DIR starts generation 0 of RunFromCommandLine from every genome file (see GenomeFile) in DIR
    /// instead of random weights (see GenerationalEvolution.FromSeedPool); a genome exported without a fitness of its
    /// own, or with one from another fitness profile, counts as 0. -eden-export-top N DIR writes the N best genomes of the last generation RunFromCommandLine
    /// trained to DIR (see GenomeFile), one file per genome, ranked by their training fitness.
    /// Settings not given keep the proposed defaults of <see cref="EvolutionSettings"/> and <see cref="BatterySettings"/>.
    /// </summary>
    public static class EvolutionBatch
    {
        private const int DefaultGenerations = 20;

        [MenuItem("EDEN/Evolution/Run 20 generations (seed 1)")]
        public static void RunFromMenu()
        {
            Run(new EvolutionSettings { ExperimentSeed = 1 }, DefaultGenerations, null, null, null);
        }

        public static void RunFromCommandLine()
        {
            RunAndExit(() =>
            {
                var settings = new EvolutionSettings { ExperimentSeed = 1 };
                var battery = new BatterySettings { Generations = DefaultGenerations };
                var output = ReadArguments(settings, battery, out var seedGenomes);
                Run(settings, battery.Generations, output, seedGenomes, ReadExportTop());
            });
        }

        public static void RunBatteryFromCommandLine()
        {
            RunAndExit(() =>
            {
                var settings = new EvolutionSettings { ExperimentSeed = 1 };
                var battery = new BatterySettings();
                var output = ReadArguments(settings, battery, out _);
                RunBattery(settings, battery, output);
            });
        }

        public static void ComparePresetsFromCommandLine()
        {
            RunAndExit(() =>
            {
                var settings = new EvolutionSettings { ExperimentSeed = 1 };
                var battery = new BatterySettings();
                var output = ReadArguments(settings, battery, out _);
                ComparePresets(settings, battery, ReadPresets(), output);
            });
        }

        public static void ComparePlasticityFromCommandLine()
        {
            RunAndExit(() =>
            {
                var settings = new EvolutionSettings { ExperimentSeed = 1 };
                var battery = new BatterySettings();
                var output = ReadArguments(settings, battery, out _);
                ComparePlasticity(settings, battery, ReadFoodShift(), output);
            });
        }

        public static void AblateBodyFromCommandLine()
        {
            RunAndExit(() =>
            {
                var settings = new EvolutionSettings { ExperimentSeed = 1 };
                var battery = new BatterySettings();
                var output = ReadArguments(settings, battery, out _);
                AblateBody(settings, battery, ReadVariants(), output);
            });
        }

        private static void Run(EvolutionSettings settings, int generations, string output, string seedGenomesDirectory,
            (int Count, string Directory)? exportTop)
        {
            if (generations < 1)
                throw new ArgumentOutOfRangeException(nameof(generations), "At least one generation is required.");

            GenerationalEvolution evolution;
            if (seedGenomesDirectory == null)
            {
                evolution = new GenerationalEvolution(settings);
            }
            else
            {
                SplitPool(seedGenomesDirectory, out var pool, out var poolFitness);
                evolution = GenerationalEvolution.FromSeedPool(settings, pool, poolFitness);
            }

            var directory = output ?? DefaultDirectory($"seed{settings.ExperimentSeed}");
            var header = new ExperimentHeader(settings, 0, UnityEngine.Application.version, DateTime.UtcNow);
            var started = DateTime.UtcNow;
            GenerationReport report = null;

            using (var log = new EvolutionCsvLog(directory, header))
            {
                for (var g = 0; g < generations; g++)
                {
                    report = evolution.Advance();
                    log.Append(report);
                    LogSummary(0, report.Summary);
                }
            }

            if (exportTop.HasValue)
                ExportTop(report, settings.ExperimentSeed, exportTop.Value.Count, exportTop.Value.Directory);

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "EDEN evolution finished: generations={0}, seconds={1:0.0}, output={2}",
                generations, (DateTime.UtcNow - started).TotalSeconds, directory));
        }

        // A genome exported without its own fitness (or scored by another profile) ranks last among real scores rather
        // than always losing ties.
        private static void SplitPool(string directory, out Genome[] genomes, out float[] fitness)
        {
            var pool = GenomeFile.LoadPool(directory);
            genomes = new Genome[pool.Length];
            fitness = new float[pool.Length];
            for (var i = 0; i < pool.Length; i++)
            {
                genomes[i] = pool[i].Genome;
                fitness[i] = pool[i].FitnessFor(Fitness.Profile) ?? 0f;
            }
        }

        private static void ExportTop(GenerationReport report, ulong experimentSeed, int count, string directory)
        {
            var candidates = report.Evaluation.Candidates;
            var fitness = new float[candidates.Count];
            for (var i = 0; i < fitness.Length; i++)
                fitness[i] = candidates[i].MeanFitness;
            var ranking = Reproduction.Rank(fitness);
            var exported = Math.Min(count, report.Individuals.Count);
            for (var i = 0; i < exported; i++)
            {
                var individual = report.Individuals[ranking[i]];
                var record = new GenomeRecord(individual.Genome, fitness[ranking[i]],
                    new GenomeSource(experimentSeed, report.Evaluation.Generation, individual.Id));
                GenomeFile.Save(Path.Combine(directory, $"genome-rank{i + 1}-id{individual.Id}.json"), record,
                    UnityEngine.Application.version, DateTime.UtcNow);
            }

            Debug.Log($"EDEN exported top {exported} genomes to {directory}");
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

        private static void AblateBody(EvolutionSettings settings, BatterySettings battery,
            IReadOnlyList<BodyVariant> variants, string output)
        {
            var ablation = new BodyAblation(settings, battery, variants);
            var directory = output ?? DefaultDirectory($"body-ablation-seed{settings.ExperimentSeed}x{battery.Runs}");
            var logs = new Dictionary<(string, int), EvolutionCsvLog>();
            try
            {
                var result = ablation.Run((variant, run, report) =>
                {
                    if (!logs.TryGetValue((variant.Name, run), out var log))
                    {
                        var runSettings = ablation.Settings(variant).WithExperimentSeed(settings.ExperimentSeed + (ulong)run);
                        log = RunLog(Path.Combine(directory, variant.Name), run, runSettings, battery, null);
                        logs.Add((variant.Name, run), log);
                    }

                    log.Append(report);
                    LogSummary(run, report.Summary);
                });

                BodyAblationReport.Write(directory, ablation, result, UnityEngine.Application.version, DateTime.UtcNow);
                foreach (var line in BodyAblationReport.Describe(result))
                    Debug.Log("EDEN body ablation | " + line);
                Debug.Log("EDEN body ablation finished: output=" + directory);
            }
            finally
            {
                foreach (var log in logs.Values)
                    log.Dispose();
            }
        }

        private static void ComparePlasticity(EvolutionSettings settings, BatterySettings battery, float foodShift,
            string output)
        {
            var comparison = new PlasticityComparison(settings, battery, foodShift);
            var directory = output ?? DefaultDirectory($"plasticity-seed{settings.ExperimentSeed}x{battery.Runs}");
            var logs = new Dictionary<(string, int), EvolutionCsvLog>();
            try
            {
                var result = comparison.Run((cell, run, report) =>
                {
                    if (!logs.TryGetValue((cell.Name, run), out var log))
                    {
                        var runSettings = comparison.Settings(cell).WithExperimentSeed(settings.ExperimentSeed + (ulong)run);
                        log = RunLog(Path.Combine(directory, cell.Name), run, runSettings, battery, null);
                        logs.Add((cell.Name, run), log);
                    }

                    log.Append(report);
                    LogSummary(run, report.Summary);
                });

                PlasticityComparisonReport.Write(directory, comparison, result, UnityEngine.Application.version, DateTime.UtcNow);
                foreach (var line in PlasticityComparisonReport.Describe(result))
                    Debug.Log("EDEN plasticity | " + line);
                Debug.Log("EDEN plasticity comparison finished: output=" + directory);
            }
            finally
            {
                foreach (var log in logs.Values)
                    log.Dispose();
            }
        }

        private static float ReadFoodShift()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-eden-food-shift")
                    return float.Parse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            return 60f;
        }

        private static IReadOnlyList<BodyVariant> ReadVariants()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] != "-eden-variants")
                    continue;
                if (args[i + 1] == "ablation")
                    return BodyVariant.Ablation;
                if (args[i + 1] == "extended")
                    return BodyVariant.Extended;
                return args[i + 1].Split(',').Select(name => BodyVariant.Parse(name.Trim())).ToList();
            }

            return BodyVariant.Ablation;
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

        private static string ReadArguments(EvolutionSettings settings, BatterySettings battery,
            out string seedGenomesDirectory)
        {
            string output = null;
            string seedGenomes = null;
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 1 < args.Length; i++)
            {
                var value = args[i + 1];
                switch (args[i])
                {
                    case "-eden-seed-genomes":
                        seedGenomes = value;
                        break;
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
                    case "-eden-plasticity" when value == "on" || value == "off":
                        settings.Plasticity = value == "on";
                        break;
                    case "-eden-algorithm" when value == "mlp" || value == "neat":
                        settings.Algorithm = value == "neat" ? EvolutionAlgorithm.Neat : EvolutionAlgorithm.Mlp;
                        break;
                    case "-eden-senses" when value == "legacy" || value == "eyes":
                        settings.Senses = value == "eyes" ? Senses.Eyes : Senses.Legacy;
                        break;
                    case "-eden-output":
                        output = value;
                        break;
                }
            }

            seedGenomesDirectory = seedGenomes;
            return output;
        }

        /// <summary>-eden-export-top N DIR, or null when not given.</summary>
        private static (int Count, string Directory)? ReadExportTop()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i + 2 < args.Length; i++)
            {
                if (args[i] == "-eden-export-top")
                    return (Integer(args[i + 1]), args[i + 2]);
            }

            return null;
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
