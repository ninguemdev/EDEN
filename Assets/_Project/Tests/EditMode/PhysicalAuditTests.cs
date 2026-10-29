using System;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Persistence;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>
    /// The audit of the physical arena (P2.9): with solid bodies, obstacles, finite water, eyes and births, every input
    /// and output of energy and water is explained (food that appeared, absorbed and wasted; maintenance, sensor, effort,
    /// eyes, turning; births and deaths; drinking, thirst, recharge) and the balances close within float rounding; the
    /// window metrics count contacts, births refused for room and delayed respawns.
    /// </summary>
    public class PhysicalAuditTests
    {
        [Test]
        public void EveryBalance_Closes_WithSolidBodiesObstaclesWaterEyesAndBirths()
        {
            var config = new SimulationConfig(Crowded());
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(config));
            foreach (var creature in simulation.World.Creatures.Where(c => c.Id.Value % 2 == 0))
                creature.Eyes = EyeSet.Founder;
            var brains = BrainController.Shared(config, new HeuristicBrain());
            var audit = new WorldAudit(simulation.World);
            var metrics = new MetricsRecorder(config, config.TicksPerSecond);
            long contacts = 0, blocked = 0, delayed = 0, windowContacts = 0, windowBlocked = 0, windowDelayed = 0;
            var windows = 0;

            for (var t = 0; t < 2400 && !simulation.IsExtinct; t++)
            {
                var energyBefore = WorldAudit.CreatureEnergy(simulation.World);
                var foodBefore = WorldAudit.FoodEnergy(simulation.World);
                var waterBefore = WorldAudit.LimitedWater(simulation.World);
                simulation.Step(brains);
                brains.Forget(simulation.LastDeaths);
                audit.Add(simulation);
                var flows = simulation.LastEnergy;
                Assert.That(WorldAudit.CreatureEnergy(simulation.World) - energyBefore,
                    Is.EqualTo(flows.Balance).Within(1e-3), $"tick {t}: creature energy");
                Assert.That(WorldAudit.FoodEnergy(simulation.World) - foodBefore,
                    Is.EqualTo(flows.FoodStockBalance).Within(1e-6), $"tick {t}: food in the world");
                Assert.That(WorldAudit.LimitedWater(simulation.World) - waterBefore,
                    Is.EqualTo(simulation.LastHydration.SourceBalance).Within(1e-3), $"tick {t}: water in the sources");
                Assert.That(flows.Unpaid, Is.GreaterThanOrEqualTo(-1e-6), "nobody pays more than it was charged");
                contacts += simulation.LastContacts;
                blocked += simulation.LastBirthsBlocked;
                delayed += simulation.LastFoodRespawnsDelayed;
                if (!metrics.Record(simulation))
                    continue;
                windows++;
                windowContacts += (long)Math.Round(metrics.LastSample.ContactsPerTick * config.TicksPerSecond);
                windowBlocked += metrics.LastSample.BirthsBlockedInWindow;
                windowDelayed += metrics.LastSample.RespawnsDelayedInWindow;
            }

            var e = audit.Energy;
            var h = audit.Hydration;
            TestContext.Out.WriteLine(FormattableString.Invariant(
                $"ticks {audit.Ticks}: appeared {e.FoodAppeared:0.#}, eaten {e.Food:0.#}, absorbed {e.Absorbed:0.#}, ") +
                FormattableString.Invariant(
                    $"wasted {e.Overflow:0.##}, charged {e.Charged:0.#} = maintenance {e.Maintenance:0.#} + sensor ") +
                FormattableString.Invariant(
                    $"{e.SensorCosts:0.#} + effort {e.Effort:0.#} + eyes {e.EyeCosts:0.#} + turning {e.TurnCosts:0.#}, ") +
                FormattableString.Invariant(
                    $"unpaid {e.Unpaid:0.##}, births {e.BirthCosts:0.#}, drunk {h.Drunk:0.#}, lost {h.Lost:0.#}, ") +
                FormattableString.Invariant(
                    $"contacts {contacts}, blocked {blocked}, delayed {delayed}, largest error {audit.LargestError(simulation.World):0.######}"));

            Assert.That(audit.Ticks, Is.EqualTo(2400), "the arena did not die out");
            Assert.That(e.FoodAppeared, Is.GreaterThan(0), "food came back");
            Assert.That(e.Maintenance, Is.GreaterThan(0));
            Assert.That(e.SensorCosts, Is.GreaterThan(0), "the legacy half pays its sensor");
            Assert.That(e.EyeCosts, Is.GreaterThan(0), "the eyed half pays its eyes");
            Assert.That(e.Effort, Is.GreaterThan(0));
            Assert.That(e.TurnCosts, Is.GreaterThan(0));
            Assert.That(e.BirthCosts, Is.GreaterThan(0), "there were births");
            Assert.That(h.Drunk, Is.GreaterThan(0));
            Assert.That(h.Recharged, Is.GreaterThan(0));
            Assert.That(contacts, Is.GreaterThan(0), "bodies touched");

            // Only float rounding is left, against thousands of units that flowed.
            Assert.That(audit.CreatureEnergyError(simulation.World), Is.EqualTo(0).Within(0.1));
            Assert.That(audit.FoodEnergyError(simulation.World), Is.EqualTo(0).Within(1e-6));
            Assert.That(audit.HydrationError(simulation.World), Is.EqualTo(0).Within(0.1));
            Assert.That(audit.WaterError(simulation.World), Is.EqualTo(0).Within(0.1));
            Assert.That(audit.CostSplitError, Is.EqualTo(0).Within(0.01),
                "maintenance + sensor + effort + eyes + turning = what was charged");

            Assert.That(windows, Is.EqualTo(120));
            Assert.That(windowContacts, Is.EqualTo(contacts), "contacts per tick × ticks of each window");
            Assert.That(windowBlocked, Is.EqualTo(blocked));
            Assert.That(windowDelayed, Is.EqualTo(delayed));
            var row = MetricsCsvLog.FormatRow(metrics.LastSample).Split(',');
            Assert.That(row.Length, Is.EqualTo(MetricsCsvLog.Header.Split(',').Length));
        }

        [Test]
        public void TheAudit_StartsFromTheWorldItIsGiven_AndCountsWhatNobodyCouldPay()
        {
            var world = TestWorlds.Empty(s => s.MetabolismPerSecond = 40f);
            world.AddCreature(new Float2(10f, 10f), 0f, 1f);
            world.AddFood(new Float2(40f, 40f), 10f);
            var simulation = new WorldSimulation(world);
            var audit = new WorldAudit(world);
            simulation.Step(new ConstantController(CreatureAction.Idle));
            audit.Add(simulation);

            var e = audit.Energy;
            Assert.That(e.Charged, Is.EqualTo((40.0 + 0.25) / 20.0).Within(1e-6), "metabolism + sensor for a tick");
            Assert.That(e.Spent, Is.EqualTo(1.0).Within(1e-6), "it held only 1");
            Assert.That(e.Unpaid, Is.EqualTo(e.Charged - 1.0).Within(1e-6));
            Assert.That(e.CarriedAwayByDeaths, Is.Zero, "it starved");
            Assert.That(world.Creatures, Is.Empty);
            Assert.That(audit.LargestError(world), Is.LessThan(1e-6));
        }

        private static SimulationSettings Crowded()
        {
            var settings = new SimulationSettings
            {
                Seed = 21, ArenaWidth = 30f, ArenaHeight = 30f, InitialPopulation = 48, InitialFood = 60,
                SolidBodies = true, Reproduction = true, PopulationCap = 90, MaturitySeconds = 8f,
                ReproductionCooldownSeconds = 6f, HydrationLossPerSecond = 0.2f
            };
            settings.Obstacles = ObstacleLayouts.Create(ObstacleLayout.Pillars, settings);
            settings.Water = WaterLayouts.Create(WaterLayout.Ponds, settings, capacity: 60f, rechargePerSecond: 1f);
            return settings;
        }
    }
}
