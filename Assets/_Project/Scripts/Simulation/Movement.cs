using System;
using Eden.Domain;

namespace Eden.Simulation
{
    /// <summary>
    /// Kinematic 2D locomotion without physics bodies: turn, accelerate in the new local frame, damp, cap speed,
    /// move, then clamp to the arena and stop velocity into the wall. Acceleration and speed cap are the creature's own
    /// body traits. Creatures do not push each other; with solid bodies <see cref="Steer"/> only turns and accelerates and
    /// <see cref="Contacts"/> moves the bodies.
    /// </summary>
    internal static class Movement
    {
        private const float FullTurn = 2f * MathF.PI;

        /// <summary>
        /// The turning and velocity of <see cref="Apply"/> without moving: the same expressions, kept apart from
        /// <see cref="Apply"/> so the point-body path stays exactly as it was (splitting it would round differently).
        /// </summary>
        public static void Steer(CreatureState creature, CreatureAction action, SimulationConfig config)
        {
            var dt = config.TickDuration;
            var heading = WrapAngle(creature.Heading + action.Turn * config.MaxTurnRate * dt);
            var forwardX = MathF.Cos(heading);
            var forwardY = MathF.Sin(heading);

            var maxAcceleration = creature.Body.MaxAcceleration;
            var accelerationX = (forwardX * action.Thrust - forwardY * action.Strafe) * maxAcceleration;
            var accelerationY = (forwardY * action.Thrust + forwardX * action.Strafe) * maxAcceleration;

            var damping = MathF.Max(0f, 1f - config.Damping * dt);
            var velocityX = (creature.Velocity.X + accelerationX * dt) * damping;
            var velocityY = (creature.Velocity.Y + accelerationY * dt) * damping;
            var speedSquared = velocityX * velocityX + velocityY * velocityY;
            var maxSpeed = creature.Body.MaxSpeed;
            if (speedSquared > maxSpeed * maxSpeed)
            {
                var scale = maxSpeed / MathF.Sqrt(speedSquared);
                velocityX *= scale;
                velocityY *= scale;
            }

            creature.Heading = heading;
            creature.Velocity = new Float2(velocityX, velocityY);
        }

        public static void Apply(CreatureState creature, CreatureAction action, SimulationConfig config)
        {
            var dt = config.TickDuration;
            var heading = WrapAngle(creature.Heading + action.Turn * config.MaxTurnRate * dt);
            var forwardX = MathF.Cos(heading);
            var forwardY = MathF.Sin(heading);

            // Left of forward is (-forwardY, forwardX).
            var maxAcceleration = creature.Body.MaxAcceleration;
            var accelerationX = (forwardX * action.Thrust - forwardY * action.Strafe) * maxAcceleration;
            var accelerationY = (forwardY * action.Thrust + forwardX * action.Strafe) * maxAcceleration;

            var damping = MathF.Max(0f, 1f - config.Damping * dt);
            var velocityX = (creature.Velocity.X + accelerationX * dt) * damping;
            var velocityY = (creature.Velocity.Y + accelerationY * dt) * damping;
            var speedSquared = velocityX * velocityX + velocityY * velocityY;
            var maxSpeed = creature.Body.MaxSpeed;
            if (speedSquared > maxSpeed * maxSpeed)
            {
                var scale = maxSpeed / MathF.Sqrt(speedSquared);
                velocityX *= scale;
                velocityY *= scale;
            }

            var x = creature.Position.X + velocityX * dt;
            var y = creature.Position.Y + velocityY * dt;
            ClampAxis(ref x, ref velocityX, config.ArenaWidth);
            ClampAxis(ref y, ref velocityY, config.ArenaHeight);

            creature.Heading = heading;
            creature.Velocity = new Float2(velocityX, velocityY);
            creature.Position = new Float2(x, y);
        }

        private static void ClampAxis(ref float position, ref float velocity, float size)
        {
            if (position < 0f)
            {
                position = 0f;
                if (velocity < 0f)
                    velocity = 0f;
            }
            else if (position > size)
            {
                position = size;
                if (velocity > 0f)
                    velocity = 0f;
            }
        }

        private static float WrapAngle(float angle)
        {
            if (angle >= FullTurn)
                angle -= FullTurn;
            else if (angle < 0f)
                angle += FullTurn;
            // Rounding can leave exactly FullTurn after adding it to a tiny negative angle.
            return angle >= FullTurn ? 0f : angle;
        }
    }
}
