using System;
using Eden.Domain;

namespace Eden.Simulation
{
    /// <summary>
    /// What eyes see, for <see cref="NeuralContract.Eyes"/>. An eye sits on the body's surface (the solid disc's radius
    /// with solid bodies, otherwise the eating radius, in the eye's direction; kept inside the arena) and sees a target
    /// only when it is within the eye's range (the body's sensor range × <see cref="Eye.Range"/>) <b>and</b> within
    /// half the aperture of the eye's axis, borders included. Food is a point; on equal distance the lower id wins,
    /// because food is kept in ascending id order. The arena's border is a barrier the eye sees at the nearest point of
    /// the border inside its cone and range; nothing outside the cone is ever reported, so there is no radar.
    /// Directions are given in the body's frame (sine positive to the left, cosine 1 straight ahead).
    /// <see cref="SimulationConfig.Obstacles"/> are opaque: food behind one is not seen (see <see cref="LineOfSight"/>),
    /// and each is a barrier the eye sees, like the border, at its nearest point inside the cone and range. Creatures
    /// hide nothing. In a world with water (<see cref="NeuralContract.EyesWater"/>) an eye also sees the nearest point of
    /// a water source inside its cone and range unless an obstacle hides it; water hides nothing and is not a barrier.
    /// </summary>
    public static class Vision
    {
        // Rounding slack on the cone's border, so a target exactly on the edge counts as inside.
        private const float EdgeSlack = 1e-5f;


        /// <summary>
        /// Writes the <see cref="EyeChannels.Count"/> channels of eye slot <paramref name="slot"/> of
        /// <paramref name="eyes"/> into <paramref name="buffer"/> at <paramref name="offset"/>: all zeros for an empty
        /// slot, otherwise present = 1 and what the eye sees.
        /// </summary>
        public static void See(WorldState world, CreatureState creature, EyeSet eyes, int slot, float[] buffer, int offset) =>
            See(world, creature, eyes, slot, buffer, offset, false);

        /// <summary>
        /// As above; with <paramref name="water"/> it writes the <see cref="EyeChannels.WithWater"/> channels of
        /// <see cref="NeuralContract.EyesWater"/>, the last four for the nearest water the eye sees. Water is not
        /// opaque, so it hides neither food nor barriers, but obstacles hide water: a water point counts only with a free
        /// line to it.
        /// </summary>
        public static void See(WorldState world, CreatureState creature, EyeSet eyes, int slot, float[] buffer, int offset,
            bool water)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (creature == null)
                throw new ArgumentNullException(nameof(creature));
            if (eyes == null)
                throw new ArgumentNullException(nameof(eyes));
            var channels = water ? EyeChannels.WithWater : EyeChannels.Count;
            if (buffer == null || offset < 0 || offset > buffer.Length - channels)
                throw new ArgumentOutOfRangeException(nameof(offset));

            Array.Clear(buffer, offset, channels);
            if (!eyes.Has(slot))
                return;
            var eye = eyes[slot];
            buffer[offset + (int)EyeChannel.Present] = 1f;

            var heading = creature.Heading;
            var axis = heading + eye.Orientation;
            var axisX = MathF.Cos(axis);
            var axisY = MathF.Sin(axis);
            var cosHalf = MathF.Cos(eye.Aperture * 0.5f);
            var range = creature.Body.SensorRange * eye.Range;
            var origin = EyePosition(world.Config, creature, eye);
            var forwardX = MathF.Cos(heading);
            var forwardY = MathF.Sin(heading);

            if (NearestFood(world, origin, axisX, axisY, cosHalf, range, out var food, out var foodDistance))
            {
                buffer[offset + (int)EyeChannel.FoodVisible] = 1f;
                WriteDirection(buffer, offset + (int)EyeChannel.FoodSin, food, origin, foodDistance, axisX, axisY,
                    forwardX, forwardY);
                buffer[offset + (int)EyeChannel.FoodProximity] = Clamp01(1f - foodDistance / range);
            }

            if (NearestBarrier(world.Config, origin, axis, eye.Aperture * 0.5f, cosHalf, axisX, axisY, range,
                    out var wall, out var wallDistance))
            {
                buffer[offset + (int)EyeChannel.BarrierVisible] = 1f;
                WriteDirection(buffer, offset + (int)EyeChannel.BarrierSin, wall, origin, wallDistance, axisX, axisY,
                    forwardX, forwardY);
                buffer[offset + (int)EyeChannel.BarrierProximity] = Clamp01(1f - wallDistance / range);
            }

            if (water && NearestWater(world.Config, origin, axis, eye.Aperture * 0.5f, cosHalf, axisX, axisY, range,
                    out var pond, out var pondDistance))
            {
                buffer[offset + (int)EyeChannel.WaterVisible] = 1f;
                WriteDirection(buffer, offset + (int)EyeChannel.WaterSin, pond, origin, pondDistance, axisX, axisY,
                    forwardX, forwardY);
                buffer[offset + (int)EyeChannel.WaterProximity] = Clamp01(1f - pondDistance / range);
            }
        }

