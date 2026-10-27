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
            if (visible == 0f && (observation.FoodSin != 0f || observation.FoodCos != 0f || observation.FoodProximity != 0f))
                return false;
            return observation.EyeInputs == null || CountEyeCorrections(observation.EyeInputs, false) == 0;
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

            if (observation.EyeInputs != null)
                corrections += CountEyeCorrections(observation.EyeInputs, true);
            return new Observation(
                Fix(observation.Energy, 0, ref corrections),
                Fix(observation.Age, 1, ref corrections),
                Fix(observation.VelocityForward, 2, ref corrections),
                Fix(observation.VelocityLeft, 3, ref corrections),
                foodSin, foodCos, foodProximity, flag,
                Fix(observation.WallFront, 8, ref corrections),
                Fix(observation.WallRight, 9, ref corrections),
                Fix(observation.WallBack, 10, ref corrections),
                Fix(observation.WallLeft, 11, ref corrections),
                observation.EyeInputs);
        }

        /// <summary>
        /// The same policy for the inputs of <see cref="NeuralContract.Eyes"/>: body channels in their legacy ranges;
        /// per eye, the present and visible flags exactly 0 or 1, sines and cosines in [-1, 1], proximities in [0, 1];
        /// an absent eye reads all 0, and a target not seen zeroes its direction and proximity. With
        /// <paramref name="fix"/> the values are corrected in place; the count is returned either way.
        /// </summary>
        public static int CountEyeCorrections(float[] inputs, bool fix)
        {
            if (inputs == null)
                throw new ArgumentNullException(nameof(inputs));
            if (inputs.Length != NeuralContract.Eyes.InputCount)
                throw new ArgumentException($"Expected {NeuralContract.Eyes.InputCount} eye inputs, got {inputs.Length}.",
                    nameof(inputs));

            var corrections = 0;
            for (var i = 0; i < NeuralContract.EyesBodyInputs; i++)
                corrections += Limit(inputs, i, Minimums[i], Maximums[i], fix);
            for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
            {
                var start = NeuralContract.EyeInputIndex(slot, EyeChannel.Present);
                corrections += Flag(inputs, start, fix);
                corrections += Group(inputs, start + (int)EyeChannel.FoodVisible, fix);
                corrections += Group(inputs, start + (int)EyeChannel.BarrierVisible, fix);
                if (Read(inputs, start, fix) == 0f)
                    corrections += Zero(inputs, start + 1, EyeChannels.Count - 1, fix);
            }

            return corrections;
        }

        // A visibility flag followed by sine, cosine and proximity; not seen zeroes the three.
        private static int Group(float[] inputs, int flagIndex, bool fix)
        {
            var corrections = Flag(inputs, flagIndex, fix);
            corrections += Limit(inputs, flagIndex + 1, -1f, 1f, fix) + Limit(inputs, flagIndex + 2, -1f, 1f, fix) +
                           Limit(inputs, flagIndex + 3, 0f, 1f, fix);
            if (Read(inputs, flagIndex, fix) == 0f)
                corrections += Zero(inputs, flagIndex + 1, 3, fix);
            return corrections;
        }

        // While only counting, a flag that is not 0 or 1 reads as its corrected value for the masks that follow.
        private static float Read(float[] inputs, int index, bool fix) =>
            fix ? inputs[index] : float.IsNaN(inputs[index]) ? 0f : inputs[index] >= 0.5f ? 1f : 0f;

        private static int Flag(float[] inputs, int index, bool fix)
        {
            var value = inputs[index];
            var flag = !float.IsNaN(value) && value >= 0.5f ? 1f : 0f;
            if (flag == value)
                return 0;
            if (fix)
                inputs[index] = flag;
            return 1;
        }

        private static int Limit(float[] inputs, int index, float min, float max, bool fix)
        {
            var value = inputs[index];
            float corrected;
            if (float.IsNaN(value) || float.IsInfinity(value))
                corrected = 0f;
            else if (value < min)
                corrected = min;
            else if (value > max)
                corrected = max;
            else
                return 0;
            if (fix)
                inputs[index] = corrected;
            return 1;
        }

        private static int Zero(float[] inputs, int start, int count, bool fix)
        {
            var corrections = 0;
            for (var i = start; i < start + count; i++)
            {
                if (inputs[i] == 0f)
                    continue;
                corrections++;
                if (fix)
                    inputs[i] = 0f;
            }

            return corrections;
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
