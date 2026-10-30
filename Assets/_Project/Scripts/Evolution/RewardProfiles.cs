using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>The formulas of the stage scorings beyond the legacy one; every score is in [0, 1] or invalid.</summary>
    public static class StageScores
    {
        /// <summary>
        /// Balanced resources: <paramref name="survivalWeight"/> × survival + <paramref name="replenishWeight"/> × useful
        /// replenishment (the mean of energy and hydration, each capped at 1, so plenty of one never makes up for the
        /// other; energy alone without water) + <paramref name="reservesWeight"/> × final reserves (0 after a death).
        /// </summary>
        /// <param name="hydrationReplenished">NaN in a world without water.</param>
        public static float BalancedResources(double survivalWeight, double replenishWeight, double reservesWeight,
            double survival, double energyReplenished, double hydrationReplenished, double reserves)
        {
            var replenished = double.IsNaN(hydrationReplenished)
                ? energyReplenished
                : (energyReplenished + hydrationReplenished) / 2;
            return Valid(survivalWeight * survival + replenishWeight * replenished + reservesWeight * reserves);
        }

        public static float ExploreCollect(double exploration, double detection, double collection)
        {
            foreach (var value in new[] { exploration, detection, collection })
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1)
                    throw new ArgumentOutOfRangeException(nameof(exploration), "E, D and C must be finite in [0, 1].");
            return (float)((0.65 * exploration + 0.35 * collection) * (1 + 0.5 * detection + 2 * collection) / 3.5);
        }

        /// <summary>Share of <paramref name="need"/> that <paramref name="taken"/> covers, capped at 1 (1 when nothing is needed).</summary>
        public static double Replenished(double taken, double need) => need > 0 ? Clamp01(taken / need) : 1;

        /// <summary>
        /// Search: (Σ over reached targets of (1 − β + β × speed) + α × approach) ÷ targets. The approach counts only
        /// while a target is left, and at most α (below one target), so reaching always beats approaching.
        /// </summary>
        public static float Search(int targets, double alpha, double beta, IReadOnlyList<float> speeds, double approach)
        {
            if (targets < 1 || speeds == null || speeds.Count > targets)
                return Fitness.Worst;
            var sum = 0.0;
            foreach (var speed in speeds)
                sum += 1 - beta + beta * Clamp01(speed);
            if (speeds.Count < targets)
                sum += alpha * Clamp01(approach);
            return Valid(sum / targets);
        }

        /// <summary>
        /// Speed of reaching a target: the path length of the stretch over the distance the maximum speed covers in the
        /// time it took, in [0, 1]; 1 is the shortest path at full speed.
        /// </summary>
        public static float Speed(double pathLength, double maxSpeed, double seconds) =>
            maxSpeed > 0 && seconds > 0 ? (float)Clamp01(pathLength / (maxSpeed * seconds)) : 0f;

        /// <summary>
        /// Approach of the open stretch: how much of the path to the nearest target left at its start the best position
        /// reached since has cut, in [0, 1]. Going in circles or back and forth never raises it.
        /// </summary>
        public static float Approach(double startDistance, double bestDistance) =>
            startDistance > 0 && !double.IsInfinity(startDistance) && !double.IsNaN(bestDistance)
                ? (float)Clamp01((startDistance - bestDistance) / startDistance)
                : 0f;

        private static double Clamp01(double value) => value < 0 || double.IsNaN(value) ? 0 : value > 1 ? 1 : value;

        private static float Valid(double score) =>
            double.IsNaN(score) || double.IsInfinity(score) ? Fitness.Worst : (float)score;
    }

    /// <summary>Choices of the two-stage search profile and of its search stage's world.</summary>
    public sealed class TwoStageOptions
    {
        /// <summary>Share of the search stage in the total (the resources stage gets the rest).</summary>
        public double SearchWeight = 0.3;

        public int FoodTargets = 3;
        public int WaterTargets = 2;

        /// <summary>Shortest path between targets and from the founder (twice the vision range; decision D7).</summary>
        public double MinDistance = 30;

        /// <summary>Most the approach to the next target is worth, as a share of one target.</summary>
        public double Alpha = 0.5;

        /// <summary>Share of each reached target that depends on how fast it was reached.</summary>
        public double Beta = 0.3;

        public double WaterSide = 2;
        public double MaxArenaSide = 200;

        /// <summary>Largest share of the search stage the greedy tour at full speed may take; longer is refused.</summary>
        public double FeasibleFraction = 0.5;

        public double SurvivalWeight = 0.5;
        public double ReplenishWeight = 0.3;
        public double ReservesWeight = 0.2;

        /// <summary>Length of a search episode.</summary>
        public float SearchSeconds = 120f;

        /// <summary>Side of the search arena before it is enlarged to fit the targets.</summary>
        public float SearchArena = 50f;

        public ObstacleLayout SearchObstacles = ObstacleLayout.Pillars;

        /// <summary>Training and validation seeds of the search stage; 0 takes the experiment's.</summary>
        public int SearchTrainingSeeds;

        public int SearchValidationSeeds;

        /// <summary>Food of the resources world (decision D7: 64); null keeps the experiment's.</summary>
        public int? ResourceFood = 64;

        /// <summary>Obstacles of the resources world (decision D7: pillars); null keeps the experiment's.</summary>
        public ObstacleLayout? ResourceObstacles = ObstacleLayout.Pillars;

        public TwoStageOptions Clone() => (TwoStageOptions)MemberwiseClone();
    }

    /// <summary>The reward profiles beyond the legacy one, built from their parameters.</summary>
    public static class RewardProfiles
    {
        public const string BalancedResourcesId = "balanced-resources-v1";
        public const string BalancedResourcesDryId = "balanced-resources-dry-v1";
        public const string TwoStageSearchId = "two-stage-search-v1";
        public const string ActiveSearchId = "search-explore-collect-active-v1";
        public const string ActiveTwoStageId = "two-stage-explore-collect-active-v1";
        public static bool IsActive(RewardProfile profile) =>
            profile.Id == ActiveSearchId || profile.Id == ActiveTwoStageId;

        public const string ResourcesStage = "resources";
        public const string SearchStage = "search";

        public const string SurvivalWeight = "survival_weight";
        public const string ReplenishWeight = "replenish_weight";
        public const string ReservesWeight = "reserves_weight";
        public const string SearchWeight = "search_weight";
        public const string FoodTargets = "food_targets";
        public const string WaterTargets = "water_targets";
        public const string MinDistance = "min_distance";
        public const string Alpha = "alpha";
        public const string Beta = "beta";
        public const string WaterSide = "water_side";
        public const string MaxArena = "max_arena";
        public const string FeasibleFraction = "feasible_fraction";

        private static readonly string[] BalancedNames =
        {
            "survival", "energy_replenished", "hydration_replenished", "reserves", "absorbed_energy", "wasted_energy",
            "drunk"
        };

        private static readonly string[] BalancedDryNames =
            { "survival", "energy_replenished", "reserves", "absorbed_energy", "wasted_energy" };

        private static readonly string[] SearchNames =
        {
            "collected", "collected_food", "collected_water", "speed", "approach", "score_without_aid", "uncharged_energy"
        };

        /// <summary>Balanced resources alone, one stage (with or without water).</summary>
        public static RewardProfile BalancedResources(bool water, double survival = 0.5, double replenish = 0.3,
            double reserves = 0.2)
        {
            CheckWeights(survival, replenish, reserves);
            return new RewardProfile(water ? BalancedResourcesId : BalancedResourcesDryId,
                new[] { ResourcesStageOf(water, 1) }, BalancedParameters(survival, replenish, reserves));
        }

        /// <summary>
        /// two-stage-search-v1: the resources stage (the experiment's world) first and the search stage second, weighted
        /// 1 − <see cref="TwoStageOptions.SearchWeight"/> and <see cref="TwoStageOptions.SearchWeight"/>.
        /// </summary>
        public static RewardProfile TwoStageSearch(TwoStageOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            CheckWeights(options.SurvivalWeight, options.ReplenishWeight, options.ReservesWeight);
            var errors = new List<string>();
            if (!(options.SearchWeight > 0 && options.SearchWeight < 1))
                errors.Add($"the search weight must be in (0, 1) (was {options.SearchWeight})");
            if (!(options.Alpha >= 0 && options.Alpha < 1))
                errors.Add($"alpha must be in [0, 1) (was {options.Alpha})");
            if (!(options.Beta >= 0 && options.Beta <= 1))
                errors.Add($"beta must be in [0, 1] (was {options.Beta})");
            if (!(options.FeasibleFraction > 0 && options.FeasibleFraction <= 1))
                errors.Add($"the feasible fraction must be in (0, 1] (was {options.FeasibleFraction})");
            if (errors.Count > 0)
                throw new ArgumentException("Invalid two-stage profile: " + string.Join("; ", errors));
            // Validates the targets.
            new SearchTargets(options.FoodTargets, options.WaterTargets, (float)options.MinDistance,
                (float)options.WaterSide, (float)options.MaxArenaSide);

            var water = options.WaterTargets > 0;
            var parameters = new List<RewardParameter>
            {
                new RewardParameter(SearchWeight, options.SearchWeight),
                new RewardParameter(FoodTargets, options.FoodTargets),
                new RewardParameter(WaterTargets, options.WaterTargets),
                new RewardParameter(MinDistance, options.MinDistance),
                new RewardParameter(Alpha, options.Alpha),
                new RewardParameter(Beta, options.Beta),
                new RewardParameter(WaterSide, options.WaterSide),
                new RewardParameter(MaxArena, options.MaxArenaSide),
                new RewardParameter(FeasibleFraction, options.FeasibleFraction)
            };
            parameters.AddRange(BalancedParameters(options.SurvivalWeight, options.ReplenishWeight, options.ReservesWeight));
            return new RewardProfile(TwoStageSearchId, new[]
            {
                ResourcesStageOf(water, 1 - options.SearchWeight),
                new RewardStage(SearchStage, options.SearchWeight, StageScoring.Search, SearchNames)
            }, parameters);
        }

        /// <summary>
        /// The profile <paramref name="id"/> built again from its recorded parameters (a checkpoint or a file); null for an
        /// unknown id. The caller compares the hash.
        /// </summary>
        public static RewardProfile Create(string id, IReadOnlyList<RewardParameter> parameters)
        {
            double Value(string name, double fallback)
            {
                foreach (var parameter in parameters)
                {
                    if (parameter.Name == name)
                        return parameter.Value;
                }

                return fallback;
            }

            switch (id)
            {
                case Fitness.Profile:
                    return RewardProfile.Legacy;
                case BalancedResourcesId:
                case BalancedResourcesDryId:
                    return BalancedResources(id == BalancedResourcesId, Value(SurvivalWeight, 0.5),
                        Value(ReplenishWeight, 0.3), Value(ReservesWeight, 0.2));
                case TwoStageSearchId:
                    return TwoStageSearch(OptionsOf(parameters));
                case ActiveSearchId:
                case ActiveTwoStageId:
                    return ActiveSearch(OptionsOf(parameters), ActiveSearchOptions.Read(parameters), id == ActiveSearchId);
                default:
                    return null;
            }
        }

        /// <summary>The profile choices recorded in <paramref name="parameters"/> (the stage worlds keep their defaults).</summary>
        public static TwoStageOptions OptionsOf(IReadOnlyList<RewardParameter> parameters)
        {
            var options = new TwoStageOptions();
            foreach (var parameter in parameters)
            {
                var v = parameter.Value;
                switch (parameter.Name)
                {
                    case SearchWeight: options.SearchWeight = v; break;
                    case FoodTargets: options.FoodTargets = (int)v; break;
                    case WaterTargets: options.WaterTargets = (int)v; break;
                    case MinDistance: options.MinDistance = v; break;
                    case Alpha: options.Alpha = v; break;
                    case Beta: options.Beta = v; break;
                    case WaterSide: options.WaterSide = v; break;
                    case MaxArena: options.MaxArenaSide = v; break;
                    case FeasibleFraction: options.FeasibleFraction = v; break;
                    case SurvivalWeight: options.SurvivalWeight = v; break;
                    case ReplenishWeight: options.ReplenishWeight = v; break;
                    case ReservesWeight: options.ReservesWeight = v; break;
                }
            }

            return options;
        }

        /// <summary>The search targets of a profile with a search stage.</summary>
        public static SearchTargets TargetsOf(RewardProfile profile) =>
            new SearchTargets((int)profile.Parameter(FoodTargets, 3), (int)profile.Parameter(WaterTargets, 2),
                (float)profile.Parameter(MinDistance, 30), (float)profile.Parameter(WaterSide, 2),
                (float)profile.Parameter(MaxArena, 200));

        /// <summary>
        /// Scores the experiment with two-stage-search-v1: the experiment's world becomes the resources stage (with water,
        /// unless no water target is asked for, and the food and obstacles of <paramref name="options"/> when given) and a
        /// search stage of its own is added after it.
        /// </summary>
        public static void UseTwoStageSearch(EvolutionSettings settings, TwoStageOptions options)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            settings.Profile = TwoStageSearch(options);
            var world = settings.World;
            if (options.ResourceObstacles.HasValue)
            {
                world.SolidBodies |= options.ResourceObstacles.Value != ObstacleLayout.None;
                world.Obstacles = ObstacleLayouts.Create(options.ResourceObstacles.Value, world);
            }

            if (options.ResourceFood.HasValue)
                world.InitialFood = options.ResourceFood.Value;
            if (options.WaterTargets > 0 && world.Water.Length == 0)
            {
                world.SolidBodies = true;
                world.Water = WaterLayouts.Create(WaterLayout.Ponds, world);
            }

            var search = world.Clone();
            search.ArenaWidth = options.SearchArena;
            search.ArenaHeight = options.SearchArena;
            search.SolidBodies = true;
            search.Obstacles = ObstacleLayouts.Create(options.SearchObstacles, search);
            search.Water = Array.Empty<WaterSource>();
            search.InitialFood = 0;
            search.MaxAgeSeconds = Math.Max(search.MaxAgeSeconds, 2f * options.SearchSeconds);
            settings.FurtherStages = new List<StageSettings>
            {
                new StageSettings
                {
                    World = search,
                    EpisodeSeconds = options.SearchSeconds,
                    TrainingSeedsPerGeneration = options.SearchTrainingSeeds > 0
                        ? options.SearchTrainingSeeds
                        : settings.TrainingSeedsPerGeneration,
                    ValidationSeedCount = options.SearchValidationSeeds > 0
                        ? options.SearchValidationSeeds
                        : settings.ValidationSeedCount
                }
            };
        }

        public static RewardProfile ActiveSearch(TwoStageOptions search, ActiveSearchOptions active, bool onlySearch)
        {
            active.Validate();
            if (active.MaxAge < search.SearchSeconds) throw new ArgumentException("Search maximum age precedes its time limit.");
            var old = TwoStageSearch(search); // validates shared layout and resource options
            var parameters = new List<RewardParameter>(old.Parameters);
            parameters.AddRange(active.Parameters());
            var names = new List<string>();
            foreach (var component in RewardComponent.Of(StageScoring.ExploreCollectActive)) names.Add(component.Name);
            var stage = new RewardStage(SearchStage, onlySearch ? 1 : search.SearchWeight,
                StageScoring.ExploreCollectActive, names.ToArray());
            return new RewardProfile(onlySearch ? ActiveSearchId : ActiveTwoStageId,
                onlySearch ? new[] { stage } : new[] { old.Stages[0], stage }, parameters);
        }

        public static void UseActiveSearch(EvolutionSettings settings, TwoStageOptions search,
            ActiveSearchOptions active, bool onlySearch)
        {
            var profile = ActiveSearch(search, active, onlySearch);
            UseTwoStageSearch(settings, search);
            var stage = settings.FurtherStages[0];
            active.Apply(stage.World);
            if (onlySearch)
            {
                settings.World = stage.World;
                settings.EpisodeSeconds = stage.EpisodeSeconds;
                settings.TrainingSeedsPerGeneration = stage.TrainingSeedsPerGeneration;
                settings.ValidationSeedCount = stage.ValidationSeedCount;
                settings.FurtherStages = null;
            }
            settings.Profile = profile;
        }

        private static RewardStage ResourcesStageOf(bool water, double weight) =>
            new RewardStage(ResourcesStage, weight,
                water ? StageScoring.BalancedResources : StageScoring.BalancedResourcesDry,
                water ? BalancedNames : BalancedDryNames);

        private static RewardParameter[] BalancedParameters(double survival, double replenish, double reserves) =>
            new[]
            {
                new RewardParameter(SurvivalWeight, survival), new RewardParameter(ReplenishWeight, replenish),
                new RewardParameter(ReservesWeight, reserves)
            };

        private static void CheckWeights(double survival, double replenish, double reserves)
        {
            if (!(survival >= 0 && replenish >= 0 && reserves >= 0) ||
                Math.Abs(survival + replenish + reserves - 1) > RewardProfile.WeightTolerance)
                throw new ArgumentException("The survival, replenishment and reserves weights must be non-negative and " +
                                            $"add up to 1 (were {survival}, {replenish}, {reserves}).");
        }
    }
}
