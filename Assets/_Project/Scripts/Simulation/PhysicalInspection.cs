using System;
using System.Collections.Generic;
using Eden.Domain;

namespace Eden.Simulation
{
    /// <summary>Two solid bodies touching: indices into <see cref="WorldState.Creatures"/>, lower index first.</summary>
    public readonly struct TouchingPair
    {
        public TouchingPair(int first, int second)
        {
            First = first;
            Second = second;
        }

        public int First { get; }
        public int Second { get; }
    }

    /// <summary>What touches one solid body now.</summary>
    public readonly struct BodyContacts
    {
        public BodyContacts(int creatures, bool wall, bool obstacle)
        {
            Creatures = creatures;
            Wall = wall;
            Obstacle = obstacle;
        }

        public int Creatures { get; }
        public bool Wall { get; }
        public bool Obstacle { get; }
        public bool Any => Creatures > 0 || Wall || Obstacle;
    }

    /// <summary>What the obstacles keep from one creature now.</summary>
    public readonly struct Occlusion
    {
        public Occlusion(int hiddenFromEyes, bool legacyTargetHidden, int blockedFromMouth)
        {
            HiddenFromEyes = hiddenFromEyes;
            LegacyTargetHidden = legacyTargetHidden;
            BlockedFromMouth = blockedFromMouth;
        }

        /// <summary>Food inside the cone and range of some eye that no eye whose cone holds it can see.</summary>
        public int HiddenFromEyes { get; }

        /// <summary>The legacy food sense, which does not know obstacles, points at food behind one.</summary>
        public bool LegacyTargetHidden { get; }

        /// <summary>Food within eating reach that an obstacle keeps from being eaten.</summary>
        public int BlockedFromMouth { get; }
    }

    /// <summary>
    /// Read-only questions about solid bodies and obstacles, for the lab's inspector and view: which bodies touch, by the
    /// rule the contact solver counts them with (<see cref="WorldSimulation.LastContacts"/>), and what obstacles hide.
    /// It never changes the world and never draws from its random streams.
    /// </summary>
    public sealed class PhysicalInspection
    {
        private int[] order = Array.Empty<int>();
        private float[] keys = Array.Empty<float>();

        /// <summary>
        /// Fills <paramref name="pairs"/> with the bodies touching now (none without solid bodies) and returns how many.
        /// Between ticks these are the pairs <see cref="WorldSimulation.LastContacts"/> counted, but for those with a
        /// creature that died in that tick. Allocation-free once the buffers have grown to the population.
        /// </summary>
        public int FindPairs(WorldState world, List<TouchingPair> pairs)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (pairs == null)
                throw new ArgumentNullException(nameof(pairs));
            pairs.Clear();
            if (!world.Config.SolidBodies)
                return 0;

            var creatures = world.Creatures;
            var count = creatures.Count;
            if (order.Length < count)
            {
                order = new int[Math.Max(count, order.Length * 2)];
                keys = new float[order.Length];
            }

            var largest = 0f;
            for (var i = 0; i < count; i++)
            {
                order[i] = i;
                keys[i] = creatures[i].Position.X;
                largest = Math.Max(largest, creatures[i].Body.Radius);
            }

            Array.Sort(keys, order, 0, count);
            for (var a = 0; a < count; a++)
            {
                var first = creatures[order[a]];
                for (var b = a + 1; b < count; b++)
                {
                    if (keys[b] - keys[a] > first.Body.Radius + largest + Contacts.TouchSlack)
                        break;
                    if (Touch(first, creatures[order[b]]))
                        pairs.Add(new TouchingPair(Math.Min(order[a], order[b]), Math.Max(order[a], order[b])));
                }
            }

            return pairs.Count;
        }

        /// <summary>
        /// What touches <paramref name="creature"/>: other bodies (their ids appended to <paramref name="touching"/> in
        /// id order), the arena's border and obstacles. Nothing touches anything without solid bodies.
        /// </summary>
        public static BodyContacts ContactsOf(WorldState world, CreatureState creature, List<CreatureId> touching)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (creature == null)
                throw new ArgumentNullException(nameof(creature));
            var config = world.Config;
            if (!config.SolidBodies)
                return default;

