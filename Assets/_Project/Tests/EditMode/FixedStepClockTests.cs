using System;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class FixedStepClockTests
    {
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void NormalSpeed_RunsTwentyTicksPerRealSecond(int framesPerSecond)
        {
            var clock = new FixedStepClock(20);
            var total = RunFrames(clock, framesPerSecond * 10, 1.0 / framesPerSecond);

            Assert.That(total, Is.EqualTo(200));
            Assert.That(clock.EffectiveSpeed, Is.EqualTo(1f).Within(0.05f));
            Assert.That(clock.DroppedTicks, Is.EqualTo(0));
        }

        [Test]
        public void SameTickCount_GivesIdenticalWorldAtAnyFrameRate()
        {
            var at30 = RunWorld(_ => 1.0 / 30);
            var at60 = RunWorld(_ => 1.0 / 60);
            var at144 = RunWorld(_ => 1.0 / 144);
            var jitter = new Pcg32(9, 9);
            // Irregular frames plus 5 s hitches that exceed the clock limits and drop due ticks.
            var irregular = RunWorld(frame => frame % 97 == 0 ? 5.0 : jitter.NextFloat(1f / 240f, 1f / 20f));

            Assert.That(at60, Is.EqualTo(at30));
            Assert.That(at144, Is.EqualTo(at30));
            Assert.That(irregular, Is.EqualTo(at30));
        }

        [TestCase(4f, 800)]
        [TestCase(0.5f, 100)]
        public void Speed_RunsMoreOrFewerWholeTicks(float speed, int expectedTicks)
        {
            var clock = new FixedStepClock(20);
            clock.SetSpeed(speed);
            Assert.That(RunFrames(clock, 600, 1.0 / 60), Is.EqualTo(expectedTicks));
        }

        [Test]
        public void Pause_StopsTicksAndResumeDoesNotBurst()
        {
            var clock = new FixedStepClock(20);
            clock.Pause();

            Assert.That(RunFrames(clock, 300, 1.0 / 60), Is.EqualTo(0));
            Assert.That(clock.IsPaused, Is.True);
            Assert.That(clock.EffectiveSpeed, Is.EqualTo(0f));

            clock.Resume();
            Assert.That(clock.Advance(1.0 / 60), Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void RequestStep_PausesAndRunsExactlyOneTickPerRequest()
        {
            var clock = new FixedStepClock(20);
            clock.RequestStep();

            Assert.That(clock.IsPaused, Is.True);
            Assert.That(clock.Advance(1.0 / 60), Is.EqualTo(1));
            Assert.That(clock.Advance(1.0 / 60), Is.EqualTo(0));

            clock.RequestStep();
            clock.RequestStep();
            Assert.That(clock.Advance(0), Is.EqualTo(2));
        }

        [Test]
        public void Overload_RespectsLimitsAndReportsLowerEffectiveSpeed()
        {
            var clock = new FixedStepClock(20, maxTicksPerFrame: 16, maxBacklogTicks: 32);
            clock.SetSpeed(64f);

            for (var frame = 0; frame < 50; frame++)
                Assert.That(clock.Advance(0.1), Is.LessThanOrEqualTo(16));

            Assert.That(clock.DroppedTicks, Is.GreaterThan(0));
            Assert.That(clock.EffectiveSpeed, Is.EqualTo(8f).Within(0.01f));
            Assert.That(clock.EffectiveSpeed, Is.LessThan(clock.RequestedSpeed));
        }

        [Test]
        public void Hitch_IsCappedInsteadOfReplayedAllAtOnce()
        {
            var clock = new FixedStepClock(20, maxTicksPerFrame: 64, maxBacklogTicks: 128);

            Assert.That(clock.Advance(10.0), Is.EqualTo(64));
            Assert.That(clock.DroppedTicks, Is.EqualTo(200 - 64 - 128));
            Assert.That(clock.Advance(0), Is.EqualTo(64));
            Assert.That(clock.Advance(0), Is.EqualTo(64));
            Assert.That(clock.Advance(0), Is.EqualTo(0));
        }

        [Test]
        public void InvalidInput_IsRejected()
        {
            var clock = new FixedStepClock(20);
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-0.01));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetSpeed(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetSpeed(100f));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetSpeed(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepClock(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepClock(20, maxTicksPerFrame: 0));
        }

        private static long RunFrames(FixedStepClock clock, int frames, double frameSeconds)
        {
            long total = 0;
            for (var frame = 0; frame < frames; frame++)
                total += clock.Advance(frameSeconds);
            return total;
        }

        private static ulong RunWorld(Func<int, double> frameSeconds)
        {
            const int targetTicks = 600;
            var world = WorldFactory.CreateInitial(SimulationConfig.CreateDefault(77));
            var clock = new FixedStepClock(world.Config.TicksPerSecond);
            clock.SetSpeed(2f);

            for (var frame = 0; world.Tick < targetTicks; frame++)
            {
                var ticks = clock.Advance(frameSeconds(frame));
                for (var i = 0; i < ticks && world.Tick < targetTicks; i++)
                    TestTick(world);
            }

            return WorldFingerprint.Compute(world);
        }

        // Stand-in for the T12 rules: uses world randomness and the fixed tick duration, never frame time.
        private static void TestTick(WorldState world)
        {
            var index = (int)world.WorldRandom.NextUInt((uint)world.Creatures.Count);
            var creature = world.Creatures[index];
            creature.Energy -= world.Config.TickDuration;
            creature.AgeTicks++;
            world.AdvanceTick();
        }
    }
}
