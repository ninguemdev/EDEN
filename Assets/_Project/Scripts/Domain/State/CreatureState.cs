namespace Eden.Domain
{
    /// <summary>Living state of one creature. Not inherited: offspring start from their own initial state.</summary>
    public sealed class CreatureState
    {
        public CreatureState(CreatureId id, Float2 position, float heading, float energy)
        {
            Id = id;
            Position = position;
            Velocity = Float2.Zero;
            Heading = heading;
            Energy = energy;
        }

        public CreatureId Id { get; }
        public Float2 Position { get; set; }
        public Float2 Velocity { get; set; }

        /// <summary>Facing angle in radians, counterclockwise from +X.</summary>
        public float Heading { get; set; }

        public float Energy { get; set; }

        /// <summary>Age in simulated ticks; never derived from wall-clock time.</summary>
        public long AgeTicks { get; set; }
    }
}
