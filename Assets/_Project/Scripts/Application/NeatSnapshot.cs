using System;
using Eden.Brains;
using Eden.Domain;

namespace Eden.Application
{
    /// <summary>
    /// Value of every node and the connections of one NEAT network for one observation, with a column per node for
    /// drawing: the bias and inputs in column 0, each hidden node one column after the deepest node feeding it
    /// (counting disabled connections too, so toggling a gene never moves a node), and the outputs in the last column.
    /// The structure is copied when the brain changes; capturing the same brain again does not allocate. Reading it
    /// never affects the simulation.
    /// </summary>
    public sealed class NeatSnapshot
    {
        private NeatBrain source;
        private int[] nodeIds = Array.Empty<int>();
        private NeatNodeKind[] kinds = Array.Empty<NeatNodeKind>();
        private int[] columns = Array.Empty<int>();
        private float[] values = Array.Empty<float>();
        private NeatConnection[] connections = Array.Empty<NeatConnection>();
        private int[] fromSlots = Array.Empty<int>();
        private int[] toSlots = Array.Empty<int>();
        private readonly float[] outputs = new float[NeuralContract.Legacy.OutputCount];

        public bool HasData { get; private set; }
        public CreatureId Creature { get; private set; }

        /// <summary>How many captures were made.</summary>
        public long Captures { get; private set; }

        /// <summary>Changes whenever the captured structure changes (another brain), so drawings can lay out again.</summary>
        public int StructureVersion { get; private set; }

        public NeuralContract Contract => source?.Genome.Contract;
        public int NodeCount => nodeIds.Length;
        public int ColumnCount { get; private set; }
        public int NodeId(int slot) => nodeIds[slot];
        public NeatNodeKind Kind(int slot) => kinds[slot];
        public int Column(int slot) => columns[slot];
        public float Value(int slot) => values[slot];
        public int ConnectionCount => connections.Length;
        public NeatConnection Connection(int index) => connections[index];
        public int FromSlot(int index) => fromSlots[index];
        public int ToSlot(int index) => toSlots[index];

        /// <summary>The action the output values map to; equals the brain's action for the same observation.</summary>
        public CreatureAction Action
        {
            get
            {
                var first = NeatNetwork.OutputNodeId(NeuralContract.Legacy, 0);
                for (var o = 0; o < outputs.Length; o++)
                    outputs[o] = values[first + o];
                return MlpBrain.ToAction(outputs);
            }
        }

        public void Capture(CreatureId creature, NeatBrain brain, in Observation observation)
        {
            if (brain == null)
                throw new ArgumentNullException(nameof(brain));
            if (!ReferenceEquals(brain, source))
                CopyStructure(brain);
            brain.ComputeActivations(observation, values);
            Creature = creature;
            HasData = true;
            Captures++;
        }

        public void Clear()
        {
            HasData = false;
            source = null;
        }

        private void CopyStructure(NeatBrain brain)
        {
            var network = brain.Genome.Neat;
            var contract = network.Contract;
            var count = brain.NodeIds.Count;
            nodeIds = new int[count];
            kinds = new NeatNodeKind[count];
            columns = new int[count];
            values = new float[count];
            for (var s = 0; s < count; s++)
            {
                nodeIds[s] = brain.NodeIds[s];
                kinds[s] = network.KindOf(nodeIds[s]);
                columns[s] = kinds[s] == NeatNodeKind.Hidden ? 1 : 0;
            }

            connections = new NeatConnection[network.Connections.Count];
            fromSlots = new int[connections.Length];
            toSlots = new int[connections.Length];
            for (var c = 0; c < connections.Length; c++)
            {
                connections[c] = network.Connections[c];
                fromSlots[c] = SlotOf(connections[c].From);
                toSlots[c] = SlotOf(connections[c].To);
            }

            // Longest path from a source; the network has no cycle, so this settles within one pass per node.
            for (var pass = 0; pass < count; pass++)
            {
                var changed = false;
                for (var c = 0; c < connections.Length; c++)
                {
                    if (kinds[toSlots[c]] != NeatNodeKind.Hidden)
                        continue;
                    var depth = columns[fromSlots[c]] + 1;
                    if (depth > columns[toSlots[c]])
                    {
                        columns[toSlots[c]] = depth;
                        changed = true;
                    }
                }

                if (!changed)
                    break;
            }

            var outputColumn = 1;
            for (var s = 0; s < count; s++)
            {
                if (kinds[s] == NeatNodeKind.Hidden)
                    outputColumn = Math.Max(outputColumn, columns[s] + 1);
            }

            for (var o = 0; o < contract.OutputCount; o++)
                columns[SlotOf(NeatNetwork.OutputNodeId(contract, o))] = outputColumn;
            ColumnCount = outputColumn + 1;
            source = brain;
            StructureVersion++;
        }

        private int SlotOf(int nodeId)
        {
            for (var s = 0; s < nodeIds.Length; s++)
            {
                if (nodeIds[s] == nodeId)
                    return s;
            }

            throw new ArgumentException($"Node {nodeId} is not in the brain.");
        }
    }
}
