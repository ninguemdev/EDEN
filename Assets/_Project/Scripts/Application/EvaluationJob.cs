using System;
using System.Collections.Generic;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Simulation;

namespace Eden.Application
{
    /// <summary>
    /// One isolated episode that can be advanced a few ticks at a time: a world built from the episode seed with a
    /// single creature, whose body the plan expresses from the candidate's body genes. It finishes after the configured
    /// duration or when the creature dies; the remaining ticks cannot change the result, so they are not simulated,
    /// but the episode still counts as its configured duration.
    /// </summary>
    public sealed class IsolatedEpisode
    {
        private readonly EvaluationPlan plan;
        private readonly EvaluationMode mode;
        private readonly int generation;

        public IsolatedEpisode(EvaluationPlan plan, Func<IBrain> createBrain, ulong episodeSeed, int candidate,
            int seedIndex, int generation, EvaluationMode mode, BodyGenes body = default)
            : this(plan, createBrain, episodeSeed, candidate, seedIndex, generation, mode, body, null, null)
        {
        }

        internal IsolatedEpisode(EvaluationPlan plan, Func<IBrain> createBrain, ulong episodeSeed, int candidate,
            int seedIndex, int generation, EvaluationMode mode, BodyGenes body, SimulationSnapshot savedWorld,
            IReadOnlyList<BrainSlotState> savedBrains)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            if (createBrain == null)
                throw new ArgumentNullException(nameof(createBrain));

            var config = plan.EpisodeConfig(episodeSeed);
            var expressed = plan.ExpressedGenes(body);
            Simulation = savedWorld == null
                ? new WorldSimulation(WorldFactory.CreateInitial(config, _ => expressed))
                : WorldSimulation.Restore(config, savedWorld);
            if (Simulation.World.Creatures.Count != 1 || Simulation.World.Creatures[0].Id != new CreatureId(1) ||
                Tick >= plan.EpisodeTicks)
                throw new ArgumentException("A running episode has its single creature alive and ticks left.",
                    nameof(savedWorld));
            if (!Simulation.World.Creatures[0].Genes.Equals(expressed))
                throw new ArgumentException($"The saved creature has body genes {Simulation.World.Creatures[0].Genes}; " +
                                            $"the candidate expresses {expressed}.", nameof(savedWorld));
            Creature = Simulation.World.Creatures[0];
            Controller = new BrainController(config, _ => createBrain());
            if (savedBrains != null)
                Controller.Restore(savedBrains);
            EpisodeSeed = episodeSeed;
            Candidate = candidate;
            SeedIndex = seedIndex;
            this.generation = generation;
            this.mode = mode;
        }

        public WorldSimulation Simulation { get; }
        public BrainController Controller { get; }

        /// <summary>The only creature of the episode; it stays readable after its death.</summary>
        public CreatureState Creature { get; }

        public ulong EpisodeSeed { get; }
        public int Candidate { get; }
        public int SeedIndex { get; }

        /// <summary>Ticks simulated so far (the episode world starts at tick 0).</summary>
        public long Tick => Simulation.World.Tick;

        public bool IsFinished => Tick >= plan.EpisodeTicks || Simulation.IsExtinct;

        /// <summary>Simulates up to <paramref name="maxTicks"/> ticks and returns how many ran.</summary>
        public long Advance(long maxTicks)
        {
            var ran = 0L;
            while (ran < maxTicks && !IsFinished)
            {
                Simulation.Step(Controller);
                ran++;
            }

            return ran;
        }

