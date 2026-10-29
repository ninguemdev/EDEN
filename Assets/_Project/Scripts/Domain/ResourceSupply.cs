using System;

namespace Eden.Domain
{
    /// <summary>
    /// What a scenario offers, so scenarios compare by supply and not only by counts: many food items that come back
    /// slowly can give less energy than a few that come back fast. Food is never created, only respawned: an eaten item
    /// returns after the respawn time, so the most energy food can give per second, if every item were eaten the moment
    /// it appears, is items × energy / respawn time. Limited water sources give at most their recharge per second once
    /// their reserve is drunk; inexhaustible ones have no limit. "Per creature" divides by the creatures the scenario
    /// starts with (at least one), and is compared with what a creature spends at rest (metabolism and the legacy sensor
    /// of the reference body) and the hydration it loses per second. Densities are per 100 square units (a 10 × 10
    /// square). Upper bounds of the offer, not what creatures actually get.
    /// </summary>
    public readonly struct ResourceSupply
    {
        /// <summary>Densities are given per this area (a 10 × 10 square).</summary>
        public const float DensityArea = 100f;

        public ResourceSupply(SimulationConfig config, int creatures)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (creatures < 0)
                throw new ArgumentOutOfRangeException(nameof(creatures));
            Creatures = creatures;
            Area = config.ArenaWidth * config.ArenaHeight;
            FoodItems = config.InitialFood;
            FoodStock = (double)config.InitialFood * config.FoodEnergy;
            FoodEnergyPerSecond = FoodStock * config.TicksPerSecond / config.FoodRespawnTicks;
            RestingCostPerSecond = config.MetabolismPerSecond + config.SensorCostPerSecond;
            WaterSources = config.Water.Count;
            var stock = 0.0;
            var recharge = 0.0;
            var inexhaustible = false;
            foreach (var source in config.Water)
            {
                if (source.Inexhaustible)
                {
                    inexhaustible = true;
                    continue;
                }

                stock += source.Capacity;
                recharge += source.RechargePerSecond;
            }

            InexhaustibleWater = inexhaustible;
            WaterStock = inexhaustible ? double.PositiveInfinity : stock;
            WaterPerSecond = inexhaustible ? double.PositiveInfinity : recharge;
            HydrationLossPerSecond = config.HasWater ? config.HydrationLossPerSecond : 0f;
        }

        /// <summary>Creatures the scenario starts with.</summary>
        public int Creatures { get; }

        /// <summary>Arena area, in square units.</summary>
        public float Area { get; }

        public int FoodItems { get; }

        /// <summary>Food items per <see cref="DensityArea"/>.</summary>
        public double FoodDensity => FoodItems * (double)DensityArea / Area;

        /// <summary>Creatures per <see cref="DensityArea"/>.</summary>
        public double CreatureDensity => Creatures * (double)DensityArea / Area;

        /// <summary>Energy of all food present at the start.</summary>
        public double FoodStock { get; }

        /// <summary>Most energy food can give per second (every item eaten as soon as it respawns).</summary>
        public double FoodEnergyPerSecond { get; }

        public double FoodEnergyPerCreaturePerSecond => FoodEnergyPerSecond / Math.Max(1, Creatures);

        public double FoodStockPerCreature => FoodStock / Math.Max(1, Creatures);

        /// <summary>What the reference body spends per second at rest with the legacy sensor (metabolism + sensor).</summary>
        public float RestingCostPerSecond { get; }

        /// <summary>How many creatures at rest the most food energy per second could feed.</summary>
        public double CreaturesFedAtRest => FoodEnergyPerSecond / RestingCostPerSecond;

        public int WaterSources { get; }

        /// <summary>Whether a source never empties; then the water figures are infinite.</summary>
        public bool InexhaustibleWater { get; }

        /// <summary>Water held at the start by the limited sources (infinite with an inexhaustible one).</summary>
        public double WaterStock { get; }

        /// <summary>Most water the sources give per second once their reserves are drunk: the sum of recharges.</summary>
        public double WaterPerSecond { get; }

        public double WaterPerCreaturePerSecond => WaterPerSecond / Math.Max(1, Creatures);

        /// <summary>Hydration a creature loses per second; 0 without water.</summary>
        public float HydrationLossPerSecond { get; }
    }
}
