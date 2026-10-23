using Eden.Domain;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class WorldFactoryTests
    {
        [Test]
        public void SameConfig_BuildsIdenticalWorlds()
        {
            var first = WorldFactory.CreateInitial(SimulationConfig.CreateDefault(2026));
            var second = WorldFactory.CreateInitial(SimulationConfig.CreateDefault(2026));

            Assert.That(WorldFingerprint.Compute(second), Is.EqualTo(WorldFingerprint.Compute(first)));
            for (var i = 0; i < first.Creatures.Count; i++)
            {
                Assert.That(second.Creatures[i].Position, Is.EqualTo(first.Creatures[i].Position));
                Assert.That(second.Creatures[i].Heading, Is.EqualTo(first.Creatures[i].Heading));
            }
        }

        [Test]
        public void DifferentSeed_BuildsDifferentWorld()
        {
            var first = WorldFactory.CreateInitial(SimulationConfig.CreateDefault(1));
            var second = WorldFactory.CreateInitial(SimulationConfig.CreateDefault(2));
            Assert.That(WorldFingerprint.Compute(second), Is.Not.EqualTo(WorldFingerprint.Compute(first)));
        }

        [Test]
        public void InitialWorld_HasConfiguredContentsInsideTheArena()
        {
            var config = SimulationConfig.CreateDefault(99);
            var world = WorldFactory.CreateInitial(config);

            Assert.That(world.Tick, Is.EqualTo(0));
            Assert.That(world.Creatures, Has.Count.EqualTo(config.InitialPopulation));
            Assert.That(world.Food, Has.Count.EqualTo(config.InitialFood));
            Assert.That(world.NextCreatureId, Is.EqualTo(config.InitialPopulation + 1));
            Assert.That(world.NextFoodId, Is.EqualTo(config.InitialFood + 1));

            for (var i = 0; i < world.Creatures.Count; i++)
            {
                var creature = world.Creatures[i];
                Assert.That(creature.Id.Value, Is.EqualTo(i + 1), "ids ascend without gaps");
                AssertInsideArena(creature.Position, config);
                Assert.That(creature.Heading, Is.GreaterThanOrEqualTo(0f).And.LessThan(2f * System.MathF.PI));
                Assert.That(creature.Energy, Is.EqualTo(config.InitialEnergy));
                Assert.That(creature.Velocity, Is.EqualTo(Float2.Zero));
                Assert.That(creature.AgeTicks, Is.EqualTo(0));
            }

            for (var i = 0; i < world.Food.Count; i++)
            {
                Assert.That(world.Food[i].Id.Value, Is.EqualTo(i + 1));
                AssertInsideArena(world.Food[i].Position, config);
                Assert.That(world.Food[i].Energy, Is.EqualTo(config.FoodEnergy));
            }
        }

        [Test]
        public void Spawning_DrawsOnlyFromTheSpawnStream()
        {
            var config = SimulationConfig.CreateDefault(5);
            var world = WorldFactory.CreateInitial(config);
            var untouchedWorldStream = SeedDerivation.CreateStream(config.Seed, RandomStream.World);
            var untouchedSpawnStream = SeedDerivation.CreateStream(config.Seed, RandomStream.Spawn);

            Assert.That(world.WorldRandom.State, Is.EqualTo(untouchedWorldStream.State));
            Assert.That(world.SpawnRandom.State, Is.Not.EqualTo(untouchedSpawnStream.State));
        }

        [Test]
        public void Fingerprint_ChangesWithStateAndTick()
        {
            var world = WorldFactory.CreateInitial(SimulationConfig.CreateDefault(3));
            var initial = WorldFingerprint.Compute(world);

            world.Creatures[10].Energy -= 0.001f;
            var afterEnergyChange = WorldFingerprint.Compute(world);
            world.AdvanceTick();

            Assert.That(afterEnergyChange, Is.Not.EqualTo(initial));
            Assert.That(WorldFingerprint.Compute(world), Is.Not.EqualTo(afterEnergyChange));
        }

        [Test]
        public void EmptyWorld_IsValidAndDeterministic()
        {
            var config = new SimulationConfig(new SimulationSettings
            {
                Seed = 8, ArenaWidth = 10f, ArenaHeight = 10f, InitialPopulation = 0, InitialFood = 0
            });
            var world = WorldFactory.CreateInitial(config);
            Assert.That(world.Creatures, Is.Empty);
            Assert.That(WorldFingerprint.Compute(world), Is.EqualTo(WorldFingerprint.Compute(WorldFactory.CreateInitial(config))));
        }

        private static void AssertInsideArena(Float2 position, SimulationConfig config)
        {
            Assert.That(position.X, Is.InRange(0f, config.ArenaWidth));
            Assert.That(position.Y, Is.InRange(0f, config.ArenaHeight));
        }
    }
}
