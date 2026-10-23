using System;

namespace Eden.Domain
{
    /// <summary>Complete serializable state of a <see cref="Pcg32"/> generator.</summary>
    public readonly struct Pcg32State : IEquatable<Pcg32State>
    {
        public Pcg32State(ulong state, ulong increment)
        {
            if ((increment & 1UL) == 0)
                throw new ArgumentException("PCG32 increment must be odd.", nameof(increment));
            State = state;
            Increment = increment;
        }

        public ulong State { get; }
        public ulong Increment { get; }

        public bool Equals(Pcg32State other) => State == other.State && Increment == other.Increment;
        public override bool Equals(object obj) => obj is Pcg32State other && Equals(other);
        public override int GetHashCode() => State.GetHashCode() ^ (Increment.GetHashCode() * 397);
        public override string ToString() => $"pcg32(state=0x{State:x16}, inc=0x{Increment:x16})";
    }

    /// <summary>
    /// PCG32 (XSH-RR, 64-bit state, 32-bit output), a port of pcg32_random_r / pcg32_srandom_r /
    /// pcg32_boundedrand_r from the reference pcg-c-basic implementation by M. E. O'Neill.
    /// </summary>
    public sealed class Pcg32 : IRandomSource
    {
        public const string AlgorithmName = "pcg32-xsh-rr-64-32";

        private const ulong Multiplier = 6364136223846793005UL;
        private const float FloatUnit = 1f / (1 << 24);

        private ulong state;
        private readonly ulong increment;

        /// <summary>Seeds like pcg32_srandom_r(initState, sequence); different sequences give independent streams.</summary>
        public Pcg32(ulong initState, ulong sequence)
        {
            increment = (sequence << 1) | 1UL;
            state = 0UL;
            NextUInt();
            state = unchecked(state + initState);
            NextUInt();
        }

        private Pcg32(Pcg32State saved)
        {
            state = saved.State;
            increment = saved.Increment;
        }

        public static Pcg32 FromState(Pcg32State saved) => new Pcg32(saved);

        public Pcg32State State => new Pcg32State(state, increment);

        public uint NextUInt()
        {
            var old = state;
            state = unchecked(old * Multiplier + increment);
            var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            var rotation = (int)(old >> 59);
            return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
        }

        public uint NextUInt(uint bound)
        {
            if (bound == 0)
                throw new ArgumentOutOfRangeException(nameof(bound), "Bound must be positive.");

            // Rejects the low values that would bias r % bound, as in pcg32_boundedrand_r.
            var threshold = unchecked(0U - bound) % bound;
            while (true)
            {
                var value = NextUInt();
                if (value >= threshold)
                    return value % bound;
            }
        }

        public float NextFloat() => (NextUInt() >> 8) * FloatUnit;

        public float NextFloat(float min, float max)
        {
            if (!(min <= max) || float.IsInfinity(max - min))
                throw new ArgumentException($"Invalid range [{min}, {max}].");
            return min + (max - min) * NextFloat();
        }
    }
}
