using System;
using System.Collections.Generic;

namespace Eden.Domain
{
    /// <summary>Role of a NEAT node. Persisted in reports: never renumber.</summary>
    public enum NeatNodeKind
    {
        /// <summary>Constant input 1; takes the place of per-node biases.</summary>
        Bias = 0,

        /// <summary>Reads one input channel of the neural contract.</summary>
        Input = 1,

        /// <summary>Writes one output channel of the neural contract.</summary>
        Output = 2,

        /// <summary>Created by splitting a connection; never part of the contract.</summary>
        Hidden = 3
    }

    /// <summary>
    /// One connection gene: its historical marking (<see cref="Innovation"/>), the nodes it joins, its weight and whether
    /// it is expressed. A disabled connection stays in the genome (it can be enabled again and still aligns genomes in
    /// crossover) but carries no signal.
    /// </summary>
    public readonly struct NeatConnection : IEquatable<NeatConnection>
    {
        public NeatConnection(int innovation, int from, int to, float weight, bool enabled)
        {
            Innovation = innovation;
            From = from;
            To = to;
            Weight = weight;
            Enabled = enabled;
        }

        public int Innovation { get; }
        public int From { get; }
        public int To { get; }
        public float Weight { get; }
        public bool Enabled { get; }

        public NeatConnection WithWeight(float weight) => new NeatConnection(Innovation, From, To, weight, Enabled);
        public NeatConnection WithEnabled(bool enabled) => new NeatConnection(Innovation, From, To, Weight, enabled);

        public bool Equals(NeatConnection other) =>
            Innovation == other.Innovation && From == other.From && To == other.To && Weight.Equals(other.Weight) &&
            Enabled == other.Enabled;

        public override bool Equals(object obj) => obj is NeatConnection other && Equals(other);
        public override int GetHashCode() => (Innovation * 397 ^ From) * 397 ^ To;

        public override string ToString() =>
            $"#{Innovation} {From}→{To} {Weight}{(Enabled ? string.Empty : " (disabled)")}";
    }

    /// <summary>
    /// Heritable NEAT network (Stanley and Miikkulainen, 2002): a list of connection genes, each with a historical
    /// marking, between nodes whose roles come from the neural contract. Immutable and always valid:
    /// <list type="bullet">
    /// <item>node ids are fixed by the contract for the bias (0), the inputs (1 + input index) and the outputs
    /// (1 + input count + output index); hidden nodes have ids from <see cref="FirstHiddenNodeId"/> on and exist because
    /// a connection names them, so genomes of one experiment share the ids of every node they have in common;</item>
    /// <item>connections start at the bias, an input or a hidden node and end at a hidden or output node, never at
    /// themselves; innovations and (from, to) pairs are unique;</item>
    /// <item>the graph of all connections, enabled or not, has no cycle, so enabling a connection again never makes
    /// the network recurrent;</item>
    /// <item>weights are finite and within ±<see cref="MaxAbsWeight"/>.</item>
    /// </list>
    /// Connections are kept in innovation order. Unlike the MLP, there are no per-node biases: the bias node feeds them.
    /// </summary>
    public sealed class NeatNetwork
    {
        /// <summary>Kind of network recorded in files.</summary>
        public const string Kind = "neat";

        public const int BiasNodeId = 0;
        public const float MaxAbsWeight = Genome.MaxAbsWeight;

        /// <summary>Upper bound on connection genes, so files and mutations stay within a known size.</summary>
        public const int MaxConnections = 16384;

        private readonly NeatConnection[] connections;
        private readonly int[] hiddenNodes;

        public NeatNetwork(NeuralContract contract, IReadOnlyList<NeatConnection> connections)
        {
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            if (connections == null)
                throw new ArgumentNullException(nameof(connections));
            if (connections.Count > MaxConnections)
                throw new ArgumentException($"At most {MaxConnections} connections (got {connections.Count}).",
                    nameof(connections));

            this.connections = new NeatConnection[connections.Count];
            for (var i = 0; i < this.connections.Length; i++)
                this.connections[i] = connections[i];
            Array.Sort(this.connections, (a, b) => a.Innovation.CompareTo(b.Innovation));
            hiddenNodes = Validate(contract, this.connections);
        }

