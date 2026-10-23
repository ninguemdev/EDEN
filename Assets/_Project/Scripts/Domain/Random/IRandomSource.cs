namespace Eden.Domain
{
    /// <summary>
    /// Deterministic random source owned by a single stream. Simulation code must never use
    /// System.Random or UnityEngine.Random, and presentation must never draw from simulation streams.
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>Next raw 32-bit value.</summary>
        uint NextUInt();

        /// <summary>Unbiased integer in [0, bound). Throws if bound is zero.</summary>
        uint NextUInt(uint bound);

        /// <summary>Uniform float in [0, 1) built from 24 random bits.</summary>
        float NextFloat();

        /// <summary>Uniform float between min and max; float rounding can return max itself.</summary>
        float NextFloat(float min, float max);
    }
}
