using System;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>Occlusion and access (P2.3): obstacles hide food from eyes, are seen as barriers and block eating.</summary>
    public class OcclusionTests
    {
        private static readonly Obstacle Wall = new Obstacle(24f, 20f, 26f, 30f);

        [Test]
        public void LineOfSight_IsBlockedOnlyThroughTheInsideOfAnObstacle()
        {
            var config = Config(new Obstacle(25f, 22f, 27f, 25f));
            Assert.That(Vision.LineOfSight(config, new Float2(20f, 23f), new Float2(30f, 23f)), Is.False, "straight through");
            Assert.That(Vision.LineOfSight(config, new Float2(20f, 23f), new Float2(26f, 23f)), Is.False, "ends inside");
            Assert.That(Vision.LineOfSight(config, new Float2(20f, 20f), new Float2(30f, 30f)), Is.True,
                "only touches the top-left corner");
            Assert.That(Vision.LineOfSight(config, new Float2(20f, 25f), new Float2(30f, 25f)), Is.True, "along the top side");
            Assert.That(Vision.LineOfSight(config, new Float2(20f, 26f), new Float2(30f, 26f)), Is.True, "above it");
            Assert.That(Vision.LineOfSight(config, new Float2(26f, 23f), new Float2(26f, 23f)), Is.False,
                "a point inside sees nothing");
            Assert.That(Vision.LineOfSight(Config(), new Float2(20f, 23f), new Float2(30f, 23f)), Is.True, "open arena");
        }

        [Test]
        public void FoodBehindAnObstacle_IsNotSeen_AndTheObstacleIsTheNearestBarrier()
        {
            var reading = Look(Config(Wall), new Eye(0f, 2f * MathF.PI / 3f, 1f), new Float2(30f, 25f));
            Assert.That(reading[(int)EyeChannel.FoodVisible], Is.EqualTo(0f), "hidden by the wall");
            Assert.That(reading[(int)EyeChannel.BarrierVisible], Is.EqualTo(1f));
            // Eye at (20.5, 25); the wall's nearest point (24, 25) is 3.5 away, the arena's border 29.5.
            Assert.That(reading[(int)EyeChannel.BarrierProximity], Is.EqualTo(0.65f).Within(1e-5f));
            Assert.That(reading[(int)EyeChannel.BarrierCos], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(reading[(int)EyeChannel.BarrierSin], Is.EqualTo(0f).Within(1e-5f));

            var open = Look(Config(), new Eye(0f, 2f * MathF.PI / 3f, 1f), new Float2(30f, 25f));
            Assert.That(open[(int)EyeChannel.FoodVisible], Is.EqualTo(1f), "the same food with no wall");

            var before = Look(Config(Wall), new Eye(0f, 2f * MathF.PI / 3f, 1f), new Float2(22f, 27f));
            Assert.That(before[(int)EyeChannel.FoodVisible], Is.EqualTo(1f), "food on this side of the wall");
            Assert.That(before[(int)EyeChannel.FoodProximity], Is.EqualTo(1f - 2.5f / 10f).Within(1e-5f));
        }

        [Test]
        public void AnObstacleOutsideTheAxis_IsSeenWhereTheConesEdgeMeetsIt()
        {
            // A 30° eye: the block's nearest point (22, 26) is 33.7° off the axis, outside the cone, but the cone's upper
            // edge (15°) meets the block's lower side y = 26 at 1 / sin 15° from the eye.
            var block = new Obstacle(22f, 26f, 30f, 30f);
            var reading = Look(Config(block), new Eye(0f, MathF.PI / 6f, 1f), new Float2(1f, 1f));
            var expected = 1f / MathF.Sin(MathF.PI / 12f);
            Assert.That(reading[(int)EyeChannel.BarrierVisible], Is.EqualTo(1f));
            Assert.That(reading[(int)EyeChannel.BarrierProximity], Is.EqualTo(1f - expected / 10f).Within(1e-4f));
            Assert.That(reading[(int)EyeChannel.BarrierSin], Is.EqualTo(MathF.Sin(MathF.PI / 12f)).Within(1e-4f));

            var behind = Look(Config(block), new Eye(MathF.PI, MathF.PI / 6f, 1f), new Float2(1f, 1f));
            Assert.That(behind[(int)EyeChannel.BarrierVisible], Is.EqualTo(0f), "a backward eye sees neither block nor border");
        }

        [Test]
        public void FoodThroughAnObstacle_IsNotEaten_FoodOnThisSideIs()
        {
            // A body pressed against the wall (centre 0.5 from it); food 0.2 inside the wall is within the eat radius 0.75.
            var world = new WorldState(Config(Wall));
            world.AddCreature(new Float2(23.5f, 25f), 0f, 50f);
            world.AddFood(new Float2(24.2f, 25f), 10f);
            var simulation = new WorldSimulation(world);
            simulation.Step(new ConstantController(CreatureAction.Idle));
            Assert.That(simulation.LastFoodEaten, Is.EqualTo(0), "the wall is in the way");

            world.AddFood(new Float2(23.5f, 25.7f), 10f);
            simulation.Step(new ConstantController(CreatureAction.Idle));
            Assert.That(simulation.LastFoodEaten, Is.EqualTo(1));
            Assert.That(world.Food.Count, Is.EqualTo(1), "the walled food is still there");
        }

        [Test]
        public void ABlockedCreature_LetsAFartherOneWithAClearLineEat()
        {
            // A long eat radius reaches across the wall: the nearer creature is behind it, the farther one is not.
            var world = new WorldState(Config(s => s.EatRadius = 3.5f, Wall));
            var blocked = world.AddCreature(new Float2(23.5f, 25f), 0f, 50f);
            var clear = world.AddCreature(new Float2(26.3f, 28f), 0f, 50f);
            world.AddFood(new Float2(26.3f, 25f), 10f);
            var simulation = new WorldSimulation(world);
            simulation.Step(new ConstantController(CreatureAction.Idle));
            Assert.That(clear.FoodEnergyEaten, Is.EqualTo(10f));
            Assert.That(blocked.FoodEnergyEaten, Is.EqualTo(0f));
        }

        private static float[] Look(SimulationConfig config, Eye eye, Float2 food)
        {
            var world = new WorldState(config);
            var creature = world.AddCreature(new Float2(20f, 25f), 0f, 50f);
            world.AddFood(food, 10f);
            var buffer = new float[EyeChannels.Count];
            Vision.See(world, creature, new EyeSet(eye), 0, buffer, 0);
            return buffer;
        }

        private static SimulationConfig Config(params Obstacle[] obstacles) => Config(null, obstacles);

        private static SimulationConfig Config(Action<SimulationSettings> configure, params Obstacle[] obstacles)
        {
            var settings = new SimulationSettings
            {
                Seed = 4, InitialPopulation = 0, InitialFood = 0, SolidBodies = true, Obstacles = obstacles
            };
            configure?.Invoke(settings);
            return new SimulationConfig(settings);
        }
    }
}
