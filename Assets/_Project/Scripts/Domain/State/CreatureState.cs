namespace Eden.Domain
{
    public enum DeathCause : byte
    {
        None = 0,
        Starvation = 1,
        OldAge = 2
    }

    /// <summary>
    /// Living state of one creature and the body it lives with. Not inherited: offspring start from their own initial
    /// state, and their body is expressed from their own genes.
    /// </summary>
    public sealed class CreatureState
    {
        public CreatureState(CreatureId id, Float2 position, float heading, float energy, CreatureBody body)
        {
            Id = id;
            Body = body;
            Position = position;
            Velocity = Float2.Zero;
            Heading = heading;
            Energy = energy;
        }

        public CreatureId Id { get; }

        /// <summary>Traits expressed when the creature was created; fixed for its whole life.</summary>
        public CreatureBody Body { get; }

        public Float2 Position { get; set; }
        public Float2 Velocity { get; set; }

        /// <summary>Facing angle in radians in [0, 2π), counterclockwise from +X.</summary>
        public float Heading { get; set; }

        public float Energy { get; set; }

        /// <summary>Age in simulated ticks; never derived from wall-clock time.</summary>
        public long AgeTicks { get; set; }

        /// <summary>Total food energy eaten during this life, before clamping to the energy maximum.</summary>
        public float FoodEnergyEaten { get; set; }

        /// <summary>Set during the tick the creature dies; dead creatures leave the world at the end of that tick.</summary>
        public DeathCause DeathCause { get; set; }

        public bool IsAlive => DeathCause == DeathCause.None;
    }
}
