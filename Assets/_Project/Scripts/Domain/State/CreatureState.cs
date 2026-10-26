namespace Eden.Domain
{
    public enum DeathCause : byte
    {
        None = 0,
        Starvation = 1,
        OldAge = 2
    }

    /// <summary>
    /// Living state of one creature, with the body genes it was born with and the body they express. Not inherited:
    /// offspring start from their own initial state, and their body is expressed from their own genes.
    /// </summary>
    public sealed class CreatureState
    {
        /// <param name="body">
        /// <paramref name="genes"/> expressed in the world's config; <see cref="WorldState.Restore"/> refuses any other.
        /// </param>
        public CreatureState(CreatureId id, Float2 position, float heading, float energy, BodyGenes genes,
            CreatureBody body)
        {
            Id = id;
            Genes = genes;
            Body = body;
            Position = position;
            Velocity = Float2.Zero;
            Heading = heading;
            Energy = energy;
        }

        public CreatureId Id { get; }

        /// <summary>Body genes the creature expresses (genes a run does not express are neutral here).</summary>
        public BodyGenes Genes { get; }

        /// <summary>Traits and costs expressed from <see cref="Genes"/> when the creature was created; fixed for life.</summary>
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

        /// <summary>Parent of a creature born in the world; null for a creature the run started with.</summary>
        public CreatureId? Parent { get; set; }

        /// <summary>Genealogical generation: 0 for the creatures the run started with, 1 + the parent's otherwise.</summary>
        public int Generation { get; set; }

        /// <summary>Ticks left before the creature may reproduce again (0: no cooldown).</summary>
        public long ReproductionCooldownTicks { get; set; }

        /// <summary>Set during the tick the creature dies; dead creatures leave the world at the end of that tick.</summary>
        public DeathCause DeathCause { get; set; }

        public bool IsAlive => DeathCause == DeathCause.None;
    }
}
