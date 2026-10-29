using System;
using System.Collections.Generic;

namespace Eden.Domain
{
    /// <summary>
    /// Where solid bodies and food may appear (<see cref="SimulationConfig.SolidBodies"/>). A body fits where its whole
    /// disc is inside the arena, out of every obstacle and water source (<see cref="SimulationConfig.Barriers"/>) and clear
    /// of every living creature. Food is a point; it fits where it is not inside an obstacle or water, not under a living body and within reach of a place the reference body can stand
    /// (see <see cref="FreeSpace"/>), so it never appears in a nook no body can eat from. Placement draws positions from a
    /// random stream and gives up after a bounded number of tries; callers decide what giving up means.
    /// </summary>
    public static class Placement
    {
        /// <summary>Tries for each founder before the scenario is declared infeasible.</summary>
        public const int FounderAttempts = 256;

        /// <summary>Tries for a food item; a respawn that finds no place waits for the next tick.</summary>
        public const int FoodAttempts = 32;

        /// <summary>Directions tried around the parent before a birth is refused for lack of room.</summary>
        public const int BirthAttempts = 8;

        /// <summary>Whether a disc of <paramref name="radius"/> at <paramref name="position"/> fits.</summary>
        public static bool BodyFits(SimulationConfig config, IReadOnlyList<CreatureState> creatures, Float2 position,
            float radius)
        {
            if (!(position.X >= radius && position.X <= config.ArenaWidth - radius && position.Y >= radius &&
                  position.Y <= config.ArenaHeight - radius))
                return false;
            var barriers = config.Barriers;
            for (var i = 0; i < barriers.Count; i++)
            {
                if (barriers[i].DistanceTo(position) < radius)
                    return false;
            }

            for (var i = 0; i < creatures.Count; i++)
            {
                var other = creatures[i];
                if (!other.IsAlive)
                    continue;
                var dx = other.Position.X - position.X;
                var dy = other.Position.Y - position.Y;
                var reach = other.Body.Radius + radius;
                if (dx * dx + dy * dy < reach * reach)
                    return false;
            }

            return true;
        }

        /// <summary>Whether food may appear at <paramref name="position"/>.</summary>
        public static bool FoodFits(SimulationConfig config, IReadOnlyList<CreatureState> creatures, Float2 position)
        {
            var barriers = config.Barriers;
            for (var i = 0; i < barriers.Count; i++)
            {
                if (barriers[i].Contains(position))
                    return false;
            }

            for (var i = 0; i < creatures.Count; i++)
            {
                var other = creatures[i];
                if (!other.IsAlive)
                    continue;
                var dx = other.Position.X - position.X;
                var dy = other.Position.Y - position.Y;
                if (dx * dx + dy * dy < other.Body.Radius * other.Body.Radius)
                    return false;
            }

            return config.FreeSpace == null || config.FreeSpace.Reaches(position, config.EatRadius, config.Obstacles);
        }

        /// <summary>
        /// A place for food at <paramref name="tick"/> (see <see cref="WorldFactory.RandomFoodPosition"/>), drawn up to
        /// <see cref="FoodAttempts"/> times; false when none fits.
        /// </summary>
        public static bool TryPlaceFood(IRandomSource random, SimulationConfig config, IReadOnlyList<CreatureState> creatures,
            long tick, out Float2 position)
        {
            for (var attempt = 0; attempt < FoodAttempts; attempt++)
            {
                position = WorldFactory.RandomFoodPosition(random, config, tick);
                if (FoodFits(config, creatures, position))
                    return true;
            }

            position = default;
            return false;
        }
    }

    /// <summary>
    /// A scenario that cannot be set up as asked: the requested creatures or food do not fit. It reports how many were
    /// placed; nothing is reduced silently.
    /// </summary>
    public sealed class ScenarioInfeasibleException : Exception
    {
        public ScenarioInfeasibleException(string what, int requested, int placed)
            : base($"Infeasible scenario: only {placed} of {requested} {what} fit in the arena.")
        {
            What = what;
            Requested = requested;
            Placed = placed;
        }

        /// <summary>"creatures" or "food items".</summary>
        public string What { get; }

        public int Requested { get; }

        /// <summary>How many were placed before no room was found: the capacity found.</summary>
        public int Placed { get; }
    }
}
