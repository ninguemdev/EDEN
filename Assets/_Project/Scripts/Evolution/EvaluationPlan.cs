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
        /// World of an isolated episode. Population is always forced to one creature; food is proposed at 32 so that
        /// foraging alone in the 50 × 50 arena is not trivial. Values are starting points to be measured.
        /// </summary>
        public SimulationSettings World = new SimulationSettings { InitialPopulation = 1, InitialFood = 32 };

        /// <summary>
        /// Body genes expressed in the episodes. <see cref="BodyGeneSet.None"/> (the default) gives every creature the
        /// reference body of <see cref="World"/>: the fixed-body control. Genes outside the set start neutral.
        /// </summary>
        public BodyGeneSet ExpressedBodyGenes = BodyGeneSet.None;

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
            return copy;
        }
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
    /// Validated, immutable evaluation rules. Every genome of a generation runs alone, once per training seed, in a
    /// world built from the same seed, so all genomes face identical starting conditions and never compete.
    /// Training seed scheme (version 1): SplitMix64 output (index + 1) of seed (SplitMix64 output (generation + 1)
    /// of seed (SplitMix64 output <see cref="TrainingTag"/> of the experiment seed)).
    /// Validation seeds: SplitMix64 output (index + 1) of seed (SplitMix64 output <see cref="ValidationTag"/> of the
    /// validation seed). They are fixed for the whole experiment and are never passed to selection.
    /// </summary>
    public sealed class EvaluationPlan
    {
        public const int SeedSchemeVersion = 1;
        public const int MaxTrainingSeeds = 64;
        public const float MaxEpisodeSeconds = 100000f;
        private const ulong TrainingTag = 0x74726169UL; // "trai"
        private const ulong ValidationTag = 0x76616C69UL; // "vali"

        private readonly SimulationSettings world;
        private readonly ulong trainingRoot;
        private readonly ulong[] validationSeeds;

        public EvaluationPlan(EvolutionSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            if (settings.World == null)
                throw new ArgumentException("A world template is required.", nameof(settings));

            var errors = new List<string>();
            if (float.IsNaN(settings.EpisodeSeconds) || settings.EpisodeSeconds <= 0f || settings.EpisodeSeconds > MaxEpisodeSeconds)
                errors.Add($"{nameof(settings.EpisodeSeconds)} must be in (0, {MaxEpisodeSeconds}] (was {settings.EpisodeSeconds})");
            if (settings.TrainingSeedsPerGeneration < 1 || settings.TrainingSeedsPerGeneration > MaxTrainingSeeds)
                errors.Add($"{nameof(settings.TrainingSeedsPerGeneration)} must be in [1, {MaxTrainingSeeds}] " +
                           $"(was {settings.TrainingSeedsPerGeneration})");
            if (settings.ValidationSeedCount < 1 || settings.ValidationSeedCount > MaxTrainingSeeds)
                errors.Add($"{nameof(settings.ValidationSeedCount)} must be in [1, {MaxTrainingSeeds}] " +
                           $"(was {settings.ValidationSeedCount})");
            if (!BodyGenes.IsValidSet(settings.ExpressedBodyGenes))
                errors.Add($"{nameof(settings.ExpressedBodyGenes)} has unknown genes ({(int)settings.ExpressedBodyGenes})");
            if (errors.Count > 0)
                throw new ArgumentException("Invalid evolution settings: " + string.Join("; ", errors));

            world = settings.World.Clone();
            world.InitialPopulation = 1;
            var validated = new SimulationConfig(world);

            ExperimentSeed = settings.ExperimentSeed;
            ExpressedBodyGenes = settings.ExpressedBodyGenes;
            EpisodeSeconds = settings.EpisodeSeconds;
            TrainingSeedsPerGeneration = settings.TrainingSeedsPerGeneration;
            EpisodeTicks = Math.Max(1L, (long)Math.Round((double)EpisodeSeconds * validated.TicksPerSecond));
            trainingRoot = SplitMix64.Output(ExperimentSeed, TrainingTag);
            ValidationSeed = settings.ValidationSeed;
            var validationRoot = SplitMix64.Output(ValidationSeed, ValidationTag);
            validationSeeds = new ulong[settings.ValidationSeedCount];
            for (var i = 0; i < validationSeeds.Length; i++)
                validationSeeds[i] = SplitMix64.Output(validationRoot, (ulong)i + 1);
        }

        public ulong ExperimentSeed { get; }

        /// <summary>Body genes that shape the episode creature; <see cref="BodyGeneSet.None"/> is the fixed body.</summary>
        public BodyGeneSet ExpressedBodyGenes { get; }

        public float EpisodeSeconds { get; }
        public int TrainingSeedsPerGeneration { get; }
        public ulong ValidationSeed { get; }
        public int ValidationSeedCount => validationSeeds.Length;

        /// <summary>Configured episode length in ticks; an episode always counts as this long, even after a death.</summary>
        public long EpisodeTicks { get; }

        public ulong TrainingSeed(int generation, int index)
        {
            if (generation < 0)
                throw new ArgumentOutOfRangeException(nameof(generation));
            if (index < 0 || index >= TrainingSeedsPerGeneration)
                throw new ArgumentOutOfRangeException(nameof(index));
            var generationSeed = SplitMix64.Output(trainingRoot, (ulong)generation + 1);
            return SplitMix64.Output(generationSeed, (ulong)index + 1);
        }

        public ulong[] TrainingSeeds(int generation)
        {
            var seeds = new ulong[TrainingSeedsPerGeneration];
            for (var i = 0; i < seeds.Length; i++)
                seeds[i] = TrainingSeed(generation, i);
            return seeds;
        }

        /// <summary>Held-out seeds, identical for every generation of the experiment (a fresh copy).</summary>
        public ulong[] ValidationSeeds() => (ulong[])validationSeeds.Clone();

        /// <summary>The genes of <paramref name="genes"/> that shape the episode creature; the others are neutral.</summary>
        public BodyGenes ExpressedGenes(BodyGenes genes) => genes.Masked(ExpressedBodyGenes);

        /// <summary>World configuration of one isolated episode (single creature) for a seed.</summary>
        public SimulationConfig EpisodeConfig(ulong episodeSeed)
        {
            var episode = world.Clone();
            episode.Seed = episodeSeed;
            return new SimulationConfig(episode);
        }
    }
}
