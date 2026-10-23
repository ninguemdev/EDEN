using System;
using System.Linq;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class WorldSimulationTests
    {
        private const float Tolerance = 1e-5f;
        private static readonly ICreatureController Idle = new ConstantController(CreatureAction.Idle);

        [Test]
        public void FullThrust_AcceleratesButNeverExceedsMaxSpeed()
        {
            var world = TestWorlds.Empty(s => { s.ArenaWidth = 200f; s.ArenaHeight = 200f; });
            var creature = world.AddCreature(new Float2(10f, 100f), 0f, 100f);
            var simulation = new WorldSimulation(world);
            var thrust = new ConstantController(new CreatureAction(1f, 0f, 0f));

            simulation.Step(thrust);
            Assert.That(creature.Velocity.X, Is.EqualTo(0.475f).Within(Tolerance), "(a·dt)·(1 - damping·dt)");

            var fastest = 0f;
            for (var i = 0; i < 60; i++)
            {
                simulation.Step(thrust);
                fastest = MathF.Max(fastest, Speed(creature));
            }

            Assert.That(fastest, Is.LessThanOrEqualTo(world.Config.MaxSpeed + Tolerance));
            Assert.That(Speed(creature), Is.EqualTo(world.Config.MaxSpeed).Within(1e-3f));
            Assert.That(creature.Position.Y, Is.EqualTo(100f));
        }

        [Test]
        public void Turning_UsesMaxTurnRateAndWrapsHeading()
        {
            var world = TestWorlds.Empty();
            var left = world.AddCreature(new Float2(25f, 25f), 0f, 50f);
            var simulation = new WorldSimulation(world);

            simulation.Step(new ConstantController(new CreatureAction(0f, 1f, 0f)));
            Assert.That(left.Heading, Is.EqualTo(MathF.PI * 0.05f).Within(Tolerance));

            simulation.Step(new ConstantController(new CreatureAction(0f, -1f, 0f)));
            simulation.Step(new ConstantController(new CreatureAction(0f, -1f, 0f)));
            Assert.That(left.Heading, Is.EqualTo(2f * MathF.PI - MathF.PI * 0.05f).Within(1e-4f));
            Assert.That(left.Heading, Is.GreaterThanOrEqualTo(0f).And.LessThan(2f * MathF.PI));
        }

        [Test]
        public void Damping_SlowsAnIdleCreature()
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(25f, 25f), 0f, 50f);
            creature.Velocity = new Float2(4f, 0f);

            new WorldSimulation(world).Step(Idle);

            Assert.That(creature.Velocity.X, Is.EqualTo(3.8f).Within(Tolerance));
            Assert.That(creature.Position.X, Is.EqualTo(25f + 3.8f * 0.05f).Within(Tolerance));
        }

        [Test]
        public void Strafe_PushesToTheLeftOfTheHeading()
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(25f, 25f), 0f, 50f);

            new WorldSimulation(world).Step(new ConstantController(new CreatureAction(0f, 0f, 1f)));

            Assert.That(creature.Velocity.Y, Is.GreaterThan(0f));
            Assert.That(creature.Velocity.X, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void ArenaBorders_StopCreaturesAtTheWall()
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(49.9f, 25f), 0f, 100f);
            var simulation = new WorldSimulation(world);

            for (var i = 0; i < 20; i++)
                simulation.Step(new ConstantController(new CreatureAction(1f, 0f, 0f)));

            Assert.That(creature.Position.X, Is.EqualTo(world.Config.ArenaWidth));
            Assert.That(creature.Velocity.X, Is.EqualTo(0f));
            Assert.That(creature.Position.Y, Is.EqualTo(25f));
        }

        [Test]
        public void ClosestCreatureEats_AndEnergyIsGainedOnce()
        {
            var world = TestWorlds.Empty();
            var far = world.AddCreature(new Float2(10.6f, 10f), 0f, 50f);
            var near = world.AddCreature(new Float2(10.2f, 10f), 0f, 50f);
            world.AddFood(new Float2(10f, 10f), 10f);
            var simulation = new WorldSimulation(world);

            simulation.Step(Idle);

            Assert.That(near.FoodEnergyEaten, Is.EqualTo(10f));
            Assert.That(far.FoodEnergyEaten, Is.EqualTo(0f));
            Assert.That(simulation.LastFoodEaten, Is.EqualTo(1));
            Assert.That(world.Food, Is.Empty);
            Assert.That(near.Energy - far.Energy, Is.EqualTo(10f).Within(Tolerance));
        }

        [Test]
        public void EqualDistance_GoesToTheLowerId()
        {
            var world = TestWorlds.Empty();
            var first = world.AddCreature(new Float2(9.5f, 10f), 0f, 50f);
            var second = world.AddCreature(new Float2(10.5f, 10f), 0f, 50f);
            var stacked = world.AddCreature(new Float2(9.5f, 10f), 0f, 50f);
            world.AddFood(new Float2(10f, 10f), 10f);

            new WorldSimulation(world).Step(Idle);

            Assert.That(first.FoodEnergyEaten, Is.EqualTo(10f));
            Assert.That(second.FoodEnergyEaten + stacked.FoodEnergyEaten, Is.EqualTo(0f));
        }

        [Test]
        public void FoodOutsideEatRadius_IsNotEaten()
        {
            var world = TestWorlds.Empty();
            world.AddCreature(new Float2(11f, 10f), 0f, 50f);
            world.AddFood(new Float2(10f, 10f), 10f);

            var simulation = new WorldSimulation(world);
            simulation.Step(Idle);

            Assert.That(simulation.LastFoodEaten, Is.EqualTo(0));
            Assert.That(world.Food, Has.Count.EqualTo(1));
        }

        [Test]
        public void EatenFood_RespawnsAfterTheConfiguredDelay()
        {
            var world = TestWorlds.Empty();
            world.AddCreature(new Float2(10f, 10f), 0f, 100f);
            world.AddFood(new Float2(10f, 10f), 10f);
            var simulation = new WorldSimulation(world);

            simulation.Step(Idle);
            Assert.That(simulation.SnapshotAgenda().Single().DueTick, Is.EqualTo(world.Config.FoodRespawnTicks));

            while (world.Tick < world.Config.FoodRespawnTicks)
                simulation.Step(Idle);
            Assert.That(world.Food, Is.Empty, "not before the due tick");

            simulation.Step(Idle);
            var respawned = world.Food.Single();
            Assert.That(respawned.Id.Value, Is.EqualTo(2));
            Assert.That(respawned.Energy, Is.EqualTo(world.Config.FoodEnergy));
            Assert.That(respawned.Position.X, Is.InRange(0f, world.Config.ArenaWidth));
            Assert.That(respawned.Position.Y, Is.InRange(0f, world.Config.ArenaHeight));
        }

        [Test]
        public void EnergyCost_FollowsMetabolismMovementAndSensorTerms()
        {
            var world = TestWorlds.Empty();
            var idle = world.AddCreature(new Float2(10f, 10f), 0f, 50f);
            var moving = world.AddCreature(new Float2(30f, 30f), 0f, 50f);
            var controller = new ByIdController(idle.Id, CreatureAction.Idle, new CreatureAction(1f, 0f, 1f));

            new WorldSimulation(world).Step(controller);

            var config = world.Config;
            var baseCost = config.TickDuration * (config.MetabolismPerSecond + config.SensorCostPerSecond);
            Assert.That(idle.Energy, Is.EqualTo(50f - baseCost).Within(Tolerance));
            Assert.That(moving.Energy, Is.EqualTo(50f - baseCost - config.TickDuration * config.MovementCostPerSecond * 2f)
                .Within(Tolerance));
        }

        [Test]
        public void Energy_IsClampedToTheMaximum()
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(10f, 10f), 0f, 99f);
            world.AddFood(new Float2(10f, 10f), 10f);

            new WorldSimulation(world).Step(Idle);

            Assert.That(creature.Energy, Is.EqualTo(world.Config.MaxEnergy));
            Assert.That(creature.FoodEnergyEaten, Is.EqualTo(10f));
        }

        [Test]
        public void Starvation_KillsAtZeroWithoutNegativeEnergy()
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(10f, 10f), 0f, 0.2f);
            var simulation = new WorldSimulation(world);

            while (creature.IsAlive)
            {
                simulation.Step(Idle);
                Assert.That(creature.Energy, Is.GreaterThanOrEqualTo(0f));
            }

            Assert.That(creature.Energy, Is.EqualTo(0f));
            Assert.That(creature.DeathCause, Is.EqualTo(DeathCause.Starvation));
            Assert.That(world.Tick, Is.EqualTo(4), "0.2 energy at 0.0625 per tick");
            Assert.That(simulation.LastDeaths.Single().Creature, Is.EqualTo(creature.Id));
            Assert.That(simulation.LastDeaths.Single().Tick, Is.EqualTo(3));
            Assert.That(simulation.IsExtinct, Is.True);
        }

        [Test]
        public void OldAge_KillsAtMaxAgeTicks()
        {
            var world = TestWorlds.Empty(s => s.MaxAgeSeconds = 1f);
            var creature = world.AddCreature(new Float2(10f, 10f), 0f, 50f);
            var simulation = new WorldSimulation(world);

            for (var i = 0; i < 19; i++)
                simulation.Step(Idle);
            Assert.That(creature.IsAlive, Is.True);

            simulation.Step(Idle);
            Assert.That(creature.DeathCause, Is.EqualTo(DeathCause.OldAge));
            Assert.That(simulation.LastDeaths.Single().Tick, Is.EqualTo(19));
            Assert.That(world.Creatures, Is.Empty);
        }

        [Test]
        public void Extinction_DoesNotStopTheClock()
        {
            var world = WorldFactory.CreateInitial(new SimulationConfig(new SimulationSettings
            {
                Seed = 5, InitialPopulation = 8, InitialFood = 0, InitialEnergy = 1f
            }));
            var simulation = new WorldSimulation(world);
            var deaths = 0;

            for (var i = 0; i < 300; i++)
            {
                simulation.Step(new SeekFoodController());
                deaths += simulation.LastDeaths.Count;
            }

            Assert.That(simulation.IsExtinct, Is.True);
            Assert.That(deaths, Is.EqualTo(8));
            Assert.That(world.Tick, Is.EqualTo(300));
        }

        [Test]
        public void Controller_IsCalledOncePerCreatureInIdOrder()
        {
            var world = WorldFactory.CreateInitial(SimulationConfig.CreateDefault(1));
            var recorder = new RecordingController();

            new WorldSimulation(world).Step(recorder);

            Assert.That(recorder.Calls.Select(id => id.Value), Is.EqualTo(Enumerable.Range(1, 64)));
        }

        [Test]
        public void InvalidActionValues_AreSanitized()
        {
            var action = new CreatureAction(float.NaN, float.PositiveInfinity, float.NegativeInfinity);
            Assert.That(action.Thrust, Is.EqualTo(0f));
            Assert.That(action.Turn, Is.EqualTo(1f));
            Assert.That(action.Strafe, Is.EqualTo(-1f));
            Assert.That(new CreatureAction(-3f, -0.5f, 2f).Thrust, Is.EqualTo(0f));

            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(25f, 25f), 0f, 50f);
            new WorldSimulation(world).Step(new ConstantController(action));
            Assert.That(float.IsNaN(creature.Position.X) || float.IsNaN(creature.Energy), Is.False);
        }

        [Test]
        public void LongRun_IsReproducibleAndKeepsInvariants()
        {
            var first = RunChecked(42, 1500);
            var second = RunChecked(42, 1500);
            var otherSeed = RunChecked(43, 1500);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(otherSeed, Is.Not.EqualTo(first));
        }

        private static ulong RunChecked(ulong seed, int ticks)
        {
            var config = SimulationConfig.CreateDefault(seed);
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(config));
            var controller = new SeekFoodController();
            var eaten = 0;

            for (var t = 0; t < ticks; t++)
            {
                simulation.Step(controller);
                eaten += simulation.LastFoodEaten;

                var world = simulation.World;
                Assert.That(world.Food.Count + simulation.SnapshotAgenda().Length, Is.EqualTo(config.InitialFood),
                    "food units are only replaced, never created");
                foreach (var creature in world.Creatures)
                {
                    if (creature.Energy < 0f || creature.Energy > config.MaxEnergy || float.IsNaN(creature.Energy) ||
                        creature.Position.X < 0f || creature.Position.X > config.ArenaWidth ||
                        creature.Position.Y < 0f || creature.Position.Y > config.ArenaHeight ||
                        Speed(creature) > config.MaxSpeed + Tolerance)
                        Assert.Fail($"Invariant broken at tick {world.Tick} for {creature.Id}");
                }
            }

            Assert.That(eaten, Is.GreaterThan(0), "the scenario should exercise feeding");
            return simulation.ComputeFingerprint();
        }

        private static float Speed(CreatureState creature) =>
            MathF.Sqrt(creature.Velocity.X * creature.Velocity.X + creature.Velocity.Y * creature.Velocity.Y);

        private sealed class ByIdController : ICreatureController
        {
            private readonly CreatureId special;
            private readonly CreatureAction specialAction;
            private readonly CreatureAction otherAction;

            public ByIdController(CreatureId special, CreatureAction specialAction, CreatureAction otherAction)
            {
                this.special = special;
                this.specialAction = specialAction;
                this.otherAction = otherAction;
            }

            public CreatureAction Decide(CreatureId creature, in Observation observation) =>
                creature == special ? specialAction : otherAction;
        }
    }
}
