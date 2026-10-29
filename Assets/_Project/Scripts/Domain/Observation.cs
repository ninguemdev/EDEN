using System;

namespace Eden.Domain
{
    /// <summary>
    /// What one creature senses at the start of a tick: only local information, never global food positions.
    /// Local frame: +x forward, +y to the creature's left; angles are counterclockwise. Values are normalized
    /// as listed per property, in the input order used by brains (<see cref="CopyTo"/>). A creature with eyes also
    /// carries <see cref="EyeInputs"/>, the inputs of its eye contract (<see cref="NeuralContract.Eyes"/>, or
    /// <see cref="NeuralContract.EyesWater"/> in a world with water); brains of that contract read only
    /// those, so the circular food sense and the wall radar above never reach them.
    /// </summary>
    public readonly struct Observation
    {
        public const int Size = 12;

        public Observation(float energy, float age, float velocityForward, float velocityLeft,
            float foodSin, float foodCos, float foodProximity, float foodVisible,
            float wallFront, float wallRight, float wallBack, float wallLeft, float[] eyeInputs = null)
        {
            EyeInputs = eyeInputs;
            Energy = energy;
            Age = age;
            VelocityForward = velocityForward;
            VelocityLeft = velocityLeft;
            FoodSin = foodSin;
            FoodCos = foodCos;
            FoodProximity = foodProximity;
            FoodVisible = foodVisible;
            WallFront = wallFront;
            WallRight = wallRight;
            WallBack = wallBack;
            WallLeft = wallLeft;
        }

        /// <summary>1: energy / max energy, [0, 1].</summary>
        public float Energy { get; }

        /// <summary>2: age / max age, [0, 1].</summary>
        public float Age { get; }

        /// <summary>3: forward velocity / max speed, [-1, 1].</summary>
        public float VelocityForward { get; }

        /// <summary>4: leftward velocity / max speed, [-1, 1].</summary>
        public float VelocityLeft { get; }

        /// <summary>5: sine of the local angle to the nearest visible food (positive = left); 0 if none.</summary>
        public float FoodSin { get; }

        /// <summary>6: cosine of the local angle to the nearest visible food (1 = ahead); 0 if none.</summary>
        public float FoodCos { get; }

        /// <summary>7: 1 - distance / sensor range for the nearest visible food, [0, 1]; 0 if none.</summary>
        public float FoodProximity { get; }

        /// <summary>8: 1 if any food is within sensor range, otherwise 0.</summary>
        public float FoodVisible { get; }

        /// <summary>9–12: distance to the arena wall along each local direction / sensor range, capped at 1.</summary>
        public float WallFront { get; }

        public float WallRight { get; }
        public float WallBack { get; }
        public float WallLeft { get; }

        /// <summary>
        /// Inputs of the eye contract in contract order for a creature with eyes, otherwise null. The
        /// array is the simulation's buffer for this tick: read it while deciding, copy it to keep it.
        /// </summary>
        public float[] EyeInputs { get; }

        /// <summary>Input by brain order (0-based), same order as <see cref="CopyTo"/>.</summary>
        public float this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return Energy;
                    case 1: return Age;
                    case 2: return VelocityForward;
                    case 3: return VelocityLeft;
                    case 4: return FoodSin;
                    case 5: return FoodCos;
                    case 6: return FoodProximity;
                    case 7: return FoodVisible;
                    case 8: return WallFront;
                    case 9: return WallRight;
                    case 10: return WallBack;
                    case 11: return WallLeft;
                    default: throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
        }

        public void CopyTo(float[] buffer, int offset)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length - Size)
                throw new ArgumentOutOfRangeException(nameof(offset));

            buffer[offset] = Energy;
            buffer[offset + 1] = Age;
            buffer[offset + 2] = VelocityForward;
            buffer[offset + 3] = VelocityLeft;
            buffer[offset + 4] = FoodSin;
            buffer[offset + 5] = FoodCos;
            buffer[offset + 6] = FoodProximity;
            buffer[offset + 7] = FoodVisible;
            buffer[offset + 8] = WallFront;
            buffer[offset + 9] = WallRight;
            buffer[offset + 10] = WallBack;
            buffer[offset + 11] = WallLeft;
        }
    }
}
