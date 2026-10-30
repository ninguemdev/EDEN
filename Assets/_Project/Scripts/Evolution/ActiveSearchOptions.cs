using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>One source for the active search's canonical profile and effective physiology. No global defaults change.</summary>
    public sealed class ActiveSearchOptions
    {
        public double CellSide = 2, Hysteresis = 0.2, ExplorationGoal = 200, WaterThreshold = 5;
        public double MaxEnergy = 160, InitialEnergy = 80, FoodEnergy = 30, Metabolism = 0.5,
            Movement = 0.5, Sensor = 0.1, Turn = 0.25, MaxHydration = 100, HydrationLoss = 1.25,
            DrinkRate = 20, MaxAge = 240;
        public ActiveSearchOptions Clone() => (ActiveSearchOptions)MemberwiseClone();
        public System.Collections.Generic.IEnumerable<RewardParameter> Parameters()
        {
            yield return new RewardParameter("active_rules_version", 1);
            yield return new RewardParameter("mask_version", 1);
            yield return new RewardParameter("frozen_needs", 0);
            yield return new RewardParameter("food_respawn", 0);
            yield return new RewardParameter("finish_all_targets", 0);
            foreach (var field in typeof(ActiveSearchOptions).GetFields())
                yield return new RewardParameter("active_" + field.Name, (double)field.GetValue(this));
        }
        public static ActiveSearchOptions Read(System.Collections.Generic.IReadOnlyList<RewardParameter> parameters)
        {
            var result = new ActiveSearchOptions();
            foreach (var field in typeof(ActiveSearchOptions).GetFields())
                foreach (var parameter in parameters)
                    if (parameter.Name == "active_" + field.Name) field.SetValue(result, parameter.Value);
            result.Validate();
            return result;
        }
        public void Validate()
        {
            foreach (var field in typeof(ActiveSearchOptions).GetFields())
            {
                var value = (double)field.GetValue(this);
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > 10000)
                    throw new System.ArgumentException("Invalid active search option: " + field.Name);
            }
            if (CellSide < 1 || CellSide > 10 || Hysteresis >= CellSide / 2 || ExplorationGoal != System.Math.Floor(ExplorationGoal) ||
                InitialEnergy > MaxEnergy || WaterThreshold > MaxHydration)
                throw new System.ArgumentException("Inconsistent active search grid, reserves or water threshold.");
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
