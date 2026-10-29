using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Brains
{
    /// <summary>
    /// Feed-forward NEAT network built from a <see cref="Genome"/>'s <see cref="NeatNetwork"/> on the legacy contract:
    /// the bias node reads 1, input node 1 + i reads observation input i, and every hidden and output node is tanh of
    /// the sum of its enabled incoming connections, added in innovation order. Disabled connections carry nothing; a node
    /// without enabled inputs reads tanh(0) = 0, so an unconnected output gives the neutral action of a zero network:
    /// thrust 0.5, turn 0, strafe 0. Outputs map to actions like the MLP's (<see cref="MlpBrain.ToAction"/>). The
    /// evaluation order is computed once; inference reuses buffers, so it does not allocate and an instance is not
    /// thread-safe. It has no memory between decisions and does not learn during a life.
    /// </summary>
    public sealed class NeatBrain : IBrain
    {
        public const string BrainName = "neat-v1";

        private readonly int[] nodeIds;
        private readonly float[] values;
        private readonly int[] order;
        private readonly int[] incomingStart;
        private readonly int[] incomingSource;
        private readonly float[] incomingWeight;
        private readonly int[] outputSlots;
        private readonly float[] input = new float[Observation.Size];
        private readonly float[] output;

        public NeatBrain(Genome genome)
        {
            if (genome == null)
                throw new ArgumentNullException(nameof(genome));
            var network = genome.Neat ??
                          throw new ArgumentException($"NeatBrain needs a NEAT genome, got a {genome.Network} one.",
                              nameof(genome));
            if (network.Contract != NeuralContract.Legacy)
                throw new ArgumentException($"NeatBrain reads the {NeuralContract.LegacyId} observation, not " +
                                            $"{network.Contract.Id}.", nameof(genome));

            Genome = genome;
            var contract = network.Contract;
            var hidden = network.HiddenNodes;
            var first = NeatNetwork.FirstHiddenNodeId(contract);
            // Slots: bias, inputs and outputs at their node ids, then hidden nodes in id order.
            nodeIds = new int[first + hidden.Count];
            for (var i = 0; i < first; i++)
                nodeIds[i] = i;
            for (var h = 0; h < hidden.Count; h++)
                nodeIds[first + h] = hidden[h];
            var slotOf = new Dictionary<int, int>(nodeIds.Length);
            for (var s = 0; s < nodeIds.Length; s++)
                slotOf.Add(nodeIds[s], s);

            values = new float[nodeIds.Length];
            output = new float[contract.OutputCount];
            outputSlots = new int[contract.OutputCount];
            for (var o = 0; o < outputSlots.Length; o++)
                outputSlots[o] = NeatNetwork.OutputNodeId(contract, o);

            order = EvaluationOrder(network, slotOf, contract);
            var enabled = new List<NeatConnection>();
            foreach (var connection in network.Connections)
            {
                if (connection.Enabled)
                    enabled.Add(connection);
            }

            incomingStart = new int[order.Length + 1];
            incomingSource = new int[enabled.Count];
            incomingWeight = new float[enabled.Count];
            var next = 0;
            for (var n = 0; n < order.Length; n++)
            {
                incomingStart[n] = next;
                var target = nodeIds[order[n]];
                foreach (var connection in enabled)
                {
                    if (connection.To != target)
                        continue;
                    incomingSource[next] = slotOf[connection.From];
                    incomingWeight[next] = connection.Weight;
                    next++;
                }
            }

            incomingStart[order.Length] = next;
        }

        public string Name => BrainName;

        /// <summary>The heritable description this brain was built from.</summary>
        public Genome Genome { get; }

        /// <summary>Node ids in slot order: bias, inputs, outputs, then hidden nodes ascending.</summary>
        public IReadOnlyList<int> NodeIds => nodeIds;

        /// <summary>Inputs follow the <see cref="ObservationLimits"/> policy, so NaN or out-of-range values never reach the network.</summary>
        public CreatureAction Evaluate(in Observation observation, BrainState state)
        {
            Forward(observation, values);
            for (var o = 0; o < output.Length; o++)
                output[o] = values[outputSlots[o]];
            return MlpBrain.ToAction(output);
        }

        /// <summary>
        /// Writes the value of every node, in <see cref="NodeIds"/> order, for an observation into the caller's buffer:
        /// 1 for the bias, the sanitized inputs, and tanh activations for outputs and hidden nodes. Pure function of the
        /// genome and the observation, meant for on-demand inspection.
        /// </summary>
        public void ComputeActivations(in Observation observation, float[] nodeValues)
        {
            if (nodeValues == null || nodeValues.Length < nodeIds.Length)
                throw new ArgumentException($"Needs at least {nodeIds.Length} values.", nameof(nodeValues));
            Forward(observation, nodeValues);
        }

        private void Forward(in Observation observation, float[] nodeValues)
        {
            ObservationLimits.Sanitize(observation, out _).CopyTo(input, 0);
            nodeValues[NeatNetwork.BiasNodeId] = 1f;
            for (var i = 0; i < input.Length; i++)
                nodeValues[NeatNetwork.InputNodeId(i)] = input[i];
            for (var n = 0; n < order.Length; n++)
            {
                var sum = 0f;
                for (var c = incomingStart[n]; c < incomingStart[n + 1]; c++)
                    sum += incomingWeight[c] * nodeValues[incomingSource[c]];
                nodeValues[order[n]] = MathF.Tanh(sum);
            }
        }

        // Slots of the hidden and output nodes so that every node comes after the sources of its enabled connections
        // (Kahn's algorithm, lowest node id first among the ready ones). The network has no cycle, so all are placed.
        private static int[] EvaluationOrder(NeatNetwork network, Dictionary<int, int> slotOf, NeuralContract contract)
        {
            var pendingInputs = new Dictionary<int, int>();
            for (var o = 0; o < contract.OutputCount; o++)
                pendingInputs[NeatNetwork.OutputNodeId(contract, o)] = 0;
            foreach (var node in network.HiddenNodes)
                pendingInputs[node] = 0;
            foreach (var connection in network.Connections)
            {
                if (connection.Enabled && pendingInputs.ContainsKey(connection.From))
                    pendingInputs[connection.To]++;
            }

            var ready = new SortedSet<int>();
            foreach (var pair in pendingInputs)
            {
                if (pair.Value == 0)
                    ready.Add(pair.Key);
            }

            var order = new int[pendingInputs.Count];
            var placed = 0;
            while (ready.Count > 0)
            {
                var node = ready.Min;
                ready.Remove(node);
                order[placed++] = slotOf[node];
                foreach (var connection in network.Connections)
                {
                    if (!connection.Enabled || connection.From != node)
                        continue;
                    var left = --pendingInputs[connection.To];
                    if (left == 0)
                        ready.Add(connection.To);
                }
            }

            if (placed != order.Length)
                throw new InvalidOperationException("A valid NEAT network has no cycle.");
            return order;
        }
    }
}