        public EpisodeResult Result()
        {
            if (!IsFinished)
                throw new InvalidOperationException("The episode is still running.");
            return new EpisodeResult(mode, generation, Candidate, SeedIndex, EpisodeSeed, plan.EpisodeTicks,
                plan.EpisodeSeconds, Creature.FoodEnergyEaten, Creature.AgeTicks, Creature.IsAlive, Creature.Energy);
        }
    }

    /// <summary>
    /// Every candidate × seed episode of one evaluation, run in order (candidate-major, seed-minor) a bounded number
    /// of ticks at a time, so that a frame-driven caller never blocks. How the ticks are split never changes the
    /// result: it is the same <see cref="GenerationEvaluation"/> as running the job to the end in one call.
    /// </summary>
    public sealed class EvaluationJob
    {
        private readonly EvaluationPlan plan;
        private readonly IReadOnlyList<Func<IBrain>> candidates;
        private readonly BodyGenes[] bodies;
        private readonly ulong[] seeds;
        private readonly List<EpisodeResult> episodes;
        private readonly CandidateEvaluation[] aggregates;
        private readonly EpisodeResult[] perSeed;
        private readonly int ticksPerSecond;
        private GenerationEvaluation result;

        /// <param name="bodies">Body genes of each candidate, in candidate order; null gives every candidate neutral genes.</param>
        public EvaluationJob(EvaluationPlan plan, IReadOnlyList<Func<IBrain>> candidates, IReadOnlyList<ulong> seeds,
            int generation, EvaluationMode mode, IReadOnlyList<BodyGenes> bodies = null)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            this.candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
            if (candidates.Count == 0)
                throw new ArgumentException("At least one candidate is required.", nameof(candidates));
            if (seeds == null || seeds.Count == 0)
                throw new ArgumentException("At least one seed is required.", nameof(seeds));
            for (var c = 0; c < candidates.Count; c++)
            {
                if (candidates[c] == null)
                    throw new ArgumentException($"Candidate {c} is null.", nameof(candidates));
            }

            if (bodies != null && bodies.Count != candidates.Count)
                throw new ArgumentException($"Expected body genes for {candidates.Count} candidates, got {bodies.Count}.",
                    nameof(bodies));

            this.bodies = new BodyGenes[candidates.Count];
            for (var c = 0; bodies != null && c < bodies.Count; c++)
                this.bodies[c] = bodies[c];
            this.seeds = new ulong[seeds.Count];
            for (var s = 0; s < seeds.Count; s++)
                this.seeds[s] = seeds[s];
            Generation = generation;
            Mode = mode;
            episodes = new List<EpisodeResult>(candidates.Count * seeds.Count);
            aggregates = new CandidateEvaluation[candidates.Count];
            perSeed = new EpisodeResult[seeds.Count];
            ticksPerSecond = plan.EpisodeConfig(this.seeds[0]).TicksPerSecond;
            Current = CreateEpisode(0, 0);
        }

        public int Generation { get; }
        public EvaluationMode Mode { get; }
        public int CandidateCount => candidates.Count;
        public int SeedCount => seeds.Length;
        public int TotalEpisodes => candidates.Count * seeds.Length;
        public int CompletedEpisodes { get; private set; }

        /// <summary>Episode being simulated; null once every episode is done.</summary>
        public IsolatedEpisode Current { get; private set; }

        public bool IsComplete => result != null;

        /// <summary>The whole evaluation; only available once <see cref="IsComplete"/>.</summary>
        public GenerationEvaluation Result =>
            result ?? throw new InvalidOperationException("The evaluation is still running.");

        /// <summary>
        /// Simulates up to <paramref name="maxTicks"/> ticks, moving to the next episode whenever one finishes, and
        /// returns how many ticks ran. Finishing an episode early (death) does not use up the remaining budget.
        /// </summary>
        public long Advance(long maxTicks)
        {
            if (maxTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(maxTicks));
            var ran = 0L;
            while (Current != null)
            {
                ran += Current.Advance(maxTicks - ran);
                if (!Current.IsFinished)
                    break;
                CompleteEpisode();
            }

            return ran;
        }

        /// <summary>
        /// Saved progress between ticks: the finished episodes in run order and the episode being simulated. Only a
        /// running job has progress to save.
        /// </summary>
        public EvaluationProgress Capture()
        {
            if (Current == null)
                throw new InvalidOperationException("A complete evaluation has no progress to save; keep its result.");
            var completed = new List<EpisodeResult>(episodes);
            for (var s = 0; s < Current.SeedIndex; s++)
                completed.Add(perSeed[s]);
            return new EvaluationProgress(completed, Current.Simulation.Capture(), Current.Controller.Capture());
        }

        /// <summary>
        /// Continues a new job (nothing simulated yet) from saved <paramref name="progress"/> of the same evaluation:
        /// every result must be the one this job would have produced at its position (mode, generation, candidate,
        /// seed), and the running episode is rebuilt from its saved world and brain state. On any problem the job is
        /// left unchanged and an <see cref="ArgumentException"/> explains why.
        /// </summary>
        public void Resume(EvaluationProgress progress)
        {
            if (progress == null)
                throw new ArgumentNullException(nameof(progress));
            if (CompletedEpisodes != 0 || Current == null || Current.Tick != 0)
                throw new InvalidOperationException("Only a job that has not simulated anything can be resumed.");

            var completed = progress.Completed;
            if (completed.Count >= TotalEpisodes)
                throw new ArgumentException($"{completed.Count} finished episodes leave none of {TotalEpisodes} running.",
                    nameof(progress));
            var maxEnergy = CreatureBody.MaxPossibleEnergy(plan.EpisodeConfig(seeds[0]));
            for (var i = 0; i < completed.Count; i++)
                CheckResult(completed[i], i / seeds.Length, i % seeds.Length, maxEnergy);

            var candidate = completed.Count / seeds.Length;
            var seedIndex = completed.Count % seeds.Length;
            var running = new IsolatedEpisode(plan, candidates[candidate], seeds[seedIndex], candidate, seedIndex,
                Generation, Mode, bodies[candidate], progress.CurrentWorld, progress.CurrentBrains);

            for (var i = 0; i < completed.Count; i++)
            {
                perSeed[i % seeds.Length] = completed[i];
                if (i % seeds.Length != seeds.Length - 1)
                    continue;
                var results = (EpisodeResult[])perSeed.Clone();
                episodes.AddRange(results);
                aggregates[i / seeds.Length] = new CandidateEvaluation(i / seeds.Length, results, ticksPerSecond);
            }

            CompletedEpisodes = completed.Count;
            Current = running;
        }

        /// <summary>Runs every remaining episode and returns the evaluation.</summary>
        public GenerationEvaluation RunToEnd()
        {
            while (!IsComplete)
                Advance(long.MaxValue);
            return result;
        }

        private void CompleteEpisode()
        {
            var episode = Current;
            perSeed[episode.SeedIndex] = episode.Result();
            CompletedEpisodes++;
            var nextSeed = episode.SeedIndex + 1;
            var candidate = episode.Candidate;
            if (nextSeed < seeds.Length)
            {
                Current = CreateEpisode(candidate, nextSeed);
                return;
            }

            var results = (EpisodeResult[])perSeed.Clone();
            episodes.AddRange(results);
            aggregates[candidate] = new CandidateEvaluation(candidate, results, ticksPerSecond);
            if (candidate + 1 < candidates.Count)
            {
                Current = CreateEpisode(candidate + 1, 0);
                return;
            }

            Current = null;
            result = new GenerationEvaluation(Generation, Mode, (ulong[])seeds.Clone(), episodes, aggregates);
        }

        private void CheckResult(EpisodeResult result, int candidate, int seedIndex, float maxEnergy)
        {
            if (result.Mode != Mode || result.Generation != Generation || result.Candidate != candidate ||
                result.SeedIndex != seedIndex || result.EpisodeSeed != seeds[seedIndex] ||
                result.EpisodeTicks != plan.EpisodeTicks)
                throw new ArgumentException($"Saved episode {candidate}×{seedIndex} does not belong to this evaluation.",
                    nameof(result));
            if (!(result.FoodEnergy >= 0f) || float.IsInfinity(result.FoodEnergy) || result.SurvivedTicks < 1 ||
                result.SurvivedTicks > plan.EpisodeTicks || !(result.FinalEnergy >= 0f && result.FinalEnergy <= maxEnergy) ||
                result.Survived && (result.FinalEnergy <= 0f || result.SurvivedTicks != plan.EpisodeTicks))
                throw new ArgumentException($"Saved episode {candidate}×{seedIndex} has impossible values.", nameof(result));
        }

        private IsolatedEpisode CreateEpisode(int candidate, int seedIndex) =>
            new IsolatedEpisode(plan, candidates[candidate], seeds[seedIndex], candidate, seedIndex, Generation, Mode,
                bodies[candidate]);
    }

    /// <summary>Progress of an <see cref="EvaluationJob"/> saved between ticks.</summary>
    public sealed class EvaluationProgress
    {
        public EvaluationProgress(IReadOnlyList<EpisodeResult> completed, SimulationSnapshot currentWorld,
            IReadOnlyList<BrainSlotState> currentBrains)
        {
            if (completed == null)
                throw new ArgumentNullException(nameof(completed));
            var copy = new EpisodeResult[completed.Count];
            for (var i = 0; i < copy.Length; i++)
                copy[i] = completed[i];
            Completed = copy;
            CurrentWorld = currentWorld ?? throw new ArgumentNullException(nameof(currentWorld));
            if (currentBrains == null)
                throw new ArgumentNullException(nameof(currentBrains));
            var brains = new BrainSlotState[currentBrains.Count];
            for (var i = 0; i < brains.Length; i++)
                brains[i] = currentBrains[i];
            CurrentBrains = brains;
        }

        /// <summary>Finished episodes in run order (candidate-major, seed-minor), before aggregation.</summary>
        public IReadOnlyList<EpisodeResult> Completed { get; }

        /// <summary>World of the episode being simulated.</summary>
        public SimulationSnapshot CurrentWorld { get; }

        /// <summary>Brain state of that episode's creature (empty before its first decision).</summary>
        public IReadOnlyList<BrainSlotState> CurrentBrains { get; }
    }
}
