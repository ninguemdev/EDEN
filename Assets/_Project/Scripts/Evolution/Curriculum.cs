using System;
using System.Collections.Generic;

namespace Eden.Evolution
{
    public static class Curriculum
    {
        public static readonly string[] Names = { "Interceptação", "Alinhamento", "Busca", "Sobrevivência", "Arena completa" };

        public static int StageForSeed(int stage, int seedIndex) =>
            stage > 0 && seedIndex % 5 == 0 ? stage - 1 : stage;

        public static void Observe(EvolutionSettings settings, GenerationEvaluation evaluation)
        {
            if (!settings.CurriculumEnabled || settings.CurriculumStage == 4) return;
            if (evaluation.Mode != EvaluationMode.Training)
                throw new ArgumentException("Only training can advance the curriculum.");
            var passed = false;
            foreach (var candidate in evaluation.Candidates)
            {
                var successes = 0;
                var current = 0;
                var currentSuccesses = 0;
                foreach (var episode in candidate.Episodes)
                {
                    var stage = StageForSeed(settings.CurriculumStage, episode.SeedIndex);
                    var success = episode.IsValid && episode.FoodItems > 0 && (stage < 3 || episode.Survived);
                    if (success) successes++;
                    if (stage == settings.CurriculumStage)
                    {
                        current++;
                        if (success) currentSuccesses++;
                    }
                }
                if (current > 0 && currentSuccesses >= Math.Ceiling(current * 0.8) &&
                    successes >= Math.Ceiling(candidate.Episodes.Count * 0.8) &&
                    successes - currentSuccesses == candidate.Episodes.Count - current) passed = true;
            }
            settings.CurriculumStreak = passed ? settings.CurriculumStreak + 1 : 0;
            if (settings.CurriculumStreak >= 20)
            {
                settings.CurriculumStage++;
                settings.CurriculumStreak = 0;
            }
        }
    }

    public static class CompetenceFitness
    {
        public const string Profile = "curriculum-lexicographic-v1";

        // Fixed radix digits give a transitive lexicographic order, exactly representable as a float.
        // Competence > reliable feeding/survival > useful collection > absorption efficiency > approach.
        public static float Score(IReadOnlyList<EpisodeResult> episodes)
        {
            double fed = 0, sustainable = 0, survival = 0, useful = 0, usefulItems = 0, food = 0, approach = 0;
            foreach (var e in episodes)
            {
                if (!e.IsValid) return Fitness.Worst;
                if (e.FoodItems > 0) fed++;
                if (e.FoodItems > 0 && e.Survived) sustainable++;
                survival += Math.Min(1, (double)e.SurvivedTicks / e.EpisodeTicks);
                useful += e.UsefulFoodEnergy;
                if (e.FoodEnergy > 0) usefulItems += e.FoodItems * (double)e.UsefulFoodEnergy / e.FoodEnergy;
                food += e.FoodEnergy;
                approach += e.Approach;
            }
            var n = episodes.Count;
            var tier = sustainable == n ? 3 : fed == n ? 2 : fed > 0 ? 1 : 0;
            var reliability = (int)Math.Floor(15 * (fed + sustainable) / (2 * n));
            var longevity = (int)Math.Floor(15 * survival / n);
            var collection = (int)Math.Min(63, Math.Floor(usefulItems / n));
            var efficiency = food > 0 ? (int)Math.Floor(15 * Math.Min(1, useful / food)) : 0;
            var proximity = (int)Math.Floor(15 * approach / n);
            return (((((tier * 16 + reliability) * 16 + longevity) * 64 + collection) * 16 + efficiency) * 16 + proximity);
        }
    }
}
