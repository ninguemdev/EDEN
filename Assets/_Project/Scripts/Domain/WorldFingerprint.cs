using System;

namespace Eden.Domain
{
    /// <summary>
    /// Digest of the full world state, including exact float bits and random stream states.
    /// Equal fingerprints mean identical runs in the same environment; used by replay and checkpoint checks.
    /// </summary>
    public static class WorldFingerprint
    {
        public static ulong Compute(WorldState world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));

            var hash = StateHasher.Create();
            hash.Add((ulong)world.Config.SchemaVersion);
            hash.Add(world.Config.Seed);
            hash.Add(world.Tick);
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
                hash.Add(creature.AgeTicks);
                hash.Add(creature.FoodEnergyEaten);
                hash.Add((ulong)creature.DeathCause);
                // The body follows from the genes and the config.
                hash.Add(creature.Genes.Size);
                hash.Add(creature.Genes.Speed);
                hash.Add(creature.Genes.Sensor);
                hash.Add(creature.Genes.Efficiency);
                hash.Add(creature.Parent.HasValue ? (ulong)creature.Parent.Value.Value : 0UL);
                hash.Add((ulong)creature.Generation);
                hash.Add(creature.ReproductionCooldownTicks);
            }

            hash.Add((ulong)world.Food.Count);
            foreach (var item in world.Food)
            {
                hash.Add((ulong)item.Id.Value);
                hash.Add(item.Position);
                hash.Add(item.Energy);
                hash.Add(item.IsEaten ? 1UL : 0UL);
            }

            return hash.Value;
        }
    }
}
