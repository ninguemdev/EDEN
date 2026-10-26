using System;
using System.Collections.Generic;
using System.Diagnostics;
using Eden.Evolution;

namespace Eden.Application
{
    /// <summary>
    /// The same battery (experiment seeds, validation seeds, generations and episode world) run once per preset, so
    /// that the presets differ only in the number of genomes per generation. A larger population also evaluates more
    /// episodes per generation, so the comparison reports how many each preset used: equal generations are not an equal
    /// budget.
    /// </summary>
    public sealed class PresetComparison
    {
        private readonly EvolutionSettings template;
        private readonly BatterySettings battery;

        public PresetComparison(EvolutionSettings template, BatterySettings battery, IReadOnlyList<ExperimentPreset> presets)
        {
            this.template = template ?? throw new ArgumentNullException(nameof(template));
            this.battery = battery ?? throw new ArgumentNullException(nameof(battery));
            if (presets == null || presets.Count == 0)
                throw new ArgumentException("At least one preset is required.", nameof(presets));
            var names = new HashSet<string>();
            foreach (var preset in presets)
            {
                if (preset == null || !names.Add(preset.Name))
                    throw new ArgumentException("Presets must be distinct.", nameof(presets));
            }

            battery.Validate();
            Presets = presets;
            foreach (var preset in presets)
                new ReproductionRules(Settings(preset));
        }

        public IReadOnlyList<ExperimentPreset> Presets { get; }
        public BatterySettings Battery => battery;

        /// <summary>The template with the preset's population; everything else is shared by all presets.</summary>
        public EvolutionSettings Settings(ExperimentPreset preset)
        {
            if (preset == null)
                throw new ArgumentNullException(nameof(preset));
            var settings = template.WithExperimentSeed(template.ExperimentSeed);
            settings.PopulationSize = preset.Population;
            return settings;
        }

        /// <summary>
        /// Runs every preset in order. <paramref name="onReport"/> receives each training and validation report with its
        /// preset and run index, for exports and progress logs.
        /// </summary>
        public PresetComparisonResult Run(Action<ExperimentPreset, int, GenerationReport> onReport = null)
        {
            var outcomes = new List<PresetOutcome>(Presets.Count);
            foreach (var preset in Presets)
            {
                var training = 0L;
                var validation = 0L;
                var watch = Stopwatch.StartNew();
                var result = new EvolutionBattery(Settings(preset), battery).Run((run, report) =>
                {
                    if (report.Evaluation.Mode == EvaluationMode.Training)
                        training += report.Evaluation.Episodes.Count;
                    else
                        validation += report.Evaluation.Episodes.Count;
                    onReport?.Invoke(preset, run, report);
                });
                outcomes.Add(new PresetOutcome(preset, result, training, validation, watch.Elapsed.TotalSeconds));
            }

            return new PresetComparisonResult(battery, outcomes);
        }
    }

    /// <summary>One preset's battery and what it cost: episodes evaluated (deterministic) and wall-clock seconds (informative).</summary>
    public sealed class PresetOutcome
    {
        public PresetOutcome(ExperimentPreset preset, BatteryResult result, long trainingEpisodes, long validationEpisodes,
            double seconds)
        {
            Preset = preset ?? throw new ArgumentNullException(nameof(preset));
            Result = result ?? throw new ArgumentNullException(nameof(result));
            TrainingEpisodes = trainingEpisodes;
            ValidationEpisodes = validationEpisodes;
            Seconds = seconds;
        }

        public ExperimentPreset Preset { get; }
        public BatteryResult Result { get; }

        /// <summary>Training episodes over all runs: runs × generations × population × training seeds.</summary>
        public long TrainingEpisodes { get; }

        /// <summary>Validation episodes over all runs, reference brains excluded.</summary>
        public long ValidationEpisodes { get; }

        public double Seconds { get; }
    }

    /// <summary>
    /// Presets side by side on the same validation seeds. <see cref="Best"/> is the preset with the highest mean final
    /// validation median; <see cref="IsSeparated"/> says whether its runs are clearly apart from every other preset's
    /// (mean ± standard deviation intervals that do not overlap, with at least two runs) or the difference is within
    /// the spread across runs.
    /// </summary>
    public sealed class PresetComparisonResult
    {
        public PresetComparisonResult(BatterySettings battery, IReadOnlyList<PresetOutcome> outcomes)
        {
            Battery = battery ?? throw new ArgumentNullException(nameof(battery));
            if (outcomes == null || outcomes.Count == 0)
                throw new ArgumentException("At least one preset outcome is required.", nameof(outcomes));
            Outcomes = outcomes;

            Best = outcomes[0];
            foreach (var outcome in outcomes)
            {
                if (outcome.Result.FinalValidationMedian.Mean > Best.Result.FinalValidationMedian.Mean)
                    Best = outcome;
            }

            var best = Best.Result.FinalValidationMedian;
            // A single run has no spread to compare against.
            IsSeparated = outcomes.Count > 1 && battery.Runs > 1;
            foreach (var outcome in outcomes)
            {
                var other = outcome.Result.FinalValidationMedian;
                if (outcome != Best && other.Mean + other.StdDev >= best.Mean - best.StdDev)
                    IsSeparated = false;
            }
        }

        public BatterySettings Battery { get; }
        public IReadOnlyList<PresetOutcome> Outcomes { get; }
        public PresetOutcome Best { get; }
        public bool IsSeparated { get; }
        public bool IsPreliminary => Outcomes[0].Result.IsPreliminary;
    }
}
