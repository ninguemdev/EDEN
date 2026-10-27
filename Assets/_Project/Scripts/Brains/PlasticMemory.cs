using System;

namespace Eden.Brains
{
    /// <summary>
    /// What a plastic network has learned during one life: the change of each hidden → output weight
    /// (<see cref="DeltaWeights"/>, output-major like <see cref="NeuralLayout"/>, each within ±<see cref="MaxDelta"/>) and
    /// what the last decision saw and did (hidden and output activations and the energy input), which the next decision
    /// needs to apply the rule. It lives in the creature's <see cref="BrainState"/>: it starts empty at birth and in
    /// every new episode, is saved to continue a life and is never part of a genome, so it is never inherited.
    /// </summary>
    public sealed class PlasticMemory
    {
        public const int WeightCount = NeuralLayout.Hidden * NeuralLayout.Outputs;
        public const float MaxDelta = 1f;

        public float[] DeltaWeights { get; } = new float[WeightCount];
        public float[] LastHidden { get; } = new float[NeuralLayout.Hidden];
        public float[] LastOutput { get; } = new float[NeuralLayout.Outputs];
        public float LastEnergy { get; set; }

        /// <summary>False until the first decision: the rule needs a previous decision to pair with a change of energy.</summary>
        public bool HasPrevious { get; set; }

        /// <summary>Largest absolute weight change (0 for a memory that has not learned anything).</summary>
        public float MaxAbsDelta
        {
            get
            {
                var max = 0f;
                foreach (var delta in DeltaWeights)
                    max = Math.Max(max, Math.Abs(delta));
                return max;
            }
        }

        public void CopyFrom(PlasticMemory other)
        {
            if (other == null)
                throw new ArgumentNullException(nameof(other));
            Array.Copy(other.DeltaWeights, DeltaWeights, WeightCount);
            Array.Copy(other.LastHidden, LastHidden, NeuralLayout.Hidden);
            Array.Copy(other.LastOutput, LastOutput, NeuralLayout.Outputs);
            LastEnergy = other.LastEnergy;
            HasPrevious = other.HasPrevious;
        }

        /// <summary>
        /// Every value finite, weight changes within ±<see cref="MaxDelta"/>, activations within [−1, 1] and the energy
        /// input within [0, 1]; what a saved memory must satisfy before it is used.
        /// </summary>
        public bool IsValid()
        {
            foreach (var delta in DeltaWeights)
            {
                if (!(delta >= -MaxDelta && delta <= MaxDelta))
                    return false;
            }

            return InUnitRange(LastHidden) && InUnitRange(LastOutput) && LastEnergy >= 0f && LastEnergy <= 1f;
        }

        private static bool InUnitRange(float[] values)
        {
            foreach (var value in values)
            {
                if (!(value >= -1f && value <= 1f))
                    return false;
            }

            return true;
        }
    }
}
