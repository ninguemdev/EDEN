using System;

namespace Eden.Domain
{
    /// <summary>Creature identifier. Positive, unique within a run and never reused; ordering defines update order.</summary>
    public readonly struct CreatureId : IEquatable<CreatureId>, IComparable<CreatureId>
    {
        public CreatureId(int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Ids start at 1.");
            Value = value;
        }

        public int Value { get; }

        public bool Equals(CreatureId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is CreatureId other && Equals(other);
        public override int GetHashCode() => Value;
        public int CompareTo(CreatureId other) => Value.CompareTo(other.Value);
        public override string ToString() => "C" + Value;

        public static bool operator ==(CreatureId left, CreatureId right) => left.Equals(right);
        public static bool operator !=(CreatureId left, CreatureId right) => !left.Equals(right);
    }

    /// <summary>Food item identifier. Positive, unique within a run and never reused; ordering breaks ties.</summary>
    public readonly struct FoodId : IEquatable<FoodId>, IComparable<FoodId>
    {
        public FoodId(int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Ids start at 1.");
            Value = value;
        }

        public int Value { get; }

        public bool Equals(FoodId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is FoodId other && Equals(other);
        public override int GetHashCode() => Value;
        public int CompareTo(FoodId other) => Value.CompareTo(other.Value);
        public override string ToString() => "F" + Value;

        public static bool operator ==(FoodId left, FoodId right) => left.Equals(right);
        public static bool operator !=(FoodId left, FoodId right) => !left.Equals(right);
    }
}
