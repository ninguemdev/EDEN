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
    /// single creature. It finishes after the configured duration or when the creature dies; the remaining ticks
    /// cannot change the result, so they are not simulated, but the episode still counts as its configured duration.
    /// </summary>
    public sealed class IsolatedEpisode
    {
        private readonly EvaluationPlan plan;
        private readonly EvaluationMode mode;
        private readonly int generation;

        public IsolatedEpisode(EvaluationPlan plan, Func<IBrain> createBrain, ulong episodeSeed, int candidate,
            int seedIndex, int generation, EvaluationMode mode)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            if (createBrain == null)
                throw new ArgumentNullException(nameof(createBrain));

            var config = plan.EpisodeConfig(episodeSeed);
            Simulation = new WorldSimulation(WorldFactory.CreateInitial(config));
            Creature = Simulation.World.Creatures[0];
            Controller = new BrainController(config, _ => createBrain());
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

        /// <summary>Ticks simulated so far.</summary>
        public long Tick { get; private set; }

        public bool IsFinished => Tick >= plan.EpisodeTicks || Simulation.IsExtinct;

        /// <summary>Simulates up to <paramref name="maxTicks"/> ticks and returns how many ran.</summary>
        public long Advance(long maxTicks)
        {
            var ran = 0L;
            while (ran < maxTicks && !IsFinished)
            {
                Simulation.Step(Controller);
                Tick++;
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
        private readonly ulong[] seeds;
        private readonly List<EpisodeResult> episodes;
        private readonly CandidateEvaluation[] aggregates;
        private readonly EpisodeResult[] perSeed;
        private readonly int ticksPerSecond;
        private GenerationEvaluation result;

        public EvaluationJob(EvaluationPlan plan, IReadOnlyList<Func<IBrain>> candidates, IReadOnlyList<ulong> seeds,
            int generation, EvaluationMode mode)
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

        private IsolatedEpisode CreateEpisode(int candidate, int seedIndex) =>
            new IsolatedEpisode(plan, candidates[candidate], seeds[seedIndex], candidate, seedIndex, Generation, Mode);
    }
}
