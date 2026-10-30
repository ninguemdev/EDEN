using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>Editable parameters of an evolution experiment; frozen into an <see cref="EvaluationPlan"/>.</summary>
    [Serializable]
    public sealed class EvolutionSettings
    {
        public ulong ExperimentSeed;

        /// <summary>Simulated length of every evaluation episode (proposed 120 s).</summary>
        public float EpisodeSeconds = 120f;

        /// <summary>Training seeds per generation, shared by every genome of that generation.</summary>
        public int TrainingSeedsPerGeneration = 3;

        /// <summary>
        /// Seed of the validation scenarios, separate from <see cref="ExperimentSeed"/> so that independent runs of a
        /// battery can be compared on the same held-out scenarios.
        /// </summary>
        public ulong ValidationSeed = 1;

        /// <summary>Held-out seeds that never feed selection (proposed 5); the same ones in every generation.</summary>
        public int ValidationSeedCount = 5;

        /// <summary>
        /// World of an isolated episode. Population is always forced to one creature, which never reproduces; food is
        /// proposed at 32 so that foraging alone in the 50 × 50 arena is not trivial. Values are starting points to be
        /// measured.
        /// </summary>
        public SimulationSettings World = new SimulationSettings { InitialPopulation = 1, InitialFood = 32 };

        /// <summary>
        /// Body genes expressed in the episodes. <see cref="BodyGeneSet.None"/> (the default) gives every creature the
        /// reference body of <see cref="World"/>: the fixed-body control. Genes outside the set start neutral.
        /// </summary>
        public BodyGeneSet ExpressedBodyGenes = BodyGeneSet.None;

        /// <summary>
        /// Lifetime plasticity (M7 experiment): genomes start with random plasticity coefficients and their brains change
        /// during each life. Off (the default) keeps every network fixed during a life, as before.
        /// </summary>
        public bool Plasticity;

        /// <summary>
        /// How generations are bred: the fixed-topology MLP (the default and legacy control) or NEAT. NEAT networks have no
        /// lifetime plasticity.
        /// </summary>
        public EvolutionAlgorithm Algorithm = EvolutionAlgorithm.Mlp;

        /// <summary>NEAT parameters; used only with <see cref="EvolutionAlgorithm.Neat"/>.</summary>
        public NeatSettings Neat = new NeatSettings();

        /// <summary>
        /// How creatures sense: the legacy circular food sense and wall radar (the default and the only senses of the
        /// MLP control) or eyes (<see cref="NeuralContract.Eyes"/>, NEAT only; founders have one frontal eye).
        /// </summary>
        public Senses Senses = Senses.Legacy;

        /// <summary>
        /// With eyes: whether orientation, aperture and range of each eye are inherited and mutate (<see cref="NeatSettings"/>
        /// gives how). Off keeps every eye as the founders' (the fixed-eye control).
        /// </summary>
        public bool EvolvingEyes;

        /// <summary>
        /// How genomes are scored. <see cref="RewardProfile.Legacy"/> (the default and the control) is food energy per
        /// second, recorded exactly as before profiles existed. Immutable, so copies share it.
        /// </summary>
        public RewardProfile Profile = RewardProfile.Legacy;

        /// <summary>
        /// The stages after the first, one per further stage of <see cref="Profile"/> and in its order; null or empty
        /// (the default) for a single-stage profile. The first stage is the one described by <see cref="World"/>,
        /// <see cref="EpisodeSeconds"/>, <see cref="TrainingSeedsPerGeneration"/> and <see cref="ValidationSeedCount"/>.
        /// </summary>
        public List<StageSettings> FurtherStages;

        /// <summary>Genomes per generation (proposed 64).</summary>
        public int PopulationSize = 64;

        /// <summary>Best genomes copied unchanged into the next generation and evaluated again (proposed 4).</summary>
        public int EliteCount = 4;

        /// <summary>Candidates drawn, with replacement, for each parent tournament (proposed 3).</summary>
        public int TournamentSize = 3;

        /// <summary>Chance that each weight of a child is mutated (proposed 0.05).</summary>
        public float MutationProbability = 0.05f;

        /// <summary>Standard deviation of the Gaussian noise added to a mutated weight (proposed 0.10).</summary>
        public float MutationSigma = 0.10f;

        /// <summary>Deep copy with another experiment seed (e.g. one run of a battery); this instance is not changed.</summary>
        public EvolutionSettings WithExperimentSeed(ulong experimentSeed)
        {
            var copy = (EvolutionSettings)MemberwiseClone();
            copy.ExperimentSeed = experimentSeed;
            copy.World = World?.Clone();
            copy.Neat = Neat?.Clone();
            copy.FurtherStages = FurtherStages?.ConvertAll(stage => stage?.Clone());
            return copy;
        }
    }

    /// <summary>World, length and seeds of one evaluation stage after the first (<see cref="EvolutionSettings.FurtherStages"/>).</summary>
    [Serializable]
    public sealed class StageSettings
    {
        /// <summary>Simulated length of every episode of the stage.</summary>
        public float EpisodeSeconds = 120f;

        /// <summary>Training seeds of the stage per generation, shared by every genome of that generation.</summary>
        public int TrainingSeedsPerGeneration = 3;

        /// <summary>Held-out seeds of the stage, derived from <see cref="EvolutionSettings.ValidationSeed"/>.</summary>
        public int ValidationSeedCount = 5;

        /// <summary>World of an episode of the stage; population is forced to one creature that never reproduces.</summary>
        public SimulationSettings World = new SimulationSettings { InitialPopulation = 1, InitialFood = 32 };

        public StageSettings Clone()
        {
            var copy = (StageSettings)MemberwiseClone();
            copy.World = World?.Clone();
            return copy;
        }
    }

    /// <summary>Algorithm that breeds the generations. Persisted: never renumber.</summary>
    public enum EvolutionAlgorithm
    {
        /// <summary>Fixed-topology MLP: elites, tournaments and Gaussian weight mutation (the legacy control).</summary>
        Mlp = 0,

        /// <summary>NEAT: minimal networks that grow, crossover aligned by innovation and species.</summary>
        Neat = 1
    }

    /// <summary>How creatures sense their world. Persisted: never renumber.</summary>
    public enum Senses
    {
        /// <summary>Food nearest within a circle and four wall distances (<see cref="NeuralContract.Legacy"/>).</summary>
        Legacy = 0,

        /// <summary>
        /// Only what eyes' cones and ranges reach (<see cref="NeuralContract.Eyes"/>; with water,
        /// <see cref="NeuralContract.EyesWater"/>, which also sees water and feels hydration).
        /// </summary>
        Eyes = 1
    }

    /// <summary>Kind of evaluation a result belongs to. Persisted in reports: never renumber.</summary>
    public enum EvaluationMode
    {
        /// <summary>Isolated episodes on the generation's training seeds; the only results selection may use.</summary>
        Training = 0,

        /// <summary>Isolated episodes on held-out seeds that never feed selection.</summary>
        Validation = 1,

        /// <summary>Observation of a shared world; not an isolated evaluation.</summary>
        SharedInspection = 2
    }


    /// <summary>
    /// One stage of an <see cref="EvaluationPlan"/>: the episode world, length and seeds that a stage of the reward
    /// profile scores. Every candidate runs every stage.
    /// </summary>
    public sealed class EvaluationStage
    {
        private readonly SimulationConfig episodeTemplate;
        private readonly ulong trainingRoot;
        private readonly ulong[] validationSeeds;

        internal EvaluationStage(int index, RewardStage reward, SimulationConfig episodeTemplate, SearchWorld search,
            float episodeSeconds, int trainingSeedsPerGeneration, ulong trainingRoot, ulong validationRoot,
            int validationSeedCount)
        {
            Index = index;
            Reward = reward;
            Search = search;
            this.episodeTemplate = episodeTemplate;
            EpisodeSeconds = episodeSeconds;
            EpisodeTicks = Math.Max(1L, (long)Math.Round((double)episodeSeconds * episodeTemplate.TicksPerSecond));
            TrainingSeedsPerGeneration = trainingSeedsPerGeneration;
            this.trainingRoot = trainingRoot;
            validationSeeds = new ulong[validationSeedCount];
            for (var i = 0; i < validationSeeds.Length; i++)
                validationSeeds[i] = SplitMix64.Output(validationRoot, (ulong)i + 1);
        }

        /// <summary>Position of the stage in the plan and in the profile.</summary>
        public int Index { get; }

        /// <summary>The profile stage that scores these episodes.</summary>
        public RewardStage Reward { get; }

        public string Name => Reward.Name;

        /// <summary>The targets and arena of a search stage (which runs under search rules); null for the others.</summary>
        public SearchWorld Search { get; }

        public bool IsSearch => Search != null;
        public bool ActiveSearch => Reward.Scoring == StageScoring.ExploreCollectActive;
        public bool FrozenNeeds => IsSearch && !ActiveSearch;
        public bool RespawnFood => !IsSearch;
        public bool FinishOnCollection => IsSearch && !ActiveSearch;

        public float EpisodeSeconds { get; }

        /// <summary>Configured episode length in ticks; an episode always counts as this long, even after a death.</summary>
        public long EpisodeTicks { get; }

        public int TrainingSeedsPerGeneration { get; }
        public int ValidationSeedCount => validationSeeds.Length;

        /// <summary>World configuration of one isolated episode (single creature) of the stage for a seed.</summary>
        public SimulationConfig EpisodeConfig(ulong episodeSeed) =>
            Search?.Layout(episodeSeed).Config ?? episodeTemplate.WithSeed(episodeSeed);

        /// <summary>The stage world for what does not depend on the seed (tick rate, reference body, capacity).</summary>
        public SimulationConfig TemplateConfig => episodeTemplate;

        internal ulong TrainingSeed(int generation, int index)
        {
            var generationSeed = SplitMix64.Output(trainingRoot, (ulong)generation + 1);
            return SplitMix64.Output(generationSeed, (ulong)index + 1);
        }

        internal ulong ValidationSeed(int index) => validationSeeds[index];
    }

    /// <summary>
    /// Validated, immutable evaluation rules. Every genome of a generation runs alone, once per training seed of every
    /// stage, in a world built from the same seed, so all genomes face identical starting conditions and never compete.
    /// The seeds of an evaluation are listed stage after stage, so its episodes run candidate → stage → seed.
    /// Training seed scheme version 1 (one stage): SplitMix64 output (index + 1) of seed (SplitMix64 output
    /// (generation + 1) of seed (SplitMix64 output <see cref="TrainingTag"/> of the experiment seed)).
    /// Validation seeds: SplitMix64 output (index + 1) of seed (SplitMix64 output <see cref="ValidationTag"/> of the
    /// validation seed). They are fixed for the whole experiment and are never passed to selection.
    /// Version 2 (several stages) derives the seeds of each stage the same way from a root of its own: SplitMix64 output
    /// (<see cref="StageTag"/> + stage index) of the version 1 root.
    /// </summary>
    public sealed class EvaluationPlan
    {
        public const int SeedSchemeVersion = 1;
        public const int StagedSeedSchemeVersion = 2;
        public const int MaxTrainingSeeds = 64;
        public const float MaxEpisodeSeconds = 100000f;
        private const ulong TrainingTag = 0x74726169UL; // "trai"
        private const ulong ValidationTag = 0x76616C69UL; // "vali"
        private const ulong StageTag = 0x73746700UL; // "stg" + stage index

        private readonly EvaluationStage[] stages;

        public EvaluationPlan(EvolutionSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (settings.World == null)
                throw new ArgumentException("A world template is required.", nameof(settings));

            var errors = new List<string>();
            var further = settings.FurtherStages ?? new List<StageSettings>();
            CheckStage(errors, "", settings.EpisodeSeconds, settings.TrainingSeedsPerGeneration,
                settings.ValidationSeedCount);
            for (var i = 0; i < further.Count; i++)
            {
                if (further[i]?.World == null)
                    errors.Add($"stage {i + 2} needs its settings and world");
                else
                    CheckStage(errors, $"stage {i + 2} ", further[i].EpisodeSeconds,
                        further[i].TrainingSeedsPerGeneration, further[i].ValidationSeedCount);
            }

            if (!BodyGenes.IsValidSet(settings.ExpressedBodyGenes))
                errors.Add($"{nameof(settings.ExpressedBodyGenes)} has unknown genes ({(int)settings.ExpressedBodyGenes})");
            if (settings.Algorithm != EvolutionAlgorithm.Mlp && settings.Algorithm != EvolutionAlgorithm.Neat)
                errors.Add($"{nameof(settings.Algorithm)} is unknown ({(int)settings.Algorithm})");
            if (settings.Algorithm == EvolutionAlgorithm.Neat && settings.Plasticity)
                errors.Add("NEAT networks have no lifetime plasticity");
            if (settings.Algorithm == EvolutionAlgorithm.Neat && settings.Neat == null)
                errors.Add("NEAT needs its settings");
            if (settings.Senses != Senses.Legacy && settings.Senses != Senses.Eyes)
                errors.Add($"{nameof(settings.Senses)} is unknown ({(int)settings.Senses})");
            if (settings.Senses == Senses.Eyes && settings.Algorithm != EvolutionAlgorithm.Neat)
                errors.Add("eyes need NEAT; the MLP control keeps the legacy senses");
            if (settings.EvolvingEyes && settings.Senses != Senses.Eyes)
                errors.Add("evolving eyes need the eye senses");
            if (settings.Profile == null)
                errors.Add("a reward profile is required");
            else if (settings.Profile.Stages.Count != further.Count + 1)
                errors.Add($"reward profile \"{settings.Profile.Id}\" has {settings.Profile.Stages.Count} stages and " +
                           $"the settings describe {further.Count + 1}");
            if (errors.Count > 0)
                throw new ArgumentException("Invalid evolution settings: " + string.Join("; ", errors));

            // Every episode of a stage differs only by its seed: the validated rules (and their free-space grid) are
            // built once per stage.
            var templates = new SimulationConfig[further.Count + 1];
            var searches = new SearchWorld[templates.Length];
            for (var i = 0; i < templates.Length; i++)
            {
                var world = i == 0 ? settings.World : further[i - 1].World;
                if (settings.Profile.Stages[i].Scoring == StageScoring.ExploreCollectActive)
                {
                    if (settings.Senses != Senses.Eyes || settings.Algorithm != EvolutionAlgorithm.Neat ||
                        settings.Profile.Parameter(RewardProfiles.WaterTargets, 0) < 1)
                        errors.Add("active search requires NEAT, eyes-water-v1 and water targets");
                    ActiveSearchOptions.Read(settings.Profile.Parameters).Check(world);
                }
                if (settings.Profile.Stages[i].Scoring == StageScoring.Search ||
                    settings.Profile.Stages[i].Scoring == StageScoring.ExploreCollectActive)
                    searches[i] = SearchWorld.Choose(world, RewardProfiles.TargetsOf(settings.Profile),
                        i == 0 ? settings.EpisodeSeconds : further[i - 1].EpisodeSeconds,
                        settings.Profile.Parameter(RewardProfiles.FeasibleFraction, 0.5), $"stage {i + 1}", errors,
                        RewardProfiles.IsActive(settings.Profile) ? settings.Profile.ParameterHash : "");
                templates[i] = searches[i]?.Template ?? Template(world);
            }

            Senses = settings.Senses;
            Contract = ContractOf(Senses, templates[0], searches[0]);
            for (var i = 1; i < templates.Length; i++)
            {
                var contract = ContractOf(Senses, templates[i], searches[i]);
                if (contract.Id != Contract.Id)
                    errors.Add($"stage {i + 1} reads the neural contract {contract.Id} and stage 1 reads {Contract.Id}; " +
                               "a genome needs the same contract in every stage");
                if (templates[i].TicksPerSecond != templates[0].TicksPerSecond)
                    errors.Add($"stage {i + 1} runs {templates[i].TicksPerSecond} ticks per second and stage 1 runs " +
                               $"{templates[0].TicksPerSecond}");
            }

            if (errors.Count > 0)
                throw new ArgumentException("Invalid evolution settings: " + string.Join("; ", errors));

            ExperimentSeed = settings.ExperimentSeed;
            Profile = settings.Profile;
            Algorithm = settings.Algorithm;
            EvolvingEyes = settings.EvolvingEyes;
            ExpressedBodyGenes = settings.ExpressedBodyGenes;
            Plasticity = settings.Plasticity;
            ValidationSeed = settings.ValidationSeed;
            var trainingRoot = SplitMix64.Output(ExperimentSeed, TrainingTag);
            var validationRoot = SplitMix64.Output(ValidationSeed, ValidationTag);
            var staged = templates.Length > 1;
            stages = new EvaluationStage[templates.Length];
            for (var i = 0; i < stages.Length; i++)
            {
                var stage = i == 0 ? null : further[i - 1];
                stages[i] = new EvaluationStage(i, Profile.Stages[i], templates[i], searches[i],
                    stage?.EpisodeSeconds ?? settings.EpisodeSeconds,
                    stage?.TrainingSeedsPerGeneration ?? settings.TrainingSeedsPerGeneration,
                    IsActiveProfile(settings.Profile) ? SplitMix64.Output(trainingRoot, LogicalTag(Profile.Stages[i].Name)) :
                    staged ? SplitMix64.Output(trainingRoot, StageTag + (ulong)i) : trainingRoot,
                    IsActiveProfile(settings.Profile) ? SplitMix64.Output(validationRoot, LogicalTag(Profile.Stages[i].Name)) :
                    staged ? SplitMix64.Output(validationRoot, StageTag + (ulong)i) : validationRoot,
                    stage?.ValidationSeedCount ?? settings.ValidationSeedCount);
                TrainingSeedsPerGeneration += stages[i].TrainingSeedsPerGeneration;
                ValidationSeedCount += stages[i].ValidationSeedCount;
            }
        }

        public ulong ExperimentSeed { get; }

        /// <summary>How episodes are scored and recorded.</summary>
        public RewardProfile Profile { get; }

        public EvolutionAlgorithm Algorithm { get; }

        public Senses Senses { get; }

        /// <summary>Whether eyes are inherited with mutation (otherwise every eye stays the founders').</summary>
        public bool EvolvingEyes { get; }

        /// <summary>
        /// What the brains read and write, the same in every stage: with eyes, <see cref="NeuralContract.EyesWater"/> in
        /// a world with water and <see cref="NeuralContract.Eyes"/> without; else the legacy contract, which neither sees
        /// water nor feels thirst.
        /// </summary>
        public NeuralContract Contract { get; }

        /// <summary>Body genes that shape the episode creature; <see cref="BodyGeneSet.None"/> is the fixed body.</summary>
        public BodyGeneSet ExpressedBodyGenes { get; }

        /// <summary>Whether genomes carry plasticity coefficients (brains change during a life).</summary>
        public bool Plasticity { get; }

        /// <summary>The stages, in profile order; a single one unless the profile has several.</summary>
        public IReadOnlyList<EvaluationStage> Stages => stages;

        /// <summary>How seeds are derived: <see cref="SeedSchemeVersion"/> for one stage, else <see cref="StagedSeedSchemeVersion"/>.</summary>
        public int SeedScheme => IsActiveProfile(Profile) ? LogicalSeedSchemeVersion : stages.Length == 1 ? SeedSchemeVersion : StagedSeedSchemeVersion;
        public const int LogicalSeedSchemeVersion = 3;
        private static bool IsActiveProfile(RewardProfile profile) => RewardProfiles.IsActive(profile);
        private static ulong LogicalTag(string role) => role == RewardProfiles.SearchStage ? 0x7372636833UL : 0x72657333UL;

        /// <summary>Episode length of the first stage.</summary>
        public float EpisodeSeconds => stages[0].EpisodeSeconds;

        /// <summary>Training seeds per generation of all stages together.</summary>
        public int TrainingSeedsPerGeneration { get; }

        public ulong ValidationSeed { get; }

        /// <summary>Validation seeds of all stages together.</summary>
        public int ValidationSeedCount { get; }

        /// <summary>Episode length in ticks of the first stage.</summary>
        public long EpisodeTicks => stages[0].EpisodeTicks;

        /// <summary>Training seed at <paramref name="index"/> among those of all stages, listed stage after stage.</summary>
        public ulong TrainingSeed(int generation, int index)
        {
            if (generation < 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            if (index < 0 || index >= TrainingSeedsPerGeneration)
                throw new ArgumentOutOfRangeException(nameof(index));
            foreach (var stage in stages)
            {
                if (index < stage.TrainingSeedsPerGeneration)
                    return stage.TrainingSeed(generation, index);
                index -= stage.TrainingSeedsPerGeneration;
            }

            throw new InvalidOperationException("The index was checked against every stage.");
        }

        public ulong[] TrainingSeeds(int generation)
        {
            var seeds = new ulong[TrainingSeedsPerGeneration];
            for (var i = 0; i < seeds.Length; i++)
                seeds[i] = TrainingSeed(generation, i);
            return seeds;
        }

        /// <summary>Held-out seeds of all stages, stage after stage, identical for every generation (a fresh copy).</summary>
        public ulong[] ValidationSeeds()
        {
            var seeds = new ulong[ValidationSeedCount];
            var next = 0;
            foreach (var stage in stages)
            {
                for (var i = 0; i < stage.ValidationSeedCount; i++)
                    seeds[next++] = stage.ValidationSeed(i);
            }

            return seeds;
        }

        /// <summary>
        /// The stage of each of <paramref name="seedCount"/> seeds listed stage after stage. A single-stage plan runs any
        /// number of seeds; with several stages the count must be the plan's for <paramref name="mode"/>.
        /// </summary>
        public int[] StageOfSeeds(EvaluationMode mode, int seedCount)
        {
            var stageOf = new int[seedCount];
            if (stages.Length == 1)
                return stageOf;

            var expected = mode == EvaluationMode.Training ? TrainingSeedsPerGeneration
                : mode == EvaluationMode.Validation ? ValidationSeedCount : -1;
            if (seedCount != expected)
                throw new ArgumentException($"A plan of {stages.Length} stages evaluates {mode} on {expected} seeds, " +
                                            $"not {seedCount}.", nameof(seedCount));
            var next = 0;
            foreach (var stage in stages)
            {
                var count = mode == EvaluationMode.Training ? stage.TrainingSeedsPerGeneration : stage.ValidationSeedCount;
                for (var i = 0; i < count; i++)
                    stageOf[next++] = stage.Index;
            }

            return stageOf;
        }

        /// <summary>The genes of <paramref name="genes"/> that shape the episode creature; the others are neutral.</summary>
        public BodyGenes ExpressedGenes(BodyGenes genes) => genes.Masked(ExpressedBodyGenes);

        /// <summary>World configuration of one isolated episode (single creature) of the first stage for a seed.</summary>
        public SimulationConfig EpisodeConfig(ulong episodeSeed) => stages[0].EpisodeConfig(episodeSeed);

        private static void CheckStage(List<string> errors, string stage, float episodeSeconds, int trainingSeeds,
            int validationSeeds)
        {
            if (float.IsNaN(episodeSeconds) || episodeSeconds <= 0f || episodeSeconds > MaxEpisodeSeconds)
                errors.Add($"{stage}{nameof(EvolutionSettings.EpisodeSeconds)} must be in (0, {MaxEpisodeSeconds}] " +
                           $"(was {episodeSeconds})");
            if (trainingSeeds < 1 || trainingSeeds > MaxTrainingSeeds)
                errors.Add($"{stage}{nameof(EvolutionSettings.TrainingSeedsPerGeneration)} must be in " +
                           $"[1, {MaxTrainingSeeds}] (was {trainingSeeds})");
            if (validationSeeds < 1 || validationSeeds > MaxTrainingSeeds)
                errors.Add($"{stage}{nameof(EvolutionSettings.ValidationSeedCount)} must be in [1, {MaxTrainingSeeds}] " +
                           $"(was {validationSeeds})");
        }

        private static SimulationConfig Template(SimulationSettings settings)
        {
            var world = settings.Clone();
            world.InitialPopulation = 1;
            world.Reproduction = false;
            return new SimulationConfig(world);
        }

        // A search world gets its water targets per episode, so its contract follows the targets.
        private static NeuralContract ContractOf(Senses senses, SimulationConfig world, SearchWorld search) =>
            senses != Senses.Eyes ? NeuralContract.Legacy
            : NeuralContract.ForEyes(search != null ? search.Targets.Water > 0 : world.HasWater);
    }
}
