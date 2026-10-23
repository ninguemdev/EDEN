using Eden.Domain;

namespace Eden.Brains
{
    /// <summary>
    /// Hand-written sanity reference, deterministic and without memory: steer to the nearest visible food,
    /// turning in place toward food behind; with no food in sight, cruise straight and veer toward the more
    /// open side near walls. Constants are untuned starting values.
    /// </summary>
    public sealed class HeuristicBrain : IBrain
    {
        public const string BrainName = "heuristic-v1";

        private const float TurnGain = 3f;
        private const float CruiseThrust = 0.6f;
        private const float TurningThrust = 0.25f;
        private const float WallAvoidReading = 0.3f;

        public string Name => BrainName;

        public CreatureAction Evaluate(in Observation observation, BrainState state)
        {
            if (observation.FoodVisible > 0f)
            {
                if (observation.FoodCos >= 0f)
                    return new CreatureAction(1f, TurnGain * observation.FoodSin, 0f);
                return new CreatureAction(TurningThrust, observation.FoodSin >= 0f ? 1f : -1f, 0f);
            }

            if (observation.WallFront < WallAvoidReading)
                return new CreatureAction(TurningThrust, observation.WallLeft >= observation.WallRight ? 1f : -1f, 0f);

            return new CreatureAction(CruiseThrust, 0f, 0f);
        }
    }
}
