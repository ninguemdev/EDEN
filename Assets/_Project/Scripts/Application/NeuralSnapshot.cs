using System;
using Eden.Brains;
using Eden.Domain;

namespace Eden.Application
{
    /// <summary>
    /// Inputs, hidden and output activations and weights of one MLP for one observation, for on-demand inspection
    /// of a single creature. Buffers are allocated once; <see cref="Capture"/> does not allocate and only copies the
    /// weights again when the brain changes. Reading it never affects the simulation.
    /// </summary>
    public sealed class NeuralSnapshot
    {
        private readonly float[] inputs = new float[NeuralLayout.Inputs];
        private readonly float[] hidden = new float[NeuralLayout.Hidden];
        private readonly float[] outputs = new float[NeuralLayout.Outputs];
        private readonly float[] weights = new float[NeuralLayout.ParameterCount];
        private MlpBrain weightsSource;

        public bool HasData { get; private set; }
        public CreatureId Creature { get; private set; }

        /// <summary>How many captures were made; lets the lab prove nothing is computed without a selection.</summary>
        public long Captures { get; private set; }

        public float Input(int index) => inputs[index];
        public float Hidden(int index) => hidden[index];
        public float Output(int index) => outputs[index];
        public float Weight(int parameterIndex) => weights[parameterIndex];

        /// <summary>The action these outputs map to; equals the brain's action for the same observation.</summary>
        public CreatureAction Action => MlpBrain.ToAction(outputs);

        public void Capture(CreatureId creature, MlpBrain brain, in Observation observation)
        {
            if (brain == null)
                throw new ArgumentNullException(nameof(brain));

            ObservationLimits.Sanitize(observation, out _).CopyTo(inputs, 0);
            brain.ComputeActivations(observation, hidden, outputs);
            if (!ReferenceEquals(brain, weightsSource))
            {
                brain.CopyParametersTo(weights);
                weightsSource = brain;
            }

            Creature = creature;
            HasData = true;
            Captures++;
        }

        public void Clear()
        {
            HasData = false;
            weightsSource = null;
        }
    }
}
