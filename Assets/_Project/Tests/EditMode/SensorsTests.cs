using System;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class SensorsTests
    {
        private const float Tolerance = 1e-5f;

        [Test]
        public void FoodBeyondRange_IsInvisibleAndZeroed()
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(25f, 25f), 0f, 50f);
            world.AddFood(new Float2(40f, 25f), 10f);

            var observation = Sensors.Observe(world, creature);

            Assert.That(observation.FoodVisible, Is.EqualTo(0f));
            Assert.That(observation.FoodSin, Is.EqualTo(0f));
            Assert.That(observation.FoodCos, Is.EqualTo(0f));
            Assert.That(observation.FoodProximity, Is.EqualTo(0f));
        }

        [Test]
        public void FoodAhead_AndOnTheLeft_UseTheLocalFrame()
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(25f, 25f), 0f, 50f);
            world.AddFood(new Float2(30f, 25f), 10f);

            var ahead = Sensors.Observe(world, creature);
            Assert.That(ahead.FoodVisible, Is.EqualTo(1f));
            Assert.That(ahead.FoodCos, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(ahead.FoodSin, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(ahead.FoodProximity, Is.EqualTo(0.5f).Within(Tolerance));

            creature.Heading = -MathF.PI / 2f + 2f * MathF.PI;
            var left = Sensors.Observe(world, creature);
            Assert.That(left.FoodSin, Is.EqualTo(1f).Within(Tolerance), "facing -y, +x is on the left");
            Assert.That(left.FoodCos, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void NearestFoodWins_AndEqualDistanceGoesToLowerId()
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(25f, 25f), 0f, 50f);
            world.AddFood(new Float2(27f, 25f), 10f);
            world.AddFood(new Float2(23f, 25f), 10f);

            Assert.That(Sensors.Observe(world, creature).FoodCos, Is.EqualTo(1f).Within(Tolerance), "tie: lower id ahead");

            world.AddFood(new Float2(25f, 26f), 10f);
            Assert.That(Sensors.Observe(world, creature).FoodSin, Is.EqualTo(1f).Within(Tolerance), "closer food on the left");
        }

        [Test]
        public void FoodExactlyAtRange_IsVisibleWithZeroProximity()
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(20f, 20f), 0f, 50f);
            world.AddFood(new Float2(30f, 20f), 10f);

            var observation = Sensors.Observe(world, creature);
            Assert.That(observation.FoodVisible, Is.EqualTo(1f));
            Assert.That(observation.FoodProximity, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void WallDistances_AreLocalAndCappedAtSensorRange()
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(2f, 3f), 0f, 50f);

            var facingEast = Sensors.Observe(world, creature);
            Assert.That(facingEast.WallFront, Is.EqualTo(1f));
            Assert.That(facingEast.WallBack, Is.EqualTo(0.2f).Within(Tolerance));
            Assert.That(facingEast.WallRight, Is.EqualTo(0.3f).Within(Tolerance));
            Assert.That(facingEast.WallLeft, Is.EqualTo(1f));

            creature.Heading = MathF.PI;
            var facingWest = Sensors.Observe(world, creature);
            Assert.That(facingWest.WallFront, Is.EqualTo(0.2f).Within(Tolerance));
            Assert.That(facingWest.WallLeft, Is.EqualTo(0.3f).Within(Tolerance));
        }

        [Test]
        public void RaysAlongAWall_AreNotBlockedByThatWall()
        {
            var world = TestWorlds.Empty();
            var onBottomWall = world.AddCreature(new Float2(25f, 0f), MathF.PI, 50f);

            var reading = Sensors.Observe(world, onBottomWall);

            // sin(MathF.PI) is about -8.7e-8; the front ray runs along the wall and must see 25 units ahead.
            Assert.That(reading.WallFront, Is.EqualTo(1f));
            Assert.That(reading.WallLeft, Is.EqualTo(0f).Within(Tolerance), "facing -x, the bottom wall is on the left");
            Assert.That(reading.WallRight, Is.EqualTo(1f));
        }

        [Test]
        public void SelfState_IsNormalized()
        {
            var world = TestWorlds.Empty();
            var creature = world.AddCreature(new Float2(25f, 25f), MathF.PI / 2f, 25f);
            creature.AgeTicks = world.Config.MaxAgeTicks / 2;
            creature.Velocity = new Float2(2.5f, 0f);

            var observation = Sensors.Observe(world, creature);

            Assert.That(observation.Energy, Is.EqualTo(0.25f).Within(Tolerance));
            Assert.That(observation.Age, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(observation.VelocityForward, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(observation.VelocityLeft, Is.EqualTo(-0.5f).Within(Tolerance), "facing +y, +x is to the right");
        }

        [Test]
        public void CopyTo_WritesInputsInDocumentedOrder()
        {
            var observation = new Observation(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
            var buffer = new float[Observation.Size + 2];

            observation.CopyTo(buffer, 1);

            Assert.That(buffer, Is.EqualTo(new float[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => observation.CopyTo(buffer, 3));
        }
    }
}
