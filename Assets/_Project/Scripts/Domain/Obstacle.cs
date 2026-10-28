using System;

namespace Eden.Domain
{
    /// <summary>
    /// A static rectangle aligned with the arena's axes that solid bodies cannot enter (see
    /// <see cref="SimulationSettings.Obstacles"/>). It is part of the config, never moves and has no state.
    /// </summary>
    public readonly struct Obstacle : IEquatable<Obstacle>
    {
        // Share of a segment that must lie inside to block it; grazing a side or corner does not block.
        private const float CrossingSlack = 1e-6f;

        public Obstacle(float minX, float minY, float maxX, float maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public float MinX { get; }
        public float MinY { get; }
        public float MaxX { get; }
        public float MaxY { get; }
        public float Width => MaxX - MinX;
        public float Height => MaxY - MinY;

        /// <summary>Whether a point lies strictly inside (its sides do not count).</summary>
        public bool Contains(Float2 point) =>
            point.X > MinX && point.X < MaxX && point.Y > MinY && point.Y < MaxY;

        /// <summary>Distance from a point to the rectangle's nearest point; 0 on or inside it.</summary>
        public float DistanceTo(Float2 point)
        {
            var dx = Math.Max(Math.Max(MinX - point.X, 0f), point.X - MaxX);
            var dy = Math.Max(Math.Max(MinY - point.Y, 0f), point.Y - MaxY);
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Whether the segment from <paramref name="from"/> to <paramref name="to"/> passes through the inside of the
        /// rectangle (slab method). Running along a side or touching a corner does not count.
        /// </summary>
        public bool Blocks(Float2 from, Float2 to)
        {
            var enter = 0f;
            var leave = 1f;
            return Slab(from.X, to.X - from.X, MinX, MaxX, ref enter, ref leave) &&
                   Slab(from.Y, to.Y - from.Y, MinY, MaxY, ref enter, ref leave) &&
                   leave - enter > CrossingSlack;
        }

        private static bool Slab(float start, float delta, float min, float max, ref float enter, ref float leave)
        {
            if (MathF.Abs(delta) < 1e-9f)
                return start > min && start < max;
            var first = (min - start) / delta;
            var second = (max - start) / delta;
            if (first > second)
                (first, second) = (second, first);
            enter = MathF.Max(enter, first);
            leave = MathF.Min(leave, second);
            return leave > enter;
        }

        public bool Equals(Obstacle other) =>
            MinX == other.MinX && MinY == other.MinY && MaxX == other.MaxX && MaxY == other.MaxY;

        public override bool Equals(object obj) => obj is Obstacle other && Equals(other);

        public override int GetHashCode() =>
            ((MinX.GetHashCode() * 397 ^ MinY.GetHashCode()) * 397 ^ MaxX.GetHashCode()) * 397 ^ MaxY.GetHashCode();

        public override string ToString() => $"[{MinX}, {MinY}] – [{MaxX}, {MaxY}]";
    }

    /// <summary>Named obstacle layouts, built for an arena by <see cref="ObstacleLayouts.Create"/>.</summary>
    public enum ObstacleLayout
    {
        /// <summary>The open arena.</summary>
        None = 0,

        /// <summary>Nine square pillars in a 3 × 3 grid, with wide lanes between them.</summary>
        Pillars = 1,

        /// <summary>A wall across the middle of the arena with one gap: the two halves meet only there.</summary>
        Corridor = 2
    }

    /// <summary>
    /// The obstacles of a named layout for an arena, scaled with its size. Every layout leaves the free space connected
    /// for the largest body the size gene allows, so every place can be reached; gaps scale with the body radius.
    /// </summary>
    public static class ObstacleLayouts
    {
        public static Obstacle[] Create(ObstacleLayout layout, SimulationSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            var width = settings.ArenaWidth;
            var height = settings.ArenaHeight;
            switch (layout)
            {
                case ObstacleLayout.None:
                    return Array.Empty<Obstacle>();
                case ObstacleLayout.Pillars:
                {
                    // Pillars a twelfth of the arena wide, centred at a quarter, half and three quarters of each side.
                    var halfX = width / 24f;
                    var halfY = height / 24f;
                    var pillars = new Obstacle[9];
                    for (var row = 0; row < 3; row++)
                    {
                        for (var column = 0; column < 3; column++)
                        {
                            var x = width * (column + 1) / 4f;
                            var y = height * (row + 1) / 4f;
                            pillars[row * 3 + column] = new Obstacle(x - halfX, y - halfY, x + halfX, y + halfY);
                        }
                    }

                    return pillars;
                }
                case ObstacleLayout.Corridor:
                {
                    // A wall two body radii thick at mid-width, broken by a gap eight body radii high at mid-height.
                    var half = settings.BodyRadius;
                    var gap = 4f * settings.BodyRadius;
                    var x = width * 0.5f;
                    var y = height * 0.5f;
                    return new[]
                    {
                        new Obstacle(x - half, 0f, x + half, y - gap),
                        new Obstacle(x - half, y + gap, x + half, height)
                    };
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown obstacle layout.");
            }
        }
    }
}
