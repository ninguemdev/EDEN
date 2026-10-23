using Eden.Domain;

namespace Eden.Simulation
{
    /// <summary>
    /// Decides creature actions. Called once per living creature per tick, in ascending id order, with the
    /// observation taken before any creature moved. It only receives the id and local observation, never
    /// global state, so brains cannot read positions they could not sense.
    /// </summary>
    public interface ICreatureController
    {
        CreatureAction Decide(CreatureId creature, in Observation observation);
    }
}
