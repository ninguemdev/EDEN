using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Simulation;

namespace Eden.Application
{
    /// <summary>
    /// One isolated episode of a plan stage that can be advanced a few ticks at a time: a world built from the episode
    /// seed with a single creature, whose body the plan expresses from the candidate's body genes. It finishes after the
    /// stage's duration or when the creature dies; the remaining ticks cannot change the result, so they are not
    /// simulated, but the episode still counts as its configured duration.
    /// </summary>
    public sealed class IsolatedEpisode
    {
        private readonly EvaluationPlan plan;
        private readonly EvaluationStage stage;
        private readonly EvaluationMode mode;
        private readonly int generation;

        /// <param name="eyes">The candidate's eyes (genomes of the eye senses); null for the legacy senses.</param>
        /// <param name="stage">Index of the plan stage the episode runs.</param>
        public IsolatedEpisode(EvaluationPlan plan, Func<IBrain> createBrain, ulong episodeSeed, int candidate,
            int seedIndex, int generation, EvaluationMode mode, BodyGenes body = default, EyeSet eyes = null, int stage = 0)
            : this(plan, createBrain, episodeSeed, candidate, seedIndex, generation, mode, body, eyes, stage, null, null,
                null)
        {
        }

        internal IsolatedEpisode(EvaluationPlan plan, Func<IBrain> createBrain, ulong episodeSeed, int candidate,
            int seedIndex, int generation, EvaluationMode mode, BodyGenes body, EyeSet eyes, int stage,
            SimulationSnapshot savedWorld, IReadOnlyList<BrainSlotState> savedBrains, EpisodeTracker savedTracker)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            if (createBrain == null)
                throw new ArgumentNullException(nameof(createBrain));
            if (stage < 0 || stage >= plan.Stages.Count)
                throw new ArgumentOutOfRangeException(nameof(stage));

            this.stage = plan.Stages[stage];
            Layout = this.stage.Search?.Layout(episodeSeed);
            var config = this.stage.EpisodeConfig(episodeSeed);
            var expressed = plan.ExpressedGenes(body);
            if (savedWorld != null)
                Simulation = WorldSimulation.Restore(config, savedWorld);
            else if (Layout == null)
                Simulation = new WorldSimulation(WorldFactory.CreateInitial(config, _ => expressed));
            else
                Simulation = new WorldSimulation(SearchWorldState(config, expressed));
            if (savedWorld == null)
                Simulation.World.Creatures[0].Eyes = eyes;
            Simulation.FrozenNeeds = Layout != null;

            if (Simulation.World.Creatures.Count != 1 || Simulation.World.Creatures[0].Id != new CreatureId(1) ||
                Tick >= this.stage.EpisodeTicks)
                throw new ArgumentException("A running episode has its single creature alive and ticks left.",
                    nameof(savedWorld));
            if (!Simulation.World.Creatures[0].Genes.Equals(expressed))
                throw new ArgumentException($"The saved creature has body genes {Simulation.World.Creatures[0].Genes}; " +
                                            $"the candidate expresses {expressed}.", nameof(savedWorld));
            if (!Equals(Simulation.World.Creatures[0].Eyes, eyes))
                throw new ArgumentException("The saved creature does not have the candidate's eyes.", nameof(savedWorld));
            Creature = Simulation.World.Creatures[0];
            if (this.stage.Reward.Scoring != StageScoring.FoodEnergyPerSecond)
            {
                if (savedWorld != null && savedTracker == null)
                    throw new ArgumentException("A saved episode of this stage needs what it measured so far.",
                        nameof(savedTracker));
                Tracker = savedTracker?.Clone() ?? new EpisodeTracker();
                if (savedWorld == null && Layout != null)
                    Tracker.Begin(Layout, Creature.Position);
            }

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

        /// <summary>Index of the plan stage the episode runs.</summary>
        public int Stage => stage.Index;

        /// <summary>The targets of a search episode; null in the other stages.</summary>
        public SearchLayout Layout { get; }

        /// <summary>What the stage's scoring measured so far; null for the legacy scoring.</summary>
        public EpisodeTracker Tracker { get; }

