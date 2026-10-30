using Eden.Domain;

namespace Eden.Simulation
{
    /// <summary>Kinds of agenda events. Values are persisted with checkpoints: never renumber.</summary>
    public enum SimulationEventKind : byte
    {
        FoodRespawn = 1
    }

    /// <summary>Plain-data agenda payload.</summary>
    public readonly struct SimulationEvent
    {
        public SimulationEvent(SimulationEventKind kind)
        {
            Kind = kind;
        }

        public SimulationEventKind Kind { get; }
    }

    /// <summary>Why a ready parent had no child.</summary>
    public enum BirthRefusalReason
    {
        /// <summary>The population was at its cap.</summary>
        PopulationCap = 0,

        /// <summary>No room for the child's disc next to the parent (solid bodies).</summary>
        NoRoom = 1
    }

    /// <summary>A birth refused for free: the parent paid nothing and no child exists.</summary>
    public readonly struct BirthRefusal
    {
        public BirthRefusal(CreatureId parent, long tick, BirthRefusalReason reason)
        {
            Parent = parent;
            Tick = tick;
            Reason = reason;
        }

        public CreatureId Parent { get; }
        public long Tick { get; }
        public BirthRefusalReason Reason { get; }
    }

    public readonly struct CreatureBirth
    {
        public CreatureBirth(CreatureId child, CreatureId parent, long tick)
        {
            Child = child;
            Parent = parent;
            Tick = tick;
        }

        public CreatureId Child { get; }
        public CreatureId Parent { get; }
        public long Tick { get; }
    }

    public readonly struct CreatureDeath
    {
        public CreatureDeath(CreatureId creature, long tick, DeathCause cause)
        {
            Creature = creature;
            Tick = tick;
            Cause = cause;
        }

        public CreatureId Creature { get; }
        public long Tick { get; }
        public DeathCause Cause { get; }
    }
}

namespace Eden.Simulation
{
    public readonly struct FoodConsumption
    {
        public FoodConsumption(Eden.Domain.CreatureId creature, Eden.Domain.FoodId food, float energy)
        { Creature = creature; Food = food; Energy = energy; }
        public Eden.Domain.CreatureId Creature { get; }
        public Eden.Domain.FoodId Food { get; }
        public float Energy { get; }
    }
    public readonly struct WaterConsumption
    {
        public WaterConsumption(Eden.Domain.CreatureId creature, int source, float useful)
        { Creature = creature; Source = source; Useful = useful; }
        public Eden.Domain.CreatureId Creature { get; }
        public int Source { get; }
        public float Useful { get; }
    }
}
