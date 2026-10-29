using System;
using System.Linq;
using Eden.Application;
using Eden.Domain;
using Eden.Evolution;
using Eden.Persistence;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>
    /// Scenarios (P2.7): larger arenas keeping the density or the quantity of resources, each factor set apart, the
    /// supply per creature, and how all of it is recorded. A scenario that sets nothing changes nothing.
    /// </summary>
    public class ScenarioTests
    {
        private static readonly DateTime SavedAt = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void AnUnsetScenario_ChangesNothing()
        {
            var template = new EvolutionSettings();
            var applied = new EvolutionSettings();
            new ScenarioSettings().Apply(applied);
            Assert.That(Canonical(applied), Is.EqualTo(Canonical(template)));
            Assert.That(applied.EpisodeSeconds, Is.EqualTo(template.EpisodeSeconds));
            Assert.That(new ScenarioSettings().Describe(), Is.Null);
            Assert.That(new ScenarioSettings().ResourceFactor, Is.EqualTo(1f));
        }

        [Test]
        public void LargerArenas_KeepTheDensityOrTheQuantityOfResources()
        {
            var dense = Applied(new ScenarioSettings { ArenaSide = 100f, Water = WaterLayout.Pond });
            Assert.That(dense.ArenaWidth, Is.EqualTo(100f));
            Assert.That(dense.ArenaHeight, Is.EqualTo(100f));
            Assert.That(dense.InitialFood, Is.EqualTo(32 * 4), "four times the area, four times the food");
            Assert.That(dense.Water.Single().Capacity, Is.EqualTo(WaterLayouts.DefaultCapacity * 4f));
            Assert.That(dense.Water.Single().RechargePerSecond, Is.EqualTo(WaterLayouts.DefaultRechargePerSecond * 4f));
            Assert.That(dense.Water.Single().Area, Is.EqualTo(new Obstacle(32.5f, 32.5f, 42.5f, 42.5f)),
                "the pond follows the arena");
            Assert.That(dense.SolidBodies, Is.True, "water implies solid bodies");

            var sparse = Applied(new ScenarioSettings
            {
                ArenaSide = 200f, Scaling = ResourceScaling.KeepQuantity, Water = WaterLayout.Ponds
            });
            Assert.That(sparse.InitialFood, Is.EqualTo(32), "the same food, sixteen times the space");
            Assert.That(sparse.Water.Select(w => w.Capacity), Is.All.EqualTo(WaterLayouts.DefaultCapacity));
            Assert.That(Applied(new ScenarioSettings { ArenaSide = 200f, Food = 10 }).InitialFood, Is.EqualTo(160),
                "the food given is for the 50 × 50 control");

            Assert.That(new ScenarioSettings { ArenaSide = 100f }.Describe(),
                Is.EqualTo("arena 100x100;resources keep density (x4 of the 50x50 control)"));
            Assert.That(new ScenarioSettings { ArenaSide = 200f, Scaling = ResourceScaling.KeepQuantity }.Describe(),
                Is.EqualTo("arena 200x200;resources keep quantity (x1 of the 50x50 control)"));
            Assert.That(() => new SimulationConfig(dense), Throws.Nothing);
            Assert.That(() => new SimulationConfig(sparse), Throws.Nothing);
        }

        [Test]
        public void EachFactor_IsSetApart_AndLayoutsFollowTheArena()
        {
            var evolution = new EvolutionSettings();
            new ScenarioSettings
            {
                ArenaSide = 100f, FoodEnergy = 4f, FoodRespawnSeconds = 2f, MaxAgeSeconds = 90f, InitialEnergy = 30f,
                EpisodeSeconds = 45f, Obstacles = ObstacleLayout.Pillars, Water = WaterLayout.Ponds,
                InexhaustibleWater = true, WaterCapacity = 50f, WaterRechargePerSecond = 0.5f
            }.Apply(evolution);
            var world = evolution.World;
            Assert.That((world.FoodEnergy, world.FoodRespawnSeconds, world.MaxAgeSeconds, world.InitialEnergy),
                Is.EqualTo((4f, 2f, 90f, 30f)));
            Assert.That(evolution.EpisodeSeconds, Is.EqualTo(45f));
            Assert.That(world.SolidBodies, Is.True, "obstacles imply solid bodies");
            Assert.That(ObstacleLayouts.Identify(world), Is.EqualTo(ObstacleLayout.Pillars), "pillars for 100 × 100");
            Assert.That(world.Obstacles.Max(o => o.MaxX), Is.GreaterThan(50f));
            Assert.That(WaterLayouts.Identify(world), Is.EqualTo(WaterLayout.Ponds));
            Assert.That(world.Water.All(w => w.Inexhaustible && w.Capacity == 200f && w.RechargePerSecond == 2f), Is.True);
            Assert.That(() => new EvaluationPlan(evolution), Throws.Nothing);
        }

        [Test]
        public void ReadingAScenario_GivesBackTheSameWorld_WithTheTemplatesFoodCount()
        {
            var original = new ScenarioSettings
            {
                ArenaSide = 100f, FoodEnergy = 6f, Water = WaterLayout.Pond, WaterCapacity = 80f, MaxAgeSeconds = 150f
            };
            var world = Applied(original);
            var read = ScenarioSettings.Read(world, 60f);
            Assert.That(read.Food, Is.Null, "the count is left to the template");
            Assert.That(read.Scaling, Is.EqualTo(ResourceScaling.KeepDensity));
            Assert.That(read.WaterCapacity, Is.EqualTo(80f), "brought back to the control arena");
            Assert.That(read.EpisodeSeconds, Is.EqualTo(60f));
            var again = Applied(read);
            Assert.That(Canonical(new EvolutionSettings { World = again }),
                Is.EqualTo(Canonical(new EvolutionSettings { World = world })));
        }

        [Test]
        public void Supply_GivesDensitiesAndTheMostEnergyAndWaterPerCreature()
        {
            var settings = new SimulationSettings { InitialPopulation = 64, InitialFood = 32, SolidBodies = true };
            settings.Water = WaterLayouts.Create(WaterLayout.Ponds, settings);
            var supply = new ResourceSupply(new SimulationConfig(settings), 64);
            Assert.That(supply.Area, Is.EqualTo(2500f));
            Assert.That(supply.FoodDensity, Is.EqualTo(1.28).Within(1e-9));
            Assert.That(supply.CreatureDensity, Is.EqualTo(2.56).Within(1e-9));
            Assert.That(supply.FoodStock, Is.EqualTo(320.0));
            Assert.That(supply.FoodEnergyPerSecond, Is.EqualTo(64.0).Within(1e-9), "32 items × 10 energy every 5 s");
            Assert.That(supply.FoodEnergyPerCreaturePerSecond, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(supply.RestingCostPerSecond, Is.EqualTo(1.25f));
            Assert.That(supply.CreaturesFedAtRest, Is.EqualTo(51.2).Within(1e-6), "fewer than the 64 creatures");
            Assert.That(supply.WaterStock, Is.EqualTo(400.0));
            Assert.That(supply.WaterPerSecond, Is.EqualTo(4.0));
            Assert.That(supply.WaterPerCreaturePerSecond, Is.EqualTo(4.0 / 64).Within(1e-12));
            Assert.That(supply.HydrationLossPerSecond, Is.EqualTo(1f));

            // Many items that come back slowly can offer less than a few that come back fast.
            var many = new ResourceSupply(new SimulationConfig(new SimulationSettings
                { InitialFood = 100, FoodRespawnSeconds = 50f }), 1);
            var few = new ResourceSupply(new SimulationConfig(new SimulationSettings
                { InitialFood = 10, FoodRespawnSeconds = 2f }), 1);
            Assert.That(many.FoodEnergyPerSecond, Is.LessThan(few.FoodEnergyPerSecond));

            settings.Water = WaterLayouts.Create(WaterLayout.Pond, settings, inexhaustible: true);
            var spring = new ResourceSupply(new SimulationConfig(settings), 0);
            Assert.That(spring.InexhaustibleWater, Is.True);
            Assert.That(double.IsPositiveInfinity(spring.WaterPerSecond), Is.True);
            Assert.That(spring.FoodEnergyPerCreaturePerSecond, Is.EqualTo(spring.FoodEnergyPerSecond), "no creature: as one");
            Assert.That(new ResourceSupply(new SimulationConfig(new SimulationSettings()), 1).HydrationLossPerSecond,
                Is.Zero, "no water, no thirst");
        }

        [Test]
        public void Records_KeepTheResources_TheSupplyAndAScenarioOtherThanTheControl()
        {
            var control = new ExperimentHeader(new EvolutionSettings(), 1, "0.1.0", SavedAt).Describe();
            Assert.That(control, Does.Contain("resources=food_energy 10;food_respawn_seconds 5;initial_energy 50;max_age_seconds 180"));
            Assert.That(control, Has.Some.StartsWith("supply=creatures 1;creature_density 0.04;food_density 1.28;" +
                                                     "food_stock 320;food_energy_per_second 64;per_creature 64;"));
            Assert.That(control, Has.None.StartsWith("scenario="));

            var scenario = new ScenarioSettings { ArenaSide = 200f, Water = WaterLayout.Pond };
            var settings = new EvolutionSettings();
            scenario.Apply(settings);
            var lines = new ExperimentHeader(settings, 1, "0.1.0", SavedAt, scenario: scenario.Describe()).Describe();
            Assert.That(lines, Does.Contain("arena=200x200").And.Contain("episode_food=512"));
            Assert.That(lines, Does.Contain("scenario=arena 200x200;resources keep density (x16 of the 50x50 control)"));
            Assert.That(lines, Has.Some.Contains("food_energy_per_second 1024;").And.Some.Contains(";water_per_second 32;"));
            Assert.That(ExperimentJson.Experiment(new ExperimentHeader(settings, 1, "0.1.0", SavedAt)),
                Does.Contain("\"foodEnergyPerSecond\": 1024"));

            var shared = new SimulationSettings { InitialPopulation = 64 };
            Assert.That(RunInfo.Describe(new SimulationConfig(shared), "heuristic-v1", "0.1.0", SavedAt),
                Has.Some.StartsWith("supply=creatures 64;").And.None.StartsWith("scenario="));
        }

        [Test]
        public void EpisodeConfigs_ShareTheFreeSpace_AndBuildTheSameWorldsAsNewConfigs()
        {
            var settings = new EvolutionSettings();
            new ScenarioSettings { ArenaSide = 100f, Obstacles = ObstacleLayout.Pillars, Water = WaterLayout.Ponds }
                .Apply(settings);
            var plan = new EvaluationPlan(settings);
            var first = plan.EpisodeConfig(11);
            var second = plan.EpisodeConfig(12);
            Assert.That(second.Seed, Is.EqualTo(12UL));
            Assert.That(second.FreeSpace, Is.SameAs(first.FreeSpace), "built once, not per episode");

            var world = settings.World.Clone();
            world.InitialPopulation = 1;
            world.Seed = 12;
            Assert.That(WorldFingerprint.Compute(WorldFactory.CreateInitial(second)),
                Is.EqualTo(WorldFingerprint.Compute(WorldFactory.CreateInitial(new SimulationConfig(world)))));
        }

        // The canonical settings text that checkpoints and exports hash.
        private static string Canonical(EvolutionSettings settings) =>
            CheckpointCodec.EncodeEvolutionSettings(settings, 1).ToCompactString();

        private static SimulationSettings Applied(ScenarioSettings scenario)
        {
            var evolution = new EvolutionSettings();
            scenario.Apply(evolution);
            return evolution.World;
        }
    }
}
