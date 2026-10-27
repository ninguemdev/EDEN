using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Evolution
{
    /// <summary>Connection innovations and hidden node created by splitting one connection.</summary>
    public readonly struct NodeSplit
    {
        public NodeSplit(int nodeId, int inInnovation, int outInnovation)
        {
            NodeId = nodeId;
            InInnovation = inInnovation;
            OutInnovation = outInnovation;
        }

        /// <summary>The new hidden node.</summary>
        public int NodeId { get; }

        /// <summary>Innovation of the connection from the split connection's source to the new node.</summary>
        public int InInnovation { get; }

        /// <summary>Innovation of the connection from the new node to the split connection's target.</summary>
        public int OutInnovation { get; }
    }

    /// <summary>What an <see cref="InnovationHistory"/> must keep between generations to continue exactly.</summary>
    public readonly struct InnovationHistoryState
    {
        public InnovationHistoryState(string contract, int generation, int nextInnovation, int nextNodeId)
        {
            Contract = contract;
            Generation = generation;
            NextInnovation = nextInnovation;
            NextNodeId = nextNodeId;
        }

        public string Contract { get; }
        public int Generation { get; }
        public int NextInnovation { get; }
        public int NextNodeId { get; }
    }

    /// <summary>
    /// Historical markings of one NEAT experiment. Structural mutations ask it for numbers: within one generation the
    /// same new structure gets the same numbers in every genome (the same (from, to) connection, or the split of the same
    /// connection into the same new node), so genomes that grew the same structure independently still line up in
    /// crossover (Stanley and Miikkulainen, 2002). A new generation forgets those structures and keeps only the counters,
    /// so a structure discovered again later gets new numbers; numbers are never reused. Connections of the minimal
    /// network always keep their fixed numbers (<see cref="NeatNetwork.MinimalInnovation"/>), whoever adds them.
    /// Only the counters and the generation need saving: breeding a generation completes between two checkpoints.
    /// Markings of independent experiments are unrelated even when the numbers coincide.
    /// </summary>
    public sealed class InnovationHistory
    {
        private readonly Dictionary<long, int> connectionsThisGeneration = new Dictionary<long, int>();
        private readonly Dictionary<int, NodeSplit> splitsThisGeneration = new Dictionary<int, NodeSplit>();

        /// <summary>History of a new experiment: only the minimal network exists, at generation 0.</summary>
        public InnovationHistory(NeuralContract contract)
        {
            Contract = contract ?? throw new ArgumentNullException(nameof(contract));
            NextInnovation = NeatNetwork.MinimalConnectionCount(contract);
            NextNodeId = NeatNetwork.FirstHiddenNodeId(contract);
        }

        public NeuralContract Contract { get; }

        /// <summary>Generation whose new structures are being numbered.</summary>
        public int Generation { get; private set; }

        /// <summary>Number the next new connection gets.</summary>
        public int NextInnovation { get; private set; }

        /// <summary>Id the next new hidden node gets.</summary>
        public int NextNodeId { get; private set; }

        /// <summary>Starts numbering the structures of a later generation; the previous ones are forgotten.</summary>
        public void BeginGeneration(int generation)
        {
            if (generation <= Generation)
                throw new ArgumentOutOfRangeException(nameof(generation),
                    $"Generation {generation} does not come after {Generation}.");
            Generation = generation;
            connectionsThisGeneration.Clear();
            splitsThisGeneration.Clear();
        }

        /// <summary>
        /// Innovation of a connection from <paramref name="from"/> to <paramref name="to"/> added in this generation:
        /// the fixed number of a minimal-network connection, the number already given to the same connection in this
        /// generation, or a new one.
        /// </summary>
        public int ConnectionInnovation(int from, int to)
        {
            var minimal = NeatNetwork.MinimalInnovation(Contract, from, to);
            if (minimal >= 0)
                return minimal;
            var key = (long)from << 32 | (uint)to;
            if (connectionsThisGeneration.TryGetValue(key, out var innovation))
                return innovation;
            innovation = TakeInnovation();
            connectionsThisGeneration.Add(key, innovation);
            return innovation;
        }

        /// <summary>
        /// New node and connections for splitting the connection with <paramref name="innovation"/>, from
        /// <paramref name="from"/> to <paramref name="to"/>, in this generation: the same ones as an earlier split of
        /// that connection in this generation, or new ones. The two new connections count as structures of this
        /// generation, so adding one of them as a plain connection later in the generation gets the same number.
        /// </summary>
        public NodeSplit Split(int innovation, int from, int to)
        {
            if (innovation < 0)
                throw new ArgumentOutOfRangeException(nameof(innovation));
            if (splitsThisGeneration.TryGetValue(innovation, out var split))
                return split;
            var node = TakeNodeId();
            var inInnovation = TakeInnovation();
            var outInnovation = TakeInnovation();
            split = new NodeSplit(node, inInnovation, outInnovation);
            splitsThisGeneration.Add(innovation, split);
            connectionsThisGeneration[(long)from << 32 | (uint)node] = inInnovation;
            connectionsThisGeneration[(long)node << 32 | (uint)to] = outInnovation;
            return split;
        }

        public InnovationHistoryState Capture() =>
            new InnovationHistoryState(Contract.Id, Generation, NextInnovation, NextNodeId);

        /// <summary>History saved between generations; the counters must lie beyond the minimal network.</summary>
        public static InnovationHistory Restore(InnovationHistoryState state)
        {
            if (!NeuralContract.TryFind(state.Contract, out var contract))
                throw new ArgumentException($"Neural contract \"{state.Contract}\" is unknown.", nameof(state));
            if (state.Generation < 0)
                throw new ArgumentException($"Generation {state.Generation} is negative.", nameof(state));
            if (state.NextInnovation < NeatNetwork.MinimalConnectionCount(contract) || state.NextInnovation == int.MaxValue)
                throw new ArgumentException($"Next innovation {state.NextInnovation} is impossible.", nameof(state));
            if (state.NextNodeId < NeatNetwork.FirstHiddenNodeId(contract) || state.NextNodeId == int.MaxValue)
                throw new ArgumentException($"Next node id {state.NextNodeId} is impossible.", nameof(state));
            return new InnovationHistory(contract)
            {
                Generation = state.Generation, NextInnovation = state.NextInnovation, NextNodeId = state.NextNodeId
            };
        }

        private int TakeInnovation()
        {
            if (NextInnovation == int.MaxValue)
                throw new InvalidOperationException("The experiment ran out of innovation numbers.");
            return NextInnovation++;
        }

        private int TakeNodeId()
        {
            if (NextNodeId == int.MaxValue)
                throw new InvalidOperationException("The experiment ran out of node ids.");
            return NextNodeId++;
        }
    }
}
