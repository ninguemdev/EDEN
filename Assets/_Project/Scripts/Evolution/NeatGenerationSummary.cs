using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>
    /// NEAT structure of one evaluated generation: the species each individual was placed in and how large the networks
    /// are. Only training generations of NEAT experiments have one.
    /// </summary>
    public sealed class NeatGenerationSummary
    {
        private readonly int[] species;
        private readonly int[] hiddenNodes;
        private readonly int[] connections;
        private readonly int[] enabledConnections;
        private readonly int[] eyes;

        public NeatGenerationSummary(IReadOnlyList<Individual> individuals, IReadOnlyList<int> speciesOf)
        {
            if (individuals == null)
                throw new ArgumentNullException(nameof(individuals));
            if (speciesOf == null || speciesOf.Count != individuals.Count || individuals.Count == 0)
                throw new ArgumentException("Every individual needs its species.", nameof(speciesOf));

            var count = individuals.Count;
            species = new int[count];
            hiddenNodes = new int[count];
            connections = new int[count];
            enabledConnections = new int[count];
            eyes = new int[count];
            var sizes = new Dictionary<int, int>();
            var eyesSeen = 0;
            double aperture = 0.0, range = 0.0, eyesTotal = 0.0;
            for (var i = 0; i < count; i++)
            {
                var network = individuals[i].Genome.Neat ??
                              throw new ArgumentException($"Individual {individuals[i].Id} is not NEAT.", nameof(individuals));
                species[i] = speciesOf[i];
                hiddenNodes[i] = network.HiddenNodes.Count;
                connections[i] = network.Connections.Count;
                enabledConnections[i] = network.EnabledConnectionCount;
                sizes.TryGetValue(species[i], out var size);
                sizes[species[i]] = size + 1;
                MaxHiddenNodes = Math.Max(MaxHiddenNodes, hiddenNodes[i]);
                MeanHiddenNodes += hiddenNodes[i];
                MeanConnections += connections[i];
                MeanEnabledConnections += enabledConnections[i];
                var set = individuals[i].Genome.Eyes;
                if (set == null)
                    continue;
                HasEyes = true;
                eyes[i] = set.Count;
                eyesTotal += set.Count;
                for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
                {
                    if (!set.Has(slot))
                        continue;
                    aperture += set[slot].Aperture;
                    range += set[slot].Range;
                    eyesSeen++;
                }
            }

            MeanEyes = HasEyes ? (float)(eyesTotal / count) : float.NaN;
            MeanEyeApertureDegrees = eyesSeen > 0 ? (float)(aperture / eyesSeen * 180.0 / Math.PI) : float.NaN;
            MeanEyeRange = eyesSeen > 0 ? (float)(range / eyesSeen) : float.NaN;

            MeanHiddenNodes /= count;
            MeanConnections /= count;
            MeanEnabledConnections /= count;
            SpeciesCount = sizes.Count;
            foreach (var size in sizes.Values)
                LargestSpecies = Math.Max(LargestSpecies, size);
        }

        public int SpeciesCount { get; }
        public int LargestSpecies { get; }
        public float MeanHiddenNodes { get; }
        public int MaxHiddenNodes { get; }
        public float MeanConnections { get; }
        public float MeanEnabledConnections { get; }

        /// <summary>Whether the genomes have eyes (the eye senses).</summary>
        public bool HasEyes { get; }

        /// <summary>Mean number of eyes per genome; NaN without the eye senses.</summary>
        public float MeanEyes { get; }

        /// <summary>Mean aperture of all eyes, degrees; NaN when there is no eye.</summary>
        public float MeanEyeApertureDegrees { get; }

        /// <summary>Mean range factor of all eyes; NaN when there is no eye.</summary>
        public float MeanEyeRange { get; }

        public int EyesOf(int candidate) => eyes[candidate];

        /// <summary>Species id of each individual, in candidate order.</summary>
        public IReadOnlyList<int> Species => species;

        public int HiddenNodesOf(int candidate) => hiddenNodes[candidate];
        public int ConnectionsOf(int candidate) => connections[candidate];
        public int EnabledConnectionsOf(int candidate) => enabledConnections[candidate];
    }
}
