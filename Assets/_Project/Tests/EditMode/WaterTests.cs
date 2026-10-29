using System;
using System.Linq;
using Eden.Application;
using Eden.Domain;
using Eden.Persistence;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>
    /// Water and hydration (P2.6a): hydration is its own reserve, drinking gives no energy and eating no water, thirst
    /// kills, sources empty and recharge, a shortage is split by the declared rule, every drop is accounted for, and runs
    /// without water are untouched.
    /// </summary>
    public class WaterTests
    {
        private static readonly Obstacle PondArea = new Obstacle(20f, 20f, 25f, 25f);
        private static readonly Float2 LeftShore = new Float2(19.5f, 22.5f);
        private static readonly Float2 RightShore = new Float2(25.5f, 22.5f);
        private const float Tolerance = 1e-4f;

        [Test]
        public void WithoutWater_NothingHasHydration_AndTheBarriersAreTheObstacles()
        {
            var settings = new SimulationSettings { SolidBodies = true };
            settings.Obstacles = ObstacleLayouts.Create(ObstacleLayout.Pillars, settings);
            var config = new SimulationConfig(settings);
            Assert.That(config.HasWater, Is.False);
            Assert.That(config.Barriers, Is.EqualTo(config.Obstacles));
            var world = WorldFactory.CreateInitial(config);
            Assert.That(world.Creatures.All(c => c.Hydration == 0f), Is.True);
            Assert.That(world.WaterReserves, Is.Empty);
            var simulation = new WorldSimulation(world);
            simulation.Step(new SeekFoodController());
            Assert.That(simulation.LastHydration.Drunk + simulation.LastHydration.Lost, Is.Zero);
            Assert.That(world.Creatures.All(c => c.Hydration == 0f), Is.True);
        }

        [Test]
        public void Drinking_GivesNoEnergy_AndEating_GivesNoWater()
        {
            var world = new WorldState(Config(Pond()));
            var drinker = world.AddCreature(LeftShore, 0f, 50f);
            var idle = world.AddCreature(new Float2(5f, 5f), 0f, 50f);
            var eater = world.AddCreature(new Float2(40f, 40f), 0f, 50f);
            world.AddFood(new Float2(40.5f, 40f), 10f);
            drinker.Hydration = idle.Hydration = eater.Hydration = 50f;
            Assert.That(world.WaterReserves, Is.EqualTo(new[] { 200f }), "a source starts full");

            var simulation = new WorldSimulation(world);
            simulation.Step(new ConstantController(CreatureAction.Idle));

            Assert.That(drinker.Hydration, Is.EqualTo(50f + 1f - 0.05f).Within(Tolerance), "drank 20/s for a tick, lost 1/s");
            Assert.That(idle.Hydration, Is.EqualTo(50f - 0.05f).Within(Tolerance));
            Assert.That(drinker.Energy, Is.EqualTo(idle.Energy), "water gives no energy");
            Assert.That(eater.Energy, Is.GreaterThan(idle.Energy));
            Assert.That(eater.Hydration, Is.EqualTo(idle.Hydration), "food gives no water");
            Assert.That(simulation.LastHydration.Drunk, Is.EqualTo(1.0).Within(Tolerance));
            Assert.That(world.WaterReserves[0], Is.EqualTo(199f).Within(Tolerance));
        }

        [Test]
        public void Thirst_Kills_AndHungerComesFirst()
        {
            var world = new WorldState(Config(Pond()));
            var thirsty = world.AddCreature(new Float2(5f, 5f), 0f, 50f);
            thirsty.Hydration = 0.04f;
            var both = world.AddCreature(new Float2(10f, 5f), 0f, 0.01f);
            both.Hydration = 0.04f;
            var simulation = new WorldSimulation(world);
            simulation.Step(new ConstantController(CreatureAction.Idle));

            Assert.That(simulation.LastDeaths.Select(d => (d.Creature, d.Cause)), Is.EqualTo(new[]
            {
                (thirsty.Id, DeathCause.Dehydration), (both.Id, DeathCause.Starvation)
            }));
            Assert.That(simulation.LastHydration.Lost, Is.EqualTo(0.08).Within(Tolerance), "no more than they held");
            Assert.That(simulation.LastHydration.CarriedAwayByDeaths, Is.Zero);
            Assert.That(GenealogyCsvLog.CauseName(DeathCause.Dehydration), Is.EqualTo("dehydration"));
        }

        [Test]
        public void ALimitedSource_Empties_AndRechargesUpToItsCapacity()
        {
            var world = new WorldState(Config(Pond(capacity: 1f, recharge: 2f)));
            var drinker = world.AddCreature(LeftShore, 0f, 50f);
            drinker.Hydration = 10f;
            var simulation = new WorldSimulation(world);
            var idle = new ConstantController(CreatureAction.Idle);

            simulation.Step(idle);
            Assert.That(world.WaterReserves[0], Is.Zero, "full at 1, recharge capped, then drunk dry");
            Assert.That(simulation.LastHydration.Recharged, Is.Zero);
            simulation.Step(idle);
            Assert.That(simulation.LastHydration.Recharged, Is.EqualTo(0.1).Within(Tolerance));
            Assert.That(simulation.LastHydration.Drunk, Is.EqualTo(0.1).Within(Tolerance), "only what came back");
            Assert.That(world.WaterReserves[0], Is.EqualTo(0f).Within(Tolerance));

            drinker.Position = new Float2(5f, 5f);
            for (var tick = 0; tick < 5; tick++)
                simulation.Step(idle);
            Assert.That(world.WaterReserves[0], Is.EqualTo(0.5f).Within(Tolerance));
            for (var tick = 0; tick < 20; tick++)
                simulation.Step(idle);
            Assert.That(world.WaterReserves[0], Is.EqualTo(1f), "never above the capacity");
        }

        [Test]
        public void AShortage_IsSplitInEqualFractionsOfEachAsk_WhateverTheIdsAndOrder()
        {
            foreach (var swapped in new[] { false, true })
            {
                var world = new WorldState(Config(Pond(capacity: 1f, recharge: 0f)));
                var first = world.AddCreature(swapped ? RightShore : LeftShore, 0f, 50f);
                var second = world.AddCreature(swapped ? LeftShore : RightShore, 0f, 50f);
                var left = swapped ? second : first;
                var right = swapped ? first : second;
                left.Hydration = 50f;
                right.Hydration = 50f;
                var simulation = new WorldSimulation(world);
                simulation.Step(new ConstantController(CreatureAction.Idle));
                Assert.That(left.Hydration, Is.EqualTo(50f + 0.5f - 0.05f).Within(Tolerance), $"swapped {swapped}");
                Assert.That(right.Hydration, Is.EqualTo(left.Hydration), "both asked 1 of a reserve of 1");
                Assert.That(world.WaterReserves[0], Is.Zero);
            }

            var uneven = new WorldState(Config(Pond(capacity: 1f, recharge: 0f)));
            var nearlyFull = uneven.AddCreature(LeftShore, 0f, 50f);
            var dry = uneven.AddCreature(RightShore, 0f, 50f);
            nearlyFull.Hydration = 99.6f;
            dry.Hydration = 20f;
            new WorldSimulation(uneven).Step(new ConstantController(CreatureAction.Idle));
            Assert.That(nearlyFull.Hydration, Is.EqualTo(99.6f + 0.4f / 1.4f - 0.05f).Within(Tolerance),
                "it asked only what it lacked (0.4) and got the same fraction of it");
            Assert.That(dry.Hydration, Is.EqualTo(20f + 1f / 1.4f - 0.05f).Within(Tolerance));
        }

        [Test]
        public void AnInexhaustibleSource_NeverChanges_AndAFullCreatureTakesNothing()
        {
            var world = new WorldState(Config(Pond(inexhaustible: true)));
            var drinker = world.AddCreature(LeftShore, 0f, 50f);
            var full = world.AddCreature(RightShore, 0f, 50f);
            drinker.Hydration = 10f;
            var simulation = new WorldSimulation(world);
            simulation.Step(new ConstantController(CreatureAction.Idle));
            Assert.That(world.WaterReserves[0], Is.EqualTo(200f));
            Assert.That(simulation.LastHydration.Drunk, Is.EqualTo(1.0 + 0.0).Within(Tolerance),
                "the full creature lost 0.05 only after drinking, so it asked nothing");
            Assert.That(simulation.LastHydration.DrunkFromInexhaustible, Is.EqualTo(simulation.LastHydration.Drunk));
            Assert.That(simulation.LastHydration.SourceBalance, Is.Zero);
            Assert.That(full.Hydration, Is.EqualTo(100f - 0.05f).Within(Tolerance));
        }

        [Test]
        public void Water_BlocksBodiesFoundersAndFood_AndIsCheckedWithTheConfig()
        {
            var world = new WorldState(Config(Pond()));
            var swimmer = world.AddCreature(new Float2(15f, 22.5f), 0f, 50f);
            var simulation = new WorldSimulation(world);
            for (var tick = 0; tick < 60; tick++)
                simulation.Step(new ConstantController(new CreatureAction(1f, 0f, 0f)));
            Assert.That(PondArea.DistanceTo(swimmer.Position), Is.GreaterThanOrEqualTo(swimmer.Body.Radius - 1e-3f),
                "the water holds the body like an obstacle");

            var settings = new SimulationSettings
            {
                Seed = 9, SolidBodies = true, ArenaWidth = 30f, ArenaHeight = 30f, InitialPopulation = 150,
                InitialFood = 200
            };
            settings.Water = WaterLayouts.Create(WaterLayout.Ponds, settings);
            var config = new SimulationConfig(settings);
            var founded = WorldFactory.CreateInitial(config);
            foreach (var source in config.Water)
            {
                foreach (var creature in founded.Creatures)
                    Assert.That(source.Area.DistanceTo(creature.Position), Is.GreaterThanOrEqualTo(creature.Body.Radius));
                foreach (var food in founded.Food)
                    Assert.That(source.Area.Contains(food.Position), Is.False);
            }

            Assert.That(founded.Creatures.All(c => c.Hydration == config.MaxHydration), Is.True, "founders start full");
            Assert.That(config.FreeSpace.Regions, Is.EqualTo(1));

            Assert.That(() => new SimulationConfig(new SimulationSettings { Water = new[] { Pond() } }),
                Throws.ArgumentException.With.Message.Contains("water needs solid bodies"));
            Assert.That(() => new SimulationConfig(new SimulationSettings
                {
                    SolidBodies = true, Water = new[] { Pond() }, Obstacles = new[] { new Obstacle(24f, 24f, 28f, 28f) }
                }),
                Throws.ArgumentException.With.Message.Contains("overlaps obstacle"));
            Assert.That(() => new SimulationConfig(new SimulationSettings
                {
                    SolidBodies = true, Water = new[] { Pond(capacity: 0f) }
                }),
                Throws.ArgumentException.With.Message.Contains("capacity"));
        }

        [Test]
        public void AChild_GetsTheOffspringShareOfItsParentsWater()
        {
            var settings = new SimulationSettings
            {
                Seed = 13, InitialPopulation = 1, InitialFood = 0, SolidBodies = true, Reproduction = true,
                Water = new[] { Pond() }
            };
            var config = new SimulationConfig(settings);
            var world = new WorldState(config);
            var parent = world.AddCreature(new Float2(40f, 40f), 0f, config.MaxEnergy);
            parent.AgeTicks = config.MaturityTicks;
            parent.Hydration = 80f;
            var simulation = new WorldSimulation(world);
            simulation.Step(new ConstantController(CreatureAction.Idle));
            Assert.That(simulation.LastBirths, Has.Count.EqualTo(1));
            var child = world.Creatures[1];
            // Time takes its share before births, so the parent hands over 40% of 79.95.
            Assert.That(child.Hydration, Is.EqualTo(79.95f * 0.4f).Within(Tolerance));
            Assert.That(parent.Hydration, Is.EqualTo(79.95f * 0.6f).Within(Tolerance));
            Assert.That(simulation.LastHydration.Transferred, Is.EqualTo(79.95 * 0.4).Within(Tolerance));
        }

        [Test]
        public void EveryDrop_IsAccountedFor_InACrowdThatDrinksBreedsAndDies()
        {
            var settings = new SimulationSettings
            {
                Seed = 21, SolidBodies = true, ArenaWidth = 24f, ArenaHeight = 24f, InitialPopulation = 40,
                InitialFood = 30, Reproduction = true, MaturitySeconds = 2f, ReproductionCooldownSeconds = 2f,
                ReproductionEnergyFraction = 0.3f, HydrationLossPerSecond = 3f
            };
            settings.Water = WaterLayouts.Create(WaterLayout.Ponds, settings, capacity: 30f, rechargePerSecond: 5f);
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(new SimulationConfig(settings)));
            var world = simulation.World;
            var total = default(HydrationFlows);
            var births = 0;
            var thirstDeaths = 0;
            for (var tick = 0; tick < 800 && world.Creatures.Count > 0; tick++)
            {
                var creaturesBefore = world.Creatures.Sum(c => (double)c.Hydration);
                var sourcesBefore = world.WaterReserves.Sum(r => (double)r);
                simulation.Step(new SeekFoodController());
                var flows = simulation.LastHydration;
                total.Add(flows);
                births += simulation.LastBirths.Count;
                thirstDeaths += simulation.LastDeaths.Count(d => d.Cause == DeathCause.Dehydration);
                Assert.That(world.Creatures.Sum(c => (double)c.Hydration) - creaturesBefore,
                    Is.EqualTo(flows.CreatureBalance).Within(1e-3), $"creatures, tick {tick}");
                Assert.That(world.WaterReserves.Sum(r => (double)r) - sourcesBefore,
                    Is.EqualTo(flows.SourceBalance).Within(1e-4), $"sources, tick {tick}");
                Assert.That(world.WaterReserves.All(r => r >= 0f && r <= 30f), Is.True);
            }

            TestContext.WriteLine($"drunk {total.Drunk}, recharged {total.Recharged}, births {births}, thirst deaths {thirstDeaths}");
            Assert.That(total.Drunk, Is.GreaterThan(0.0), "the crowd should find the water");
            Assert.That(births, Is.GreaterThan(0));
            Assert.That(thirstDeaths, Is.GreaterThan(0), "fast thirst kills those that do not drink");
            Assert.That(total.Drunk - total.DrunkFromInexhaustible, Is.EqualTo(total.TakenFromSources).Within(1e-3));
        }

        [Test]
        public void ARunWithWater_ResumesExactly_AndOnlyRunsWithWaterWriteIt()
        {
            var settings = new SimulationSettings
            {
                Seed = 6, ArenaWidth = 20f, ArenaHeight = 20f, InitialPopulation = 20, InitialFood = 20, SolidBodies = true,
                HydrationLossPerSecond = 2f
            };
            settings.Water = WaterLayouts.Create(WaterLayout.Pond, settings, capacity: 20f, rechargePerSecond: 1f);
            var config = new SimulationConfig(settings);
            var original = new WorldSimulation(WorldFactory.CreateInitial(config));
            var brains = BrainController.Shared(config, new Eden.Brains.HeuristicBrain());
            Run(original, brains, 80);
            var saved = original.Capture();
            var savedBrains = brains.Capture();
            Run(original, brains, 120);

            var text = CheckpointCodec.Encode(new Checkpoint("shared", "minimal", 6, "heuristic", null,
                new SharedWorldSnapshot(settings, saved, savedBrains)), "0.1.0", "test", new DateTime(2026, 10, 29, 0, 0, 0,
                DateTimeKind.Utc));
            Assert.That(text, Does.Contain("\"water\""));
            Assert.That(text, Does.Contain("\"waterReserves\""));
            Assert.That(text, Does.Contain("\"hydration\""));
            Assert.That(text, Does.Contain("\"hydrationLossPerSecond\": 2"));
            var loaded = CheckpointCodec.Decode(text, out _).SharedWorld;
            Assert.That(loaded.Settings.Water, Is.EqualTo(settings.Water));
            var restored = WorldSimulation.Restore(new SimulationConfig(loaded.Settings), loaded.World);
            var restoredBrains = BrainController.Shared(config, new Eden.Brains.HeuristicBrain());
            restoredBrains.Restore(loaded.Brains);
            Run(restored, restoredBrains, 120);
            Assert.That(restored.ComputeFingerprint(), Is.EqualTo(original.ComputeFingerprint()));

            var plain = new SimulationSettings { Seed = 6, InitialPopulation = 4, InitialFood = 4 };
            var plainText = CheckpointCodec.Encode(new Checkpoint("shared", "minimal", 6, "heuristic", null,
                new SharedWorldSnapshot(plain, new WorldSimulation(WorldFactory.CreateInitial(new SimulationConfig(plain)))
                    .Capture(), Array.Empty<BrainSlotState>())), "0.1.0", "test", DateTime.UtcNow);
            Assert.That(plainText, Does.Not.Contain("water").IgnoreCase.And.Not.Contain("ydration"));

            var describe = RunInfo.Describe(config, "heuristic-v1", "0.1.0", DateTime.UtcNow);
            Assert.That(describe, Has.Some.StartsWith("water=1;"));
            Assert.That(describe, Does.Contain("water_supply=finite"));
        }

        private static void Run(WorldSimulation simulation, ICreatureController controller, int ticks)
        {
            for (var i = 0; i < ticks; i++)
                simulation.Step(controller);
        }

        private static WaterSource Pond(float capacity = 200f, float recharge = 2f, bool inexhaustible = false) =>
            new WaterSource(PondArea, capacity, recharge, inexhaustible);

        private static SimulationConfig Config(params WaterSource[] water) =>
            new SimulationConfig(new SimulationSettings
            {
                Seed = 5, InitialPopulation = 0, InitialFood = 0, SolidBodies = true, Water = water
            });
    }
}
