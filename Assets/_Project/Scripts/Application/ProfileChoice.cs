using System;
using System.Globalization;
using Eden.Domain;
using Eden.Evolution;

namespace Eden.Application
{
    /// <summary>Which reward profile an experiment is scored by, as the batch, the player and the lab choose it.</summary>
    public enum ProfileKind
    {
        /// <summary>food-energy-per-second-v1, the control.</summary>
        Legacy = 0,

        /// <summary>two-stage-search-v1: resources stage and search stage.</summary>
        TwoStage = 1,

        /// <summary>Balanced resources alone, with or without water (the world decides).</summary>
        Resources = 2
    }

    /// <summary>
    /// The profile and the search-stage options of an experiment: -eden-profile legacy|two-stage|resources and the
    /// -eden-stage1-* options (weight, seconds, distance, arena, obstacles, food, water, alpha, beta, seeds). Invalid
    /// values are refused, never ignored.
    /// </summary>
    public sealed class ProfileChoice
    {
        public ProfileKind Kind = ProfileKind.Legacy;
        public TwoStageOptions TwoStage = new TwoStageOptions();

        public ProfileChoice Clone()
        {
            var copy = (ProfileChoice)MemberwiseClone();
            copy.TwoStage = TwoStage.Clone();
            return copy;
        }

        /// <summary>Reads one option; false when <paramref name="name"/> is not one of these options.</summary>
        public bool TryRead(string name, string value)
        {
            switch (name)
            {
                case "-eden-profile":
                    Kind = value == "legacy" ? ProfileKind.Legacy
                        : value == "two-stage" ? ProfileKind.TwoStage
                        : value == "resources" ? ProfileKind.Resources
                        : throw Invalid(name, value, "legacy, two-stage or resources");
                    return true;
                case "-eden-stage1-weight":
                    TwoStage.SearchWeight = Number(name, value);
                    return true;
                case "-eden-stage1-seconds":
                    TwoStage.SearchSeconds = (float)Number(name, value);
                    return true;
                case "-eden-stage1-distance":
                    TwoStage.MinDistance = Number(name, value);
                    return true;
                case "-eden-stage1-arena":
                    TwoStage.SearchArena = (float)Number(name, value);
                    return true;
                case "-eden-stage1-max-arena":
                    TwoStage.MaxArenaSide = Number(name, value);
                    return true;
                case "-eden-stage1-obstacles":
                    TwoStage.SearchObstacles = value == "none" ? ObstacleLayout.None
                        : value == "pillars" ? ObstacleLayout.Pillars
                        : value == "corridor" ? ObstacleLayout.Corridor
                        : throw Invalid(name, value, "none, pillars or corridor");
                    return true;
                case "-eden-stage1-food":
                    TwoStage.FoodTargets = Integer(name, value);
                    return true;
                case "-eden-stage1-water":
                    TwoStage.WaterTargets = Integer(name, value);
                    return true;
                case "-eden-stage1-alpha":
                    TwoStage.Alpha = Number(name, value);
                    return true;
                case "-eden-stage1-beta":
                    TwoStage.Beta = Number(name, value);
                    return true;
                case "-eden-stage1-seeds":
                    TwoStage.SearchTrainingSeeds = Integer(name, value);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Scores <paramref name="settings"/> by the chosen profile, after the scenario was applied. With two stages, the
        /// resources world gets 64 food and pillars (decision D7) unless the scenario set its own.
        /// </summary>
        public void Apply(EvolutionSettings settings, ScenarioSettings scenario)
        {
            switch (Kind)
            {
                case ProfileKind.Legacy:
                    settings.Profile = RewardProfile.Legacy;
                    settings.FurtherStages = null;
                    break;
                case ProfileKind.Resources:
                    settings.Profile = RewardProfiles.BalancedResources(settings.World.Water.Length > 0,
                        TwoStage.SurvivalWeight, TwoStage.ReplenishWeight, TwoStage.ReservesWeight);
                    settings.FurtherStages = null;
                    break;
                case ProfileKind.TwoStage:
                    var options = TwoStage.Clone();
                    options.ResourceFood = scenario?.Food.HasValue == true ? (int?)null : options.ResourceFood;
                    options.ResourceObstacles = scenario != null && scenario.Obstacles != ObstacleLayout.None
                        ? null
                        : options.ResourceObstacles;
                    RewardProfiles.UseTwoStageSearch(settings, options);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown profile.");
            }
        }

        /// <summary>The choice a run was started with, read back from its settings (a loaded or resumed run).</summary>
        public static ProfileChoice Read(EvolutionSettings settings)
        {
            var choice = new ProfileChoice();
            var profile = settings?.Profile;
            if (profile == null || profile.IsLegacy)
                return choice;
            var options = RewardProfiles.OptionsOf(profile.Parameters);
            choice.TwoStage = options;
            if (profile.Id != RewardProfiles.TwoStageSearchId)
            {
                choice.Kind = ProfileKind.Resources;
                return choice;
            }

            choice.Kind = ProfileKind.TwoStage;
            var search = settings.FurtherStages != null && settings.FurtherStages.Count > 0 ? settings.FurtherStages[0] : null;
            if (search != null)
            {
                options.SearchSeconds = search.EpisodeSeconds;
                options.SearchArena = search.World.ArenaWidth;
                options.SearchObstacles = ObstacleLayouts.Identify(search.World) ?? ObstacleLayout.None;
                options.SearchTrainingSeeds = search.TrainingSeedsPerGeneration;
                options.SearchValidationSeeds = search.ValidationSeedCount;
            }

            // The resources world already is what the run was started with.
            options.ResourceFood = null;
            options.ResourceObstacles = null;
            return choice;
        }

        /// <summary>Name of a profile on the command line.</summary>
        public static string Name(ProfileKind kind) =>
            kind == ProfileKind.TwoStage ? "two-stage" : kind == ProfileKind.Resources ? "resources" : "legacy";

        private static double Number(string name, string value)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                double.IsNaN(number) || double.IsInfinity(number))
                throw Invalid(name, value, "a finite number");
            return number;
        }

        private static int Integer(string name, string value)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < 0)
                throw Invalid(name, value, "a whole number of at least 0");
            return number;
        }

        private static ArgumentException Invalid(string name, string value, string expected) =>
            new ArgumentException($"{name} {value}: expected {expected}.");
    }
}