        public NeuralContract Contract { get; }

        /// <summary>Connection genes in innovation order.</summary>
        public IReadOnlyList<NeatConnection> Connections => connections;

        /// <summary>Ids of the hidden nodes, ascending.</summary>
        public IReadOnlyList<int> HiddenNodes => hiddenNodes;

        public int EnabledConnectionCount
        {
            get
            {
                var count = 0;
                foreach (var connection in connections)
                {
                    if (connection.Enabled)
                        count++;
                }

                return count;
            }
        }

        public static int InputNodeId(int inputIndex) => 1 + inputIndex;
        public static int OutputNodeId(NeuralContract contract, int outputIndex) => 1 + contract.InputCount + outputIndex;
        public static int FirstHiddenNodeId(NeuralContract contract) => 1 + contract.InputCount + contract.OutputCount;

        /// <summary>
        /// Innovations of the minimal network: the connection from source s (0 = bias, 1 + i = input i) to output o is
        /// innovation s × outputs + o, the same in every genome of a contract; later innovations start at
        /// <see cref="MinimalConnectionCount"/>.
        /// </summary>
        public static int MinimalConnectionCount(NeuralContract contract) => (contract.InputCount + 1) * contract.OutputCount;

        /// <summary>Innovation of a minimal-network connection, or -1 when (from, to) is not one.</summary>
        public static int MinimalInnovation(NeuralContract contract, int from, int to)
        {
            var output = to - OutputNodeId(contract, 0);
            if (from < BiasNodeId || from > contract.InputCount || output < 0 || output >= contract.OutputCount)
                return -1;
            return from * contract.OutputCount + output;
        }

        public NeatNodeKind KindOf(int nodeId)
        {
            if (nodeId == BiasNodeId)
                return NeatNodeKind.Bias;
            if (nodeId >= InputNodeId(0) && nodeId < OutputNodeId(Contract, 0))
                return NeatNodeKind.Input;
            if (nodeId >= OutputNodeId(Contract, 0) && nodeId < FirstHiddenNodeId(Contract))
                return NeatNodeKind.Output;
            if (nodeId >= FirstHiddenNodeId(Contract) && Array.BinarySearch(hiddenNodes, nodeId) >= 0)
                return NeatNodeKind.Hidden;
            throw new ArgumentOutOfRangeException(nameof(nodeId), $"Node {nodeId} is not in this network.");
        }

        public bool ContainsNode(int nodeId) =>
            nodeId >= BiasNodeId && (nodeId < FirstHiddenNodeId(Contract) || Array.BinarySearch(hiddenNodes, nodeId) >= 0);

        /// <summary>Whether a connection from <paramref name="from"/> to <paramref name="to"/> exists, enabled or not.</summary>
        public bool HasConnection(int from, int to)
        {
            foreach (var connection in connections)
            {
                if (connection.From == from && connection.To == to)
                    return true;
            }

            return false;
        }

        /// <summary>Index of the connection with this innovation, or a negative number when there is none.</summary>
        public int IndexOfInnovation(int innovation)
        {
            int low = 0, high = connections.Length - 1;
            while (low <= high)
            {
                var middle = (low + high) >> 1;
                var value = connections[middle].Innovation;
                if (value == innovation)
                    return middle;
                if (value < innovation)
                    low = middle + 1;
                else
                    high = middle - 1;
            }

            return ~low;
        }

        /// <summary>Whether adding from → to would close a cycle through the existing connections (enabled or not).</summary>
        public bool WouldCreateCycle(int from, int to)
        {
            if (from == to)
                return true;
            // A cycle appears when "from" is already reachable from "to".
            var pending = new Stack<int>();
            var seen = new HashSet<int>();
            pending.Push(to);
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                if (node == from)
                    return true;
                if (!seen.Add(node))
                    continue;
                foreach (var connection in connections)
                {
                    if (connection.From == node)
                        pending.Push(connection.To);
                }
            }

            return false;
        }

