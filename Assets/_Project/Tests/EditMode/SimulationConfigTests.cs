using System;
using Eden.Domain;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class SimulationConfigTests
    {
        [Test]
        public void Default_UsesProposedStartingValues()
        {
            var config = SimulationConfig.CreateDefault(7);

            Assert.That(config.Seed, Is.EqualTo(7UL));
            Assert.That(config.ArenaWidth, Is.EqualTo(50f));
            Assert.That(config.ArenaHeight, Is.EqualTo(50f));
            Assert.That(config.TicksPerSecond, Is.EqualTo(20));
            Assert.That(config.TickDuration, Is.EqualTo(0.05f));
            Assert.That(config.InitialPopulation, Is.EqualTo(64));
            Assert.That(config.InitialFood, Is.EqualTo(128));
            Assert.That(config.SchemaVersion, Is.EqualTo(SimulationConfig.CurrentSchemaVersion));
        }

        [TestCase(float.NaN, 50f, 20, 64, 100f, 50f, "arenaWidth")]
        [TestCase(50f, float.PositiveInfinity, 20, 64, 100f, 50f, "arenaHeight")]
        [TestCase(50f, 50f, 0, 64, 100f, 50f, "ticksPerSecond")]
        [TestCase(50f, 50f, 20, -1, 100f, 50f, "initialPopulation")]
        [TestCase(50f, 50f, 20, 64, 0f, 50f, "maxEnergy")]
        [TestCase(50f, 50f, 20, 64, 100f, 150f, "initialEnergy")]
        [TestCase(50f, 50f, 20, 64, 100f, float.NaN, "initialEnergy")]
        public void InvalidValues_AreRejectedWithTheFieldName(float width, float height, int ticks, int population,
            float maxEnergy, float initialEnergy, string field)
        {
            var error = Assert.Throws<ArgumentException>(() => new SimulationConfig(
                1, width, height, ticks, population, initialFood: 10, maxEnergy, initialEnergy, foodEnergy: 5f));
            Assert.That(error.Message, Does.Contain(field));
        }

        [Test]
        public void SeveralErrors_AreReportedTogether()
        {
            var error = Assert.Throws<ArgumentException>(() => new SimulationConfig(
                1, -1f, 50f, 500, 64, -5, 100f, 50f, -2f));
            Assert.That(error.Message, Does.Contain("arenaWidth").And.Contain("ticksPerSecond")
                .And.Contain("initialFood").And.Contain("foodEnergy"));
        }
    }
}
