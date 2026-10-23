using System;
using System.Globalization;

namespace Eden.Domain
{
    /// <summary>2D vector in arena units, independent of UnityEngine.Vector2.</summary>
    public readonly struct Float2 : IEquatable<Float2>
    {
        public static readonly Float2 Zero = new Float2(0f, 0f);

        public Float2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }
        public float Y { get; }

        public bool Equals(Float2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Float2 other && Equals(other);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() * 397);
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0}, {1})", X, Y);
    }
}
