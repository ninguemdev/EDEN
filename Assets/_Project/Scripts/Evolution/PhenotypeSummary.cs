using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>
    /// Body genes of one generation: per gene, the mean and the population standard deviation over its individuals
    /// (as for the weights). <see cref="Diversity"/> is the mean standard deviation of the expressed genes, the body
    /// counterpart of <see cref="GenerationSummary.WeightStdDev"/>. Genes are in [−1, 1]; each trait factor is
    /// 1 + 0.5·gene, so a gene mean maps directly to the mean trait.
    /// </summary>
    public sealed class PhenotypeSummary
    {
        private readonly float[] means = new float[BodyGenes.Count];
        private readonly float[] stdDevs = new float[BodyGenes.Count];

        public PhenotypeSummary(int generation, IReadOnlyList<Individual> individuals)
        {
            if (individuals == null || individuals.Count == 0)
                throw new ArgumentException("At least one individual is required.", nameof(individuals));
            if (generation < 0)
                throw new ArgumentOutOfRangeException(nameof(generation));

            Generation = generation;
            PopulationSize = individuals.Count;
            for (var g = 0; g < BodyGenes.Count; g++)
            {
                double sum = 0;
                foreach (var individual in individuals)
                    sum += individual.Genome.Body[(BodyGene)g];
                var mean = sum / individuals.Count;
                double squares = 0;
                foreach (var individual in individuals)
                {
                    var delta = individual.Genome.Body[(BodyGene)g] - mean;
                    squares += delta * delta;
                }

                means[g] = (float)mean;
                stdDevs[g] = (float)Math.Sqrt(squares / individuals.Count);
            }
        }

        public int Generation { get; }
        public int PopulationSize { get; }

        public float Mean(BodyGene gene) => means[Index(gene)];
        public float StdDev(BodyGene gene) => stdDevs[Index(gene)];

        /// <summary>Mean standard deviation of the <paramref name="expressed"/> genes; 0 for the fixed body.</summary>
        public float Diversity(BodyGeneSet expressed)
        {
            BodyGenes.RequireSet(expressed);
            double sum = 0;
            var count = 0;
            for (var g = 0; g < BodyGenes.Count; g++)
            {
                if (((int)expressed & (1 << g)) == 0)
                    continue;
                sum += stdDevs[g];
                count++;
            }

            return count == 0 ? 0f : (float)(sum / count);
        }

        private static int Index(BodyGene gene)
        {
            var index = (int)gene;
            if (index < 0 || index >= BodyGenes.Count)
                throw new ArgumentOutOfRangeException(nameof(gene));
            return index;
        }
    }
}
