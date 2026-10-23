using System;

namespace Eden.Domain
{
    /// <summary>Complete serializable state of a <see cref="GaussianSampler"/>, including the cached second sample.</summary>
    public readonly struct GaussianSamplerState : IEquatable<GaussianSamplerState>
    {
        public GaussianSamplerState(Pcg32State source, bool hasSpare, double spare)
        {
            Source = source;
            HasSpare = hasSpare;
            Spare = hasSpare ? spare : 0.0;
        }

        public Pcg32State Source { get; }
        public bool HasSpare { get; }
        public double Spare { get; }

        public bool Equals(GaussianSamplerState other) =>
            Source.Equals(other.Source) && HasSpare == other.HasSpare && Spare.Equals(other.Spare);

        public override bool Equals(object obj) => obj is GaussianSamplerState other && Equals(other);
        public override int GetHashCode() => Source.GetHashCode() ^ (HasSpare ? Spare.GetHashCode() * 31 : 0);
    }

    /// <summary>
    /// Standard normal samples from a <see cref="Pcg32"/> by the Box–Muller transform: two 32-bit draws give two
    /// samples, the second kept as a spare for the next call. u1 = (r1 + 1) / 2^32 is never 0, so every sample is
    /// finite (|z| ≤ about 6.7). Uniform draws for the same purpose (e.g. mutation coin flips) come from
    /// <see cref="Source"/>, so one state captures the whole stream. Uses double-precision System.Math, which is
    /// reproducible on one runtime and platform.
    /// </summary>
    public sealed class GaussianSampler
    {
        public const string AlgorithmName = "box-muller-pcg32-v1";

        private const double UnitScale = 1.0 / 4294967296.0;

        private bool hasSpare;
        private double spare;

        public GaussianSampler(Pcg32 source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public static GaussianSampler FromState(GaussianSamplerState saved) =>
            new GaussianSampler(Pcg32.FromState(saved.Source)) { hasSpare = saved.HasSpare, spare = saved.Spare };

        /// <summary>The underlying uniform stream; its draws and the spare together are the sampler's state.</summary>
        public Pcg32 Source { get; }

        public GaussianSamplerState State => new GaussianSamplerState(Source.State, hasSpare, spare);

        /// <summary>Sample of the standard normal distribution (mean 0, standard deviation 1).</summary>
        public double NextStandard()
        {
            if (hasSpare)
            {
                hasSpare = false;
                return spare;
            }

            var u1 = (Source.NextUInt() + 1.0) * UnitScale;
            var u2 = Source.NextUInt() * UnitScale;
            var radius = Math.Sqrt(-2.0 * Math.Log(u1));
            var angle = 2.0 * Math.PI * u2;
            spare = radius * Math.Sin(angle);
            hasSpare = true;
            return radius * Math.Cos(angle);
        }
    }
}
