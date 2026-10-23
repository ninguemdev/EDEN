using System;
using System.Collections.Generic;
using System.Diagnostics;
using Eden.Brains;
using Eden.Evolution;

namespace Eden.Application
{
    /// <summary>
    /// Runs a <see cref="BatterySettings"/> battery: independent generational runs (experiment seeds base, base + 1, …)
    /// that share the validation seeds, plus the random and heuristic brains on the same validation seeds and number
    /// of episodes. Validation only observes: each run evolves exactly as it would without it.
    /// </summary>
    public sealed class EvolutionBattery
    {
        private readonly EvolutionSettings settings;
        private readonly BatterySettings battery;

        public EvolutionBattery(EvolutionSettings settings, BatterySettings battery)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.battery = battery ?? throw new ArgumentNullException(nameof(battery));
            battery.Validate();
            Plan = new EvaluationPlan(settings);
        }

        /// <summary>Plan of the first run; validation seeds and episode rules are the same for every run.</summary>
        public EvaluationPlan Plan { get; }

        public ulong RunSeed(int run) => settings.ExperimentSeed + (ulong)run;

        /// <summary>
        /// Runs the whole battery. <paramref name="onReport"/> receives every training and validation report as it is
        /// produced, with the run index, so callers can export or log progress.
        /// </summary>
        public BatteryResult Run(Action<int, GenerationReport> onReport = null)
        {
            var baselines = new IsolatedEvaluator(Plan).Evaluate(
                new Func<IBrain>[] { () => new RandomBrain(), () => new HeuristicBrain() },
                Plan.ValidationSeeds(), 0, EvaluationMode.Validation);

            var runs = new List<ExperimentRun>(battery.Runs);
            for (var r = 0; r < battery.Runs; r++)
                runs.Add(RunOne(r, onReport));

            return new BatteryResult(battery, Plan, runs, baselines.Candidates[0], baselines.Candidates[1]);
        }

        private ExperimentRun RunOne(int run, Action<int, GenerationReport> onReport)
        {
            var watch = Stopwatch.StartNew();
            var evolution = new GenerationalEvolution(settings.WithExperimentSeed(RunSeed(run)));
            var points = new List<ValidationPoint>();
            for (var g = 0; g < battery.Generations; g++)
            {
                var training = evolution.Advance();
                onReport?.Invoke(run, training);
                if (!battery.IsValidated(g))
                    continue;

                var validation = evolution.Validate(training);
                onReport?.Invoke(run, validation);
                var champion = IndexOf(training.Individuals, training.Summary.BestIndividualId);
                points.Add(new ValidationPoint(training.Summary, validation.Summary, training.Summary.BestIndividualId,
                    validation.Evaluation.Candidates[champion]));
            }

            return new ExperimentRun(run, RunSeed(run), points, watch.Elapsed.TotalSeconds);
        }

        private static int IndexOf(IReadOnlyList<Individual> individuals, long id)
        {
            for (var i = 0; i < individuals.Count; i++)
            {
                if (individuals[i].Id == id)
                    return i;
            }

            throw new InvalidOperationException($"Individual {id} was not evaluated.");
        }
    }
}
