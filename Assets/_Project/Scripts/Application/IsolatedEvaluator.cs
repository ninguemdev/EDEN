using System;
using System.Collections.Generic;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;
using Eden.Simulation;

namespace Eden.Application
{
    /// <summary>
    /// Runs candidates in isolated episodes: each candidate is the only creature of a world built from the episode
    /// seed, so every candidate meets the same arena, food, start position, heading and brain stream, and nobody can
    /// take food from anybody else. Episodes run sequentially. After the creature dies the remaining ticks cannot
    /// change any result, so they are not simulated, but the episode still counts as its configured duration.
    /// </summary>
    public sealed class IsolatedEvaluator
    {
        private readonly EvaluationPlan plan;

        public IsolatedEvaluator(EvaluationPlan plan)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
        }

        /// <summary>Training evaluation of a generation of genomes on its training seeds.</summary>
        public GenerationEvaluation EvaluateTraining(IReadOnlyList<Genome> genomes, int generation)
        {
            if (genomes == null)
                throw new ArgumentNullException(nameof(genomes));
            var candidates = new Func<IBrain>[genomes.Count];
            for (var i = 0; i < genomes.Count; i++)
            {
                var genome = genomes[i] ?? throw new ArgumentException($"Genome {i} is null.", nameof(genomes));
                candidates[i] = () => new MlpBrain(genome);
            }

            return Evaluate(candidates, plan.TrainingSeeds(generation), generation, EvaluationMode.Training);
        }

        /// <summary>Evaluates any brains (genomes or reference brains) on explicit seeds.</summary>
        public GenerationEvaluation Evaluate(IReadOnlyList<Func<IBrain>> candidates, IReadOnlyList<ulong> seeds,
            int generation, EvaluationMode mode)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));
            if (seeds == null || seeds.Count == 0)
                throw new ArgumentException("At least one seed is required.", nameof(seeds));

            var episodes = new List<EpisodeResult>(candidates.Count * seeds.Count);
            var aggregates = new CandidateEvaluation[candidates.Count];
            var ticksPerSecond = plan.EpisodeConfig(seeds[0]).TicksPerSecond;
            for (var c = 0; c < candidates.Count; c++)
            {
                var perSeed = new EpisodeResult[seeds.Count];
                for (var s = 0; s < seeds.Count; s++)
                    perSeed[s] = RunEpisode(candidates[c], seeds[s], c, s, generation, mode);
                episodes.AddRange(perSeed);
                aggregates[c] = new CandidateEvaluation(c, perSeed, ticksPerSecond);
            }

            return new GenerationEvaluation(generation, mode, seeds, episodes, aggregates);
        }

        public EpisodeResult RunEpisode(Func<IBrain> createBrain, ulong episodeSeed, int candidate, int seedIndex,
            int generation, EvaluationMode mode)
        {
            if (createBrain == null)
                throw new ArgumentNullException(nameof(createBrain));

            var config = plan.EpisodeConfig(episodeSeed);
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(config));
            var creature = simulation.World.Creatures[0];
            var controller = new BrainController(config, _ => createBrain());

            for (var tick = 0L; tick < plan.EpisodeTicks && !simulation.IsExtinct; tick++)
                simulation.Step(controller);

            return new EpisodeResult(mode, generation, candidate, seedIndex, episodeSeed, plan.EpisodeTicks,
                plan.EpisodeSeconds, creature.FoodEnergyEaten, creature.AgeTicks, creature.IsAlive, creature.Energy);
        }
    }
}
