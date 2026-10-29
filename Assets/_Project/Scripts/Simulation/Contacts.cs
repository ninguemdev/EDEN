using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Simulation
{
    /// <summary>
    /// Moves solid bodies (<see cref="SimulationConfig.SolidBodies"/>): every creature is a disc of its body's
    /// <see cref="CreatureBody.Radius"/> that stays whole inside the arena, out of every
    /// <see cref="SimulationConfig.Barriers">obstacle and water source</see> and does not overlap another. Each tick,
    /// after all creatures steered, the tick is split into <see cref="SimulationConfig.SolidSubsteps"/> moves, so no body
    /// moves farther than the smallest radius in one and two bodies cannot pass through each other. After each move:
    /// <list type="bullet">
    /// <item>up to <see cref="SimulationConfig.ContactPasses"/> separation passes push each overlapping pair apart along
    /// the line between their centres, each body by the share of the other's mass (mass ∝ radius²); a body held by a
    /// wall or an obstacle cannot give way, so the other takes the rest;</item>
    /// <item>touching pairs lose the speed at which they approach each other (an inelastic contact that keeps
    /// momentum), so a creature that thrusts into another pushes it, paying the effort of its thrust as always.</item>
    /// </list>
    /// Pairs are visited in order of position (bodies sorted by x, ties by index), forwards and backwards on alternate
    /// passes, never in order of id, so no creature has priority because of its id. Nothing is remembered between ticks:
    /// positions and velocities are the whole physical state, so a run resumes exactly.
    /// </summary>
    internal sealed class Contacts
    {
        // Bodies this close beyond touching still count as touching (for the velocity rule and the count), and overlaps
        // up to this little do not ask for another pass. PhysicalInspection counts contacts by the same rule.
        internal const float TouchSlack = 1e-4f;

        // Most rounds of pushing a body out of the obstacles it overlaps.
        private const int ObstacleRounds = 4;

        // Centres closer than this have no usable direction between them.
        private const float Coincident = 1e-6f;

        private float[] x = Array.Empty<float>();
        private float[] y = Array.Empty<float>();
        private float[] velocityX = Array.Empty<float>();
        private float[] velocityY = Array.Empty<float>();
        private float[] radius = Array.Empty<float>();
        private int[] order = Array.Empty<int>();
        private int[] merge = Array.Empty<int>();
        private float largest;

        /// <summary>Pairs of bodies touching at the end of the last tick.</summary>
        public int LastContacts { get; private set; }

        public void Reserve(int count)
        {
            if (x.Length >= count)
                return;
            var size = Math.Max(count, x.Length * 2);
            x = new float[size];
            y = new float[size];
            velocityX = new float[size];
            velocityY = new float[size];
            radius = new float[size];
            order = new int[size];
            merge = new int[size];
        }

        /// <summary>Steers the first <paramref name="count"/> creatures with their actions, then moves them as solid bodies.</summary>
        public void Move(IReadOnlyList<CreatureState> creatures, CreatureAction[] actions, int count, SimulationConfig config)
        {
            Reserve(count);
            largest = 0f;
            for (var i = 0; i < count; i++)
            {
                var creature = creatures[i];
                Movement.Steer(creature, actions[i], config);
                x[i] = creature.Position.X;
                y[i] = creature.Position.Y;
                velocityX[i] = creature.Velocity.X;
                velocityY[i] = creature.Velocity.Y;
                radius[i] = creature.Body.Radius;
                largest = Math.Max(largest, radius[i]);
            }

            var substeps = config.SolidSubsteps;
            var dt = config.TickDuration / substeps;
            var contacts = 0;
            for (var step = 0; step < substeps; step++)
            {
                for (var i = 0; i < count; i++)
                {
                    x[i] += velocityX[i] * dt;
                    y[i] += velocityY[i] * dt;
                    KeepInside(i, config);
                }

                for (var pass = 0; pass < SimulationConfig.ContactPasses; pass++)
                {
                    if (!Separate(creatures, count, config, pass % 2 == 1))
                        break;
                }

                contacts = Brake(count);
            }

            for (var i = 0; i < count; i++)
            {
                creatures[i].Position = new Float2(x[i], y[i]);
                creatures[i].Velocity = new Float2(velocityX[i], velocityY[i]);
            }

            LastContacts = contacts;
        }

        // One separation pass; false when no pair overlapped by more than the slack, so later passes can be skipped.
        private bool Separate(IReadOnlyList<CreatureState> creatures, int count, SimulationConfig config, bool backwards)
        {
            Sort(count);
            var overlapped = false;
            for (var k = 0; k < count; k++)
            {
                var a = backwards ? count - 1 - k : k;
                var i = order[a];
                for (var b = a + 1; b < count; b++)
                {
                    var j = order[b];
                    if (x[j] - x[i] > radius[i] + largest)
                        break;
                    var dx = x[j] - x[i];
                    var dy = y[j] - y[i];
                    var sum = radius[i] + radius[j];
                    var distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared >= sum * sum)
                        continue;

                    var distance = MathF.Sqrt(distanceSquared);
                    float normalX, normalY;
                    if (distance > Coincident)
                    {
                        normalX = dx / distance;
                        normalY = dy / distance;
                    }
                    else
                    {
                        SeparationDirection(creatures[i].Id, creatures[j].Id, out normalX, out normalY);
                        if (creatures[i].Id.Value > creatures[j].Id.Value)
                        {
                            normalX = -normalX;
                            normalY = -normalY;
                        }
                    }

                    var overlap = sum - distance;
                    overlapped |= overlap > TouchSlack;
                    var massI = radius[i] * radius[i];
                    var massJ = radius[j] * radius[j];
                    var shareI = massJ / (massI + massJ);
                    x[i] -= normalX * overlap * shareI;
                    y[i] -= normalY * overlap * shareI;
                    x[j] += normalX * overlap * (1f - shareI);
                    y[j] += normalY * overlap * (1f - shareI);
                    var heldI = KeepInside(i, config);
                    var heldJ = KeepInside(j, config);
                    if (heldI != heldJ)
                        GiveWay(heldI ? j : i, heldI ? i : j, sum, config);
                }
            }

            return overlapped;
        }

        // Pushes the free body straight away from the one a wall holds, until they just touch.
        private void GiveWay(int free, int held, float sum, SimulationConfig config)
        {
            var dx = x[free] - x[held];
            var dy = y[free] - y[held];
            var distance = MathF.Sqrt(dx * dx + dy * dy);
            if (distance <= Coincident || distance >= sum)
                return;
            var rest = (sum - distance) / distance;
            x[free] += dx * rest;
            y[free] += dy * rest;
            KeepInside(free, config);
        }

        // Touching pairs stop approaching each other, keeping their momentum; returns how many pairs touch.
        private int Brake(int count)
        {
            Sort(count);
            var pairs = 0;
            for (var a = 0; a < count; a++)
            {
                var i = order[a];
                for (var b = a + 1; b < count; b++)
                {
                    var j = order[b];
                    if (x[j] - x[i] > radius[i] + largest + TouchSlack)
                        break;
                    var dx = x[j] - x[i];
                    var dy = y[j] - y[i];
                    var sum = radius[i] + radius[j] + TouchSlack;
                    var distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared > sum * sum)
                        continue;
                    pairs++;
                    var distance = MathF.Sqrt(distanceSquared);
                    if (distance <= Coincident)
                        continue;

                    var normalX = dx / distance;
                    var normalY = dy / distance;
                    var approach = (velocityX[j] - velocityX[i]) * normalX + (velocityY[j] - velocityY[i]) * normalY;
                    if (approach >= 0f)
                        continue;
                    var massI = radius[i] * radius[i];
                    var massJ = radius[j] * radius[j];
                    var shareI = massJ / (massI + massJ);
                    velocityX[i] += normalX * approach * shareI;
                    velocityY[i] += normalY * approach * shareI;
                    velocityX[j] -= normalX * approach * (1f - shareI);
                    velocityY[j] -= normalY * approach * (1f - shareI);
                }
            }

            return pairs;
        }

        // The whole disc stays out of the obstacles and inside the arena, and speed into them is lost; true when an
        // obstacle or a wall held the body. Walls come last: they always win.
        private bool KeepInside(int i, SimulationConfig config)
        {
            var r = radius[i];
            var held = false;
            var obstacles = config.Barriers;
            for (var round = 0; round < ObstacleRounds; round++)
            {
                // A push out of one obstacle can press the body into another (a wedge between two corners).
                var pushed = false;
                for (var o = 0; o < obstacles.Count; o++)
                    pushed |= PushOut(i, obstacles[o], r);
                held |= pushed;
                if (!pushed)
                    break;
            }

            if (x[i] < r)
            {
                x[i] = r;
                held = true;
                if (velocityX[i] < 0f)
                    velocityX[i] = 0f;
            }
            else if (x[i] > config.ArenaWidth - r)
            {
                x[i] = config.ArenaWidth - r;
                held = true;
                if (velocityX[i] > 0f)
                    velocityX[i] = 0f;
            }

            if (y[i] < r)
            {
                y[i] = r;
                held = true;
                if (velocityY[i] < 0f)
                    velocityY[i] = 0f;
            }
            else if (y[i] > config.ArenaHeight - r)
            {
                y[i] = config.ArenaHeight - r;
                held = true;
                if (velocityY[i] > 0f)
                    velocityY[i] = 0f;
            }

            return held;
        }

        // Pushes the disc out of a rectangle it overlaps: away from the nearest point of the rectangle, or, when its
        // centre is inside, through the nearest side (left, right, bottom, top on ties). Speed into the rectangle is lost.
        private bool PushOut(int i, Obstacle obstacle, float r)
        {
            var nearestX = x[i] < obstacle.MinX ? obstacle.MinX : x[i] > obstacle.MaxX ? obstacle.MaxX : x[i];
            var nearestY = y[i] < obstacle.MinY ? obstacle.MinY : y[i] > obstacle.MaxY ? obstacle.MaxY : y[i];
            var dx = x[i] - nearestX;
            var dy = y[i] - nearestY;
            var distanceSquared = dx * dx + dy * dy;
            if (distanceSquared >= r * r)
                return false;

            if (distanceSquared > 0f)
            {
                var distance = MathF.Sqrt(distanceSquared);
                var normalX = dx / distance;
                var normalY = dy / distance;
                x[i] = nearestX + normalX * r;
                y[i] = nearestY + normalY * r;
                var into = velocityX[i] * normalX + velocityY[i] * normalY;
                if (into < 0f)
                {
                    velocityX[i] -= normalX * into;
                    velocityY[i] -= normalY * into;
                }

                return true;
            }

            var left = x[i] - obstacle.MinX;
            var right = obstacle.MaxX - x[i];
            var bottom = y[i] - obstacle.MinY;
            var top = obstacle.MaxY - y[i];
            var least = Math.Min(Math.Min(left, right), Math.Min(bottom, top));
            if (left == least)
            {
                x[i] = obstacle.MinX - r;
                velocityX[i] = Math.Min(velocityX[i], 0f);
            }
            else if (right == least)
            {
                x[i] = obstacle.MaxX + r;
                velocityX[i] = Math.Max(velocityX[i], 0f);
            }
            else if (bottom == least)
            {
                y[i] = obstacle.MinY - r;
                velocityY[i] = Math.Min(velocityY[i], 0f);
            }
            else
            {
                y[i] = obstacle.MaxY + r;
                velocityY[i] = Math.Max(velocityY[i], 0f);
            }

            return true;
        }

        // Indices by x, ties in index order: a stable bottom-up merge sort that starts from index order every time, so
        // the order depends only on the positions.
        private void Sort(int count)
        {
            for (var i = 0; i < count; i++)
                order[i] = i;
            var from = order;
            var to = merge;
            for (var width = 1; width < count; width *= 2)
            {
                for (var start = 0; start < count; start += 2 * width)
                {
                    var middle = Math.Min(start + width, count);
                    var end = Math.Min(start + 2 * width, count);
                    int left = start, right = middle, k = start;
                    while (left < middle && right < end)
                        to[k++] = x[from[right]] < x[from[left]] ? from[right++] : from[left++];
                    while (left < middle)
                        to[k++] = from[left++];
                    while (right < end)
                        to[k++] = from[right++];
                }

                (from, to) = (to, from);
            }

            if (from != order)
                Array.Copy(from, order, count);
        }

        // Bodies on the same spot part in a direction drawn from their ids alone (lower id against it), so neither the
        // arena's axes nor the processing order decide it.
        private static void SeparationDirection(CreatureId first, CreatureId second, out float directionX,
            out float directionY)
        {
            var low = (uint)Math.Min(first.Value, second.Value);
            var high = (uint)Math.Max(first.Value, second.Value);
            var mixed = unchecked((low * 2654435761u) ^ (high * 2246822519u));
            mixed ^= mixed >> 15;
            var angle = (mixed & 0xFFFFFFu) / 16777216f * 2f * MathF.PI;
            directionX = MathF.Cos(angle);
            directionY = MathF.Sin(angle);
        }
    }
}
