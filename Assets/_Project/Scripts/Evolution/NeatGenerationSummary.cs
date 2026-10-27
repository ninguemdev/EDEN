using System;
using System.Collections.Generic;

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
            var sizes = new Dictionary<int, int>();
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
            }

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

        /// <summary>Species id of each individual, in candidate order.</summary>
        public IReadOnlyList<int> Species => species;

        public int HiddenNodesOf(int candidate) => hiddenNodes[candidate];
        public int ConnectionsOf(int candidate) => connections[candidate];
        public int EnabledConnectionsOf(int candidate) => enabledConnections[candidate];
    }
}