        /// <summary>Ticks simulated so far (the episode world starts at tick 0).</summary>
        public long Tick => Simulation.World.Tick;

        public bool IsFinished => Tick >= stage.EpisodeTicks || Simulation.IsExtinct ||
                                  Layout != null && Tracker.Collected.Count == Layout.TargetCount;

        /// <summary>Simulates up to <paramref name="maxTicks"/> ticks and returns how many ran.</summary>
        public long Advance(long maxTicks)
        {
            var ran = 0L;
            while (ran < maxTicks && !IsFinished)
            {
                Simulation.Step(Controller);
                Tracker?.AfterTick(Simulation, Creature, Layout);
                ran++;
            }

            return ran;
        }

        public EpisodeResult Result()
        {
            if (!IsFinished)
                throw new InvalidOperationException("The episode is still running.");
            if (Tracker == null)
                return new EpisodeResult(mode, generation, Candidate, SeedIndex, EpisodeSeed, stage.EpisodeTicks,
                    stage.EpisodeSeconds, Creature.FoodEnergyEaten, Creature.AgeTicks, Creature.IsAlive, Creature.Energy,
                    stage: stage.Index);

            var values = new Dictionary<string, float>();
            var score = stage.IsSearch ? SearchScore(values) : ResourcesScore(values);
            var components = new float[stage.Reward.Components.Count];
            for (var i = 0; i < components.Length; i++)
                components[i] = values[stage.Reward.Components[i].Name];
            return new EpisodeResult(mode, generation, Candidate, SeedIndex, EpisodeSeed, stage.EpisodeTicks,
                stage.EpisodeSeconds, Creature.FoodEnergyEaten, Creature.AgeTicks, Creature.IsAlive, Creature.Energy,
                components, stage.Index, score);
        }

        // Balanced resources (StageScores.BalancedResources): basal need over the configured duration as the measure of
        // replenishment, the reserves as shares of the creature's own capacity.
        private float ResourcesScore(Dictionary<string, float> values)
        {
            var config = Simulation.World.Config;
            var profile = plan.Profile;
            var water = stage.Reward.Scoring == StageScoring.BalancedResources;
            var survival = Math.Min(1.0, (double)Creature.AgeTicks / stage.EpisodeTicks);
            var energy = StageScores.Replenished(Tracker.AbsorbedEnergy,
                (double)Creature.Body.MetabolismPerSecond * stage.EpisodeSeconds);
            var hydration = water
                ? StageScores.Replenished(Tracker.Drunk, (double)config.HydrationLossPerSecond * stage.EpisodeSeconds)
                : double.NaN;
            var reserves = !Creature.IsAlive ? 0.0
                : water ? ((double)Creature.Energy / Creature.Body.MaxEnergy + (double)Creature.Hydration / config.MaxHydration) / 2
                : (double)Creature.Energy / Creature.Body.MaxEnergy;
            values["survival"] = (float)survival;
            values["energy_replenished"] = (float)energy;
            values["hydration_replenished"] = water ? (float)hydration : 0f;
            values["reserves"] = (float)reserves;
            values["absorbed_energy"] = (float)Tracker.AbsorbedEnergy;
            values["wasted_energy"] = (float)Tracker.WastedEnergy;
            values["drunk"] = (float)Tracker.Drunk;
            return StageScores.BalancedResources(profile.Parameter(RewardProfiles.SurvivalWeight, 0.5),
                profile.Parameter(RewardProfiles.ReplenishWeight, 0.3), profile.Parameter(RewardProfiles.ReservesWeight, 0.2),
                survival, energy, hydration, reserves);
        }

