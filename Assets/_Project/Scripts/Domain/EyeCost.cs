using System;

namespace Eden.Domain
{
    /// <summary>
    /// Upkeep of eyes, per second, paid every tick whether or not anything is in sight. An eye's cost is the body's
    /// sensor cost (what sensing the whole circle of its reference range costs) times the share of that circle the eye's
    /// sector covers: aperture / 360° × range factor². Every eye pays, overlapping ones too, so three 120° eyes cost as
    /// much as the legacy circular sense, and reaching twice as far costs four times as much while seeing twice as wide
    /// costs twice as much. The curve is a proposal to calibrate; the relation (range dearer than aperture, every eye
    /// paying) is the rule.
    /// </summary>
    public static class EyeCost
    {
        private const float FullTurn = 2f * MathF.PI;

        /// <summary>Upkeep of one eye on a body.</summary>
        public static float PerSecond(CreatureBody body, Eye eye) =>
            body.SensorCostPerSecond * (eye.Aperture / FullTurn) * eye.Range * eye.Range;

        /// <summary>Upkeep of every eye of a set on a body (0 for an empty set).</summary>
        public static float PerSecond(CreatureBody body, EyeSet eyes)
        {
            if (eyes == null)
                throw new ArgumentNullException(nameof(eyes));
            var total = 0f;
            for (var slot = 0; slot < EyeSet.MaxEyes; slot++)
            {
                if (eyes.Has(slot))
                    total += PerSecond(body, eyes[slot]);
            }

            return total;
        }
    }
}
