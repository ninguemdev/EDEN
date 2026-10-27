using System;
using System.Collections.Generic;
using Eden.Evolution;

namespace Eden.Application
{
    /// <summary>
    /// An <see cref="EvolutionSession"/> saved between ticks: settings, the current population and the whole lineage,
    /// the last training evaluation (needed to validate and to show the evolved population), the reference brains'
    /// episodes, the history shown in the lab and the evaluation in progress. Mutation needs no saved random state:
    /// each generation's stream (and its Gaussian spare) is created from the experiment seed and the generation when
    /// that generation is bred, all within one call, so no sampler is alive between ticks.
    /// </summary>
    public sealed class EvolutionSnapshot
    {
        public EvolutionSnapshot(EvolutionSettings settings, int validationInterval, int generation,
            IReadOnlyList<Individual> population, IReadOnlyList<LineageEntry> lineage,
            IReadOnlyList<Individual> lastTrainingIndividuals, IReadOnlyList<EpisodeResult> lastTrainingEpisodes,
            IReadOnlyList<EpisodeResult> baselineEpisodes, IReadOnlyList<GenerationSummary> training,
            IReadOnlyList<ValidationPoint> validation, long totalTicks, EvolutionPhase phase, EvaluationProgress job,
            NeatEvolutionState neat = null)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            Settings = settings.WithExperimentSeed(settings.ExperimentSeed);
            ValidationInterval = validationInterval;
            Generation = generation;
            Population = Copy(population ?? throw new ArgumentNullException(nameof(population)));
            Lineage = Copy(lineage ?? throw new ArgumentNullException(nameof(lineage)));
            LastTrainingIndividuals = lastTrainingIndividuals == null ? null : Copy(lastTrainingIndividuals);
            LastTrainingEpisodes = lastTrainingEpisodes == null ? null : Copy(lastTrainingEpisodes);
            BaselineEpisodes = baselineEpisodes == null ? null : Copy(baselineEpisodes);
            Training = Copy(training ?? throw new ArgumentNullException(nameof(training)));
            Validation = Copy(validation ?? throw new ArgumentNullException(nameof(validation)));
            TotalTicks = totalTicks;
            Phase = phase;
            Job = job ?? throw new ArgumentNullException(nameof(job));
            Neat = neat;
        }

        /// <summary>The settings the evolution was created with (the snapshot keeps its own copy).</summary>
        public EvolutionSettings Settings { get; }

        public int ValidationInterval { get; }

        /// <summary>Generation of <see cref="Population"/>.</summary>
        public int Generation { get; }

        /// <summary>Individuals of <see cref="Generation"/> in candidate order.</summary>
        public IReadOnlyList<Individual> Population { get; }

        /// <summary>Every individual created so far, in id order.</summary>
        public IReadOnlyList<LineageEntry> Lineage { get; }

        /// <summary>Individuals of the previous generation in candidate order; null in generation 0.</summary>
        public IReadOnlyList<Individual> LastTrainingIndividuals { get; }

        /// <summary>Their training episodes in candidate-major, seed-minor order; null in generation 0.</summary>
        public IReadOnlyList<EpisodeResult> LastTrainingEpisodes { get; }

        /// <summary>Random brain then heuristic brain on the validation seeds; null until they ran.</summary>
        public IReadOnlyList<EpisodeResult> BaselineEpisodes { get; }

        /// <summary>Training summary of every finished generation, in order.</summary>
        public IReadOnlyList<GenerationSummary> Training { get; }

        public IReadOnlyList<ValidationPoint> Validation { get; }
        public long TotalTicks { get; }
        public EvolutionPhase Phase { get; }

        /// <summary>The evaluation of <see cref="Phase"/> in progress.</summary>
        public EvaluationProgress Job { get; }

        /// <summary>Historical markings and species of a NEAT evolution; null for the MLP.</summary>
        public NeatEvolutionState Neat { get; }

        private static T[] Copy<T>(IReadOnlyList<T> items)
        {
            var copy = new T[items.Count];
            for (var i = 0; i < copy.Length; i++)
                copy[i] = items[i];
            return copy;
        }
    }

    /// <summary>
    /// What a NEAT evolution keeps between generations besides its genomes: the counters of its historical markings,
    /// its species (representatives, best fitness, last improvement) with the next species id, and the species of each
    /// individual of the last trained generation (null in generation 0).
    /// </summary>
    public sealed class NeatEvolutionState
    {
        public NeatEvolutionState(InnovationHistoryState innovations, IReadOnlyList<NeatSpeciesState> species,
            int nextSpeciesId, IReadOnlyList<int> lastTrainingSpecies)
        {
            Innovations = innovations;
            Species = Copy(species ?? throw new ArgumentNullException(nameof(species)));
            NextSpeciesId = nextSpeciesId;
            LastTrainingSpecies = lastTrainingSpecies == null ? null : Copy(lastTrainingSpecies);
        }

        public InnovationHistoryState Innovations { get; }
        public IReadOnlyList<NeatSpeciesState> Species { get; }
        public int NextSpeciesId { get; }
        public IReadOnlyList<int> LastTrainingSpecies { get; }

        private static T[] Copy<T>(IReadOnlyList<T> items)
        {
            var copy = new T[items.Count];
            for (var i = 0; i < copy.Length; i++)
                copy[i] = items[i];
            return copy;
        }
    }
}