        public bool HasSameContent(NeatNetwork other)
        {
            if (other == null || other.Contract != Contract || other.connections.Length != connections.Length)
                return false;
            for (var i = 0; i < connections.Length; i++)
            {
                if (!connections[i].Equals(other.connections[i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The minimal network of a contract: the bias and every input connected to every output, enabled, with no
        /// hidden node. Weights are drawn uniformly in [-limit, limit] from <paramref name="random"/> in innovation order.
        /// </summary>
        public static NeatNetwork CreateMinimal(NeuralContract contract, IRandomSource random, float limit)
        {
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (float.IsNaN(limit) || limit <= 0f || limit > MaxAbsWeight)
                throw new ArgumentOutOfRangeException(nameof(limit), $"Limit must be in (0, {MaxAbsWeight}].");

            var genes = new NeatConnection[MinimalConnectionCount(contract)];
            for (var source = 0; source <= contract.InputCount; source++)
            {
                for (var output = 0; output < contract.OutputCount; output++)
                {
                    var innovation = source * contract.OutputCount + output;
                    genes[innovation] = new NeatConnection(innovation, source, OutputNodeId(contract, output),
                        random.NextFloat(-limit, limit), true);
                }
            }

            return new NeatNetwork(contract, genes);
        }

        // Checks every rule in the summary and returns the hidden node ids; connections are in innovation order.
        private static int[] Validate(NeuralContract contract, NeatConnection[] connections)
        {
            var firstOutput = OutputNodeId(contract, 0);
            var firstHidden = FirstHiddenNodeId(contract);
            var pairs = new HashSet<long>();
            var hidden = new SortedSet<int>();
            for (var i = 0; i < connections.Length; i++)
            {
                var gene = connections[i];
                if (gene.Innovation < 0)
                    throw new ArgumentException($"{gene}: innovations are not negative.", nameof(connections));
                if (i > 0 && connections[i - 1].Innovation == gene.Innovation)
                    throw new ArgumentException($"Innovation {gene.Innovation} appears twice.", nameof(connections));
                if (gene.From < BiasNodeId || (gene.From >= firstOutput && gene.From < firstHidden))
                    throw new ArgumentException($"{gene}: a connection starts at the bias, an input or a hidden node.",
                        nameof(connections));
                if (gene.To < firstOutput)
                    throw new ArgumentException($"{gene}: a connection ends at an output or a hidden node.",
                        nameof(connections));
                if (gene.From == gene.To)
                    throw new ArgumentException($"{gene}: a node cannot connect to itself.", nameof(connections));
                if (!pairs.Add((long)gene.From << 32 | (uint)gene.To))
                    throw new ArgumentException($"{gene}: nodes {gene.From} and {gene.To} are already connected.",
                        nameof(connections));
                var weight = gene.Weight;
                if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < -MaxAbsWeight || weight > MaxAbsWeight)
                    throw new ArgumentException($"{gene}: weights are finite and within ±{MaxAbsWeight}.",
                        nameof(connections));
                if (gene.From >= firstHidden)
                    hidden.Add(gene.From);
                if (gene.To >= firstHidden)
                    hidden.Add(gene.To);
            }

            RequireAcyclic(connections);
            var ids = new int[hidden.Count];
            hidden.CopyTo(ids);
            return ids;
        }

        // Kahn's algorithm over every connection: nodes left with incoming edges lie on a cycle.
        private static void RequireAcyclic(NeatConnection[] connections)
        {
            var incoming = new Dictionary<int, int>();
            foreach (var gene in connections)
            {
                incoming.TryGetValue(gene.To, out var count);
                incoming[gene.To] = count + 1;
                if (!incoming.ContainsKey(gene.From))
                    incoming[gene.From] = 0;
            }

            var ready = new Stack<int>();
            foreach (var pair in incoming)
            {
                if (pair.Value == 0)
                    ready.Push(pair.Key);
            }

            var removed = 0;
            while (ready.Count > 0)
            {
                var node = ready.Pop();
                removed++;
                foreach (var gene in connections)
                {
                    if (gene.From != node)
                        continue;
                    var left = incoming[gene.To] - 1;
                    incoming[gene.To] = left;
                    if (left == 0)
                        ready.Push(gene.To);
                }
            }

            if (removed != incoming.Count)
                throw new ArgumentException("The connections form a cycle; NEAT networks here are feed-forward.",
                    nameof(connections));
        }
    }
}
