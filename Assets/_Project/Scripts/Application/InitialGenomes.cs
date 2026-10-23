using Eden.Domain;

namespace Eden.Application
{
    /// <summary>
    /// Random starting genomes for runs without evolution yet. Each creature's genome comes from its own stream
    /// (<see cref="RandomStream.Genome"/> + creature id), so it does not depend on population size or order.
    /// </summary>
    public static class InitialGenomes
    {
        /// <summary>Proposed starting range for random weights, to be measured; well inside <see cref="Genome.MaxAbsWeight"/>.</summary>
        public const float WeightLimit = 1f;

        public static Genome RandomFor(SimulationConfig config, CreatureId creature) =>
            Genome.CreateRandom(NetworkTopology.Mlp12x16x3,
                SeedDerivation.CreateCreatureStream(config.Seed, RandomStream.Genome, creature), WeightLimit);

        /// <summary>
        /// First generation of an experiment: genome i (0-based) uses the same stream as creature id i + 1 of a run
        /// with the experiment seed, so growing the population keeps the existing genomes.
        /// </summary>
        public static Genome[] Population(ulong experimentSeed, int count)
        {
            var genomes = new Genome[count];
            for (var i = 0; i < count; i++)
                genomes[i] = Genome.CreateRandom(NetworkTopology.Mlp12x16x3,
                    SeedDerivation.CreateIndexedStream(experimentSeed, RandomStream.Genome, (ulong)i + 1), WeightLimit);
            return genomes;
        }
    }
}
