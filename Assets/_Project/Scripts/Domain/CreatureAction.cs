namespace Eden.Domain
{
    /// <summary>
    /// Motor command for one tick. Values are clamped to their ranges and NaN becomes 0, so a misbehaving
    /// controller can never inject invalid numbers into the simulation.
    /// </summary>
    public readonly struct CreatureAction
    {
        public static readonly CreatureAction Idle = new CreatureAction(0f, 0f, 0f);

        public CreatureAction(float thrust, float turn, float strafe)
        {
            Thrust = Sanitize(thrust, 0f);
            Turn = Sanitize(turn, -1f);
            Strafe = Sanitize(strafe, -1f);
        }

        /// <summary>Forward propulsion, [0, 1].</summary>
        public float Thrust { get; }

        /// <summary>Turn rate fraction, [-1, 1]; positive turns left (counterclockwise).</summary>
        public float Turn { get; }

        /// <summary>Lateral propulsion, [-1, 1]; positive pushes left.</summary>
        public float Strafe { get; }

        private static float Sanitize(float value, float min)
        {
            if (float.IsNaN(value))
                return 0f;
            return value < min ? min : value > 1f ? 1f : value;
        }
    }
}
