using System;
using System.Collections.Generic;

namespace Eden.Evolution
{
    /// <summary>
    /// Independent runs of one experiment configuration, each with its own experiment seed, all validated on the
    /// same held-out seeds. The proposed acceptance budget is 5 runs × 30 generations with 3 training and 5
    /// validation seeds; anything smaller is labelled preliminary.
    /// </summary>
    [Serializable]
    public sealed class BatterySettings
    {
        public const int AcceptanceRuns = 5;
        public const int AcceptanceGenerations = 30;
        public const int AcceptanceTrainingSeeds = 3;
        public const int AcceptanceValidationSeeds = 5;

        /// <summary>Run i uses experiment seed (base seed + i).</summary>
        public int Runs = AcceptanceRuns;

        public int Generations = AcceptanceGenerations;

        /// <summary>Whole population validated every this many generations, plus generation 0 and the last one.</summary>
        public int ValidationInterval = 5;

        public void Validate()
        {
            var errors = new List<string>();
            if (Runs < 1 || Runs > 1000)
                errors.Add($"{nameof(Runs)} must be in [1, 1000] (was {Runs})");
            if (Generations < 1 || Generations > 100000)
                errors.Add($"{nameof(Generations)} must be in [1, 100000] (was {Generations})");
            if (ValidationInterval < 1)
                errors.Add($"{nameof(ValidationInterval)} must be >= 1 (was {ValidationInterval})");
            if (errors.Count > 0)
                throw new ArgumentException("Invalid battery settings: " + string.Join("; ", errors));
        }

        public bool IsValidated(int generation) =>
            generation % ValidationInterval == 0 || generation == Generations - 1;

        public bool IsPreliminary(EvaluationPlan plan) =>
            Runs < AcceptanceRuns || Generations < AcceptanceGenerations ||
            plan.TrainingSeedsPerGeneration < AcceptanceTrainingSeeds ||
            plan.ValidationSeedCount < AcceptanceValidationSeeds;
    }

    /// <summary>
    /// One validated generation of a run. The champion is the individual selection would rank first (best training
    /// fitness), so its validation score is an honest estimate; the best validation score is not, because it is
    /// picked by the validation itself.
    /// </summary>
    public sealed class ValidationPoint
    {
        public ValidationPoint(GenerationSummary training, GenerationSummary validation, long championId,
            CandidateEvaluation championValidation)
            : this(training, validation, championId,
                championValidation?.MeanFitness ?? throw new ArgumentNullException(nameof(championValidation)),
                championValidation.SurvivalRate)
        {
        }

        /// <summary>A point as it was saved: the champion's validation results as values.</summary>
        public ValidationPoint(GenerationSummary training, GenerationSummary validation, long championId,
            float championFitness, float championSurvivalRate)
        {
            Training = training ?? throw new ArgumentNullException(nameof(training));
            Validation = validation ?? throw new ArgumentNullException(nameof(validation));
            if (training.Generation != validation.Generation || training.Mode != EvaluationMode.Training ||
                validation.Mode != EvaluationMode.Validation)
                throw new ArgumentException("Validation must cover the same generation.", nameof(validation));
            ChampionId = championId;
            ChampionFitness = championFitness;
            ChampionSurvivalRate = championSurvivalRate;
        }

        public int Generation => Training.Generation;
        public GenerationSummary Training { get; }
        public GenerationSummary Validation { get; }
        public long ChampionId { get; }
        public float ChampionFitness { get; }
        public float ChampionSurvivalRate { get; }
    }

    /// <summary>All validated generations of one run, in order, and its wall-clock cost (reported, never used).</summary>
    public sealed class ExperimentRun
    {
        public ExperimentRun(int index, ulong experimentSeed, IReadOnlyList<ValidationPoint> points, double seconds)
        {
            if (points == null || points.Count == 0)
                throw new ArgumentException("A run needs at least one validated generation.", nameof(points));
            Index = index;
            ExperimentSeed = experimentSeed;
            Points = points;
            Seconds = seconds;
        }

        public int Index { get; }
        public ulong ExperimentSeed { get; }
        public IReadOnlyList<ValidationPoint> Points { get; }
        public double Seconds { get; }
        public ValidationPoint Initial => Points[0];
        public ValidationPoint Final => Points[Points.Count - 1];
    }