        // Search (StageScores.Search); also scored without the approach aid, which validation reports apart.
        private float SearchScore(Dictionary<string, float> values)
        {
            var profile = plan.Profile;
            var targets = Layout.TargetCount;
            var beta = profile.Parameter(RewardProfiles.Beta, 0.3);
            var approach = Tracker.Collected.Count < targets
                ? StageScores.Approach(Tracker.SegmentDistance, Tracker.BestDistance)
                : 0f;
            var water = 0;
            var speed = 0.0;
            for (var i = 0; i < Tracker.Collected.Count; i++)
            {
                if (Layout.IsWater(Tracker.Collected[i]))
                    water++;
                speed += Tracker.Speeds[i];
            }

            values["collected"] = Tracker.Collected.Count;
            values["collected_food"] = Tracker.Collected.Count - water;
            values["collected_water"] = water;
            values["speed"] = Tracker.Collected.Count > 0 ? (float)(speed / Tracker.Collected.Count) : 0f;
            values["approach"] = approach;
            values["score_without_aid"] = StageScores.Search(targets, 0, beta, Tracker.Speeds, approach);
            values["uncharged_energy"] = (float)Tracker.UnchargedEnergy;
            return StageScores.Search(targets, profile.Parameter(RewardProfiles.Alpha, 0.5), beta, Tracker.Speeds, approach);
        }

        // A search world: the founder and the food targets where the layout put them (food ids 1, 2… in target order).
        private WorldState SearchWorldState(SimulationConfig config, BodyGenes expressed)
        {
            var world = new WorldState(config);
            var capacity = expressed.IsNeutral ? world.ReferenceBody.MaxEnergy : CreatureBody.Express(config, expressed).MaxEnergy;
            world.AddCreature(Layout.Founder, Layout.FounderHeading, Math.Min(config.InitialEnergy, capacity), expressed);
            foreach (var food in Layout.Food)
                world.AddFood(food, config.FoodEnergy);
            return world;
        }
    }

    /// <summary>
    /// Every candidate × seed episode of one evaluation, committed in order (candidate-major, seed-minor; the seeds list
    /// the plan's stages one after another, so candidate → stage → seed) a bounded number of ticks at a time.
    /// Independent episodes can run on CPU workers. How the ticks are split never changes the result: it is the same
    /// <see cref="GenerationEvaluation"/> as running the job to the end in one call.
    /// </summary>
    public sealed class EvaluationJob
    {
        public const int MaxWorkers = 64;
        public static int AutomaticWorkerCount => Math.Max(1, Math.Min(MaxWorkers, Environment.ProcessorCount - 1));

        private readonly List<EpisodeSlot> active = new List<EpisodeSlot>();
        private readonly long[] allowances = new long[MaxWorkers];
        private readonly long[] advanced = new long[MaxWorkers];
        private readonly ParallelOptions parallelOptions = new ParallelOptions();
        private readonly Action<int> advanceSlot;
        private int workerCount = 1;
        private readonly EvaluationPlan plan;
        private readonly IReadOnlyList<Func<IBrain>> candidates;
        private readonly BodyGenes[] bodies;
        private readonly EyeSet[] eyes;
        private readonly ulong[] seeds;
        private readonly int[] stageOfSeed;
        private readonly List<EpisodeResult> episodes;
        private readonly CandidateEvaluation[] aggregates;
        private readonly EpisodeResult[] perSeed;
        private readonly int ticksPerSecond;
        private GenerationEvaluation result;

        /// <param name="bodies">Body genes of each candidate, in candidate order; null gives every candidate neutral genes.</param>
        /// <param name="eyes">Eyes of each candidate, in candidate order (null entries: legacy senses); null for none.</param>
        /// <param name="seeds">
        /// Seeds of the episodes; with several stages, the plan's seeds for <paramref name="mode"/>, stage after stage.
        /// </param>
        public EvaluationJob(EvaluationPlan plan, IReadOnlyList<Func<IBrain>> candidates, IReadOnlyList<ulong> seeds,
            int generation, EvaluationMode mode, IReadOnlyList<BodyGenes> bodies = null, IReadOnlyList<EyeSet> eyes = null)
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

            if (eyes != null && eyes.Count != candidates.Count)
                throw new ArgumentException($"Expected eyes for {candidates.Count} candidates, got {eyes.Count}.",
                    nameof(eyes));

