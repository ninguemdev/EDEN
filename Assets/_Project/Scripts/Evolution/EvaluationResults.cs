using System;
using System.Collections.Generic;

namespace Eden.Evolution
{
    /// <summary>
    /// Fitness of one episode: total food energy eaten divided by the configured episode duration (energy per
    /// simulated second). The denominator never shrinks when the creature dies early. Invalid inputs give
    /// <see cref="Worst"/>, which ranks below every valid result.
    /// </summary>
    public static class Fitness
    {
        public const float Worst = float.NegativeInfinity;

        /// <summary>
        /// Versioned name of this scoring rule, recorded next to exported scores: scores of different profiles are not
        /// comparable and must not rank genomes together.
        /// </summary>
        public const string Profile = "food-energy-per-second-v1";

        public static float FromFood(float foodEnergy, float episodeSeconds)
        {
            if (float.IsNaN(foodEnergy) || float.IsInfinity(foodEnergy) || foodEnergy < 0f ||
                float.IsNaN(episodeSeconds) || episodeSeconds <= 0f || float.IsInfinity(episodeSeconds))
                return Worst;
            return foodEnergy / episodeSeconds;
        }

        public static bool IsValid(float fitness) => !float.IsNaN(fitness) && !float.IsInfinity(fitness);
    }

    /// <summary>Outcome of one candidate on one seed. Survival and energy are kept apart from fitness.</summary>
    public readonly struct EpisodeResult
    {
        /// <param name="components">
        /// Values of the stage's reward components beyond the fields kept here, in the stage's order; null (the legacy
        /// profile) when there are none.
        /// </param>
        /// <param name="stage">Index of the plan stage the episode ran (0 for a single-stage plan).</param>
        public EpisodeResult(EvaluationMode mode, int generation, int candidate, int seedIndex, ulong episodeSeed,
            long episodeTicks, float episodeSeconds, float foodEnergy, long survivedTicks, bool survived, float finalEnergy,
            IReadOnlyList<float> components = null, int stage = 0)
        {
            Components = components;
            Stage = stage;
            Mode = mode;
            Generation = generation;
            Candidate = candidate;
            SeedIndex = seedIndex;
            EpisodeSeed = episodeSeed;
            EpisodeTicks = episodeTicks;
            FoodEnergy = foodEnergy;
            SurvivedTicks = survivedTicks;
            Survived = survived;
            FinalEnergy = finalEnergy;
            Fitness = Evolution.Fitness.FromFood(foodEnergy, episodeSeconds);
        }

        public EvaluationMode Mode { get; }
        public int Generation { get; }

        /// <summary>Index of the candidate (genome) inside its generation.</summary>
        public int Candidate { get; }

        /// <summary>Position of the seed among the evaluation's seeds, which list every stage's seeds stage after stage.</summary>
        public int SeedIndex { get; }

        public ulong EpisodeSeed { get; }

        /// <summary>Index of the plan stage the episode ran.</summary>
        public int Stage { get; }

        /// <summary>Configured episode length; the same for every result of a stage.</summary>
        public long EpisodeTicks { get; }

        public float FoodEnergy { get; }
        public float Fitness { get; }

        /// <summary>Ticks the creature lived (its age at the end or at death).</summary>
        public long SurvivedTicks { get; }

        /// <summary>True when the creature was still alive at the end of the configured duration.</summary>
        public bool Survived { get; }

        public float FinalEnergy { get; }

        /// <summary>Further reward components of the episode's stage; null when the stage reports none beyond the fields.</summary>
        public IReadOnlyList<float> Components { get; }

        public bool IsValid => Evolution.Fitness.IsValid(Fitness);
    }

    /// <summary>
    /// Aggregate of one candidate over its seeds; keeps the per-seed results it was computed from. With several stages
    /// each stage is averaged on its own seeds and the stage means are combined by the weights of the profile.
    /// </summary>
    public sealed class CandidateEvaluation
    {
        /// <param name="profile">How stage means combine; null (or a single stage) for one stage, as before stages.</param>
        public CandidateEvaluation(int candidate, IReadOnlyList<EpisodeResult> episodes, int ticksPerSecond,
            RewardProfile profile = null)
        {
            if (episodes == null || episodes.Count == 0)
                throw new ArgumentException("At least one episode is required.", nameof(episodes));

            var stageCount = profile?.Stages.Count ?? 1;
            var stageFitness = new double[stageCount];
            var stageEpisodes = new int[stageCount];
            var stageValid = new bool[stageCount];
            for (var k = 0; k < stageCount; k++)
                stageValid[k] = true;
            Candidate = candidate;
            Episodes = episodes;
            double survivedSeconds = 0, finalEnergy = 0, food = 0;
            var survivors = 0;
            foreach (var episode in episodes)
            {
                if (episode.Stage < 0 || episode.Stage >= stageCount)
                    throw new ArgumentException($"An episode of stage {episode.Stage} is not one of {stageCount} stages.",
                        nameof(episodes));
                stageValid[episode.Stage] &= episode.IsValid;
                stageFitness[episode.Stage] += episode.Fitness;
                stageEpisodes[episode.Stage]++;
                food += episode.FoodEnergy;
                survivedSeconds += (double)episode.SurvivedTicks / ticksPerSecond;
                finalEnergy += episode.FinalEnergy;
                if (episode.Survived)
                    survivors++;
            }

            var means = new float[stageCount];
            for (var k = 0; k < stageCount; k++)
            {
                if (stageEpisodes[k] == 0)
                    throw new ArgumentException($"Stage {k} has no episode.", nameof(episodes));
                means[k] = stageValid[k] ? (float)(stageFitness[k] / stageEpisodes[k]) : Fitness.Worst;
            }

            var count = episodes.Count;
            StageFitness = means;
            MeanFitness = stageCount == 1 ? means[0] : profile.Combine(means);
            MeanFoodEnergy = (float)(food / count);
            SurvivalRate = (float)survivors / count;
            MeanSurvivedSeconds = (float)(survivedSeconds / count);
            MeanFinalEnergy = (float)(finalEnergy / count);
        }

