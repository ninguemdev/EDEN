using System;
using System.Globalization;
using System.IO;
using Eden.Application;
using Eden.Evolution;
using Eden.Persistence;
using UnityEditor;
using UnityEngine;

namespace Eden.Editor
{
    /// <summary>
    /// Runs a generational experiment in isolated episodes without a scene and exports it with
    /// <see cref="EvolutionCsvLog"/> to persistentDataPath/EvolutionRuns/&lt;UTC&gt;-seed&lt;n&gt;. From the command line:
    /// Unity.exe -batchmode -projectPath . -executeMethod Eden.Editor.EvolutionBatch.RunFromCommandLine
    ///   [-eden-seed N] [-eden-generations N] [-eden-food N] [-eden-output DIR] -logFile -
    /// Settings not given keep the proposed defaults of <see cref="EvolutionSettings"/>.
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
            try
            {
                var settings = new EvolutionSettings { ExperimentSeed = 1 };
                var generations = DefaultGenerations;
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
                            generations = int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
                            break;
                        case "-eden-food":
                            settings.World.InitialFood = int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
                            break;
                        case "-eden-output":
                            output = value;
                            break;
                    }
                }

                Run(settings, generations, output);
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void Run(EvolutionSettings settings, int generations, string output)
        {
            if (generations < 1)
                throw new ArgumentOutOfRangeException(nameof(generations), "At least one generation is required.");

            var evolution = new GenerationalEvolution(settings);
            var directory = output ?? Path.Combine(UnityEngine.Application.persistentDataPath, "EvolutionRuns",
                $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-seed{settings.ExperimentSeed}");
            var info = EvolutionCsvLog.Describe(evolution.Plan, evolution.Rules, UnityEngine.Application.version,
                DateTime.UtcNow);
            var started = DateTime.UtcNow;

            using (var log = new EvolutionCsvLog(directory, info))
            {
                for (var g = 0; g < generations; g++)
                {
                    var report = evolution.Advance();
                    log.Append(report);
                    var summary = report.Summary;
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "EDEN evolution gen={0} best={1:0.###} median={2:0.###} lower_quartile={3:0.###} " +
                        "survival={4:0.###} weight_std={5:0.####} distinct_parents={6}",
                        summary.Generation, summary.Best, summary.Median, summary.LowerQuartile,
                        summary.MeanSurvivalRate, summary.WeightStdDev, summary.DistinctParents));
                }
            }

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "EDEN evolution finished: generations={0}, seconds={1:0.0}, output={2}",
                generations, (DateTime.UtcNow - started).TotalSeconds, directory));
        }
    }
}
