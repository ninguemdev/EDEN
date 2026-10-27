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
        public EpisodeResult(EvaluationMode mode, int generation, int candidate, int seedIndex, ulong episodeSeed,
            long episodeTicks, float episodeSeconds, float foodEnergy, long survivedTicks, bool survived, float finalEnergy)
        {
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

        public int SeedIndex { get; }
        public ulong EpisodeSeed { get; }

        /// <summary>Configured episode length; the same for every result of a plan.</summary>
        public long EpisodeTicks { get; }

        public float FoodEnergy { get; }
        public float Fitness { get; }

        /// <summary>Ticks the creature lived (its age at the end or at death).</summary>
        public long SurvivedTicks { get; }

        /// <summary>True when the creature was still alive at the end of the configured duration.</summary>
        public bool Survived { get; }

        public float FinalEnergy { get; }
        public bool IsValid => Evolution.Fitness.IsValid(Fitness);
    }

    /// <summary>Aggregate of one candidate over its seeds; keeps the per-seed results it was computed from.</summary>
    public sealed class CandidateEvaluation
    {
        public CandidateEvaluation(int candidate, IReadOnlyList<EpisodeResult> episodes, int ticksPerSecond)
        {
            if (episodes == null || episodes.Count == 0)
                throw new ArgumentException("At least one episode is required.", nameof(episodes));

            Candidate = candidate;
            Episodes = episodes;
            double fitness = 0, survivedSeconds = 0, finalEnergy = 0, food = 0;
            var survivors = 0;
            var valid = true;
            foreach (var episode in episodes)
            {
                valid &= episode.IsValid;
                fitness += episode.Fitness;
                food += episode.FoodEnergy;
                survivedSeconds += (double)episode.SurvivedTicks / ticksPerSecond;
                finalEnergy += episode.FinalEnergy;
                if (episode.Survived)
                    survivors++;
            }

            var count = episodes.Count;
            MeanFitness = valid ? (float)(fitness / count) : Fitness.Worst;
            MeanFoodEnergy = (float)(food / count);
            SurvivalRate = (float)survivors / count;
            MeanSurvivedSeconds = (float)(survivedSeconds / count);
            MeanFinalEnergy = (float)(finalEnergy / count);
        }

        public int Candidate { get; }
        public IReadOnlyList<EpisodeResult> Episodes { get; }

        /// <summary>Mean episode fitness, or <see cref="Fitness.Worst"/> if any episode was invalid.</summary>
        public float MeanFitness { get; }

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
            IReadOnlyList<EpisodeResult> episodes, int ticksPerSecond)
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
                    .ToArray(), ticksPerSecond);
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
