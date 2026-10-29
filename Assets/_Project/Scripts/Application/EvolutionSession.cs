using System;
using System.Collections.Generic;
using Eden.Brains;
using Eden.Domain;
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

        public EvolutionSession(EvolutionSettings settings, int validationInterval = DefaultValidationInterval,
            int evaluationWorkers = 1)
            : this(new GenerationalEvolution(settings), validationInterval, evaluationWorkers)
        {
            BeginTraining();
        }

        /// <summary>A new session whose evolution starts from an imported pool; see <see cref="GenerationalEvolution.FromSeedPool"/>.</summary>
        public static EvolutionSession FromSeedPool(EvolutionSettings settings, IReadOnlyList<Genome> pool,
            IReadOnlyList<float> poolFitness, int validationInterval = DefaultValidationInterval, int evaluationWorkers = 1)
        {
            var session = new EvolutionSession(GenerationalEvolution.FromSeedPool(settings, pool, poolFitness),
                validationInterval, evaluationWorkers);
            session.BeginTraining();
            return session;
        }

        private EvolutionSession(GenerationalEvolution evolution, int validationInterval, int evaluationWorkers = 1)
        {
            if (evaluationWorkers < 1 || evaluationWorkers > EvaluationJob.MaxWorkers)
                throw new ArgumentOutOfRangeException(nameof(evaluationWorkers));
            EvaluationWorkers = evaluationWorkers;
            if (validationInterval < 1)
                throw new ArgumentOutOfRangeException(nameof(validationInterval), "Must be at least 1.");
            Evolution = evolution;
            evaluator = new IsolatedEvaluator(Evolution.Plan);
            ValidationInterval = validationInterval;
        }

        /// <summary>
        /// Rebuilds a session saved between ticks; advancing it gives exactly what advancing the original would have.
        /// The evolution, the evaluation in progress and the history are checked against each other first, and on any
        /// problem nothing is built and an <see cref="ArgumentException"/> explains why.
        /// </summary>
        public static EvolutionSession Restore(EvolutionSnapshot saved)
        {
            if (saved == null)
                throw new ArgumentNullException(nameof(saved));

            var previous = saved.Generation > 0 ? saved.LastTrainingIndividuals : null;
            var evolution = GenerationalEvolution.Restore(saved.Settings, saved.Generation, saved.Population,
                Lineage.Restore(saved.Lineage), previous, saved.Neat);
            var session = new EvolutionSession(evolution, saved.ValidationInterval, saved.Job.WorkerCount);
            var plan = evolution.Plan;
            var ticksPerSecond = plan.EpisodeConfig(plan.ValidationSeed).TicksPerSecond;

            if (previous != null)
            {
                if (saved.LastTrainingEpisodes == null)
                    throw new ArgumentException("The last training evaluation is missing.", nameof(saved));
                var last = saved.Generation - 1;
                var lastEvaluation = GenerationEvaluation.FromEpisodes(last, EvaluationMode.Training, plan.TrainingSeeds(last),
                    saved.LastTrainingEpisodes, ticksPerSecond, plan.Profile);
                session.LastTraining = evolution.LastTrainingSpecies == null
                    ? new GenerationReport(lastEvaluation, previous)
                    : new GenerationReport(lastEvaluation, previous, evolution.LastTrainingSpecies);
            }

            if (saved.BaselineEpisodes != null)
            {
                var baselines = GenerationEvaluation.FromEpisodes(0, EvaluationMode.Validation, plan.ValidationSeeds(),
                    saved.BaselineEpisodes, ticksPerSecond, plan.Profile);
                if (baselines.Candidates.Count != 2)
                    throw new ArgumentException("Baselines are the random and the heuristic brain.", nameof(saved));
                session.RandomBaseline = baselines.Candidates[0];
                session.HeuristicBaseline = baselines.Candidates[1];
            }

            CheckHistory(saved);
            session.training.AddRange(saved.Training);
            session.validation.AddRange(saved.Validation);
            session.TotalTicks = saved.TotalTicks;

            switch (saved.Phase)
            {
                case EvolutionPhase.Training:
                    session.BeginTraining();
                    break;
                case EvolutionPhase.Baselines when session.LastTraining != null && session.HeuristicBaseline == null:
                    session.BeginBaselines();
                    break;
                case EvolutionPhase.Validation when session.LastTraining != null && session.HeuristicBaseline != null:
                    session.BeginValidation();
                    break;
                default:
                    throw new ArgumentException($"Phase {saved.Phase} does not fit the saved evolution.", nameof(saved));
            }

            session.Job.Resume(saved.Job);
            return session;
        }

        public GenerationalEvolution Evolution { get; }
        public EvaluationPlan Plan => Evolution.Plan;
        public int ValidationInterval { get; }
        public int EvaluationWorkers { get; }
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

        /// <summary>Episodes finished by this object (not saved: a restored session counts from its restore).</summary>
        public long EpisodesRun { get; private set; }

        /// <summary>Raised with every training and validation report as soon as it is complete.</summary>
        public event Action<GenerationReport> ReportCompleted;

        /// <summary>Detached copy of the session between ticks, for <see cref="Restore"/>.</summary>
        public EvolutionSnapshot Capture()
        {
            EpisodeResult[] baselines = null;
            if (HeuristicBaseline != null)
            {
                var episodes = new List<EpisodeResult>(RandomBaseline.Episodes);
                episodes.AddRange(HeuristicBaseline.Episodes);
                baselines = episodes.ToArray();
            }

            return new EvolutionSnapshot(Evolution.Settings, ValidationInterval, Evolution.Generation,
                Evolution.Individuals, Evolution.Lineage.Entries(), LastTraining?.Individuals,
                LastTraining?.Evaluation.Episodes, baselines, training, validation, TotalTicks, Phase, Job.Capture(),
                Evolution.CaptureNeat());
        }

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
                var completed = Job.CompletedEpisodes;
                ran += Job.Advance(maxTicks - ran);
                EpisodesRun += Job.CompletedEpisodes - completed;
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
            Job.WorkerCount = EvaluationWorkers;
        }

        private void BeginBaselines()
        {
            Phase = EvolutionPhase.Baselines;
            jobIndividuals = null;
            Job = evaluator.Job(new Func<IBrain>[] { () => new RandomBrain(), () => new HeuristicBrain() },
                Plan.ValidationSeeds(), 0, EvaluationMode.Validation);
            Job.WorkerCount = EvaluationWorkers;
        }

        private void BeginValidation()
        {
            Phase = EvolutionPhase.Validation;
            jobIndividuals = LastTraining.Individuals;
            Job = Evolution.BeginValidation(LastTraining);
            Job.WorkerCount = EvaluationWorkers;
        }

        // One training summary per finished generation and validation points in order, as a running session has them.
        private static void CheckHistory(EvolutionSnapshot saved)
        {
            if (saved.Training.Count != saved.Generation || saved.TotalTicks < 0)
                throw new ArgumentException($"Expected {saved.Generation} training summaries.", nameof(saved));
            for (var g = 0; g < saved.Training.Count; g++)
            {
                if (saved.Training[g].Generation != g || saved.Training[g].Mode != EvaluationMode.Training)
                    throw new ArgumentException($"Training summary {g} is out of place.", nameof(saved));
            }

            var previous = -1;
            foreach (var point in saved.Validation)
            {
                if (point.Generation <= previous || point.Generation >= saved.Generation ||
                    point.Generation % saved.ValidationInterval != 0)
                    throw new ArgumentException($"Validation of generation {point.Generation} is out of place.", nameof(saved));
                previous = point.Generation;
            }
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
