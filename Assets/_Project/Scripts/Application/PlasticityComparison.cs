using System;
using System.Collections.Generic;
using System.Diagnostics;
using Eden.Domain;
using Eden.Evolution;

namespace Eden.Application
{
    /// <summary>
    /// The M7 comparison: the same battery (experiment and validation seeds, generations, population, episode) in four
    /// cells, a static world and a world whose food moves during the episode, each with fixed and with plastic networks.
    /// Every cell evaluates the same episodes. Learning can only be credited where it makes a difference against the
    /// fixed networks, and the lesion separates it from evolution: the final plastic population is evaluated again on
    /// the validation seeds with learning switched off (same genomes, coefficients set to fixed).
    /// </summary>
    public sealed class PlasticityComparison
    {
        private readonly EvolutionSettings template;

        public PlasticityComparison(EvolutionSettings template, BatterySettings battery, float foodShiftSeconds)
        {
            this.template = template ?? throw new ArgumentNullException(nameof(template));
            Battery = battery ?? throw new ArgumentNullException(nameof(battery));
            battery.Validate();
            if (!(foodShiftSeconds > 0f))
                throw new ArgumentOutOfRangeException(nameof(foodShiftSeconds), "The changing world needs a shift time.");
            FoodShiftSeconds = foodShiftSeconds;
            foreach (var cell in Cells)
                new EvaluationPlan(Settings(cell));
        }

        /// <summary>Cells in run order: static fixed, static plastic, changing fixed, changing plastic.</summary>
        public static readonly IReadOnlyList<PlasticityCell> Cells = new[]
        {
            new PlasticityCell(false, false), new PlasticityCell(false, true), new PlasticityCell(true, false),
            new PlasticityCell(true, true)
        };

        public BatterySettings Battery { get; }
        public float FoodShiftSeconds { get; }

        /// <summary>The template with the cell's world and plasticity; everything else is shared by all cells.</summary>
        public EvolutionSettings Settings(PlasticityCell cell)
        {
            var settings = template.WithExperimentSeed(template.ExperimentSeed);
            settings.World.FoodShiftSeconds = cell.Changing ? FoodShiftSeconds : 0f;
            settings.Plasticity = cell.Plastic;
            return settings;
        }

        /// <summary>Runs every cell in order; <paramref name="onReport"/> gets each report with its cell and run.</summary>
        public PlasticityComparisonResult Run(Action<PlasticityCell, int, GenerationReport> onReport = null)
        {
            var outcomes = new List<PlasticityCellOutcome>(Cells.Count);
            foreach (var cell in Cells)
            {
                var settings = Settings(cell);
                var training = 0L;
                var validation = 0L;
                var finals = new GenerationReport[Battery.Runs];
                var watch = Stopwatch.StartNew();
                var result = new EvolutionBattery(settings, Battery).Run((run, report) =>
                {
                    if (report.Evaluation.Mode == EvaluationMode.Training)
                    {
                        training += report.Evaluation.Episodes.Count;
                        if (report.Evaluation.Generation == Battery.Generations - 1)
                            finals[run] = report;
                    }
                    else
                    {
                        validation += report.Evaluation.Episodes.Count;
                    }

                    onReport?.Invoke(cell, run, report);
                });

                Spread? lesioned = null;
                if (cell.Plastic)
                    lesioned = new Spread(Lesion(settings, finals));
                outcomes.Add(new PlasticityCellOutcome(cell, result, training, validation, lesioned,
                    watch.Elapsed.TotalSeconds));
            }

            return new PlasticityComparisonResult(Battery, FoodShiftSeconds, outcomes);
        }

        /// <summary>
        /// Validation median of each run's final population with learning switched off: the same genomes with fixed
        /// coefficients, on the same validation seeds. Read-only, like any validation.
        /// </summary>
        public static float[] Lesion(EvolutionSettings settings, IReadOnlyList<GenerationReport> finals)
        {
            var plan = new EvaluationPlan(settings);
            var medians = new float[finals.Count];
            for (var r = 0; r < finals.Count; r++)
            {
                var final = finals[r] ?? throw new ArgumentException($"Run {r} has no final generation.", nameof(finals));
                var genomes = new Genome[final.Individuals.Count];
                for (var i = 0; i < genomes.Length; i++)
                    genomes[i] = final.Individuals[i].Genome.WithPlasticity(PlasticityGenes.Fixed);
                var evaluation = new IsolatedEvaluator(plan).EvaluateValidation(genomes, final.Evaluation.Generation);
                medians[r] = new GenerationReport(evaluation, final.Individuals).Summary.Median;
            }

            return medians;
        }
    }

