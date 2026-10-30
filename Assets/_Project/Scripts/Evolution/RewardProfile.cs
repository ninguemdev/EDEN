using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>How one stage of a reward profile turns an episode into a score. Persisted: never renumber.</summary>
    public enum StageScoring
    {
        /// <summary>Food energy eaten per configured episode second (<see cref="Fitness.FromFood"/>), unbounded.</summary>
        FoodEnergyPerSecond = 0,

        /// <summary>
        /// Balanced resources with water: survival, useful replenishment of energy and hydration (waste never counts) and
        /// final reserves, weighted into [0, 1] (<see cref="StageScores.BalancedResources"/>).
        /// </summary>
        BalancedResources = 1,

        /// <summary>The balanced resources scoring of a world without water: energy alone, normalised on its own.</summary>
        BalancedResourcesDry = 2,

        /// <summary>
        /// A search stage: needs frozen, fixed targets, scored by targets reached, speed and the best approach to the next
        /// one, in [0, 1] (<see cref="StageScores.Search"/>). The stage runs under search rules.
        /// </summary>
        Search = 3,

        ExploreCollectActive = 4
    }

    /// <summary>A measured quantity a stage reports per episode, with its unit and the range its values must fall in.</summary>
    public sealed class RewardComponent
    {
        public RewardComponent(string name, string unit, double min, double max)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A component needs a name.", nameof(name));
            if (string.IsNullOrEmpty(unit))
                throw new ArgumentException($"Component \"{name}\" needs a unit.", nameof(unit));
            if (double.IsNaN(min) || double.IsNaN(max) || double.IsInfinity(min) || min > max)
                throw new ArgumentException($"Component \"{name}\" needs a finite minimum not above its maximum " +
                                            $"(was [{min}, {max}]).");
            Name = name;
            Unit = unit;
            Min = min;
            Max = max;
        }

        public string Name { get; }
        public string Unit { get; }
        public double Min { get; }

        /// <summary>Upper bound; <see cref="double.PositiveInfinity"/> for a quantity without a ceiling.</summary>
        public double Max { get; }

        /// <summary>The components each scoring can report; a stage may declare only these.</summary>
        public static IReadOnlyList<RewardComponent> Of(StageScoring scoring)
        {
            switch (scoring)
            {
                case StageScoring.FoodEnergyPerSecond:
                    return FoodEnergy;
                case StageScoring.BalancedResources:
                    return Balanced;
                case StageScoring.BalancedResourcesDry:
                    return BalancedDry;
                case StageScoring.Search:
                    return SearchComponents;
                case StageScoring.ExploreCollectActive:
                    return ActiveComponents;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scoring), scoring, "Unknown stage scoring.");
            }
        }

        // What the legacy result already keeps as its own fields (EpisodeResult.FoodEnergy, SurvivedTicks, FinalEnergy).
        private static readonly RewardComponent[] FoodEnergy =
        {
            new RewardComponent("food_energy", "energy", 0, double.PositiveInfinity),
            new RewardComponent("survived_seconds", "simulated_seconds", 0, double.PositiveInfinity),
            new RewardComponent("final_energy", "energy", 0, double.PositiveInfinity)
        };

        private static readonly RewardComponent[] Balanced =
        {
            new RewardComponent("survival", "fraction", 0, 1),
            new RewardComponent("energy_replenished", "fraction", 0, 1),
            new RewardComponent("hydration_replenished", "fraction", 0, 1),
            new RewardComponent("reserves", "fraction", 0, 1),
            new RewardComponent("absorbed_energy", "energy", 0, double.PositiveInfinity),
            new RewardComponent("wasted_energy", "energy", 0, double.PositiveInfinity),
            new RewardComponent("drunk", "hydration", 0, double.PositiveInfinity)
        };

        private static readonly RewardComponent[] BalancedDry =
        {
            new RewardComponent("survival", "fraction", 0, 1),
            new RewardComponent("energy_replenished", "fraction", 0, 1),
            new RewardComponent("reserves", "fraction", 0, 1),
            new RewardComponent("absorbed_energy", "energy", 0, double.PositiveInfinity),
            new RewardComponent("wasted_energy", "energy", 0, double.PositiveInfinity)
        };

        private static readonly RewardComponent[] SearchComponents =
        {
            new RewardComponent("collected", "targets", 0, SearchTargetLimit),
            new RewardComponent("collected_food", "targets", 0, SearchTargetLimit),
            new RewardComponent("collected_water", "targets", 0, SearchTargetLimit),
            new RewardComponent("speed", "fraction", 0, 1),
            new RewardComponent("approach", "fraction", 0, 1),
            new RewardComponent("score_without_aid", "fraction", 0, 1),
            new RewardComponent("uncharged_energy", "energy", 0, double.PositiveInfinity)
        };

        private static readonly RewardComponent[] ActiveComponents = BuildActiveComponents();

        private static RewardComponent[] BuildActiveComponents()
        {
            var result = new List<RewardComponent>();
            foreach (var name in new[] { "exploration", "coverage", "detection", "collection", "base", "score" })
                result.Add(new RewardComponent(name, "fraction", 0, 1));
            result.Add(new RewardComponent("multiplier", "factor", 1, 3.5));
            foreach (var name in new[] { "visited", "goal", "eligible", "detected_food", "detected_water",
                "collected_food", "collected_water" })
                result.Add(new RewardComponent(name, "count", 0, double.PositiveInfinity));
            foreach (var name in new[] { "initial_energy", "final_energy", "maintenance", "movement", "turning", "eyes",
                "food_energy", "absorbed_energy", "wasted_energy", "initial_hydration", "final_hydration", "drunk",
                "lived_seconds", "configured_seconds", "death_cause" })
                result.Add(new RewardComponent(name, name.Contains("seconds") ? "simulated_seconds" : name == "death_cause" ? "enum"
                    : name.Contains("hydration") || name == "drunk" ? "hydration" : "energy", 0, double.PositiveInfinity));
            // Fixed target limit keeps per-source audit columns stable across episodes. Missing values remain absent.
            for (var i = 0; i < SearchTargets.MaxTargets; i++)
                foreach (var name in new[] { "water_useful", "contact_seconds", "sip_seconds", "collection_seconds", "energy_before_collection", "hydration_before_sip" })
                    result.Add(new RewardComponent(name + "_" + i, name.Contains("seconds") ? "simulated_seconds" :
                        name == "energy_before_collection" ? "energy" : "hydration", 0, double.PositiveInfinity));
            return result.ToArray();
        }

        private const double SearchTargetLimit = 16;
    }

    /// <summary>One part of a reward profile: its scoring, its weight in the total and the components it reports.</summary>
    public sealed class RewardStage
    {
        /// <param name="components">Names among <see cref="RewardComponent.Of"/> of the scoring, in display order.</param>
        public RewardStage(string name, double weight, StageScoring scoring, params string[] components)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A stage needs a name.", nameof(name));
            if (double.IsNaN(weight) || double.IsInfinity(weight) || weight <= 0 || weight > 1)
                throw new ArgumentException($"Stage \"{name}\" needs a weight in (0, 1] (was {weight}).", nameof(weight));
            if (!Enum.IsDefined(typeof(StageScoring), scoring))
                throw new ArgumentException($"Stage \"{name}\" has an unknown scoring ({(int)scoring}).", nameof(scoring));

            var known = RewardComponent.Of(scoring);
            var declared = new RewardComponent[components?.Length ?? 0];
            for (var i = 0; i < declared.Length; i++)
            {
                var component = FindComponent(known, components[i]) ??
                                throw new ArgumentException($"Stage \"{name}\" declares component \"{components[i]}\", " +
                                                            $"which {scoring} does not report.", nameof(components));
                if (Array.IndexOf(declared, component) >= 0)
                    throw new ArgumentException($"Stage \"{name}\" declares component \"{components[i]}\" twice.",
                        nameof(components));
                declared[i] = component;
            }

            Name = name;
            Weight = weight;
            Scoring = scoring;
            Components = declared;
        }

        public string Name { get; }

        /// <summary>Share of the total score; the stages of a profile add up to 1.</summary>
        public double Weight { get; }

        public StageScoring Scoring { get; }
        public IReadOnlyList<RewardComponent> Components { get; }

        private static RewardComponent FindComponent(IReadOnlyList<RewardComponent> known, string name)
        {
            foreach (var component in known)
            {
                if (component.Name == name)
                    return component;
            }

            return null;
        }
    }

    /// <summary>A named number a profile was run with (a target count, a weight, a distance…).</summary>
    public readonly struct RewardParameter
    {
        public RewardParameter(string name, double value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }
        public double Value { get; }
    }

    /// <summary>
    /// Versioned rule that scores a genome: one or more weighted stages, each with its scoring and reported components,
    /// and the parameters frozen for the run. The <see cref="Id"/> names the formula and changes when it changes; the
    /// <see cref="ParameterHash"/> tells apart runs of one formula with other stages, weights or parameters. Scores of
    /// different profiles, or of one profile with different hashes, are not comparable. The legacy profile
    /// (<see cref="Legacy"/>) has no hash and is recorded exactly as before profiles existed.
    /// </summary>
    public sealed class RewardProfile
    {
        /// <summary>Largest difference from 1 allowed in the sum of the stage weights.</summary>
        public const double WeightTolerance = 1e-9;

        /// <summary>The scoring of every run before profiles existed: food energy per second, alone, with its components.</summary>
        public static readonly RewardProfile Legacy = new RewardProfile(Fitness.Profile,
            new[] { new RewardStage("episode", 1, StageScoring.FoodEnergyPerSecond, "food_energy", "survived_seconds",
                "final_energy") }, Array.Empty<RewardParameter>(), true);

        public RewardProfile(string id, IReadOnlyList<RewardStage> stages, IReadOnlyList<RewardParameter> parameters = null)
            : this(id, stages, parameters ?? Array.Empty<RewardParameter>(), false)
        {
        }

        private RewardProfile(string id, IReadOnlyList<RewardStage> stages, IReadOnlyList<RewardParameter> parameters,
            bool legacy)
        {
            var errors = new List<string>();
            if (string.IsNullOrEmpty(id))
                errors.Add("an id is required");
            else if (!legacy && id == Fitness.Profile)
                errors.Add($"\"{id}\" is the legacy profile; use {nameof(RewardProfile)}.{nameof(Legacy)}");
            if (stages == null || stages.Count == 0)
                errors.Add("at least one stage is required");
            else
            {
                var sum = 0.0;
                for (var i = 0; i < stages.Count; i++)
                {
                    if (stages[i] == null)
                    {
                        errors.Add($"stage {i} is missing");
                        continue;
                    }

                    sum += stages[i].Weight;
                    for (var j = 0; j < i; j++)
                    {
                        if (stages[j] != null && stages[j].Name == stages[i].Name)
                            errors.Add($"stage \"{stages[i].Name}\" appears twice");
                    }
                }

                if (Math.Abs(sum - 1) > WeightTolerance)
                    errors.Add($"stage weights must add up to 1 (they add up to {sum.ToString("R", CultureInfo.InvariantCulture)})");
            }

            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (string.IsNullOrEmpty(parameter.Name))
                    errors.Add($"parameter {i} needs a name");
                else if (double.IsNaN(parameter.Value) || double.IsInfinity(parameter.Value))
                    errors.Add($"parameter \"{parameter.Name}\" must be finite (was {parameter.Value})");
                for (var j = 0; j < i; j++)
                {
                    if (parameters[j].Name == parameter.Name)
                        errors.Add($"parameter \"{parameter.Name}\" appears twice");
                }
            }

            if (errors.Count > 0)
                throw new ArgumentException($"Invalid reward profile \"{id}\": " + string.Join("; ", errors));

            Id = id;
            IsLegacy = legacy;
            Stages = Copy(stages);
            Parameters = Copy(parameters);
            ParameterHash = legacy ? null : Fnv1a(Canonical()).ToString("x16", CultureInfo.InvariantCulture);
        }

        public string Id { get; }

        /// <summary>Whether this is <see cref="Legacy"/>, recorded with its id alone.</summary>
        public bool IsLegacy { get; }

        public IReadOnlyList<RewardStage> Stages { get; }
        public IReadOnlyList<RewardParameter> Parameters { get; }

        /// <summary>
        /// 64-bit FNV-1a, as 16 hexadecimal digits, of the canonical text of the stages and parameters; null for the
        /// legacy profile.
        /// </summary>
        public string ParameterHash { get; }

        /// <summary>
        /// The total score from one score per stage, in stage order: the weighted sum, or <see cref="Fitness.Worst"/>
        /// when any stage score is invalid. A single stage passes its score through unchanged.
        /// </summary>
        public float Combine(IReadOnlyList<float> stageScores)
        {
            if (stageScores == null || stageScores.Count != Stages.Count)
                throw new ArgumentException($"Expected {Stages.Count} stage scores.", nameof(stageScores));
            if (Stages.Count == 1)
                return Fitness.IsValid(stageScores[0]) ? stageScores[0] : Fitness.Worst;

            var total = 0.0;
            for (var i = 0; i < stageScores.Count; i++)
            {
                if (!Fitness.IsValid(stageScores[i]))
                    return Fitness.Worst;
                total += Stages[i].Weight * stageScores[i];
            }

            return (float)total;
        }

        /// <summary>The text <see cref="ParameterHash"/> is computed from: a line for the id, each stage and each parameter.</summary>
        public string Canonical()
        {
            var culture = CultureInfo.InvariantCulture;
            var text = new StringBuilder().Append("profile ").Append(Id).Append('\n');
            foreach (var stage in Stages)
            {
                text.Append("stage ").Append(stage.Name).Append(" weight ").Append(stage.Weight.ToString("R", culture))
                    .Append(" scoring ").Append(stage.Scoring).Append(" components");
                foreach (var component in stage.Components)
                    text.Append(' ').Append(component.Name);
                text.Append('\n');
            }

            foreach (var parameter in Parameters)
                text.Append("parameter ").Append(parameter.Name).Append(' ').Append(parameter.Value.ToString("R", culture))
                    .Append('\n');
            return text.ToString();
        }

        /// <summary>The value of the parameter called <paramref name="name"/>, or <paramref name="fallback"/> without one.</summary>
        public double Parameter(string name, double fallback)
        {
            foreach (var parameter in Parameters)
            {
                if (parameter.Name == name)
                    return parameter.Value;
            }

            return fallback;
        }

        /// <summary>Index of the stage scored by <paramref name="scoring"/>, or -1.</summary>
        public int StageOf(StageScoring scoring)
        {
            for (var i = 0; i < Stages.Count; i++)
            {
                if (Stages[i].Scoring == scoring)
                    return i;
            }

            return -1;
        }

        /// <summary>Whether <paramref name="id"/> and <paramref name="hash"/> name this profile run.</summary>
        public bool Matches(string id, string hash) => Id == id && ParameterHash == hash;

        private static T[] Copy<T>(IReadOnlyList<T> items)
        {
            var copy = new T[items.Count];
            for (var i = 0; i < copy.Length; i++)
                copy[i] = items[i];
            return copy;
        }

        private static ulong Fnv1a(string text)
        {
            var hash = 14695981039346656037UL;
            foreach (var b in Encoding.UTF8.GetBytes(text))
            {
                hash ^= b;
                hash = unchecked(hash * 1099511628211UL);
            }

            return hash;
        }
    }
}