            this.bodies = new BodyGenes[candidates.Count];
            for (var c = 0; bodies != null && c < bodies.Count; c++)
                this.bodies[c] = bodies[c];
            this.eyes = new EyeSet[candidates.Count];
            for (var c = 0; eyes != null && c < eyes.Count; c++)
                this.eyes[c] = eyes[c];
            this.seeds = new ulong[seeds.Count];
            for (var s = 0; s < seeds.Count; s++)
                this.seeds[s] = seeds[s];
            stageOfSeed = plan.StageOfSeeds(mode, seeds.Count);
            Generation = generation;
            Mode = mode;
            episodes = new List<EpisodeResult>(candidates.Count * seeds.Count);
            aggregates = new CandidateEvaluation[candidates.Count];
            perSeed = new EpisodeResult[seeds.Count];
            ticksPerSecond = plan.Stages[0].TemplateConfig.TicksPerSecond;
            active.Add(new EpisodeSlot(CreateEpisode(0, 0)));
            advanceSlot = i => advanced[i] = active[i].IsFinished ? 0 : active[i].Episode.Advance(allowances[i]);
        }

        public int Generation { get; }
        public EvaluationMode Mode { get; }
        public int CandidateCount => candidates.Count;
        public int SeedCount => seeds.Length;
        public int TotalEpisodes => candidates.Count * seeds.Length;
        public int CompletedEpisodes { get; private set; }

        /// <summary>Episode being simulated; null once every episode is done.</summary>
        public IsolatedEpisode Current => active.Count == 0 ? null : active[0].Episode;

        /// <summary>
        /// Execution setting only; it does not change seeds, scores or reproduction. Cannot shrink below the number
        /// of episodes already in flight. Call between Advance invocations; candidates must create independent brains.
        /// </summary>
        public int WorkerCount
        {
            get => workerCount;
            set
            {
                if (value < 1 || value > MaxWorkers || value < active.Count)
                    throw new ArgumentOutOfRangeException(nameof(value));
                workerCount = value;
                parallelOptions.MaxDegreeOfParallelism = value;
            }
        }

        public int ActiveEpisodes => active.Count;

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
            while (ran < maxTicks && !IsComplete)
            {
                var target = (int)Math.Min(workerCount, maxTicks - ran);
                while (active.Count < target && CompletedEpisodes + active.Count < TotalEpisodes)
                {
                    var index = CompletedEpisodes + active.Count;
                    active.Add(new EpisodeSlot(CreateEpisode(index / seeds.Length, index % seeds.Length)));
                }

                var remaining = maxTicks - ran;
                var unfinished = 0;
                for (var i = 0; i < active.Count; i++)
                    if (!active[i].IsFinished) unfinished++;
                for (var i = 0; i < active.Count; i++)
                {
                    allowances[i] = active[i].IsFinished ? 0 : Math.Min(256, remaining / unfinished +
                        (remaining % unfinished > 0 ? 1 : 0));
                    if (!active[i].IsFinished)
                    {
                        remaining -= allowances[i];
                        unfinished--;
                    }
                }

                // Join before touching results, saving, rendering, or invoking any application callbacks.
                if (active.Count > 1 && maxTicks - ran > 1)
                    Parallel.For(0, active.Count, parallelOptions, advanceSlot);
                else
                    for (var i = 0; i < active.Count; i++) advanceSlot(i);
                for (var i = 0; i < active.Count; i++) ran += advanced[i];
                while (active.Count > 0 && active[0].IsFinished)
                {
                    CompleteEpisode(active[0].Result());
                    active.RemoveAt(0);
                }
                if (active.Count == 0 && !IsComplete)
                    active.Add(new EpisodeSlot(CreateEpisode(CompletedEpisodes / seeds.Length, CompletedEpisodes % seeds.Length)));
            }