        public int Candidate { get; }
        public IReadOnlyList<EpisodeResult> Episodes { get; }

        /// <summary>
        /// Mean episode fitness (with several stages, the profile's combination of <see cref="StageFitness"/>), or
        /// <see cref="Fitness.Worst"/> if any episode was invalid.
        /// </summary>
        public float MeanFitness { get; }

        /// <summary>Mean episode fitness of each stage, or <see cref="Fitness.Worst"/> for a stage with an invalid episode.</summary>
        public IReadOnlyList<float> StageFitness { get; }

        public float MeanFoodEnergy { get; }
        public float SurvivalRate { get; }
        public float MeanSurvivedSeconds { get; }
        public float MeanFinalEnergy { get; }
    }

    /// <summary>All results of one generation for one mode, per candidate × seed and aggregated per candidate.</summary>
    public sealed class GenerationEvaluation
    {
        public GenerationEvaluation(int generation, EvaluationMode mode, IReadOnlyList<ulong> seeds,
            IReadOnlyList<EpisodeResult> episodes, IReadOnlyList<CandidateEvaluation> candidates)
        {
            Generation = generation;
            Mode = mode;
            Seeds = seeds ?? throw new ArgumentNullException(nameof(seeds));
            Episodes = episodes ?? throw new ArgumentNullException(nameof(episodes));
            Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        }

        /// <summary>
        /// Evaluation rebuilt from saved per-episode results, which must be complete and in candidate-major, seed-minor
        /// order for these seeds, generation and mode; candidates are aggregated as during the evaluation.
        /// </summary>
        public static GenerationEvaluation FromEpisodes(int generation, EvaluationMode mode, IReadOnlyList<ulong> seeds,
            IReadOnlyList<EpisodeResult> episodes, int ticksPerSecond, RewardProfile profile = null)
        {
            if (seeds == null || seeds.Count == 0)
                throw new ArgumentException("At least one seed is required.", nameof(seeds));
            if (episodes == null || episodes.Count == 0 || episodes.Count % seeds.Count != 0)
                throw new ArgumentException($"Expected a whole number of candidates × {seeds.Count} seeds.", nameof(episodes));
            if (ticksPerSecond < 1)
                throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));

            var ordered = new EpisodeResult[episodes.Count];
            var candidates = new CandidateEvaluation[episodes.Count / seeds.Count];
            for (var i = 0; i < ordered.Length; i++)
            {
                var episode = episodes[i];
                var candidate = i / seeds.Count;
                var seedIndex = i % seeds.Count;
                if (episode.Generation != generation || episode.Mode != mode || episode.Candidate != candidate ||
                    episode.SeedIndex != seedIndex || episode.EpisodeSeed != seeds[seedIndex])
                    throw new ArgumentException($"Episode {i} is not candidate {candidate} on seed {seedIndex}.", nameof(episodes));
                ordered[i] = episode;
            }

            var seedCopy = new ulong[seeds.Count];
            for (var s = 0; s < seedCopy.Length; s++)
                seedCopy[s] = seeds[s];
            for (var c = 0; c < candidates.Length; c++)
                candidates[c] = new CandidateEvaluation(c, new ArraySegment<EpisodeResult>(ordered, c * seeds.Count, seeds.Count)
                    .ToArray(), ticksPerSecond, profile);
            return new GenerationEvaluation(generation, mode, seedCopy, ordered, candidates);
        }

        public int Generation { get; }
        public EvaluationMode Mode { get; }
        public IReadOnlyList<ulong> Seeds { get; }

        /// <summary>Every episode in candidate-major, seed-minor order.</summary>
        public IReadOnlyList<EpisodeResult> Episodes { get; }

        public IReadOnlyList<CandidateEvaluation> Candidates { get; }
    }
}
