using Eden.Domain;

namespace Eden.Brains
{
    /// <summary>
    /// Baseline that ignores its observation: uniform thrust in [0, 1], turn and strafe in [-1, 1] at every
    /// decision, drawn from the creature's own stream. Evolved brains must beat this to show any learning.
    /// </summary>
    public sealed class RandomBrain : IBrain
    {
        public const string BrainName = "random-v1";

        public string Name => BrainName;

        public CreatureAction Evaluate(in Observation observation, BrainState state)
        {
            var random = state.Random;
            var thrust = random.NextFloat();
            var turn = random.NextFloat(-1f, 1f);
            var strafe = random.NextFloat(-1f, 1f);
            return new CreatureAction(thrust, turn, strafe);
        }
    }
}
