using System;
using Eden.Domain;

namespace Eden.Brains
{
    /// <summary>
    /// Fixed-topology multilayer perceptron 12 → 16 → 3 with tanh in both layers (<see cref="NeuralLayout"/>).
    /// Outputs map to thrust = (tanh + 1) / 2, turn = tanh, strafe = tanh. Feed-forward only: it has no memory
    /// between decisions and does not learn during a life. Parameters are copied in, so callers can never alter a
    /// brain through a shared array, and inference reuses buffers, so it does not allocate. Because of those buffers
    /// an instance is not thread-safe; the simulation evaluates creatures one at a time.
    /// </summary>
    public sealed class MlpBrain : IBrain
    {
        public const string BrainName = "mlp-12-16-3-v1";

        private readonly float[] parameters = new float[NeuralLayout.ParameterCount];
        private readonly float[] input = new float[NeuralLayout.Inputs];
        private readonly float[] hidden = new float[NeuralLayout.Hidden];
        private readonly float[] output = new float[NeuralLayout.Outputs];

        public MlpBrain(float[] parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));
            if (parameters.Length != NeuralLayout.ParameterCount)
                throw new ArgumentException(
                    $"Expected {NeuralLayout.ParameterCount} parameters, got {parameters.Length}.", nameof(parameters));
            for (var i = 0; i < parameters.Length; i++)
            {
                if (float.IsNaN(parameters[i]) || float.IsInfinity(parameters[i]))
                    throw new ArgumentException($"Parameter {i} is not finite ({parameters[i]}).", nameof(parameters));
            }

            Array.Copy(parameters, this.parameters, parameters.Length);
        }

        public string Name => BrainName;

        public float GetParameter(int index) => parameters[index];

        public void CopyParametersTo(float[] destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (destination.Length < parameters.Length)
                throw new ArgumentException("Destination is too small.", nameof(destination));
            Array.Copy(parameters, destination, parameters.Length);
        }

        /// <summary>Inputs follow the <see cref="ObservationLimits"/> policy, so NaN or out-of-range values never reach the network.</summary>
        public CreatureAction Evaluate(in Observation observation, BrainState state)
        {
            ObservationLimits.Sanitize(observation, out _).CopyTo(input, 0);
            Forward(input, hidden, output);
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
