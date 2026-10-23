using System;
using System.Collections.Generic;
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
            Assert.That(config.DecisionIntervalTicks, Is.EqualTo(2));
            Assert.That(config.InitialPopulation, Is.EqualTo(64));
            Assert.That(config.InitialFood, Is.EqualTo(128));
            Assert.That(config.MaxAgeTicks, Is.EqualTo(3600));
            Assert.That(config.FoodRespawnTicks, Is.EqualTo(100));
            Assert.That(config.SchemaVersion, Is.EqualTo(SimulationConfig.CurrentSchemaVersion));
        }

        [TestCaseSource(nameof(InvalidSettings))]
        public void InvalidValues_AreRejectedWithTheFieldName(string field, Action<SimulationSettings> change)
        {
            var settings = new SimulationSettings();
            change(settings);
            var error = Assert.Throws<ArgumentException>(() => new SimulationConfig(settings));
            Assert.That(error.Message, Does.Contain(field));
        }

        [Test]
        public void SeveralErrors_AreReportedTogether()
        {
            var settings = new SimulationSettings { ArenaWidth = -1f, TicksPerSecond = 500, InitialFood = -5, FoodEnergy = -2f };
            var error = Assert.Throws<ArgumentException>(() => new SimulationConfig(settings));
            Assert.That(error.Message, Does.Contain("ArenaWidth").And.Contain("TicksPerSecond")
                .And.Contain("InitialFood").And.Contain("FoodEnergy"));
        }

        [Test]
        public void Config_IsFrozenCopyOfSettings()
        {
            var settings = new SimulationSettings { Seed = 3 };
            var config = new SimulationConfig(settings);
            settings.ArenaWidth = 999f;
            settings.Seed = 4;
            Assert.That(config.ArenaWidth, Is.EqualTo(50f));
            Assert.That(config.Seed, Is.EqualTo(3UL));
        }

        [Test]
        public void DurationsRoundToWholeTicks()
        {
            var config = new SimulationConfig(new SimulationSettings { MaxAgeSeconds = 0.01f, FoodRespawnSeconds = 2.52f });
            Assert.That(config.MaxAgeTicks, Is.EqualTo(1));
            Assert.That(config.FoodRespawnTicks, Is.EqualTo(50));
        }

        private static IEnumerable<TestCaseData> InvalidSettings()
        {
            yield return Case("ArenaWidth", "NaN", s => s.ArenaWidth = float.NaN);
            yield return Case("ArenaHeight", "Infinity", s => s.ArenaHeight = float.PositiveInfinity);
            yield return Case("TicksPerSecond", "Zero", s => s.TicksPerSecond = 0);
            yield return Case("DecisionIntervalTicks", "Zero", s => s.DecisionIntervalTicks = 0);
            yield return Case("InitialPopulation", "Negative", s => s.InitialPopulation = -1);
            yield return Case("MaxEnergy", "Zero", s => s.MaxEnergy = 0f);
            yield return Case("InitialEnergy", "AboveMax", s => s.InitialEnergy = 150f);
            yield return Case("InitialEnergy", "NaN", s => s.InitialEnergy = float.NaN);
            yield return Case("FoodRespawnSeconds", "Zero", s => s.FoodRespawnSeconds = 0f);
            yield return Case("MaxAgeSeconds", "Infinity", s => s.MaxAgeSeconds = float.PositiveInfinity);
            yield return Case("MetabolismPerSecond", "Negative", s => s.MetabolismPerSecond = -0.1f);
            yield return Case("MovementCostPerSecond", "NaN", s => s.MovementCostPerSecond = float.NaN);
            yield return Case("SensorRange", "Zero", s => s.SensorRange = 0f);
            yield return Case("EatRadius", "Negative", s => s.EatRadius = -1f);
            yield return Case("MaxSpeed", "Zero", s => s.MaxSpeed = 0f);
            yield return Case("MaxTurnRate", "NaN", s => s.MaxTurnRate = float.NaN);
            yield return Case("Damping", "Infinity", s => s.Damping = float.PositiveInfinity);
        }

        private static TestCaseData Case(string field, string label, Action<SimulationSettings> change) =>
            new TestCaseData(field, change).SetName($"InvalidValues_AreRejectedWithTheFieldName({field}, {label})");
    }
}
