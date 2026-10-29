using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>
    /// Generation 0 of a NEAT experiment from an imported pool. Historical markings of independent experiments can
    /// share numbers with different meanings, so imported genomes are never trusted to be homologous by number: each
    /// keeps the connections of the minimal network (whose numbers are fixed for the contract) and gets new hidden node
    /// ids and new innovations, unique to it, for everything else. Two imported genomes therefore only line up on the
    /// minimal network in crossover; structures they grow afterwards in this experiment line up normally.
    /// </summary>
    public static class NeatSeeding
    {
        /// <summary>
        /// The pool renumbered for <paramref name="history"/>: per genome, hidden nodes in ascending id order get new
        /// ids, and connections in innovation order keep their minimal-network number or get a new one.
        /// </summary>
        public static Genome[] Renumber(IReadOnlyList<Genome> pool, InnovationHistory history)
        {
            if (pool == null)
                throw new ArgumentNullException(nameof(pool));
            if (history == null)
                throw new ArgumentNullException(nameof(history));
            var contract = history.Contract;
            var renumbered = new Genome[pool.Count];
            for (var i = 0; i < pool.Count; i++)
            {
                var genome = pool[i] ?? throw new ArgumentException($"Pool genome {i} is null.", nameof(pool));
                var network = genome.Neat ??
                              throw new ArgumentException($"Pool genome {i} is a {genome.Network} genome; this " +
                                                          $"evolution breeds {NeatNetwork.Kind} genomes.", nameof(pool));
                if (network.Contract != contract)
                    throw new ArgumentException($"Pool genome {i} uses {network.Contract.Id}, not {contract.Id}.",
                        nameof(pool));

                var nodes = new Dictionary<int, int>();
                foreach (var hidden in network.HiddenNodes)
                    nodes.Add(hidden, history.NewNodeId());
                var genes = new NeatConnection[network.Connections.Count];
                for (var c = 0; c < genes.Length; c++)
                {
                    var gene = network.Connections[c];
                    var from = nodes.TryGetValue(gene.From, out var newFrom) ? newFrom : gene.From;
                    var to = nodes.TryGetValue(gene.To, out var newTo) ? newTo : gene.To;
                    var minimal = NeatNetwork.MinimalInnovation(contract, from, to);
                    genes[c] = new NeatConnection(minimal >= 0 ? minimal : history.NewInnovation(), from, to, gene.Weight,
                        gene.Enabled);
                }

                renumbered[i] = new Genome(new NeatNetwork(contract, genes), genome.Body, genome.Eyes);
            }

            return renumbered;
        }

        /// <summary>
        /// The NEAT counterpart of <see cref="Reproduction.Seed"/>: the pool renumbered, the best
        /// <c>min(pool.Count, rules.PopulationSize)</c> by <paramref name="poolFitness"/> cloned, the rest filled by
        /// tournaments within the pool (<see cref="ReproductionRules.TournamentSize"/>) and NEAT mutation numbered by the
        /// history of generation 0.
        /// </summary>
        public static Genome[] Seed(ReproductionRules rules, NeatRules neat, IReadOnlyList<Genome> pool,
            IReadOnlyList<float> poolFitness, InnovationHistory history, GaussianSampler random, BodyMutation body)
        {
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));
            if (neat == null)
                throw new ArgumentNullException(nameof(neat));
            if (poolFitness == null)
                throw new ArgumentNullException(nameof(poolFitness));
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (pool == null || pool.Count == 0 || pool.Count != poolFitness.Count)
                throw new ArgumentException("Every pool genome needs exactly one fitness value.", nameof(poolFitness));

            var renumbered = Renumber(pool, history);
            var ranking = Reproduction.Rank(poolFitness);
            var eliteCount = Math.Min(renumbered.Length, rules.PopulationSize);
            var genomes = new Genome[rules.PopulationSize];
            for (var e = 0; e < eliteCount; e++)
                genomes[e] = renumbered[ranking[e]];
            for (var c = eliteCount; c < genomes.Length; c++)
            {
                var parent = Reproduction.Tournament(poolFitness, rules.TournamentSize, random.Source);
                genomes[c] = NeatMutation.Mutate(renumbered[parent], neat, history, random, out _, body);
            }

            return genomes;
        }
    }
}
