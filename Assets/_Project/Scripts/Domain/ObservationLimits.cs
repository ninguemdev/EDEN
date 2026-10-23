using System;

namespace Eden.Domain
{
    /// <summary>
    /// Numeric contract between sensors and brains: the documented range of each observation input and the policy
    /// for values that break it. Policy, applied by <see cref="Sanitize"/> and counted so it is never silent:
    /// <list type="bullet">
    /// <item>NaN or ±infinity becomes 0 (neutral input);</item>
    /// <item>a finite value outside its range is clamped to the nearest limit;</item>
    /// <item>the food-visible flag is forced to exactly 0 or 1 (threshold 0.5);</item>
    /// <item>without visible food, food sine, cosine and proximity are forced to 0 (the mask).</item>
    /// </list>
    /// </summary>
    public static class ObservationLimits
    {
        public const int FoodSinIndex = 4;
        public const int FoodCosIndex = 5;
        public const int FoodProximityIndex = 6;
        public const int FoodVisibleIndex = 7;

        private static readonly float[] Minimums = { 0f, 0f, -1f, -1f, -1f, -1f, 0f, 0f, 0f, 0f, 0f, 0f };
        private static readonly float[] Maximums = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };

        private static readonly string[] Names =
        {
            "Energy", "Age", "VelocityForward", "VelocityLeft", "FoodSin", "FoodCos", "FoodProximity",
            "FoodVisible", "WallFront", "WallRight", "WallBack", "WallLeft"
        };

        public static float Min(int index) => Minimums[Check(index)];
        public static float Max(int index) => Maximums[Check(index)];

        /// <summary>Stable identifier of an input, e.g. for labels and exported data.</summary>
        public static string Name(int index) => Names[Check(index)];

        /// <summary>True when every input is finite, inside its range, the flag is binary and the mask holds.</summary>
        public static bool IsValid(in Observation observation)
        {
            for (var i = 0; i < Observation.Size; i++)
            {
                var value = observation[i];
                if (float.IsNaN(value) || value < Minimums[i] || value > Maximums[i])
                    return false;
            }

            var visible = observation.FoodVisible;
            if (visible != 0f && visible != 1f)
                return false;
            return visible == 1f ||
                   (observation.FoodSin == 0f && observation.FoodCos == 0f && observation.FoodProximity == 0f);
        }

        /// <summary>Applies the policy; <paramref name="corrections"/> counts the values that had to change.</summary>
        public static Observation Sanitize(in Observation observation, out int corrections)
        {
            corrections = 0;
            var visible = Fix(observation.FoodVisible, FoodVisibleIndex, ref corrections);
            var flag = visible >= 0.5f ? 1f : 0f;
            if (flag != visible)
                corrections++;

            var foodSin = Fix(observation.FoodSin, FoodSinIndex, ref corrections);
            var foodCos = Fix(observation.FoodCos, FoodCosIndex, ref corrections);
            var foodProximity = Fix(observation.FoodProximity, FoodProximityIndex, ref corrections);
            if (flag == 0f && (foodSin != 0f || foodCos != 0f || foodProximity != 0f))
            {
                corrections++;
                foodSin = 0f;
                foodCos = 0f;
                foodProximity = 0f;
            }

            return new Observation(
                Fix(observation.Energy, 0, ref corrections),
                Fix(observation.Age, 1, ref corrections),
                Fix(observation.VelocityForward, 2, ref corrections),
                Fix(observation.VelocityLeft, 3, ref corrections),
                foodSin, foodCos, foodProximity, flag,
                Fix(observation.WallFront, 8, ref corrections),
                Fix(observation.WallRight, 9, ref corrections),
                Fix(observation.WallBack, 10, ref corrections),
                Fix(observation.WallLeft, 11, ref corrections));
        }

        private static float Fix(float value, int index, ref int corrections)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                corrections++;
                return 0f;
            }

            if (value < Minimums[index])
            {
                corrections++;
                return Minimums[index];
            }

            if (value > Maximums[index])
            {
                corrections++;
                return Maximums[index];
            }

            return value;
        }

        private static int Check(int index)
        {
            if (index < 0 || index >= Observation.Size)
                throw new ArgumentOutOfRangeException(nameof(index));
            return index;
        }
    }
}
