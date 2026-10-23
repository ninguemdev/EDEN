using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using UnityEditor;
using UnityEngine;

namespace Eden.Editor
{
    /// <summary>
    /// Measures the reference brains in isolated episodes over a grid of episode economies (food, initial energy,
    /// movement cost, metabolism, duration) on calibration seeds that are neither training nor validation seeds:
    /// SplitMix64 output (i + 1) of seed (SplitMix64 output "cali" of the calibration seed). Writes one CSV row per
    /// combination and brain with the spread across seeds:
    /// Unity.exe -batchmode -projectPath . -executeMethod Eden.Editor.EpisodeCalibration.RunFromCommandLine
    ///   [-eden-seed N] [-eden-seeds N] [-eden-output FILE] -logFile -
    /// </summary>
    public static class EpisodeCalibration
    {
        public const string Header =
            "food,initial_energy,movement_cost,metabolism,episode_seconds,brain,seeds,fitness_mean,fitness_std," +
            "fitness_min,fitness_max,survival_rate,mean_survived_seconds";

        private const ulong CalibrationTag = 0x63616C69UL; // "cali"

        private static readonly int[] Food = { 32, 64, 128 };
        private static readonly float[] InitialEnergy = { 50f, 100f };
        private static readonly float[] MovementCost = { 2f, 1f };
        private static readonly float[] Metabolism = { 1f, 0.5f };
        private static readonly float[] EpisodeSeconds = { 120f, 60f };

        public static void RunFromCommandLine()
        {
            try
            {
                ulong calibrationSeed = 1;
                var seedCount = 20;
                var output = Path.Combine(UnityEngine.Application.persistentDataPath, "EvolutionRuns",
                    $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-calibration.csv");
                var args = Environment.GetCommandLineArgs();
                for (var i = 0; i + 1 < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "-eden-seed":
                            calibrationSeed = ulong.Parse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture);
                            break;
                        case "-eden-seeds":
                            seedCount = int.Parse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture);
                            break;
                        case "-eden-output":
                            output = args[i + 1];
                            break;
                    }
                }

                var rows = Run(CalibrationSeeds(calibrationSeed, seedCount));
                var directory = Path.GetDirectoryName(Path.GetFullPath(output));
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(output, Header + "\n" + string.Join("\n", rows) + "\n");
                Debug.Log($"EDEN calibration finished: rows={rows.Count}, output={output}");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        public static ulong[] CalibrationSeeds(ulong calibrationSeed, int count)
        {
            var root = SplitMix64.Output(calibrationSeed, CalibrationTag);
            var seeds = new ulong[count];
            for (var i = 0; i < count; i++)
                seeds[i] = SplitMix64.Output(root, (ulong)i + 1);
            return seeds;
        }

        private static List<string> Run(ulong[] seeds)
        {
            var rows = new List<string>();
            var brains = new Func<IBrain>[] { () => new RandomBrain(), () => new HeuristicBrain() };
            var names = new[] { "random", "heuristic" };
            foreach (var seconds in EpisodeSeconds)
            foreach (var food in Food)
            foreach (var energy in InitialEnergy)
            foreach (var movement in MovementCost)
            foreach (var metabolism in Metabolism)
            {
                var settings = new EvolutionSettings { EpisodeSeconds = seconds };
                settings.World.InitialFood = food;
                settings.World.InitialEnergy = energy;
                settings.World.MovementCostPerSecond = movement;
                settings.World.MetabolismPerSecond = metabolism;
                var evaluation = new IsolatedEvaluator(new EvaluationPlan(settings))
                    .Evaluate(brains, seeds, 0, EvaluationMode.Validation); // the mode is not reported here

                for (var b = 0; b < brains.Length; b++)
                {
                    var candidate = evaluation.Candidates[b];
                    var spread = new Spread(candidate.Episodes.Select(e => e.Fitness).ToArray());
                    var row = string.Join(",",
                        food.ToString(CultureInfo.InvariantCulture),
                        Number(energy), Number(movement), Number(metabolism), Number(seconds), names[b],
                        seeds.Length.ToString(CultureInfo.InvariantCulture),
                        Number(spread.Mean), Number(spread.StdDev), Number(spread.Min), Number(spread.Max),
                        Number(candidate.SurvivalRate), Number(candidate.MeanSurvivedSeconds));
                    rows.Add(row);
                    Debug.Log("EDEN calibration " + row);
                }
            }

            return rows;
        }

        private static string Number(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
