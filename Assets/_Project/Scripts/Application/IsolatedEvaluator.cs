using System;
using System.Collections.Generic;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;

namespace Eden.Application
{
    /// <summary>
    /// Runs candidates in isolated episodes: each candidate is the only creature of a world built from the episode
    /// seed, so every candidate meets the same arena, food, start position, heading and brain stream, and nobody can
    /// take food from anybody else. A genome's body genes shape its creature as the plan expresses them; reference
    /// brains always have the reference body. Episodes run sequentially, either all at once or as an <see cref="EvaluationJob"/>
    /// advanced a few ticks at a time; both give the same results.
    /// </summary>
    public sealed class IsolatedEvaluator
    {
        private readonly EvaluationPlan plan;

        public IsolatedEvaluator(EvaluationPlan plan)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
        }

        /// <summary>Training evaluation of a generation of genomes on its training seeds.</summary>
        public GenerationEvaluation EvaluateTraining(IReadOnlyList<Genome> genomes, int generation) =>
            TrainingJob(genomes, generation).RunToEnd();

        /// <summary>Evaluation of a generation of genomes on the held-out validation seeds.</summary>
        public GenerationEvaluation EvaluateValidation(IReadOnlyList<Genome> genomes, int generation) =>
            ValidationJob(genomes, generation).RunToEnd();

        /// <summary>
        /// Evaluates any brains (genomes or reference brains) on explicit seeds; <paramref name="bodies"/> gives each
        /// candidate's body genes, and null means neutral genes for all.
        /// </summary>
        public GenerationEvaluation Evaluate(IReadOnlyList<Func<IBrain>> candidates, IReadOnlyList<ulong> seeds,
            int generation, EvaluationMode mode, IReadOnlyList<BodyGenes> bodies = null) =>
            Job(candidates, seeds, generation, mode, bodies).RunToEnd();

        public EvaluationJob TrainingJob(IReadOnlyList<Genome> genomes, int generation) =>
            Job(BrainsOf(genomes), plan.TrainingSeeds(generation), generation, EvaluationMode.Training, BodiesOf(genomes));

        public EvaluationJob ValidationJob(IReadOnlyList<Genome> genomes, int generation) =>
            Job(BrainsOf(genomes), plan.ValidationSeeds(), generation, EvaluationMode.Validation, BodiesOf(genomes));

        public EvaluationJob Job(IReadOnlyList<Func<IBrain>> candidates, IReadOnlyList<ulong> seeds, int generation,
            EvaluationMode mode, IReadOnlyList<BodyGenes> bodies = null)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));
            return new EvaluationJob(plan, candidates, seeds, generation, mode, bodies);
        }

        public EpisodeResult RunEpisode(Func<IBrain> createBrain, ulong episodeSeed, int candidate, int seedIndex,
            int generation, EvaluationMode mode, BodyGenes body = default)
        {
            var episode = new IsolatedEpisode(plan, createBrain, episodeSeed, candidate, seedIndex, generation, mode, body);
            episode.Advance(long.MaxValue);
            return episode.Result();
        }

        private static Func<IBrain>[] BrainsOf(IReadOnlyList<Genome> genomes)
        {
            if (genomes == null)
                throw new ArgumentNullException(nameof(genomes));
            var candidates = new Func<IBrain>[genomes.Count];
            for (var i = 0; i < genomes.Count; i++)
            {
                var genome = genomes[i] ?? throw new ArgumentException($"Genome {i} is null.", nameof(genomes));
                candidates[i] = () => GenomeBrains.Create(genome);
            }

            return candidates;
        }

        // Called after BrainsOf, which has already checked the list.
        private static BodyGenes[] BodiesOf(IReadOnlyList<Genome> genomes)
        {
            var bodies = new BodyGenes[genomes.Count];
            for (var i = 0; i < genomes.Count; i++)
                bodies[i] = genomes[i].Body;
            return bodies;
        }
    }
}
