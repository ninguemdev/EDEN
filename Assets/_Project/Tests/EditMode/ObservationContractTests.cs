using System;
using System.Linq;
using Eden.Brains;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class ObservationContractTests
    {
        private const float Tolerance = 1e-5f;

        [Test]
        public void Limits_DocumentEveryInputInBrainOrder()
        {
            var expected = new[]
            {
                ("Energy", 0f, 1f), ("Age", 0f, 1f), ("VelocityForward", -1f, 1f), ("VelocityLeft", -1f, 1f),
                ("FoodSin", -1f, 1f), ("FoodCos", -1f, 1f), ("FoodProximity", 0f, 1f), ("FoodVisible", 0f, 1f),
                ("WallFront", 0f, 1f), ("WallRight", 0f, 1f), ("WallBack", 0f, 1f), ("WallLeft", 0f, 1f)
            };
            for (var i = 0; i < Observation.Size; i++)
            {
                Assert.That(ObservationLimits.Name(i), Is.EqualTo(expected[i].Item1));
                Assert.That(ObservationLimits.Min(i), Is.EqualTo(expected[i].Item2));
                Assert.That(ObservationLimits.Max(i), Is.EqualTo(expected[i].Item3));
            }

            var observation = new Observation(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
            var buffer = new float[Observation.Size];
            observation.CopyTo(buffer, 0);
            Assert.That(Enumerable.Range(0, Observation.Size).Select(i => observation[i]), Is.EqualTo(buffer));
            Assert.Throws<ArgumentOutOfRangeException>(() => ObservationLimits.Min(12));
        }

        [Test]
        public void ArenaCornersAndMaxSpeed_GiveExpectedReadings()
        {
            var world = TestWorlds.Empty();
            var config = world.Config;
            var corner = world.AddCreature(new Float2(0f, 0f), 0f, config.MaxEnergy);
            corner.Velocity = new Float2(config.MaxSpeed, 0f);
            corner.AgeTicks = config.MaxAgeTicks;

            var reading = Sensors.Observe(world, corner);
            Assert.That(reading.WallBack, Is.EqualTo(0f));
            Assert.That(reading.WallRight, Is.EqualTo(0f));
            Assert.That(reading.WallFront, Is.EqualTo(1f));
            Assert.That(reading.WallLeft, Is.EqualTo(1f));
            Assert.That(reading.VelocityForward, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(reading.Energy, Is.EqualTo(1f));
            Assert.That(reading.Age, Is.EqualTo(1f));
            Assert.That(ObservationLimits.IsValid(reading), Is.True);

            var farCorner = world.AddCreature(new Float2(config.ArenaWidth, config.ArenaHeight), MathF.PI, 0f);
            farCorner.Velocity = new Float2(config.MaxSpeed, 0f);
            var reverse = Sensors.Observe(world, farCorner);
            Assert.That(reverse.VelocityForward, Is.EqualTo(-1f).Within(Tolerance), "moving +x while facing -x");
            Assert.That(reverse.WallBack, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(reverse.WallRight, Is.EqualTo(0f).Within(Tolerance), "facing -x, the +y wall is on the right");
            Assert.That(reverse.WallLeft, Is.EqualTo(1f));
            Assert.That(reverse.Energy, Is.EqualTo(0f));
            Assert.That(ObservationLimits.IsValid(reverse), Is.True);
        }

        [Test]
        public void RandomStates_AlwaysHonorTheContract()
        {
            var world = TestWorlds.Empty();
            var config = world.Config;
            var random = new Pcg32(77, 3);
            for (var i = 0; i < 40; i++)
                world.AddFood(WorldFactory.RandomArenaPosition(random, config), config.FoodEnergy);
            var creature = world.AddCreature(Float2.Zero, 0f, 1f);

            for (var sample = 0; sample < 20000; sample++)
            {
                creature.Position = Edgy(random, config.ArenaWidth, config.ArenaHeight);
                creature.Heading = random.NextFloat() * 2f * MathF.PI;
                var speed = random.NextFloat(0f, config.MaxSpeed * 1.2f);
                var direction = random.NextFloat() * 2f * MathF.PI;
                creature.Velocity = new Float2(speed * MathF.Cos(direction), speed * MathF.Sin(direction));
                creature.Energy = random.NextFloat(0f, config.MaxEnergy);
                creature.AgeTicks = random.NextUInt((uint)config.MaxAgeTicks + 1);

                var observation = Sensors.Observe(world, creature);
                if (!ObservationLimits.IsValid(observation))
                    Assert.Fail($"Invalid observation at sample {sample}");
            }
        }

        [Test]
        public void MissingFood_ZeroesTheMaskedInputs_AndFoodOnTopReadsAhead()
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(25f, 25f), 1f, 50f);

            var empty = Sensors.Observe(world, creature);
            Assert.That(new[] { empty.FoodSin, empty.FoodCos, empty.FoodProximity, empty.FoodVisible },
                Is.EqualTo(new[] { 0f, 0f, 0f, 0f }));

            world.AddFood(new Float2(25f, 25f), 10f);
            var onTop = Sensors.Observe(world, creature);
            Assert.That(new[] { onTop.FoodSin, onTop.FoodCos, onTop.FoodProximity, onTop.FoodVisible },
                Is.EqualTo(new[] { 0f, 1f, 1f, 1f }));
        }

        [Test]
        public void Sanitize_AppliesThePolicyAndCountsEveryCorrection()
        {
            var valid = new Observation(0.5f, 0.1f, -0.3f, 0.2f, 0.6f, 0.8f, 0.4f, 1f, 1f, 0.5f, 0f, 0.7f);
            var unchanged = ObservationLimits.Sanitize(valid, out var none);
            Assert.That(none, Is.EqualTo(0));
            Assert.That(Values(unchanged), Is.EqualTo(Values(valid)));

            var broken = new Observation(float.NaN, 2f, float.NegativeInfinity, -3f, 0.6f, 0.8f, 0.4f, 0.7f,
                float.PositiveInfinity, 0.5f, -0.1f, 0.7f);
            var fixedUp = ObservationLimits.Sanitize(broken, out var corrections);
            Assert.That(Values(fixedUp), Is.EqualTo(new[] { 0f, 1f, 0f, -1f, 0.6f, 0.8f, 0.4f, 1f, 0f, 0.5f, 0f, 0.7f }));
            Assert.That(corrections, Is.EqualTo(7), "NaN, 2, -inf, -3, flag 0.7, +inf, -0.1");
            Assert.That(ObservationLimits.IsValid(fixedUp), Is.True);
            Assert.That(ObservationLimits.IsValid(broken), Is.False);

            var unmasked = new Observation(0.5f, 0f, 0f, 0f, 0.3f, 0.2f, 0.9f, 0.2f, 1f, 1f, 1f, 1f);
            var masked = ObservationLimits.Sanitize(unmasked, out var maskCorrections);
            Assert.That(new[] { masked.FoodSin, masked.FoodCos, masked.FoodProximity, masked.FoodVisible },
                Is.EqualTo(new[] { 0f, 0f, 0f, 0f }));
            Assert.That(maskCorrections, Is.EqualTo(2), "flag 0.2 -> 0, then the mask");
        }

        [Test]
        public void MlpBrain_TreatsInvalidInputsAsTheirSanitizedValues()
        {
            var brain = new MlpBrain(MlpBrainTests.ReferenceParameters());
            var reference = MlpBrainTests.Reference;

            var withNaN = Replace(reference, ObservationLimits.FoodSinIndex, float.NaN);
            var withZero = Replace(reference, ObservationLimits.FoodSinIndex, 0f);
            AssertSameAction(brain.Evaluate(withNaN, null), brain.Evaluate(withZero, null));

            var overRange = Replace(reference, 0, 3f);
            var atMax = Replace(reference, 0, 1f);
            AssertSameAction(brain.Evaluate(overRange, null), brain.Evaluate(atMax, null));
        }

        [Test]
        public void CorruptState_IsCorrectedBeforeBrainsAndCounted()
        {
            var world = TestWorlds.Empty();
            var healthy = world.AddCreature(new Float2(10f, 10f), 0f, 50f);
            var corrupt = world.AddCreature(new Float2(20f, 20f), 0f, 50f);
            corrupt.Energy = float.NaN;
            var simulation = new WorldSimulation(world);
            var checker = new ContractCheckingController();

            simulation.Step(checker);

            Assert.That(checker.InvalidSeen, Is.EqualTo(0), "brains only receive valid observations");
            Assert.That(simulation.LastCorrectedInputValues, Is.EqualTo(1));
            Assert.That(simulation.CorrectedInputValues, Is.EqualTo(1));
            Assert.That(healthy.Energy, Is.LessThan(50f));
        }

        [Test]
        public void NormalRuns_NeedNoCorrections()
        {
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(SimulationConfig.CreateDefault(31)));
            var controller = new SeekFoodController();
            for (var t = 0; t < 1200; t++)
                simulation.Step(controller);
            Assert.That(simulation.CorrectedInputValues, Is.EqualTo(0));
        }

        [Test]
        public void NaNObservations_NeverReachCreatureState()
        {
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(SimulationConfig.CreateDefault(8)));
            var controller = new PoisoningController(new MlpBrain(MlpBrainTests.ReferenceParameters()));
            var config = simulation.World.Config;

            for (var t = 0; t < 400; t++)
            {
                simulation.Step(controller);
                foreach (var creature in simulation.World.Creatures)
                {
                    if (float.IsNaN(creature.Energy) || float.IsNaN(creature.Heading) ||
                        float.IsNaN(creature.Position.X) || float.IsNaN(creature.Position.Y) ||
                        float.IsNaN(creature.Velocity.X) || float.IsNaN(creature.Velocity.Y) ||
                        creature.Position.X < 0f || creature.Position.X > config.ArenaWidth)
                        Assert.Fail($"State broken for {creature.Id} at tick {simulation.World.Tick}");
                }
            }
        }

        private static Float2 Edgy(IRandomSource random, float width, float height)
        {
            // A third of the samples sit exactly on a border or a corner.
            switch (random.NextUInt(6))
            {
                case 0: return new Float2(0f, random.NextFloat(0f, height));
                case 1: return new Float2(width, random.NextFloat(0f, height));
                case 2: return new Float2(random.NextFloat(0f, width), random.NextUInt(2) == 0 ? 0f : height);
                default: return new Float2(random.NextFloat(0f, width), random.NextFloat(0f, height));
            }
        }

        private static float[] Values(Observation observation)
        {
            var values = new float[Observation.Size];
            observation.CopyTo(values, 0);
            return values;
        }

        private static Observation Replace(Observation source, int index, float value)
        {
            var values = Values(source);
            values[index] = value;
            return new Observation(values[0], values[1], values[2], values[3], values[4], values[5], values[6],
                values[7], values[8], values[9], values[10], values[11]);
        }

        private static void AssertSameAction(CreatureAction actual, CreatureAction expected)
        {
            Assert.That(actual.Thrust, Is.EqualTo(expected.Thrust));
            Assert.That(actual.Turn, Is.EqualTo(expected.Turn));
            Assert.That(actual.Strafe, Is.EqualTo(expected.Strafe));
        }

        private sealed class ContractCheckingController : ICreatureController
        {
            public int InvalidSeen;

            public CreatureAction Decide(CreatureId creature, in Observation observation)
            {
                if (!ObservationLimits.IsValid(observation))
                    InvalidSeen++;
                return CreatureAction.Idle;
            }
        }

        /// <summary>Feeds the brain observations full of NaN to prove nothing invalid reaches the world.</summary>
        private sealed class PoisoningController : ICreatureController
        {
            private readonly IBrain brain;

            public PoisoningController(IBrain brain)
            {
                this.brain = brain;
            }

            public CreatureAction Decide(CreatureId creature, in Observation observation)
            {
                var poisoned = new Observation(float.NaN, observation.Age, float.NaN, observation.VelocityLeft,
                    float.NaN, float.PositiveInfinity, observation.FoodProximity, float.NaN, observation.WallFront,
                    float.NegativeInfinity, observation.WallBack, float.NaN);
                return brain.Evaluate(poisoned, null);
            }
        }
    }
}
