using System;

namespace Eden.Domain
{
    /// <summary>64-bit FNV-1a over little-endian values; stable across runs, used for state fingerprints.</summary>
    public struct StateHasher
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;
        private ulong value;

        public static StateHasher Create() => new StateHasher { value = OffsetBasis };

        public ulong Value => value;

        public void Add(ulong data)
        {
            for (var i = 0; i < 8; i++)
            {
                value ^= (byte)(data >> (8 * i));
                value = unchecked(value * Prime);
            }
        }

        public void Add(long data) => Add(unchecked((ulong)data));

        /// <summary>Hashes the exact float bits, so -0 and 0 or different NaNs are distinguished.</summary>
        public void Add(float data) => Add((ulong)(uint)BitConverter.SingleToInt32Bits(data));

        public void Add(Float2 data)
        {
            Add(data.X);
            Add(data.Y);
        }

        public void Add(Pcg32State data)
        {
            Add(data.State);
            Add(data.Increment);
        }
    }
}