    public readonly struct PlasticityCell : IEquatable<PlasticityCell>
    {
        public PlasticityCell(bool changing, bool plastic)
        {
            Changing = changing;
            Plastic = plastic;
        }

        /// <summary>Food moves from the left to the right half of the arena during each episode.</summary>
        public bool Changing { get; }

        /// <summary>Networks may change during a life.</summary>
        public bool Plastic { get; }

        public string Name => (Changing ? "changing" : "static") + "-" + (Plastic ? "plastic" : "fixed");

        public bool Equals(PlasticityCell other) => Changing == other.Changing && Plastic == other.Plastic;
        public override bool Equals(object obj) => obj is PlasticityCell other && Equals(other);
        public override int GetHashCode() => (Changing ? 2 : 0) + (Plastic ? 1 : 0);
        public override string ToString() => Name;
    }

    /// <summary>One cell's battery, its cost and, for plastic networks, the lesioned validation median across runs.</summary>
    public sealed class PlasticityCellOutcome
    {
        public PlasticityCellOutcome(PlasticityCell cell, BatteryResult result, long trainingEpisodes, long validationEpisodes,
            Spread? lesioned, double seconds)
        {
            Cell = cell;
            Result = result ?? throw new ArgumentNullException(nameof(result));
            TrainingEpisodes = trainingEpisodes;
            ValidationEpisodes = validationEpisodes;
            Lesioned = lesioned;
            Seconds = seconds;
        }

        public PlasticityCell Cell { get; }
        public BatteryResult Result { get; }
        public long TrainingEpisodes { get; }
        public long ValidationEpisodes { get; }

        /// <summary>Final validation median with learning switched off; null for fixed networks.</summary>
        public Spread? Lesioned { get; }

        public double Seconds { get; }
    }

    /// <summary>
    /// The four cells side by side. An effect counts only when the mean ± standard deviation intervals across runs do
    /// not overlap (at least two runs); anything else is within the variation between runs.
    /// </summary>
    public sealed class PlasticityComparisonResult
    {
        public PlasticityComparisonResult(BatterySettings battery, float foodShiftSeconds,
            IReadOnlyList<PlasticityCellOutcome> outcomes)
        {
            Battery = battery ?? throw new ArgumentNullException(nameof(battery));
            if (outcomes == null || outcomes.Count != PlasticityComparison.Cells.Count)
                throw new ArgumentException("Every cell needs an outcome.", nameof(outcomes));
            FoodShiftSeconds = foodShiftSeconds;
            Outcomes = outcomes;
        }

        public BatterySettings Battery { get; }
        public float FoodShiftSeconds { get; }
        public IReadOnlyList<PlasticityCellOutcome> Outcomes { get; }
        public bool IsPreliminary => Outcomes[0].Result.IsPreliminary;

        public PlasticityCellOutcome Get(bool changing, bool plastic)
        {
            foreach (var outcome in Outcomes)
            {
                if (outcome.Cell.Changing == changing && outcome.Cell.Plastic == plastic)
                    return outcome;
            }

            throw new InvalidOperationException("Missing cell.");
        }

        /// <summary>Plastic minus fixed final validation median (means across runs) in one world.</summary>
        public float LearningEffect(bool changing) =>
            Get(changing, true).Result.FinalValidationMedian.Mean - Get(changing, false).Result.FinalValidationMedian.Mean;

        public bool IsLearningEffectSeparated(bool changing) =>
            IsSeparated(Get(changing, true).Result.FinalValidationMedian, Get(changing, false).Result.FinalValidationMedian);

        /// <summary>Final plastic validation median minus the same population evaluated without learning.</summary>
        public float LesionEffect(bool changing)
        {
            var plastic = Get(changing, true);
            return plastic.Result.FinalValidationMedian.Mean - plastic.Lesioned.Value.Mean;
        }

        public bool IsLesionEffectSeparated(bool changing)
        {
            var plastic = Get(changing, true);
            return IsSeparated(plastic.Result.FinalValidationMedian, plastic.Lesioned.Value);
        }

        public bool IsSeparated(Spread a, Spread b) =>
            Battery.Runs >= 2 && (a.Mean + a.StdDev < b.Mean - b.StdDev || b.Mean + b.StdDev < a.Mean - a.StdDev);
    }
}
