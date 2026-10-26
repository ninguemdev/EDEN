using System;
using System.Linq;
using Eden.Application;
using Eden.Brains;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>
    /// The shared world with reproduction as an ecosystem: energy and food accounts close over long runs with births,
    /// deaths and refused births, creatures compete for the same food, and an extinct world stays empty.
    /// </summary>
    public class EnergyAuditTests
    {
        [Test]
        public void LongRunWithBirthsAndDeaths_EnergyAndFoodAccountsClose()
        {
            var config = new SimulationConfig(Ecosystem(food: 48, cap: 24));
            var world = new WorldSimulation(WorldFactory.CreateInitial(config));
            var brains = BrainController.Shared(config, new HeuristicBrain());
            var total = new EnergyFlows();
            var births = 0;
            var refused = 0;
            var starved = 0;
            var aged = 0;
            long respawned = 0;
            long eaten = 0;
            var start = Energy(world.World);

            for (var t = 0; t < 6000 && !world.IsExtinct; t++)
            {
                var before = Energy(world.World);
                world.Step(brains);
                brains.Forget(world.LastDeaths);
                var flows = world.LastEnergy;
                Assert.That(Energy(world.World) - before, Is.EqualTo(flows.Balance).Within(1e-4),
                    $"tick {t}: creature energy changes only by the recorded flows");
                Assert.That(flows.Food, Is.EqualTo(world.LastFoodEaten * (double)config.FoodEnergy).Within(1e-6));
                Assert.That(flows.Spent, Is.GreaterThanOrEqualTo(0));
                Assert.That(flows.Overflow, Is.GreaterThanOrEqualTo(0));
                total.Add(flows);
                births += world.LastBirths.Count;
                refused += world.LastBirthsRefused;
                starved += world.LastDeaths.Count(d => d.Cause == DeathCause.Starvation);
                aged += world.LastDeaths.Count(d => d.Cause == DeathCause.OldAge);
                respawned += world.LastFoodRespawned;
                eaten += world.LastFoodEaten;
                Assert.That(world.World.Creatures.Count, Is.LessThanOrEqualTo(config.PopulationCap));
            }

            TestContext.Out.WriteLine(FormattableString.Invariant(
                $"ticks {world.World.Tick}, births {births}, refused {refused}, starved {starved}, old age {aged}, ") +
                FormattableString.Invariant(
                    $"food {total.Food:0.###}, spent {total.Spent:0.###}, overflow {total.Overflow:0.###}, ") +
                FormattableString.Invariant(
                    $"transferred {total.Transferred:0.###}, birth costs {total.BirthCosts:0.###}, ") +
                FormattableString.Invariant(
                    $"carried away {total.CarriedAwayByDeaths:0.###}, balance {total.Balance:0.######}, ") +
                FormattableString.Invariant($"measured {Energy(world.World) - start:0.######}"));
            Assert.That(births, Is.GreaterThan(50), "a living ecosystem, not a quiet one");
            Assert.That(refused, Is.GreaterThan(0), "the cap was reached");
            Assert.That(starved, Is.GreaterThan(0));
            Assert.That(aged, Is.GreaterThan(0));
            Assert.That(total.BirthCosts, Is.EqualTo(births * (double)config.ReproductionCost).Within(1e-6));
            // Only float rounding is left: about 0.02 of 15 830 energy of food in this run.
            Assert.That(Energy(world.World) - start, Is.EqualTo(total.Balance).Within(0.1),
                "over the whole run: food − costs − overflow − births − deaths");
            Assert.That(world.World.Food.Count, Is.EqualTo(config.InitialFood + respawned - eaten),
                "every food item is eaten at most once and only respawns come back");
            Assert.That(eaten - respawned, Is.LessThanOrEqualTo(config.InitialFood));
        }

        [Test]
        public void ScarceFood_SustainsFewerCreaturesThanAbundantFood()
        {
            var scarce = MeanPopulation(Ecosystem(food: 30, cap: 120));
            var abundant = MeanPopulation(Ecosystem(food: 120, cap: 120));
            TestContext.Out.WriteLine(FormattableString.Invariant(
                $"mean population after 50 s: 30 food {scarce:0.0}, 120 food {abundant:0.0}"));
            Assert.That(scarce, Is.GreaterThan(5), "scarce food still sustains a population");
            Assert.That(scarce, Is.LessThan(abundant * 0.75), $"competition for food: {scarce:0.0} vs {abundant:0.0}");
        }

        [Test]
        public void OneFoodItem_GoesToTheClosestOfManyCompetitors_AndOnlyOnce()
        {
            var world = TestWorlds.Empty(s => { s.MetabolismPerSecond = 0f; s.SensorCostPerSecond = 0f; });
            var config = world.Config;
            var item = world.AddFood(new Float2(20f, 20f), config.FoodEnergy);
            var far = world.AddCreature(new Float2(20.6f, 20f), 0f, 50f);
            var closest = world.AddCreature(new Float2(20f, 20.3f), 0f, 50f);
            var big = world.AddCreature(new Float2(19f, 20f), 0f, 50f, new BodyGenes(1f, 0f, 0f, 0f));
            var tie = world.AddCreature(new Float2(20f, 19.7f), 0f, 50f);
            var simulation = new WorldSimulation(world);
            simulation.Step(new ConstantController(CreatureAction.Idle));

            Assert.That(simulation.LastFoodEaten, Is.EqualTo(1));
            Assert.That(closest.FoodEnergyEaten, Is.EqualTo(config.FoodEnergy), "closest, and the lower id of a tie");
            Assert.That(far.FoodEnergyEaten + big.FoodEnergyEaten + tie.FoodEnergyEaten, Is.Zero,
                "a larger reach does not beat a closer competitor");
            Assert.That(simulation.LastEnergy.Food, Is.EqualTo(config.FoodEnergy));
            Assert.That(world.Food, Has.No.Member(item));
        }

        [Test]
        public void AnExtinctWorld_StaysEmpty_NothingIsPutBack()
        {
            var settings = Ecosystem(food: 0, cap: 30);
            var config = new SimulationConfig(settings);
            var world = new WorldSimulation(WorldFactory.CreateInitial(config));
            var brains = BrainController.Shared(config, new RandomBrain());
            var ticks = 0;
            for (; ticks < 20000 && !world.IsExtinct; ticks++)
            {
                world.Step(brains);
                brains.Forget(world.LastDeaths);
            }

            Assert.That(world.IsExtinct, Is.True);
            var nextId = world.World.NextCreatureId;
            for (var t = 0; t < 400; t++)
            {
                world.Step(brains);
                Assert.That(world.World.Creatures, Is.Empty);
                Assert.That(world.LastBirths, Is.Empty);
                Assert.That(world.LastEnergy.Balance, Is.Zero);
            }

            Assert.That(world.World.NextCreatureId, Is.EqualTo(nextId), "no creature is created after extinction");
        }

        // Short lives, quick maturity and a low threshold: births, deaths of both kinds and a reachable cap.
        private static SimulationSettings Ecosystem(int food, int cap) =>
            new SimulationSettings
            {
                Seed = 12, InitialPopulation = 16, InitialFood = food, FoodRespawnSeconds = 3f, MaxAgeSeconds = 45f,
                Reproduction = true, PopulationCap = cap, MaturitySeconds = 5f, ReproductionEnergyFraction = 0.45f,
                ReproductionCooldownSeconds = 6f
            };

        private static double MeanPopulation(SimulationSettings settings)
        {
            var config = new SimulationConfig(settings);
            var world = new WorldSimulation(WorldFactory.CreateInitial(config));
            var brains = BrainController.Shared(config, new HeuristicBrain());
            double sum = 0;
            var samples = 0;
            for (var t = 0; t < 4000 && !world.IsExtinct; t++)
            {
                world.Step(brains);
                brains.Forget(world.LastDeaths);
                if (t < 1000)
                    continue;
                sum += world.World.Creatures.Count;
                samples++;
            }

            return samples == 0 ? 0 : sum / samples;
        }

        private static double Energy(WorldState world) => world.Creatures.Sum(c => (double)c.Energy);
    }
}