            var creatures = world.Creatures;
            var others = 0;
            for (var i = 0; i < creatures.Count; i++)
            {
                if (creatures[i] == creature || !Touch(creature, creatures[i]))
                    continue;
                others++;
                touching?.Add(creatures[i].Id);
            }

            var position = creature.Position;
            var reach = creature.Body.Radius + Contacts.TouchSlack;
            var wall = position.X <= reach || position.Y <= reach ||
                       config.ArenaWidth - position.X <= reach || config.ArenaHeight - position.Y <= reach;
            var obstacle = false;
            var obstacles = config.Obstacles;
            for (var i = 0; i < obstacles.Count && !obstacle; i++)
                obstacle = obstacles[i].DistanceTo(position) <= reach;
            return new BodyContacts(others, wall, obstacle);
        }

        /// <summary>
        /// What the obstacles keep from <paramref name="creature"/> now: food hidden from its eyes, whether its legacy food
        /// sense points through an obstacle, and food within its eating reach behind one. All zero without obstacles.
        /// </summary>
        public static Occlusion OcclusionOf(WorldState world, CreatureState creature)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            if (creature == null)
                throw new ArgumentNullException(nameof(creature));
            var config = world.Config;
            if (config.Obstacles.Count == 0)
                return default;

            var food = world.Food;
            var position = creature.Position;
            var eatSquared = creature.Body.EatRadius * creature.Body.EatRadius;
            var rangeSquared = creature.Body.SensorRange * creature.Body.SensorRange;
            var hidden = 0;
            var blocked = 0;
            var nearest = -1;
            var nearestSquared = float.MaxValue;
            for (var f = 0; f < food.Count; f++)
            {
                var item = food[f].Position;
                var dx = item.X - position.X;
                var dy = item.Y - position.Y;
                var distanceSquared = dx * dx + dy * dy;
                // The legacy sense's rule: the nearest within range, the lower id on equal distance.
                if (distanceSquared <= rangeSquared && distanceSquared < nearestSquared)
                {
                    nearest = f;
                    nearestSquared = distanceSquared;
                }

                if (distanceSquared <= eatSquared && !Vision.LineOfSight(config, position, item))
                    blocked++;
                if (creature.Eyes != null && HiddenFromEyes(config, creature, item))
                    hidden++;
            }

            var legacyHidden = creature.Eyes == null && nearest >= 0 &&
                               !Vision.LineOfSight(config, position, food[nearest].Position);
            return new Occlusion(hidden, legacyHidden, blocked);
        }

        // In the cone and range of at least one eye, and seen by none of the eyes whose cone holds it.
        private static bool HiddenFromEyes(SimulationConfig config, CreatureState creature, Float2 item)
        {
            var eyes = creature.Eyes;
            var inside = false;
            for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
            {
                if (!eyes.Has(slot))
                    continue;
                var eye = eyes[slot];
                var axis = creature.Heading + eye.Orientation;
                var origin = Vision.EyePosition(config, creature, eye);
                if (!Vision.InCone(origin, MathF.Cos(axis), MathF.Sin(axis), MathF.Cos(eye.Aperture * 0.5f),
                        creature.Body.SensorRange * eye.Range, item, out _))
                    continue;
                if (Vision.LineOfSight(config, origin, item))
                    return false;
                inside = true;
            }

            return inside;
        }

        // The solver's rule (Contacts.Brake): centres no farther apart than the radii plus the touch slack.
        private static bool Touch(CreatureState a, CreatureState b)
        {
            var dx = b.Position.X - a.Position.X;
            var dy = b.Position.Y - a.Position.Y;
            var sum = a.Body.Radius + b.Body.Radius + Contacts.TouchSlack;
            return dx * dx + dy * dy <= sum * sum;
        }
    }
}