        /// <summary>Where an eye sits: on the body's surface in the eye's direction, kept inside the arena.</summary>
        public static Float2 EyePosition(SimulationConfig config, CreatureState creature, Eye eye)
        {
            var axis = creature.Heading + eye.Orientation;
            var radius = config.SolidBodies ? creature.Body.Radius : creature.Body.EatRadius;
            var x = creature.Position.X + radius * MathF.Cos(axis);
            var y = creature.Position.Y + radius * MathF.Sin(axis);
            return new Float2(Math.Min(config.ArenaWidth, Math.Max(0f, x)), Math.Min(config.ArenaHeight, Math.Max(0f, y)));
        }

        /// <summary>Whether a point is inside the cone (axis, half-angle given by its cosine) and range of an eye.</summary>
        public static bool InCone(Float2 origin, float axisX, float axisY, float cosHalf, float range, Float2 point,
            out float distance)
        {
            var dx = point.X - origin.X;
            var dy = point.Y - origin.Y;
            distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance > range)
                return false;
            if (distance == 0f)
                return true;
            return (dx * axisX + dy * axisY) / distance >= cosHalf - EdgeSlack;
        }

        /// <summary>
        /// Whether nothing opaque lies between two points: the segment does not pass through the inside of any obstacle.
        /// Touching an obstacle's side or corner does not block it, so an eye pressed against an obstacle still sees
        /// along it and away from it.
        /// </summary>
        public static bool LineOfSight(SimulationConfig config, Float2 from, Float2 to)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            var obstacles = config.Obstacles;
            for (var i = 0; i < obstacles.Count; i++)
            {
                if (obstacles[i].Blocks(from, to))
                    return false;
            }

            return true;
        }

        private static bool NearestFood(WorldState world, Float2 origin, float axisX, float axisY, float cosHalf,
            float range, out Float2 position, out float bestDistance)
        {
            position = default;
            bestDistance = float.MaxValue;
            var found = false;
            var food = world.Food;
            var occluded = world.Config.Obstacles.Count > 0;
            for (var i = 0; i < food.Count; i++)
            {
                var item = food[i].Position;
                if (!InCone(origin, axisX, axisY, cosHalf, range, item, out var distance) || distance >= bestDistance)
                    continue;
                if (occluded && !LineOfSight(world.Config, origin, item))
                    continue;
                position = item;
                bestDistance = distance;
                found = true;
            }

            return found;
        }

        // The nearest barrier point inside the sector. For each side of the arena: the foot of the perpendicular when it
        // lies in the cone, and where the two edge rays cross that side. For each obstacle: its point nearest the eye
        // when it lies in the cone, and where the edge rays first hit it (a convex shape's nearest point within a convex
        // sector is one of those). The nearest of all within range.
        private static bool NearestBarrier(SimulationConfig config, Float2 origin, float axis, float half, float cosHalf,
            float axisX, float axisY, float range, out Float2 position, out float bestDistance)
        {
            position = default;
            bestDistance = float.MaxValue;
            var found = false;
            var width = config.ArenaWidth;
            var height = config.ArenaHeight;
            // Inside a convex arena the nearest border point within the sector is a foot or an edge ray's hit, never a
            // corner: a side's foot always lies on that side.
            Consider(new Float2(0f, origin.Y), origin, axisX, axisY, cosHalf, range, ref position, ref bestDistance, ref found);
            Consider(new Float2(width, origin.Y), origin, axisX, axisY, cosHalf, range, ref position, ref bestDistance, ref found);
            Consider(new Float2(origin.X, 0f), origin, axisX, axisY, cosHalf, range, ref position, ref bestDistance, ref found);
            Consider(new Float2(origin.X, height), origin, axisX, axisY, cosHalf, range, ref position, ref bestDistance, ref found);

            for (var edge = -1; edge <= 1; edge += 2)
            {
                var angle = axis + edge * half;
                var dirX = MathF.Cos(angle);
                var dirY = MathF.Sin(angle);
                var t = RayToBorder(origin, dirX, dirY, width, height);
                if (t < float.MaxValue)
                    Consider(new Float2(origin.X + dirX * t, origin.Y + dirY * t), origin, axisX, axisY, cosHalf, range,
                        ref position, ref bestDistance, ref found);
            }

            var obstacles = config.Obstacles;
            for (var i = 0; i < obstacles.Count; i++)
            {
                var obstacle = obstacles[i];
                var nearest = new Float2(Math.Min(obstacle.MaxX, Math.Max(obstacle.MinX, origin.X)),
                    Math.Min(obstacle.MaxY, Math.Max(obstacle.MinY, origin.Y)));
                Consider(nearest, origin, axisX, axisY, cosHalf, range, ref position, ref bestDistance, ref found);
                for (var edge = -1; edge <= 1; edge += 2)
                {
                    var angle = axis + edge * half;
                    var dirX = MathF.Cos(angle);
                    var dirY = MathF.Sin(angle);
                    var t = RayToObstacle(origin, dirX, dirY, obstacle);
                    if (t < float.MaxValue)
                        Consider(new Float2(origin.X + dirX * t, origin.Y + dirY * t), origin, axisX, axisY, cosHalf,
                            range, ref position, ref bestDistance, ref found);
                }
            }

            return found;
        }

        // The nearest water point inside the sector, chosen among the same candidates as an obstacle's (each source's point
        // nearest the eye when it lies in the cone, and where the edge rays first hit it), keeping only those with a free
        // line past the obstacles. A source hidden at those points counts as unseen even if a sliver of it shows elsewhere.
        private static bool NearestWater(SimulationConfig config, Float2 origin, float axis, float half, float cosHalf,
            float axisX, float axisY, float range, out Float2 position, out float bestDistance)
        {
            position = default;
            bestDistance = float.MaxValue;
            var found = false;
            var water = config.Water;
            var occluded = config.Obstacles.Count > 0;
            for (var i = 0; i < water.Count; i++)
            {
                var area = water[i].Area;
                var nearest = new Float2(Math.Min(area.MaxX, Math.Max(area.MinX, origin.X)),
                    Math.Min(area.MaxY, Math.Max(area.MinY, origin.Y)));
                ConsiderVisible(config, occluded, nearest, origin, axisX, axisY, cosHalf, range, ref position,
                    ref bestDistance, ref found);
                for (var edge = -1; edge <= 1; edge += 2)
                {
                    var angle = axis + edge * half;
                    var dirX = MathF.Cos(angle);
                    var dirY = MathF.Sin(angle);
                    var t = RayToObstacle(origin, dirX, dirY, area);
                    if (t < float.MaxValue)
                        ConsiderVisible(config, occluded, new Float2(origin.X + dirX * t, origin.Y + dirY * t), origin,
                            axisX, axisY, cosHalf, range, ref position, ref bestDistance, ref found);
                }
            }

            return found;
        }

        private static void ConsiderVisible(SimulationConfig config, bool occluded, Float2 point, Float2 origin, float axisX,
            float axisY, float cosHalf, float range, ref Float2 position, ref float bestDistance, ref bool found)
        {
            if (!InCone(origin, axisX, axisY, cosHalf, range, point, out var distance) || distance >= bestDistance)
                return;
            if (occluded && !LineOfSight(config, origin, point))
                return;
            position = point;
            bestDistance = distance;
            found = true;
        }

        private static void Consider(Float2 point, Float2 origin, float axisX, float axisY, float cosHalf, float range,
            ref Float2 position, ref float bestDistance, ref bool found)
        {
            if (!InCone(origin, axisX, axisY, cosHalf, range, point, out var distance) || distance >= bestDistance)
                return;
            position = point;
            bestDistance = distance;
            found = true;
        }

        // Distance along a unit ray to where it first meets a rectangle (0 from inside it); float.MaxValue if it misses.
        private static float RayToObstacle(Float2 origin, float dirX, float dirY, Obstacle obstacle)
        {
            var enter = 0f;
            var leave = float.MaxValue;
            if (!RaySlab(origin.X, dirX, obstacle.MinX, obstacle.MaxX, ref enter, ref leave) ||
                !RaySlab(origin.Y, dirY, obstacle.MinY, obstacle.MaxY, ref enter, ref leave))
                return float.MaxValue;
            return enter;
        }

        private static bool RaySlab(float start, float direction, float min, float max, ref float enter, ref float leave)
        {
            const float parallel = 1e-6f;
            if (MathF.Abs(direction) < parallel)
                return start >= min && start <= max;
            var first = (min - start) / direction;
            var second = (max - start) / direction;
            if (first > second)
                (first, second) = (second, first);
            enter = MathF.Max(enter, first);
            leave = MathF.Min(leave, second);
            return leave >= enter;
        }

        // Distance along a unit ray from a point inside the arena to its border (the first side it meets).
        private static float RayToBorder(Float2 origin, float dirX, float dirY, float width, float height)
        {
            const float parallel = 1e-6f;
            var t = float.MaxValue;
            if (dirX > parallel)
                t = MathF.Min(t, (width - origin.X) / dirX);
            else if (dirX < -parallel)
                t = MathF.Min(t, -origin.X / dirX);
            if (dirY > parallel)
                t = MathF.Min(t, (height - origin.Y) / dirY);
            else if (dirY < -parallel)
                t = MathF.Min(t, -origin.Y / dirY);
            return t;
        }

        // Sine and cosine of the target's direction in the body's frame; a target at the eye's own position is reported
        // straight along the eye's axis.
        private static void WriteDirection(float[] buffer, int sinIndex, Float2 target, Float2 origin, float distance,
            float axisX, float axisY, float forwardX, float forwardY)
        {
            float dx, dy;
            if (distance > 0f)
            {
                dx = (target.X - origin.X) / distance;
                dy = (target.Y - origin.Y) / distance;
            }
            else
            {
                dx = axisX;
                dy = axisY;
            }

            buffer[sinIndex] = Clamp(dy * forwardX - dx * forwardY);
            buffer[sinIndex + 1] = Clamp(dx * forwardX + dy * forwardY);
        }

        private static float Clamp(float value) => value < -1f ? -1f : value > 1f ? 1f : value;
        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
