namespace Eden.Domain
{
    /// <summary>One food item in the arena.</summary>
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
    }
}
