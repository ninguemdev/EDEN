using System;
using System.Collections.Generic;
using Eden.Brains;
using Eden.Domain;
using Eden.Evolution;

namespace Eden.Application
{
    /// <summary>A presentation replay of evaluated genomes. It owns its world/brain/RNG and never contributes to
    /// selection, evaluation counters, export or checkpoints. Rankings use completed training means, ties by stable ID.</summary>
    public sealed class RankedReplay
    {
        private readonly EvaluationPlan plan;
        private GenerationReport latest;
        private GenerationReport source;
        private int[] ranking = Array.Empty<int>();
        private int count = 5;
        private int requestedRank = 1;
        public RankedReplay(EvaluationPlan plan) { this.plan = plan ?? throw new ArgumentNullException(nameof(plan)); }
        public IsolatedEpisode Episode { get; private set; }
        public Individual Individual { get; private set; }
        public float Score { get; private set; }
        public int Rank { get; private set; }
        public int Generation => source?.Evaluation.Generation ?? -1;
        public int Count => Math.Min(count, ranking.Length);
        public void Offer(GenerationReport report)
        {
            if (report?.Evaluation.Mode == EvaluationMode.Training) latest = report;
        }
        public void Configure(int topCount, int rank)
        {
            if (topCount < 1 || topCount > ReproductionRules.MaxPopulationSize || rank < 1 || rank > topCount)
                throw new ArgumentOutOfRangeException(nameof(topCount));
            count = topCount; requestedRank = rank; Episode = null;
        }
        public void BeginLatest()
        {
            if (latest == null) return;
            source = latest;
            var candidates = new List<int>();
            for (var i = 0; i < source.Individuals.Count; i++)
                if (Fitness.IsValid(source.Evaluation.Candidates[i].MeanFitness)) candidates.Add(i);
            candidates.Sort((a, b) =>
            {
                var score = source.Evaluation.Candidates[b].MeanFitness.CompareTo(source.Evaluation.Candidates[a].MeanFitness);
                return score != 0 ? score : source.Individuals[a].Id.CompareTo(source.Individuals[b].Id);
            });
            ranking = candidates.ToArray();
            if (ranking.Length == 0) { Episode = null; return; }
            Rank = Math.Min(requestedRank, Count);
            Begin(Rank);
        }
        private void Begin(int rank)
        {
            Rank = rank;
            var candidate = ranking[rank - 1]; Individual = source.Individuals[candidate];
            var result = source.Evaluation.Candidates[candidate]; Score = result.MeanFitness;
            var preferredStage = 0;
            foreach (var stage in plan.Stages) if (stage.IsSearch) { preferredStage = stage.Index; break; }
            EpisodeResult? best = null;
            foreach (var episode in result.Episodes)
                if (episode.Stage == preferredStage && (!best.HasValue || episode.Fitness > best.Value.Fitness)) best = episode;
            var selected = best ?? result.Episodes[0];
            var genome = Individual.Genome;
            Episode = new IsolatedEpisode(plan, () => GenomeBrains.Create(genome), selected.EpisodeSeed, candidate,
                selected.SeedIndex, selected.Generation, selected.Mode, genome.Body, genome.Eyes, selected.Stage);
        }
        public void Advance(long ticks)
        {
            if (Episode == null) BeginLatest();
            Episode?.Advance(ticks);
        }
        public void Next()
        {
            if (Episode == null) { BeginLatest(); return; }
            if (Rank < Count) Begin(Rank + 1);
            else { requestedRank = 1; BeginLatest(); }
        }
        public string Describe() => Episode == null ? "Acompanhamento: aguardando uma geração avaliada."
            : $"REPLAY — g{Generation}, posição {Rank}/{Count}, indivíduo #{Individual.Id}, nota média {Score:0.000000}\n" +
              $"Melhor episódio de {plan.Stages[Episode.Stage].Name}: seed {Episode.EpisodeSeed}, " +
              $"{Episode.Tick * (double)Episode.Simulation.World.Config.TickDuration:0.0}/{plan.Stages[Episode.Stage].EpisodeSeconds:0.0} s. " +
              "Observação independente; o treino continua.";
    }
}
