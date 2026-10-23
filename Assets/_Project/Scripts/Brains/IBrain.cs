using Eden.Domain;

namespace Eden.Brains
{
    /// <summary>
    /// Maps a creature's local observation to an action. A brain sees nothing else: no global positions,
    /// no other creatures' state. Anything a brain keeps between decisions lives in the creature's
    /// <see cref="BrainState"/>, so one brain instance can serve many creatures and recurrent brains fit later.
    /// </summary>
    public interface IBrain
    {
        /// <summary>Stable identifier used in logs, metrics and checkpoints (e.g. "heuristic-v1").</summary>
        string Name { get; }

        CreatureAction Evaluate(in Observation observation, BrainState state);
    }
}
