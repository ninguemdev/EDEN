using System;

namespace Eden.Domain
{
    /// <summary>
    /// A fixed water source (see <see cref="SimulationSettings.Water"/>): a rectangle solid bodies cannot enter, nothing
    /// can appear in and creatures drink from its edge. It holds a finite reserve that drinking empties and that recharges
    /// at a fixed rate, up to its capacity; an inexhaustible source (a control, named as such in the records) never runs
    /// out. Water is not opaque: eyes and mouths see and reach across it like across open ground.
    /// </summary>
    public readonly struct WaterSource : IEquatable<WaterSource>
    {
        public WaterSource(Obstacle area, float capacity, float rechargePerSecond, bool inexhaustible = false)
        {
            Area = area;
            Capacity = capacity;
            RechargePerSecond = rechargePerSecond;
            Inexhaustible = inexhaustible;
        }

        /// <summary>Where the water is; its inside blocks bodies like an obstacle.</summary>
        public Obstacle Area { get; }

        /// <summary>Most water the source holds; it starts full.</summary>
        public float Capacity { get; }

        /// <summary>Water added back each second, up to the capacity.</summary>
        public float RechargePerSecond { get; }

        /// <summary>The source never empties: the control without a limited supply.</summary>
        public bool Inexhaustible { get; }

        public bool Equals(WaterSource other) =>
            Area.Equals(other.Area) && Capacity == other.Capacity && RechargePerSecond == other.RechargePerSecond &&
            Inexhaustible == other.Inexhaustible;

        public override bool Equals(object obj) => obj is WaterSource other && Equals(other);

        public override int GetHashCode() =>
            ((Area.GetHashCode() * 397 ^ Capacity.GetHashCode()) * 397 ^ RechargePerSecond.GetHashCode()) * 397 ^
            Inexhaustible.GetHashCode();

        public override string ToString() =>
            $"water {Area} " + (Inexhaustible ? "inexhaustible" : $"capacity {Capacity}, recharge {RechargePerSecond}/s");
    }

    /// <summary>Named water layouts, built for an arena by <see cref="WaterLayouts.Create"/>.</summary>
    public enum WaterLayout
    {
        /// <summary>No water: creatures have no hydration at all.</summary>
        None = 0,

        /// <summary>One square pond.</summary>
        Pond = 1,

        /// <summary>Two square ponds, one on each side of the arena's middle.</summary>
        Ponds = 2
    }

    /// <summary>
    /// The water sources of a named layout for an arena, scaled with its size. Ponds are a tenth of the arena wide and
    /// sit at three and five eighths of each side, clear of the pillars and of the corridor's wall of the obstacle layouts,
    /// so water and obstacles combine.
    /// </summary>
    public static class WaterLayouts
    {
        /// <summary>Proposed water held by a pond (to be calibrated): a hundred seconds of thirst for two creatures.</summary>
        public const float DefaultCapacity = 200f;

        /// <summary>Proposed recharge per second of a pond (to be calibrated).</summary>
        public const float DefaultRechargePerSecond = 2f;

        public static WaterSource[] Create(WaterLayout layout, SimulationSettings settings,
            float capacity = DefaultCapacity, float rechargePerSecond = DefaultRechargePerSecond,
            bool inexhaustible = false)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            var width = settings.ArenaWidth;
            var height = settings.ArenaHeight;
            switch (layout)
            {
                case WaterLayout.None:
                    return Array.Empty<WaterSource>();
                case WaterLayout.Pond:
                    return new[] { Pond(width * 3f / 8f, height * 3f / 8f) };
                case WaterLayout.Ponds:
                    return new[] { Pond(width * 3f / 8f, height * 3f / 8f), Pond(width * 5f / 8f, height * 5f / 8f) };
                default:
                    throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unknown water layout.");
            }

            WaterSource Pond(float x, float y)
            {
                var halfX = width / 20f;
                var halfY = height / 20f;
                return new WaterSource(new Obstacle(x - halfX, y - halfY, x + halfX, y + halfY), capacity,
                    rechargePerSecond, inexhaustible);
            }
        }

        /// <summary>Name of a layout in files and on the command line: none, pond, ponds.</summary>
        public static string Name(WaterLayout layout) => layout.ToString().ToLowerInvariant();
    }
}
