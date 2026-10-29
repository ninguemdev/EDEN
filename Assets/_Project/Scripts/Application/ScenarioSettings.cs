using System;
using System.Globalization;
using Eden.Domain;
using Eden.Evolution;

namespace Eden.Application
{
    /// <summary>How a scenario's resources follow the arena's size. Persisted in records by name: never renumber.</summary>
    public enum ResourceScaling
    {
        /// <summary>Food items and water reserves grow with the area: the same density as the 50 × 50 control.</summary>
        KeepDensity = 0,

        /// <summary>The same food items and water reserves as the control, spread over more space.</summary>
        KeepQuantity = 1
    }

    /// <summary>
    /// The scenario of an experiment, one factor at a time, applied the same way by the lab and the batch: arena size
    /// (presets 50 × 50, the control, 100 × 100 and 200 × 200), how resources follow it, food (items, energy, respawn),
    /// water (layout, reserve, recharge, or inexhaustible), solid bodies and obstacles, episode length, maximum age and
    /// initial energy. A factor left unset keeps the template's value, so a scenario that sets nothing changes nothing.
    /// Food items and water reserves are given for the 50 × 50 control and scaled by <see cref="ResourceFactor"/>; the
    /// layouts of obstacles and water follow the arena (a pond is always a tenth of it wide). The population is not
    /// scaled: it is the preset's (shared world) or one creature (episodes).
    /// </summary>
    public sealed class ScenarioSettings
    {
        /// <summary>Side of the control arena; resources are given for it.</summary>
        public const float ControlSide = 50f;

        /// <summary>Arena sides the lab offers.</summary>
        public static readonly float[] PresetSides = { 50f, 100f, 200f };

        /// <summary>Width and height of the square arena.</summary>
        public float ArenaSide = ControlSide;

        public ResourceScaling Scaling = ResourceScaling.KeepDensity;

        /// <summary>Food items in the control arena, before scaling; null keeps the template's.</summary>
        public int? Food;

        public float? FoodEnergy;
        public float? FoodRespawnSeconds;

        /// <summary>Creatures are solid bodies; obstacles and water imply it.</summary>
        public bool SolidBodies;

        public ObstacleLayout Obstacles;
        public WaterLayout Water;

        /// <summary>The control without a limited supply: sources never empty.</summary>
        public bool InexhaustibleWater;

        /// <summary>Reserve of each water source in the control arena, before scaling; null for the proposed value.</summary>
        public float? WaterCapacity;

        /// <summary>Recharge per second of each water source in the control arena, before scaling; null for the proposed value.</summary>
        public float? WaterRechargePerSecond;

        /// <summary>Length of an evaluation episode (evolution only); null keeps the template's.</summary>
        public float? EpisodeSeconds;

        public float? MaxAgeSeconds;
        public float? InitialEnergy;

        /// <summary>What food items and water reserves are multiplied by: the area ratio to the control, or 1.</summary>
        public float ResourceFactor =>
            Scaling == ResourceScaling.KeepDensity ? ArenaSide * ArenaSide / (ControlSide * ControlSide) : 1f;

        public ScenarioSettings Clone() => (ScenarioSettings)MemberwiseClone();

        /// <summary>Writes the scenario into a world's settings; validation happens when they are frozen.</summary>
        public void Apply(SimulationSettings world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            var factor = ResourceFactor;
            world.ArenaWidth = ArenaSide;
            world.ArenaHeight = ArenaSide;
            world.InitialFood = (int)Math.Round((double)(Food ?? world.InitialFood) * factor);
            world.FoodEnergy = FoodEnergy ?? world.FoodEnergy;
            world.FoodRespawnSeconds = FoodRespawnSeconds ?? world.FoodRespawnSeconds;
            world.MaxAgeSeconds = MaxAgeSeconds ?? world.MaxAgeSeconds;
            world.InitialEnergy = InitialEnergy ?? world.InitialEnergy;
            world.SolidBodies = SolidBodies || Obstacles != ObstacleLayout.None || Water != WaterLayout.None;
            world.Obstacles = ObstacleLayouts.Create(Obstacles, world);
            world.Water = WaterLayouts.Create(Water, world,
                (WaterCapacity ?? WaterLayouts.DefaultCapacity) * factor,
                (WaterRechargePerSecond ?? WaterLayouts.DefaultRechargePerSecond) * factor, InexhaustibleWater);
        }

        /// <summary>As above for an evolution's episode world, and its episode length.</summary>
        public void Apply(EvolutionSettings evolution)
        {
            if (evolution == null)
                throw new ArgumentNullException(nameof(evolution));
            Apply(evolution.World);
            evolution.EpisodeSeconds = EpisodeSeconds ?? evolution.EpisodeSeconds;
        }

        /// <summary>
        /// The scenario of a world's settings, for the lab's controls after loading or going back to a run: every factor
        /// set to its value, with density scaling and the first water source's reserve and recharge brought back to the
        /// control arena. The food count is left to the template (the preset's in the shared world, the episodes' own),
        /// since one mode's count is not the other's. Hand-placed obstacles read as none and hand-placed water as one pond;
        /// a non-square arena reads as its width.
        /// </summary>
        public static ScenarioSettings Read(SimulationSettings world, float? episodeSeconds = null)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            var water = world.Water ?? Array.Empty<WaterSource>();
            var first = water.Length > 0 ? water[0] : (WaterSource?)null;
            var scenario = new ScenarioSettings
            {
                ArenaSide = world.ArenaWidth,
                FoodEnergy = world.FoodEnergy,
                FoodRespawnSeconds = world.FoodRespawnSeconds,
                SolidBodies = world.SolidBodies,
                Obstacles = ObstacleLayouts.Identify(world) ?? ObstacleLayout.None,
                Water = water.Length == 0 ? WaterLayout.None : WaterLayouts.Identify(world) ?? WaterLayout.Pond,
                InexhaustibleWater = first?.Inexhaustible ?? false,
                EpisodeSeconds = episodeSeconds,
                MaxAgeSeconds = world.MaxAgeSeconds,
                InitialEnergy = world.InitialEnergy
            };
            if (first.HasValue)
            {
                scenario.WaterCapacity = first.Value.Capacity / scenario.ResourceFactor;
                scenario.WaterRechargePerSecond = first.Value.RechargePerSecond / scenario.ResourceFactor;
            }

            return scenario;
        }

        /// <summary>
        /// The arena size and resource scaling for records, or null for the 50 × 50 control (every other factor is in the
        /// recorded settings themselves).
        /// </summary>
        public string Describe()
        {
            if (ArenaSide == ControlSide)
                return null;
            var culture = CultureInfo.InvariantCulture;
            return "arena " + ArenaSide.ToString(culture) + "x" + ArenaSide.ToString(culture) + ";resources keep " +
                   ScalingName(Scaling) + " (x" + ResourceFactor.ToString(culture) + " of the " +
                   ControlSide.ToString(culture) + "x" + ControlSide.ToString(culture) + " control)";
        }

        /// <summary>Name of a scaling in records and on the command line: density, quantity.</summary>
        public static string ScalingName(ResourceScaling scaling) =>
            scaling == ResourceScaling.KeepDensity ? "density" : "quantity";
    }
}
