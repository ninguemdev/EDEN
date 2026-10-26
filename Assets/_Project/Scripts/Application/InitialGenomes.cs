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
        /// First generation of an experiment with the fixed body: genome i (0-based) uses the same stream as creature
        /// id i + 1 of a run with the experiment seed, so growing the population keeps the existing genomes.
        /// </summary>
        public static Genome[] Population(ulong experimentSeed, int count) =>
            Population(experimentSeed, count, BodyGeneSet.None);

        /// <summary>
        /// First generation where the <paramref name="expressed"/> body genes start uniform within their limits and the
        /// others neutral. The body genes are drawn after the weights from the same stream, all four every time, so
        /// the weights never depend on the body genes and each gene's value never depends on which others are expressed.
        /// </summary>
        public static Genome[] Population(ulong experimentSeed, int count, BodyGeneSet expressed)
        {
            BodyGenes.RequireSet(expressed);
            var genomes = new Genome[count];
            for (var i = 0; i < count; i++)
            {
                var random = SeedDerivation.CreateIndexedStream(experimentSeed, RandomStream.Genome, (ulong)i + 1);
                var brain = Genome.CreateRandom(NetworkTopology.Mlp12x16x3, random, WeightLimit);
                var body = BodyGenes.CreateRandom(random).Masked(expressed);
                genomes[i] = body.IsNeutral ? brain : brain.WithBody(body);
            }

            return genomes;
        }
    }
}
