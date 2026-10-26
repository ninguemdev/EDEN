using System;
using System.Collections.Generic;
using Eden.Brains;
using Eden.Evolution;

namespace Eden.Application
{
    /// <summary>What an <see cref="EvolutionSession"/> is evaluating right now.</summary>
    public enum EvolutionPhase
    {
        /// <summary>The current generation on its training seeds; the only results selection uses.</summary>
        Training = 0,

        /// <summary>The random and heuristic brains on the validation seeds, once, before the first validation.</summary>
        Baselines = 1,

        /// <summary>The generation just trained, on the held-out validation seeds; never fed back to selection.</summary>
        Validation = 2
    }

    /// <summary>
    /// Open-ended generational evolution advanced a bounded number of ticks at a time, so that the lab can run it
    /// inside frames and show the episode being evaluated. Every <see cref="ValidationInterval"/> generations
    /// (starting with generation 0) the trained generation is also validated, and before the first validation the
    /// reference brains are evaluated on the same validation seeds. The generations, ids and parents are exactly
    /// those of <see cref="GenerationalEvolution.Advance()"/> with the same settings, however the ticks are split.
    /// </summary>
    public sealed class EvolutionSession
    {
        public const int DefaultValidationInterval = 5;

        private readonly IsolatedEvaluator evaluator;
        private readonly List<GenerationSummary> training = new List<GenerationSummary>();
        private readonly List<ValidationPoint> validation = new List<ValidationPoint>();
        private IReadOnlyList<Individual> jobIndividuals;

        public EvolutionSession(EvolutionSettings settings, int validationInterval = DefaultValidationInterval)
        {
            if (validationInterval < 1)
                throw new ArgumentOutOfRangeException(nameof(validationInterval), "Must be at least 1.");
            Evolution = new GenerationalEvolution(settings);
            evaluator = new IsolatedEvaluator(Evolution.Plan);
            ValidationInterval = validationInterval;
            BeginTraining();
        }

        public GenerationalEvolution Evolution { get; }
        public EvaluationPlan Plan => Evolution.Plan;
        public int ValidationInterval { get; }
        public EvolutionPhase Phase { get; private set; }

        /// <summary>Evaluation in progress; a new one starts as soon as the previous one completes.</summary>
        public EvaluationJob Job { get; private set; }

        /// <summary>Individual of the episode being simulated; null while the reference brains run.</summary>
        public Individual CurrentIndividual => jobIndividuals?[Job.Current.Candidate];

        /// <summary>Brain name of the reference episode being simulated; null outside <see cref="EvolutionPhase.Baselines"/>.</summary>
        public string CurrentReference =>
            Phase != EvolutionPhase.Baselines ? null
            : Job.Current.Candidate == 0 ? RandomBrain.BrainName
            : HeuristicBrain.BrainName;

        /// <summary>Training summary of every completed generation, in order.</summary>
        public IReadOnlyList<GenerationSummary> Training => training;

        /// <summary>Every validated generation, in order.</summary>
        public IReadOnlyList<ValidationPoint> Validation => validation;

        /// <summary>Random brain on the validation seeds; null until the first validation.</summary>
        public CandidateEvaluation RandomBaseline { get; private set; }

        /// <summary>Heuristic brain on the validation seeds; null until the first validation.</summary>
        public CandidateEvaluation HeuristicBaseline { get; private set; }

        /// <summary>Training report of the last completed generation; null before the first one.</summary>
        public GenerationReport LastTraining { get; private set; }

        /// <summary>Ticks simulated over all episodes of the session.</summary>
        public long TotalTicks { get; private set; }

        /// <summary>Raised with every training and validation report as soon as it is complete.</summary>
        public event Action<GenerationReport> ReportCompleted;

        /// <summary>
        /// Simulates up to <paramref name="maxTicks"/> ticks, completing evaluations and generations on the way, and
        /// returns how many ran.
        /// </summary>
        public long Advance(long maxTicks)
        {
            if (maxTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(maxTicks));
            var ran = 0L;
            while (ran < maxTicks)
            {
                ran += Job.Advance(maxTicks - ran);
                if (!Job.IsComplete)
                    break;
                CompleteJob();
            }

            TotalTicks += ran;
            return ran;
        }

        private void CompleteJob()
        {
            switch (Phase)
            {
                case EvolutionPhase.Training:
                    LastTraining = Evolution.Advance(Job.Result);
                    training.Add(LastTraining.Summary);
                    ReportCompleted?.Invoke(LastTraining);
                    if (LastTraining.Summary.Generation % ValidationInterval != 0)
                        BeginTraining();
                    else if (HeuristicBaseline == null)
                        BeginBaselines();
                    else
                        BeginValidation();
                    break;
                case EvolutionPhase.Baselines:
                    RandomBaseline = Job.Result.Candidates[0];
                    HeuristicBaseline = Job.Result.Candidates[1];
                    BeginValidation();
                    break;
                default:
                    var report = Evolution.Validate(LastTraining, Job.Result);
                    var championId = LastTraining.Summary.BestIndividualId;
                    validation.Add(new ValidationPoint(LastTraining.Summary, report.Summary, championId,
                        report.Evaluation.Candidates[IndexOf(report.Individuals, championId)]));
                    ReportCompleted?.Invoke(report);
                    BeginTraining();
                    break;
            }
        }

        private void BeginTraining()
        {
            Phase = EvolutionPhase.Training;
            jobIndividuals = Evolution.Individuals;
            Job = Evolution.BeginTraining();
        }

        private void BeginBaselines()
        {
            Phase = EvolutionPhase.Baselines;
            jobIndividuals = null;
            Job = evaluator.Job(new Func<IBrain>[] { () => new RandomBrain(), () => new HeuristicBrain() },
                Plan.ValidationSeeds(), 0, EvaluationMode.Validation);
        }

        private void BeginValidation()
        {
            Phase = EvolutionPhase.Validation;
            jobIndividuals = LastTraining.Individuals;
            Job = Evolution.BeginValidation(LastTraining);
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
