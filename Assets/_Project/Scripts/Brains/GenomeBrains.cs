using System;
using Eden.Domain;

namespace Eden.Brains
{
    /// <summary>The brain a genome describes, by the kind of its network.</summary>
    public static class GenomeBrains
    {
        /// <summary>An <see cref="MlpBrain"/> for an MLP genome, a <see cref="NeatBrain"/> for a NEAT one.</summary>
        public static IBrain Create(Genome genome)
        {
            if (genome == null)
                throw new ArgumentNullException(nameof(genome));
            return genome.Neat == null ? (IBrain)new MlpBrain(genome) : new NeatBrain(genome);
        }
    }
}
