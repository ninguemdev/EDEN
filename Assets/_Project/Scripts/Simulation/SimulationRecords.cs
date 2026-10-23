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