            return ran;
        }

        /// <summary>
        /// Saved progress between ticks: ordered results, the displayed episode, and all parallel episodes. Only a
        /// running job has progress to save.
        /// </summary>
        public EvaluationProgress Capture()
        {
            if (Current == null)
                throw new InvalidOperationException("A complete evaluation has no progress to save; keep its result.");
            var completed = new List<EpisodeResult>(episodes);
            for (var s = 0; s < Current.SeedIndex; s++)
                completed.Add(perSeed[s]);
            var ahead = new EvaluationAhead[active.Count - 1];
            for (var i = 1; i < active.Count; i++)
                ahead[i - 1] = active[i].IsFinished
                    ? new EvaluationAhead(active[i].Result())
                    : new EvaluationAhead(active[i].Episode.Simulation.Capture(), active[i].Episode.Controller.Capture(),
                        active[i].Episode.Tracker?.Clone());
            return new EvaluationProgress(completed, Current.Simulation.Capture(), Current.Controller.Capture(),
                workerCount, ahead, Current.Tracker?.Clone());
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
            var maxEnergy = new float[plan.Stages.Count];
            for (var k = 0; k < maxEnergy.Length; k++)
                maxEnergy[k] = CreatureBody.MaxPossibleEnergy(plan.Stages[k].TemplateConfig);
            for (var i = 0; i < completed.Count; i++)
                CheckResult(completed[i], i / seeds.Length, i % seeds.Length, maxEnergy);

            var candidate = completed.Count / seeds.Length;
            var seedIndex = completed.Count % seeds.Length;
            var running = new IsolatedEpisode(plan, candidates[candidate], seeds[seedIndex], candidate, seedIndex,
                Generation, Mode, bodies[candidate], eyes[candidate], stageOfSeed[seedIndex], progress.CurrentWorld,
                progress.CurrentBrains, progress.CurrentTracker);

            if (completed.Count + 1 + progress.Ahead.Count > TotalEpisodes)
                throw new ArgumentException("Saved parallel episodes exceed this evaluation.", nameof(progress));
            var restored = new List<EpisodeSlot> { new EpisodeSlot(running) };
            for (var i = 0; i < progress.Ahead.Count; i++)
            {
                var index = completed.Count + 1 + i;
                var c = index / seeds.Length;
                var s = index % seeds.Length;
                var saved = progress.Ahead[i];
                if (saved.Completed.HasValue)
                {
                    CheckResult(saved.Completed.Value, c, s, maxEnergy);
                    restored.Add(new EpisodeSlot(saved.Completed.Value));
                }
                else
                    restored.Add(new EpisodeSlot(new IsolatedEpisode(plan, candidates[c], seeds[s], c, s,
                        Generation, Mode, bodies[c], eyes[c], stageOfSeed[s], saved.World, saved.Brains, saved.Tracker)));
            }

            for (var i = 0; i < completed.Count; i++)
            {
                perSeed[i % seeds.Length] = completed[i];
                if (i % seeds.Length != seeds.Length - 1)
                    continue;
                var results = (EpisodeResult[])perSeed.Clone();
                episodes.AddRange(results);
                aggregates[i / seeds.Length] = new CandidateEvaluation(i / seeds.Length, results, ticksPerSecond, plan.Profile);
            }

            CompletedEpisodes = completed.Count;
            WorkerCount = progress.WorkerCount;
            active.Clear();
            active.AddRange(restored);
        }

        /// <summary>Runs every remaining episode and returns the evaluation.</summary>
        public GenerationEvaluation RunToEnd()
        {
            while (!IsComplete)
                Advance(long.MaxValue);
            return result;
        }

        private void CompleteEpisode(EpisodeResult episode)
        {
            perSeed[episode.SeedIndex] = episode;
            CompletedEpisodes++;
            var nextSeed = episode.SeedIndex + 1;
            var candidate = episode.Candidate;
            if (nextSeed < seeds.Length)
                return;

            var results = (EpisodeResult[])perSeed.Clone();
            episodes.AddRange(results);
            aggregates[candidate] = new CandidateEvaluation(candidate, results, ticksPerSecond, plan.Profile);
            if (candidate + 1 < candidates.Count)
                return;

            result = new GenerationEvaluation(Generation, Mode, (ulong[])seeds.Clone(), episodes, aggregates);
        }

        private sealed class EpisodeSlot
        {
            private readonly EpisodeResult? completed;
            public EpisodeSlot(IsolatedEpisode episode) => Episode = episode;
            public EpisodeSlot(EpisodeResult result) => completed = result;
            public IsolatedEpisode Episode { get; }
            public bool IsFinished => completed.HasValue || Episode.IsFinished;
            public EpisodeResult Result() => completed ?? Episode.Result();
        }

        private void CheckResult(EpisodeResult result, int candidate, int seedIndex, float[] maxEnergy)
        {
            var stage = plan.Stages[stageOfSeed[seedIndex]];
            if (result.Mode != Mode || result.Generation != Generation || result.Candidate != candidate ||
                result.SeedIndex != seedIndex || result.EpisodeSeed != seeds[seedIndex] || result.Stage != stage.Index ||
                result.EpisodeTicks != stage.EpisodeTicks)
                throw new ArgumentException($"Saved episode {candidate}×{seedIndex} does not belong to this evaluation.",
                    nameof(result));
            if (!(result.FoodEnergy >= 0f) || float.IsInfinity(result.FoodEnergy) || result.SurvivedTicks < 1 ||
                result.SurvivedTicks > stage.EpisodeTicks ||
                !(result.FinalEnergy >= 0f && result.FinalEnergy <= maxEnergy[stage.Index]) ||
                result.Survived && (result.FinalEnergy <= 0f || result.SurvivedTicks != stage.EpisodeTicks && !stage.IsSearch) ||
                result.HasScore != (stage.Reward.Scoring != StageScoring.FoodEnergyPerSecond) ||
                result.HasScore && !(result.Fitness >= 0f && result.Fitness <= 1f))
                throw new ArgumentException($"Saved episode {candidate}×{seedIndex} has impossible values.", nameof(result));
        }

        private IsolatedEpisode CreateEpisode(int candidate, int seedIndex) =>
            new IsolatedEpisode(plan, candidates[candidate], seeds[seedIndex], candidate, seedIndex, Generation, Mode,
                bodies[candidate], eyes[candidate], stageOfSeed[seedIndex]);
    }

    /// <summary>Progress of an <see cref="EvaluationJob"/> saved between ticks.</summary>
    public sealed class EvaluationProgress
    {
        public EvaluationProgress(IReadOnlyList<EpisodeResult> completed, SimulationSnapshot currentWorld,
            IReadOnlyList<BrainSlotState> currentBrains, int workerCount = 1, IReadOnlyList<EvaluationAhead> ahead = null,
            EpisodeTracker currentTracker = null)
        {
            CurrentTracker = currentTracker?.Clone();
            if (workerCount < 1 || workerCount > EvaluationJob.MaxWorkers || (ahead?.Count ?? 0) >= workerCount)
                throw new ArgumentOutOfRangeException(nameof(workerCount));
            WorkerCount = workerCount;
            var pending = new EvaluationAhead[ahead?.Count ?? 0];
            for (var i = 0; i < pending.Length; i++)
                pending[i] = ahead[i] ?? throw new ArgumentException("A parallel episode is missing.", nameof(ahead));
            Ahead = pending;
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

        /// <summary>Finished episodes in run order (candidate → stage → seed), before aggregation.</summary>
        public IReadOnlyList<EpisodeResult> Completed { get; }

        /// <summary>World of the episode being simulated.</summary>
        public SimulationSnapshot CurrentWorld { get; }

        /// <summary>Brain state of that episode's creature (empty before its first decision).</summary>
        public IReadOnlyList<BrainSlotState> CurrentBrains { get; }

        /// <summary>What the scoring of the running episode measured so far; null for the legacy scoring.</summary>
        public EpisodeTracker CurrentTracker { get; }

        public int WorkerCount { get; }
        /// <summary>Consecutive episodes after Current, including results waiting for earlier episodes to finish.</summary>
        public IReadOnlyList<EvaluationAhead> Ahead { get; }
    }

    public sealed class EvaluationAhead
    {
        public EvaluationAhead(EpisodeResult completed) => Completed = completed;

        public EvaluationAhead(SimulationSnapshot world, IReadOnlyList<BrainSlotState> brains,
            EpisodeTracker tracker = null)
        {
            Tracker = tracker?.Clone();
            World = world ?? throw new ArgumentNullException(nameof(world));
            if (brains == null) throw new ArgumentNullException(nameof(brains));
            var copy = new BrainSlotState[brains.Count];
            for (var i = 0; i < copy.Length; i++) copy[i] = brains[i];
            Brains = copy;
        }

        public EpisodeResult? Completed { get; }
        public SimulationSnapshot World { get; }
        public IReadOnlyList<BrainSlotState> Brains { get; }

        /// <summary>What the scoring of that episode measured so far; null for the legacy scoring.</summary>
        public EpisodeTracker Tracker { get; }
    }
}
