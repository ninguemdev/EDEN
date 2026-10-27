using System;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    /// <summary>Eyes and turning cost energy: monotonic with what an eye covers, paid every tick, audited.</summary>
    public class EyeCostTests
    {
        private const float Tolerance = 1e-5f;

        [Test]
        public void EyeUpkeep_GrowsWithApertureRangeAndEyes_RangeFastest()
        {
            var body = TestWorlds.Empty().ReferenceBody;
            var founder = EyeCost.PerSecond(body, Eye.Frontal);
            Assert.That(founder, Is.EqualTo(body.SensorCostPerSecond / 3f).Within(Tolerance), "120° is a third of the circle");

            var wider = EyeCost.PerSecond(body, new Eye(0f, Eye.FounderAperture * 1.5f, 1f));
            var farther = EyeCost.PerSecond(body, new Eye(0f, Eye.FounderAperture, 1.5f));
            Assert.That(wider, Is.EqualTo(founder * 1.5f).Within(Tolerance));
            Assert.That(farther, Is.EqualTo(founder * 2.25f).Within(Tolerance));
            Assert.That(farther, Is.GreaterThan(wider), "reaching farther costs more than seeing wider by the same factor");
            Assert.That(EyeCost.PerSecond(body, new Eye(0f, Eye.MinAperture, Eye.MinRange)), Is.LessThan(founder));

            var threeEyes = new EyeSet(Eye.Frontal, new Eye(2f * MathF.PI / 3f, Eye.FounderAperture, 1f),
                new Eye(-2f * MathF.PI / 3f, Eye.FounderAperture, 1f));
            Assert.That(EyeCost.PerSecond(body, threeEyes), Is.EqualTo(body.SensorCostPerSecond).Within(Tolerance),
                "covering 360° with eyes costs what the legacy circle did");
            var overlapping = new EyeSet(Eye.Frontal, Eye.Frontal);
            Assert.That(EyeCost.PerSecond(body, overlapping), Is.EqualTo(2f * founder).Within(Tolerance),
                "overlapping eyes pay each");
            Assert.That(EyeCost.PerSecond(body, EyeSet.None), Is.Zero);
            Assert.That(EyeCost.PerSecond(body, new EyeSet(null, null, Eye.Frontal)), Is.EqualTo(founder).Within(Tolerance));
        }

        [Test]
        public void EyesAreChargedEveryTick_EvenWithNothingInSight()
        {
            var world = TestWorlds.Empty();
            var config = world.Config;
            var eyed = world.AddCreature(new Float2(25f, 25f), 0f, 50f);
            eyed.Eyes = new EyeSet(Eye.Frontal, new Eye(MathF.PI, Eye.MaxAperture, Eye.MaxRange));
            var legacy = world.AddCreature(new Float2(10f, 10f), 0f, 50f);
            var simulation = new WorldSimulation(world);

            simulation.Step(new ConstantController(CreatureAction.Idle));

            var body = world.ReferenceBody;
            var eyeCost = EyeCost.PerSecond(body, eyed.Eyes);
            Assert.That(50f - eyed.Energy, Is.EqualTo(config.TickDuration * (body.MetabolismPerSecond + eyeCost)).Within(Tolerance));
            Assert.That(50f - legacy.Energy,
                Is.EqualTo(config.TickDuration * (body.MetabolismPerSecond + body.SensorCostPerSecond)).Within(Tolerance),
                "the legacy rule is unchanged");
            Assert.That(simulation.LastEnergy.EyeCosts, Is.EqualTo((double)config.TickDuration * eyeCost).Within(1e-6));
            Assert.That(simulation.LastEnergy.TurnCosts, Is.Zero);
        }

        [Test]
        public void Turning_CostsCreaturesWithEyes_AndTheBalanceCloses()
        {
            var world = TestWorlds.Empty();
            var config = world.Config;
            var eyed = world.AddCreature(new Float2(25f, 25f), 0f, 50f);
            eyed.Eyes = EyeSet.Founder;
            var legacy = world.AddCreature(new Float2(10f, 10f), 0f, 50f);
            var simulation = new WorldSimulation(world);
            var before = eyed.Energy + legacy.Energy;
            var flows = new EnergyFlows();

            for (var t = 0; t < 20; t++)
            {
                simulation.Step(new ConstantController(new CreatureAction(0f, 1f, 0f)));
                flows.Add(simulation.LastEnergy);
            }

            var body = world.ReferenceBody;
            var perTick = config.TickDuration * (body.MetabolismPerSecond + EyeCost.PerSecond(body, eyed.Eyes) +
                                                 body.MovementCostPerSecond * config.TurnCostFraction);
            Assert.That(50f - eyed.Energy, Is.EqualTo(20f * perTick).Within(1e-3f), "a full turn costs a quarter of a push");
            Assert.That(50f - legacy.Energy,
                Is.EqualTo(20f * config.TickDuration * (body.MetabolismPerSecond + body.SensorCostPerSecond)).Within(1e-3f),
                "turning stays free with the legacy senses");
            Assert.That(flows.TurnCosts,
                Is.EqualTo(20.0 * config.TickDuration * body.MovementCostPerSecond * config.TurnCostFraction).Within(1e-5));
            Assert.That((double)(eyed.Energy + legacy.Energy) - before, Is.EqualTo(flows.Balance).Within(1e-4),
                "every unit of energy is accounted for");
        }

        [Test]
        public void TurnCostFraction_IsValidated()
        {
            Assert.That(() => new SimulationConfig(new SimulationSettings { TurnCostFraction = -0.1f }), Throws.ArgumentException);
            Assert.That(new SimulationConfig(new SimulationSettings()).TurnCostFraction, Is.EqualTo(0.25f));
        }
    }
}
