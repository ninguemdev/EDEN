using System;

namespace Eden.Domain
{
    /// <summary>One food item in the arena. It can be eaten once and leaves the world at the end of that tick.</summary>
    public sealed class FoodState
    {
        public FoodState(FoodId id, Float2 position, float energy)
        {
            Id = id;
            Position = position;
            Energy = energy;
        }

        public FoodId Id { get; }
        public Float2 Position { get; }
        public float Energy { get; }
        public bool IsEaten { get; private set; }

        public void MarkEaten()
        {
            if (IsEaten)
                throw new InvalidOperationException($"Food {Id} was already eaten.");
            IsEaten = true;
        }
    }
}