    /// <summary>Mean, sample standard deviation, minimum and maximum of one value across runs.</summary>
    public readonly struct Spread
    {
        public Spread(IReadOnlyList<float> values)
        {
            if (values == null || values.Count == 0)
                throw new ArgumentException("At least one value is required.", nameof(values));
            double sum = 0;
            var min = float.PositiveInfinity;
            var max = float.NegativeInfinity;
            foreach (var value in values)
            {
                sum += value;
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            var mean = sum / values.Count;
            double squares = 0;
            foreach (var value in values)
                squares += (value - mean) * (value - mean);

            Count = values.Count;
            Mean = (float)mean;
            StdDev = values.Count > 1 ? (float)Math.Sqrt(squares / (values.Count - 1)) : 0f;
            Min = min;
            Max = max;
        }

        public int Count { get; }
        public float Mean { get; }
        public float StdDev { get; }
        public float Min { get; }
        public float Max { get; }
    }

    /// <summary>
    /// Outcome of a battery on the validation seeds, compared with the reference brains on the same seeds and the
    /// same number of episodes. Comparisons are counted per run with a strict "greater than", so a missing gain shows
    /// up as a low count instead of being averaged away.
    /// </summary>
    public sealed class BatteryResult
    {
        public BatteryResult(BatterySettings settings, EvaluationPlan plan, IReadOnlyList<ExperimentRun> runs,
            CandidateEvaluation randomBaseline, CandidateEvaluation heuristicBaseline)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (plan == null)
                throw new ArgumentNullException(nameof(plan));
            if (runs == null || runs.Count == 0)
                throw new ArgumentException("At least one run is required.", nameof(runs));

            Settings = settings;
            Plan = plan;
            Runs = runs;
            RandomBaseline = randomBaseline ?? throw new ArgumentNullException(nameof(randomBaseline));
            HeuristicBaseline = heuristicBaseline ?? throw new ArgumentNullException(nameof(heuristicBaseline));
            IsPreliminary = settings.IsPreliminary(plan);

            var finalMedian = new float[runs.Count];
            var finalChampion = new float[runs.Count];
            var initialMedian = new float[runs.Count];
            var finalTrainingMedian = new float[runs.Count];
            var finalSurvival = new float[runs.Count];
            for (var r = 0; r < runs.Count; r++)
            {
                var final = runs[r].Final;
                finalMedian[r] = final.Validation.Median;
                finalChampion[r] = final.ChampionFitness;
                initialMedian[r] = runs[r].Initial.Validation.Median;
                finalTrainingMedian[r] = final.Training.Median;
                finalSurvival[r] = final.Validation.MeanSurvivalRate;
                if (finalMedian[r] > randomBaseline.MeanFitness)
                    MedianBeatsRandom++;
                if (finalMedian[r] > heuristicBaseline.MeanFitness)
                    MedianBeatsHeuristic++;
                if (finalChampion[r] > randomBaseline.MeanFitness)
                    ChampionBeatsRandom++;
                if (finalChampion[r] > heuristicBaseline.MeanFitness)
                    ChampionBeatsHeuristic++;
                if (runs[r].Points.Count > 1 && finalMedian[r] > initialMedian[r])
                    MedianImproved++;
            }

            FinalValidationMedian = new Spread(finalMedian);
            FinalChampion = new Spread(finalChampion);
            InitialValidationMedian = new Spread(initialMedian);
            FinalTrainingMedian = new Spread(finalTrainingMedian);
            FinalValidationSurvival = new Spread(finalSurvival);
        }

        public BatterySettings Settings { get; }
        public EvaluationPlan Plan { get; }
        public IReadOnlyList<ExperimentRun> Runs { get; }
        public CandidateEvaluation RandomBaseline { get; }
        public CandidateEvaluation HeuristicBaseline { get; }

        /// <summary>True when the budget is below the proposed acceptance battery.</summary>
        public bool IsPreliminary { get; }

        public Spread FinalValidationMedian { get; }
        public Spread FinalChampion { get; }
        public Spread InitialValidationMedian { get; }
        public Spread FinalTrainingMedian { get; }
        public Spread FinalValidationSurvival { get; }

        public int MedianBeatsRandom { get; }
        public int MedianBeatsHeuristic { get; }
        public int ChampionBeatsRandom { get; }
        public int ChampionBeatsHeuristic { get; }

        /// <summary>Runs whose final validation median is above their generation-0 validation median.</summary>
        public int MedianImproved { get; }
    }
}
