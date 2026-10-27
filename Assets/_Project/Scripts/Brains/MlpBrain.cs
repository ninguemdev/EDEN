using System;
using Eden.Domain;

namespace Eden.Brains
{
    /// <summary>
    /// Fixed-topology multilayer perceptron 12 → 16 → 3 with tanh in both layers (<see cref="NeuralLayout"/>).
    /// Outputs map to thrust = (tanh + 1) / 2, turn = tanh, strafe = tanh. With fixed <see cref="PlasticityGenes"/> (the
    /// default) it is feed-forward only: no memory between decisions and no learning during a life. With a learning
    /// rate, the hidden → output weights become W_base + ΔW, where ΔW lives in the creature's <see cref="BrainState"/>
    /// and changes at every decision by a Hebbian rule modulated by the change of the energy input (experiment M7, not
    /// a guarantee of reinforcement learning): ΔW ← clamp((1 − decay)·ΔW + rate·m·h·y, ±<see cref="PlasticMemory.MaxDelta"/>),
    /// with m = energy input now − at the previous decision and h, y the hidden and output activations of the previous
    /// decision. Parameters are copied in, so callers can never alter a brain through a shared array, and inference
    /// reuses buffers, so it does not allocate. Because of those buffers an instance is not thread-safe; the simulation
    /// evaluates creatures one at a time.
    /// </summary>
    public sealed class MlpBrain : IBrain
    {
        public const string BrainName = "mlp-12-16-3-v1";

        private readonly float[] parameters = new float[NeuralLayout.ParameterCount];
        private readonly float[] input = new float[NeuralLayout.Inputs];
        private readonly float[] hidden = new float[NeuralLayout.Hidden];
        private readonly float[] output = new float[NeuralLayout.Outputs];

        /// <summary>Builds a brain from raw parameters, validated with the same rules as a <see cref="Domain.Genome"/>.</summary>
        public MlpBrain(float[] parameters)
            : this(new Genome(NetworkTopology.Mlp12x16x3, parameters))
        {
        }

        public MlpBrain(Genome genome)
        {
            if (genome == null)
                throw new ArgumentNullException(nameof(genome));
            if (genome.Network != Genome.MlpNetwork)
                throw new ArgumentException($"MlpBrain needs an MLP genome, got a {genome.Network} one.", nameof(genome));
            if (!genome.Topology.Equals(NetworkTopology.Mlp12x16x3) || genome.WeightCount != NeuralLayout.ParameterCount)
                throw new ArgumentException($"MlpBrain needs a {NetworkTopology.Mlp12x16x3} genome, got {genome.Topology}.",
                    nameof(genome));

            Genome = genome;
            Plasticity = genome.Plasticity;
            genome.CopyWeightsTo(parameters);
        }

        public string Name => BrainName;

        /// <summary>The heritable description this brain was built from.</summary>
        public Genome Genome { get; }

        public PlasticityGenes Plasticity { get; }

        public float GetParameter(int index) => parameters[index];

        public void CopyParametersTo(float[] destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (destination.Length < parameters.Length)
                throw new ArgumentException("Destination is too small.", nameof(destination));
            Array.Copy(parameters, destination, parameters.Length);
        }

        /// <summary>
        /// Inputs follow the <see cref="ObservationLimits"/> policy, so NaN or out-of-range values never reach the
        /// network. A plastic brain learns into <paramref name="state"/>; without a state it acts as its fixed network.
        /// </summary>
        public CreatureAction Evaluate(in Observation observation, BrainState state)
        {
            ObservationLimits.Sanitize(observation, out _).CopyTo(input, 0);
            if (!Plasticity.IsPlastic || state == null)
            {
                Forward(input, hidden, output);
                return ToAction(output);
            }

            var memory = state.EnsurePlastic();
            var energy = input[0];
            Learn(memory, energy);
            ForwardPlastic(input, memory.DeltaWeights, hidden, output);
            Array.Copy(hidden, memory.LastHidden, NeuralLayout.Hidden);
            Array.Copy(output, memory.LastOutput, NeuralLayout.Outputs);
            memory.LastEnergy = energy;
            memory.HasPrevious = true;
            return ToAction(output);
        }

