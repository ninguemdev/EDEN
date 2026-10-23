using System;

namespace Eden.Domain
{
    /// <summary>
    /// 64-bit FNV-1a digest of the full world state, including exact float bits and random stream states.
    /// Equal fingerprints mean identical runs in the same environment; used by replay and checkpoint checks.
    /// </summary>
    public static class WorldFingerprint
    {
        public static ulong Compute(WorldState world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            var hash = Fnv1a64.Create();
            hash.Add((ulong)world.Config.SchemaVersion);
            hash.Add(world.Config.Seed);
            hash.Add((ulong)world.Tick);
            hash.Add((ulong)world.NextCreatureId);
            hash.Add((ulong)world.NextFoodId);
            hash.Add(world.WorldRandom.State);
            hash.Add(world.SpawnRandom.State);

            hash.Add((ulong)world.Creatures.Count);
            foreach (var creature in world.Creatures)
            {
                hash.Add((ulong)creature.Id.Value);
                hash.Add(creature.Position);
                hash.Add(creature.Velocity);
                hash.Add(creature.Heading);
                hash.Add(creature.Energy);
                hash.Add((ulong)creature.AgeTicks);
            }

            hash.Add((ulong)world.Food.Count);
            foreach (var item in world.Food)
            {
                hash.Add((ulong)item.Id.Value);
                hash.Add(item.Position);
                hash.Add(item.Energy);
            }

            return hash.Value;
        }

        private struct Fnv1a64
        {
            private const ulong OffsetBasis = 14695981039346656037UL;
            private const ulong Prime = 1099511628211UL;
            private ulong value;

            public static Fnv1a64 Create() => new Fnv1a64 { value = OffsetBasis };

            public ulong Value => value;

            /// <summary>Hashes the 8 bytes of <paramref name="data"/> in little-endian order.</summary>
            public void Add(ulong data)
            {
                for (var i = 0; i < 8; i++)
                {
                    value ^= (byte)(data >> (8 * i));
                    value = unchecked(value * Prime);
                }
            }

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
}
