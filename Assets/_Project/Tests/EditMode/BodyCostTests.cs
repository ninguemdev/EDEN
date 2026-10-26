using System;
using System.Collections.Generic;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>Rules of the body genes: no trait is better without a cost, and the costs are paid in the world.</summary>
    public class BodyCostTests
    {
        private static readonly SimulationConfig Config = SimulationConfig.CreateDefault(1);

        [Test]
        public void EachGene_TradesItsBenefitForACost()
        {
            var reference = CreatureBody.Reference(Config);
            foreach (var amount in new[] { 1f, 0.3f })
            {
                var big = CreatureBody.Express(Config, new BodyGenes(amount, 0f, 0f, 0f));
                Assert.That(big.EatRadius, Is.GreaterThan(reference.EatRadius));
                Assert.That(big.MaxEnergy, Is.GreaterThan(reference.MaxEnergy));
                Assert.That(big.MetabolismPerSecond, Is.GreaterThan(reference.MetabolismPerSecond), "size costs metabolism");
                Assert.That(big.MovementCostPerSecond, Is.GreaterThan(reference.MovementCostPerSecond), "and movement");

                var fast = CreatureBody.Express(Config, new BodyGenes(0f, amount, 0f, 0f));
                Assert.That(fast.MaxSpeed, Is.GreaterThan(reference.MaxSpeed));
                Assert.That(fast.MovementCostPerSecond, Is.GreaterThan(reference.MovementCostPerSecond), "speed costs movement");

                var farSighted = CreatureBody.Express(Config, new BodyGenes(0f, 0f, amount, 0f));
                Assert.That(farSighted.SensorRange, Is.GreaterThan(reference.SensorRange));
                Assert.That(farSighted.SensorCostPerSecond, Is.GreaterThan(reference.SensorCostPerSecond), "range costs sensing");

                var thrifty = CreatureBody.Express(Config, new BodyGenes(0f, 0f, 0f, amount));
                Assert.That(thrifty.MetabolismPerSecond, Is.LessThan(reference.MetabolismPerSecond));
                Assert.That(thrifty.MaxAcceleration, Is.LessThan(reference.MaxAcceleration), "thrift costs power");
            }
        }

        [Test]
        public void NoBody_IsAtLeastAsGoodAsTheReferenceInEverything()
        {
            var reference = CreatureBody.Reference(Config);
            var random = new Pcg32(11, 3);
            var bodies = new List<BodyGenes>();
            for (var i = 0; i < 5000; i++)
                bodies.Add(BodyGenes.CreateRandom(random));
            for (var g = 0; g < BodyGenes.Count; g++)
            {
                foreach (var value in new[] { -1f, -0.01f, 0.01f, 1f })
                {
                    var genes = new float[BodyGenes.Count];
                    genes[g] = value;
                    bodies.Add(new BodyGenes(genes[0], genes[1], genes[2], genes[3]));
                }
            }

            foreach (var genes in bodies)
            {
                var body = CreatureBody.Express(Config, genes);
                Assert.That(Dominates(body, reference), Is.False, $"{genes} would be better at no cost");
                Assert.That(Dominates(reference, body), Is.False, $"{genes} would be worse with no gain");
            }
        }

        [Test]
        public void ACostExponentOfZero_MakesThatTraitFree()
        {
            var free = new SimulationConfig(new SimulationSettings { SpeedCostExponent = 0f });
            var fast = CreatureBody.Express(free, new BodyGenes(0f, 1f, 0f, 0f));
            Assert.That(fast.MovementCostPerSecond, Is.EqualTo(free.MovementCostPerSecond));
            Assert.That(Dominates(fast, CreatureBody.Reference(free)), Is.True, "only for controlled experiments");

            var steep = new SimulationConfig(new SimulationSettings { SensorCostExponent = 4f });
            Assert.That(CreatureBody.Express(steep, new BodyGenes(0f, 0f, 1f, 0f)).SensorCostPerSecond,
                Is.EqualTo(steep.SensorCostPerSecond * 1.5f * 1.5f * 1.5f * 1.5f).Within(1e-6f));
            Assert.Throws<ArgumentException>(() => new SimulationConfig(new SimulationSettings { SizeCostExponent = -0.1f }));
            Assert.Throws<ArgumentException>(() => new SimulationConfig(new SimulationSettings
            {
                EfficiencyPowerExponent = SimulationConfig.MaxCostExponent + 0.1f
            }));
        }

        [Test]
        public void Costs_ArePaidInTheWorld()
        {
            var idle = CreatureAction.Idle;
            var push = new CreatureAction(1f, 0f, 0f);
            var reference = EnergySpentInOneTick(BodyGenes.Neutral, idle);

            Assert.That(EnergySpentInOneTick(new BodyGenes(1f, 0f, 0f, 0f), idle), Is.GreaterThan(reference), "a big body");
            Assert.That(EnergySpentInOneTick(new BodyGenes(-1f, 0f, 0f, 0f), idle), Is.LessThan(reference), "a small one");
            Assert.That(EnergySpentInOneTick(new BodyGenes(0f, 0f, 1f, 0f), idle), Is.GreaterThan(reference), "a long sensor");
            Assert.That(EnergySpentInOneTick(new BodyGenes(0f, 0f, 0f, 1f), idle), Is.LessThan(reference), "a thrifty one");
            Assert.That(EnergySpentInOneTick(new BodyGenes(0f, 1f, 0f, 0f), push),
                Is.GreaterThan(EnergySpentInOneTick(BodyGenes.Neutral, push)), "a fast body pushing");

            // The thrifty body's price: after a second of full thrust from rest it is still slower.
            Assert.That(SpeedAfterPushing(new BodyGenes(0f, 0f, 0f, 1f), 20), Is.LessThan(SpeedAfterPushing(BodyGenes.Neutral, 20)));
            Assert.That(SpeedAfterPushing(new BodyGenes(0f, 0f, 0f, -1f), 10), Is.GreaterThan(SpeedAfterPushing(BodyGenes.Neutral, 10)));
        }

        [Test]
        public void EnergyIsHeldUpToEachBodysOwnCapacity()
        {
            var world = TestWorlds.Empty();
            var config = world.Config;
            var big = world.AddCreature(new Float2(10f, 10f), 0f, 145f, new BodyGenes(1f, 0f, 0f, 0f));
            var plain = world.AddCreature(new Float2(30f, 10f), 0f, 95f);
            var small = world.AddCreature(new Float2(10f, 30f), 0f, 45f, new BodyGenes(-1f, 0f, 0f, 0f));
            world.AddFood(new Float2(10.5f, 10f), config.FoodEnergy);
            world.AddFood(new Float2(30.5f, 10f), config.FoodEnergy);
            world.AddFood(new Float2(10.2f, 30f), config.FoodEnergy);

            new WorldSimulation(world).Step(new ConstantController(CreatureAction.Idle));
            Assert.That(big.Energy, Is.EqualTo(big.Body.MaxEnergy), "150 for the largest body");
            Assert.That(big.Energy, Is.GreaterThan(config.MaxEnergy));
            Assert.That(plain.Energy, Is.EqualTo(config.MaxEnergy));
            Assert.That(small.Energy, Is.EqualTo(small.Body.MaxEnergy), "50 for the smallest");
            Assert.That(big.FoodEnergyEaten + plain.FoodEnergyEaten + small.FoodEnergyEaten, Is.EqualTo(3f * config.FoodEnergy),
                "what was eaten is still counted in full");
        }

        // Better or equal in every trait and strictly better in one. Higher is better for abilities, lower for costs.
        private static bool Dominates(CreatureBody a, CreatureBody b)
        {
            var abilities = new[]
            {
                (a.EatRadius, b.EatRadius), (a.MaxEnergy, b.MaxEnergy), (a.MaxSpeed, b.MaxSpeed),
                (a.MaxAcceleration, b.MaxAcceleration), (a.SensorRange, b.SensorRange)
            };
            var costs = new[]
            {
                (a.MetabolismPerSecond, b.MetabolismPerSecond), (a.MovementCostPerSecond, b.MovementCostPerSecond),
                (a.SensorCostPerSecond, b.SensorCostPerSecond)
            };
            var strictly = false;
            foreach (var (mine, theirs) in abilities)
            {
                if (mine < theirs)
                    return false;
                strictly |= mine > theirs;
            }

            foreach (var (mine, theirs) in costs)
            {
                if (mine > theirs)
                    return false;
                strictly |= mine < theirs;
            }

            return strictly;
        }

        private static float EnergySpentInOneTick(BodyGenes genes, CreatureAction action)
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(25f, 25f), 0f, 40f, genes);
            new WorldSimulation(world).Step(new ConstantController(action));
            return 40f - creature.Energy;
        }

        private static float SpeedAfterPushing(BodyGenes genes, int ticks)
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(5f, 25f), 0f, 40f, genes);
            var simulation = new WorldSimulation(world);
            var push = new ConstantController(new CreatureAction(1f, 0f, 0f));
            for (var i = 0; i < ticks; i++)
                simulation.Step(push);
            return MathF.Sqrt(creature.Velocity.X * creature.Velocity.X + creature.Velocity.Y * creature.Velocity.Y);
        }
    }
}
