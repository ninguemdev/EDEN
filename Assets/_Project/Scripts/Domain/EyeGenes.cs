using System;

namespace Eden.Domain
{
    /// <summary>
    /// One eye: where it points relative to the body's heading (radians, counterclockwise, i.e. positive to the
    /// creature's left), how wide its field of view is (the full angle of the cone) and how far it sees, as a factor of
    /// the body's reference sensor range. The eye sits on the body's surface in the direction it points. Every value
    /// is checked against the limits below, which are proposals to explore, not calibrated biology.
    /// </summary>
    public readonly struct Eye : IEquatable<Eye>
    {
        /// <summary>Narrowest field of view: 30°.</summary>
        public const float MinAperture = MathF.PI / 6f;

        /// <summary>Widest field of view: 180°.</summary>
        public const float MaxAperture = MathF.PI;

        public const float MinRange = 0.5f;
        public const float MaxRange = 1.5f;

        /// <summary>Field of view of a founder's frontal eye: 120°, a proposal.</summary>
        public const float FounderAperture = 2f * MathF.PI / 3f;

        public Eye(float orientation, float aperture, float range)
        {
            if (float.IsNaN(orientation) || orientation < -MathF.PI || orientation > MathF.PI)
                throw new ArgumentOutOfRangeException(nameof(orientation), $"Orientation {orientation} is not in [-π, π].");
            if (float.IsNaN(aperture) || aperture < MinAperture || aperture > MaxAperture)
                throw new ArgumentOutOfRangeException(nameof(aperture),
                    $"Aperture {aperture} is not in [{MinAperture}, {MaxAperture}].");
            if (float.IsNaN(range) || range < MinRange || range > MaxRange)
                throw new ArgumentOutOfRangeException(nameof(range), $"Range {range} is not in [{MinRange}, {MaxRange}].");
            Orientation = orientation;
            Aperture = aperture;
            Range = range;
        }

        /// <summary>The founders' eye: straight ahead, 120°, the reference range.</summary>
        public static Eye Frontal => new Eye(0f, FounderAperture, 1f);

        /// <summary>Direction relative to the heading, radians in [-π, π]; positive is to the left.</summary>
        public float Orientation { get; }

        /// <summary>Full angle of the field of view, radians.</summary>
        public float Aperture { get; }

        /// <summary>Reach as a factor of the body's sensor range.</summary>
        public float Range { get; }

        public bool Equals(Eye other) =>
            Orientation.Equals(other.Orientation) && Aperture.Equals(other.Aperture) && Range.Equals(other.Range);

        public override bool Equals(object obj) => obj is Eye other && Equals(other);
        public override int GetHashCode() => (Orientation.GetHashCode() * 397 ^ Aperture.GetHashCode()) * 397 ^ Range.GetHashCode();
        public override string ToString() => $"eye {Orientation:0.###} rad, {Aperture:0.###} rad, ×{Range:0.###}";
    }

    /// <summary>
    /// The eyes of a genome, in <see cref="MaxEyes"/> reserved slots. A slot is an eye's identity: it keeps its slot,
    /// and with it its input channels in <see cref="NeuralContract.Eyes"/>, whatever it points at, so turning, adding or
    /// removing an eye never changes what the other eyes' channels mean. An empty slot is an absent eye. Immutable.
    /// </summary>
    public sealed class EyeSet : IEquatable<EyeSet>
    {
        public const int MaxEyes = 4;

        private readonly Eye[] eyes = new Eye[MaxEyes];
        private readonly bool[] present = new bool[MaxEyes];

        /// <param name="slots">Up to <see cref="MaxEyes"/> entries; null marks an empty slot.</param>
        public EyeSet(params Eye?[] slots)
        {
            if (slots == null)
                throw new ArgumentNullException(nameof(slots));
            if (slots.Length > MaxEyes)
                throw new ArgumentException($"At most {MaxEyes} eye slots.", nameof(slots));
            for (var s = 0; s < slots.Length; s++)
            {
                if (!slots[s].HasValue)
                    continue;
                eyes[s] = slots[s].Value;
                present[s] = true;
                Count++;
            }
        }

        /// <summary>No eyes at all (the legacy senses do not use eyes).</summary>
        public static readonly EyeSet None = new EyeSet();

        /// <summary>What founders are born with: one frontal eye in slot 0.</summary>
        public static readonly EyeSet Founder = new EyeSet(Eye.Frontal);

        public int Count { get; }

        public bool Has(int slot) => present[Check(slot)];

        /// <summary>The eye in a slot; throws for an empty slot.</summary>
        public Eye this[int slot] =>
            present[Check(slot)] ? eyes[slot] : throw new InvalidOperationException($"Eye slot {slot} is empty.");

        public bool Equals(EyeSet other)
        {
            if (other == null)
                return false;
            for (var s = 0; s < MaxEyes; s++)
            {
                if (present[s] != other.present[s] || (present[s] && !eyes[s].Equals(other.eyes[s])))
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj) => Equals(obj as EyeSet);

        public override int GetHashCode()
        {
            var hash = 17;
            for (var s = 0; s < MaxEyes; s++)
                hash = hash * 31 + (present[s] ? eyes[s].GetHashCode() : 0);
            return hash;
        }

        private static int Check(int slot)
        {
            if (slot < 0 || slot >= MaxEyes)
                throw new ArgumentOutOfRangeException(nameof(slot), $"Eye slots are 0 to {MaxEyes - 1}.");
            return slot;
        }
    }
}
