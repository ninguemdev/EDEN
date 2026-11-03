using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>One source for the active search's canonical profile and effective physiology. No global defaults change.</summary>
    public sealed class ActiveSearchOptions
    {
        /// <summary>Rules version 2: exploration measured by the cells the senses saw.</summary>
        public const double SightRules = 2;

        /// <summary>
        /// The instance fields that describe the physiology and the grid, in declaration order: the parameters of a
        /// rules version 1 profile, in the order its canonical text has always listed them.
        /// </summary>
        public static readonly System.Collections.Generic.IReadOnlyList<System.Reflection.FieldInfo> Fields =
            typeof(ActiveSearchOptions).GetFields(System.Reflection.BindingFlags.Public |
                                                  System.Reflection.BindingFlags.Instance);

        public double CellSide = 2, Hysteresis = 0.2, ExplorationGoal = 200, WaterThreshold = 5;
        public double MaxEnergy = 160, InitialEnergy = 80, FoodEnergy = 30, Metabolism = 0.5,
            Movement = 0.5, Sensor = 0.1, Turn = 0.25, MaxHydration = 100, HydrationLoss = 1.25,
            DrinkRate = 20, MaxAge = 240;

        // The options below are properties on purpose: the reflection over the fields above builds the version 1
        // parameter list, in declaration order, and its canonical text must keep producing the old hash.

        /// <summary>1 scores exploration by the cells the body entered, <see cref="SightRules"/> by the cells the senses saw.</summary>
        public double RulesVersion { get; set; } = SightRules;

        /// <summary>Share of the exploration term that comes from the cells seen; the rest comes from the cells entered.</summary>
        public double SightWeight { get; set; } = 0.8;

        /// <summary>Share of the connected mask a full sight score requires having seen.</summary>
        public double SightGoalFraction { get; set; } = 0.75;

        /// <summary>Whether exploration is scored by what the senses saw (rules version 2 and later).</summary>
        public bool Sight => RulesVersion >= SightRules;
        public ActiveSearchOptions Clone() => (ActiveSearchOptions)MemberwiseClone();
        public System.Collections.Generic.IEnumerable<RewardParameter> Parameters()
        {
            yield return new RewardParameter("active_rules_version", RulesVersion);
            yield return new RewardParameter("mask_version", 1);
            yield return new RewardParameter("frozen_needs", 0);
            yield return new RewardParameter("food_respawn", 0);
            yield return new RewardParameter("finish_all_targets", 0);
            foreach (var field in Fields)
                yield return new RewardParameter("active_" + field.Name, (double)field.GetValue(this));
            if (!Sight)
                yield break;
            yield return new RewardParameter("active_SightWeight", SightWeight);
            yield return new RewardParameter("active_SightGoalFraction", SightGoalFraction);
        }
        public static ActiveSearchOptions Read(System.Collections.Generic.IReadOnlyList<RewardParameter> parameters)
        {
            var result = new ActiveSearchOptions { RulesVersion = 1 };
            foreach (var field in Fields)
                foreach (var parameter in parameters)
                    if (parameter.Name == "active_" + field.Name) field.SetValue(result, parameter.Value);
            foreach (var parameter in parameters)
                switch (parameter.Name)
                {
                    case "active_rules_version": result.RulesVersion = parameter.Value; break;
                    case "active_SightWeight": result.SightWeight = parameter.Value; break;
                    case "active_SightGoalFraction": result.SightGoalFraction = parameter.Value; break;
                }
            result.Validate();
            return result;
        }
        public void Validate()
        {
            foreach (var field in Fields)
            {
                var value = (double)field.GetValue(this);
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > 10000)
                    throw new System.ArgumentException("Invalid active search option: " + field.Name);
            }
            if (CellSide < 1 || CellSide > 10 || Hysteresis >= CellSide / 2 || ExplorationGoal != System.Math.Floor(ExplorationGoal) ||
                InitialEnergy > MaxEnergy || WaterThreshold > MaxHydration)
                throw new System.ArgumentException("Inconsistent active search grid, reserves or water threshold.");
            if (RulesVersion != 1 && RulesVersion != SightRules)
                throw new System.ArgumentException("Unknown active search rules version: " + RulesVersion);
            if (!(SightWeight > 0 && SightWeight <= 1) || !(SightGoalFraction > 0 && SightGoalFraction <= 1))
                throw new System.ArgumentException("The sight weight and goal share must be in (0, 1].");
        }
        public void Apply(Eden.Domain.SimulationSettings world)
        {
            Validate();
            world.MaxEnergy = (float)MaxEnergy; world.InitialEnergy = (float)InitialEnergy;
            world.FoodEnergy = (float)FoodEnergy; world.MetabolismPerSecond = (float)Metabolism;
            world.MovementCostPerSecond = (float)Movement; world.SensorCostPerSecond = (float)Sensor;
            world.TurnCostFraction = (float)Turn; world.MaxHydration = (float)MaxHydration;
            world.HydrationLossPerSecond = (float)HydrationLoss; world.DrinkRatePerSecond = (float)DrinkRate;
            world.MaxAgeSeconds = (float)MaxAge; world.FoodShiftSeconds = 0; world.Reproduction = false;
        }
        public void Check(Eden.Domain.SimulationSettings world)
        {
            var expected = world.Clone(); Apply(expected);
            foreach (var name in new[] { "MaxEnergy", "InitialEnergy", "FoodEnergy", "MetabolismPerSecond", "MovementCostPerSecond",
                "SensorCostPerSecond", "TurnCostFraction", "MaxHydration", "HydrationLossPerSecond", "DrinkRatePerSecond",
                "MaxAgeSeconds", "FoodShiftSeconds", "Reproduction" })
            {
                var field = typeof(Eden.Domain.SimulationSettings).GetField(name);
                if (!Equals(field.GetValue(world), field.GetValue(expected)))
                    throw new System.ArgumentException("Active search world differs from profile: " + name);
            }
        }
    }
}
