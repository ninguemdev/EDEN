using System;

namespace Eden.Domain
{
    /// <summary>
    /// One food item in the arena. It can be eaten once and leaves the world at the end of that tick; the world may
    /// then reuse the object for a later item with a new id, so references are not kept across ticks.
    /// </summary>
    public sealed class FoodState
    {
        public FoodState(FoodId id, Float2 position, float energy)
        {
            Id = id;
            Position = position;
            Energy = energy;
        }

        public FoodId Id { get; private set; }
        public Float2 Position { get; private set; }
        public float Energy { get; private set; }
        public bool IsEaten { get; private set; }

        public void MarkEaten()
        {
            if (IsEaten)
                throw new InvalidOperationException($"Food {Id} was already eaten.");
            IsEaten = true;
        }

        /// <summary>Moves an uneaten item (the food shift of a changing world); it keeps its id and energy.</summary>
        public void MoveTo(Float2 position)
        {
            if (IsEaten)
                throw new InvalidOperationException($"Food {Id} was eaten.");
            Position = position;
        }

        /// <summary>Turns an eaten item that has left the world into a new one, so respawning does not allocate.</summary>
        internal void Reuse(FoodId id, Float2 position, float energy)
        {
            if (!IsEaten)
                throw new InvalidOperationException($"Food {Id} is still in the world.");
            Id = id;
            Position = position;
            Energy = energy;
            IsEaten = false;
        }
    }
}
