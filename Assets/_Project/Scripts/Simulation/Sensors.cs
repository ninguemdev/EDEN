using System;
using Eden.Domain;

namespace Eden.Simulation
{
    /// <summary>
    /// Builds a creature's local <see cref="Observation"/> from the current world state. Readings are clamped to
    /// their ranges, but non-finite state would pass through; <see cref="WorldSimulation"/> therefore checks every
    /// observation with <see cref="ObservationLimits.Sanitize"/> before a brain sees it. Energy, range and speed readings
    /// are relative to the creature's own body: its energy capacity, its sensor range and its speed cap. The legacy
    /// readings keep the <see cref="NeuralContract.Legacy"/> meaning on purpose and ignore obstacles: the food sense
    /// sees through them and the wall readings only know the arena's border. Only eyes see obstacles. Likewise the legacy
    /// readings neither see water nor feel thirst; only eyes in a world with water do (<see cref="EyeContract"/>).
    /// </summary>
    public static class Sensors
    {
        public static Observation Observe(WorldState world, CreatureState creature)
        {
            var config = world.Config;
            var forwardX = MathF.Cos(creature.Heading);
            var forwardY = MathF.Sin(creature.Heading);
            var position = creature.Position;
            var velocity = creature.Velocity;
            var range = creature.Body.SensorRange;
            var maxSpeed = creature.Body.MaxSpeed;

            var energy = Clamp(creature.Energy / creature.Body.MaxEnergy, 0f, 1f);
            var age = Clamp((float)creature.AgeTicks / config.MaxAgeTicks, 0f, 1f);
            var velocityForward = Clamp((velocity.X * forwardX + velocity.Y * forwardY) / maxSpeed, -1f, 1f);
            var velocityLeft = Clamp((velocity.Y * forwardX - velocity.X * forwardY) / maxSpeed, -1f, 1f);

            float foodSin = 0f, foodCos = 0f, foodProximity = 0f, foodVisible = 0f;
            var nearest = FindNearestVisibleFood(world, position, range * range, out var distanceSquared);
            if (nearest != null)
            {
                var dx = nearest.Position.X - position.X;
                var dy = nearest.Position.Y - position.Y;
                var distance = MathF.Sqrt(distanceSquared);
                foodVisible = 1f;
                foodProximity = Clamp(1f - distance / range, 0f, 1f);
                if (distance > 0f)
                {
                    foodCos = Clamp((dx * forwardX + dy * forwardY) / distance, -1f, 1f);
                    foodSin = Clamp((dy * forwardX - dx * forwardY) / distance, -1f, 1f);
                }
                else
                {
                    foodCos = 1f;
                }
            }

            return new Observation(energy, age, velocityForward, velocityLeft,
                foodSin, foodCos, foodProximity, foodVisible,
                WallReading(config, position, range, forwardX, forwardY),
                WallReading(config, position, range, forwardY, -forwardX),
                WallReading(config, position, range, -forwardX, -forwardY),
                WallReading(config, position, range, -forwardY, forwardX));
        }

        /// <summary>The eye contract of a world: <see cref="NeuralContract.EyesWater"/> with water, else <see cref="NeuralContract.Eyes"/>.</summary>
        public static NeuralContract EyeContract(SimulationConfig config) => NeuralContract.ForEyes(config.HasWater);

        /// <summary>
        /// The observation of a creature with eyes: <paramref name="observation"/> (its legacy readings) carrying the inputs
        /// of the world's <see cref="EyeContract"/> written into <paramref name="buffer"/>: the body channels copied from
        /// the legacy readings, with water hydration / max hydration, then what each eye slot sees
        /// (<see cref="Vision.See"/>, water included with water).
        /// </summary>
        public static Observation WithEyes(in Observation observation, WorldState world, CreatureState creature,
            float[] buffer)
        {
            if (creature?.Eyes == null)
                throw new ArgumentException("Only a creature with eyes senses through them.", nameof(creature));
            var contract = EyeContract(world.Config);
            if (buffer == null || buffer.Length != contract.InputCount)
                throw new ArgumentException($"Needs {contract.InputCount} values.", nameof(buffer));
            buffer[0] = observation.Energy;
            buffer[1] = observation.Age;
            buffer[2] = observation.VelocityForward;
            buffer[3] = observation.VelocityLeft;
            var water = contract.SensesWater;
            if (water)
                buffer[NeuralContract.HydrationInput] = Clamp(creature.Hydration / world.Config.MaxHydration, 0f, 1f);
            for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
                Vision.See(world, creature, creature.Eyes, slot, buffer, contract.EyeInput(slot, EyeChannel.Present), water);
            return new Observation(observation.Energy, observation.Age, observation.VelocityForward,
                observation.VelocityLeft, observation.FoodSin, observation.FoodCos, observation.FoodProximity,
                observation.FoodVisible, observation.WallFront, observation.WallRight, observation.WallBack,
                observation.WallLeft, buffer);
        }

        // Nearest food within range; on equal distance the lower id wins because food is in ascending id order.
        private static FoodState FindNearestVisibleFood(WorldState world, Float2 position, float rangeSquared,
            out float bestDistanceSquared)
        {
            FoodState best = null;
            bestDistanceSquared = float.MaxValue;
            var food = world.Food;
            for (var i = 0; i < food.Count; i++)
            {
                var item = food[i];
                var dx = item.Position.X - position.X;
                var dy = item.Position.Y - position.Y;
                var distanceSquared = dx * dx + dy * dy;
                if (distanceSquared <= rangeSquared && distanceSquared < bestDistanceSquared)
                {
                    best = item;
                    bestDistanceSquared = distanceSquared;
                }
            }

            return best;
        }

        // Direction components this small are float noise (e.g. sin(π) ≈ -8.7e-8): a ray along a wall must not
        // read distance 0 just because the creature touches that wall.
        private const float ParallelEpsilon = 1e-6f;

        // Distance along a unit direction to the arena border, divided by the sensor range and capped at 1.
        private static float WallReading(SimulationConfig config, Float2 position, float range, float directionX,
            float directionY)
        {
            var distance = float.MaxValue;
            if (directionX > ParallelEpsilon)
                distance = MathF.Min(distance, (config.ArenaWidth - position.X) / directionX);
            else if (directionX < -ParallelEpsilon)
                distance = MathF.Min(distance, -position.X / directionX);
            if (directionY > ParallelEpsilon)
                distance = MathF.Min(distance, (config.ArenaHeight - position.Y) / directionY);
            else if (directionY < -ParallelEpsilon)
                distance = MathF.Min(distance, -position.Y / directionY);
            return Clamp(distance / range, 0f, 1f);
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }
}