        /// <summary>
        /// Writes hidden and output activations (tanh values, before the action mapping) for an observation into the
        /// caller's buffers. Pure function of the parameters and the observation, meant for on-demand inspection.
        /// </summary>
        public void ComputeActivations(in Observation observation, float[] hiddenActivations, float[] outputActivations)
        {
            if (hiddenActivations == null || hiddenActivations.Length < NeuralLayout.Hidden)
                throw new ArgumentException($"Needs at least {NeuralLayout.Hidden} values.", nameof(hiddenActivations));
            if (outputActivations == null || outputActivations.Length < NeuralLayout.Outputs)
                throw new ArgumentException($"Needs at least {NeuralLayout.Outputs} values.", nameof(outputActivations));

            ObservationLimits.Sanitize(observation, out _).CopyTo(input, 0);
            Forward(input, hiddenActivations, outputActivations);
        }

        public static CreatureAction ToAction(float[] outputActivations) =>
            new CreatureAction((outputActivations[0] + 1f) * 0.5f, outputActivations[1], outputActivations[2]);

        // The first decision of a life only decays (there is nothing to pair a change of energy with yet).
        private void Learn(PlasticMemory memory, float energy)
        {
            var keep = 1f - Plasticity.Decay;
            var signal = memory.HasPrevious ? Plasticity.LearningRate * (energy - memory.LastEnergy) : 0f;
            var deltas = memory.DeltaWeights;
            for (var o = 0; o < NeuralLayout.Outputs; o++)
            {
                var post = signal * memory.LastOutput[o];
                for (var h = 0; h < NeuralLayout.Hidden; h++)
                {
                    var index = o * NeuralLayout.Hidden + h;
                    var value = deltas[index] * keep + post * memory.LastHidden[h];
                    deltas[index] = value < -PlasticMemory.MaxDelta ? -PlasticMemory.MaxDelta
                        : value > PlasticMemory.MaxDelta ? PlasticMemory.MaxDelta
                        : value;
                }
            }
        }

        // Same as Forward with W_base + ΔW for the hidden → output weights; kept apart so the fixed path never changes.
        private void ForwardPlastic(float[] inputValues, float[] deltas, float[] hiddenValues, float[] outputValues)
        {
            for (var h = 0; h < NeuralLayout.Hidden; h++)
            {
                var sum = parameters[NeuralLayout.HiddenBias(h)];
                var row = NeuralLayout.HiddenWeight(h, 0);
                for (var i = 0; i < NeuralLayout.Inputs; i++)
                    sum += parameters[row + i] * inputValues[i];
                hiddenValues[h] = MathF.Tanh(sum);
            }

            for (var o = 0; o < NeuralLayout.Outputs; o++)
            {
                var sum = parameters[NeuralLayout.OutputBias(o)];
                var row = NeuralLayout.OutputWeight(o, 0);
                var learned = o * NeuralLayout.Hidden;
                for (var h = 0; h < NeuralLayout.Hidden; h++)
                    sum += (parameters[row + h] + deltas[learned + h]) * hiddenValues[h];
                outputValues[o] = MathF.Tanh(sum);
            }
        }

        private void Forward(float[] inputValues, float[] hiddenValues, float[] outputValues)
        {
            for (var h = 0; h < NeuralLayout.Hidden; h++)
            {
                var sum = parameters[NeuralLayout.HiddenBias(h)];
                var row = NeuralLayout.HiddenWeight(h, 0);
                for (var i = 0; i < NeuralLayout.Inputs; i++)
                    sum += parameters[row + i] * inputValues[i];
                hiddenValues[h] = MathF.Tanh(sum);
            }

            for (var o = 0; o < NeuralLayout.Outputs; o++)
            {
                var sum = parameters[NeuralLayout.OutputBias(o)];
                var row = NeuralLayout.OutputWeight(o, 0);
                for (var h = 0; h < NeuralLayout.Hidden; h++)
                    sum += parameters[row + h] * hiddenValues[h];
                outputValues[o] = MathF.Tanh(sum);
            }
        }
    }
}
