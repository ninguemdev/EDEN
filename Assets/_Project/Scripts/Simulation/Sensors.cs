using System;
using Eden.Domain;

namespace Eden.Simulation
{
    /// <summary>Builds a creature's local <see cref="Observation"/> from the current world state.</summary>
    public static class Sensors
    {
        public static Observation Observe(WorldState world, CreatureState creature)
        {
            var config = world.Config;
            var forwardX = MathF.Cos(creature.Heading);
            var forwardY = MathF.Sin(creature.Heading);
            var position = creature.Position;
            var velocity = creature.Velocity;
            var range = config.SensorRange;

            var energy = Clamp(creature.Energy / config.MaxEnergy, 0f, 1f);
            var age = Clamp((float)creature.AgeTicks / config.MaxAgeTicks, 0f, 1f);
            var velocityForward = Clamp((velocity.X * forwardX + velocity.Y * forwardY) / config.MaxSpeed, -1f, 1f);
            var velocityLeft = Clamp((velocity.Y * forwardX - velocity.X * forwardY) / config.MaxSpeed, -1f, 1f);

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
                WallReading(world, position, forwardX, forwardY),
                WallReading(world, position, forwardY, -forwardX),
                WallReading(world, position, -forwardX, -forwardY),
                WallReading(world, position, -forwardY, forwardX));
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

        // Distance along a unit direction to the arena border, divided by the sensor range and capped at 1.
        private static float WallReading(WorldState world, Float2 position, float directionX, float directionY)
        {
            var config = world.Config;
            var distance = float.MaxValue;
            if (directionX > 0f)
                distance = MathF.Min(distance, (config.ArenaWidth - position.X) / directionX);
            else if (directionX < 0f)
                distance = MathF.Min(distance, -position.X / directionX);
            if (directionY > 0f)
                distance = MathF.Min(distance, (config.ArenaHeight - position.Y) / directionY);
            else if (directionY < 0f)
                distance = MathF.Min(distance, -position.Y / directionY);
            return Clamp(distance / config.SensorRange, 0f, 1f);
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    }
}
