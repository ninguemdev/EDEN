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

        [Test]
        public void TickFraction_IsThePartOfTheNextTickAlreadyElapsed()
        {
            var clock = new FixedStepClock(20);
            Assert.That(clock.Advance(0.075), Is.EqualTo(1), "1.5 ticks due");
            Assert.That(clock.TickFraction, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(clock.Advance(0.0125), Is.Zero);
            Assert.That(clock.TickFraction, Is.EqualTo(0.75f).Within(1e-5f));

            clock.Pause();
            Assert.That(clock.TickFraction, Is.EqualTo(1f), "paused: the latest state is shown as is");
            clock.RequestStep();
            Assert.That(clock.Advance(0.1), Is.EqualTo(1));
            Assert.That(clock.TickFraction, Is.EqualTo(1f));

            clock.Resume();
            clock.Advance(0.01);
            Assert.That(clock.TickFraction, Is.EqualTo(0.2f).Within(1e-5f));
            clock.SetUnpaced(true);
            Assert.That(clock.TickFraction, Is.EqualTo(1f), "unpaced: nothing to draw between");

            var behind = new FixedStepClock(20);
            behind.Advance(100.0);
            Assert.That(behind.TickFraction, Is.InRange(0f, 1f), "a backlog never draws past the current tick");
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

        [Test]
        public void Unpaced_LeavesTicksToTheCallerAndStillMeasuresSpeed()
        {
            var clock = new FixedStepClock(20);
            Assert.Throws<InvalidOperationException>(() => clock.RecordUnpaced(0.1, 10), "paced clocks pace");
            clock.SetUnpaced(true);

            Assert.That(clock.IsUnpaced, Is.True);
            Assert.That(RunFrames(clock, 10, 0.1), Is.Zero, "nothing is owed while unpaced");
            for (var frame = 0; frame < 10; frame++)
                clock.RecordUnpaced(0.1, 400);
            Assert.That(clock.EffectiveSpeed, Is.EqualTo(200f).Within(0.01f), "4000 ticks of 1/20 s in 1 s");
            Assert.That(clock.DroppedTicks, Is.Zero);

            clock.RequestStep();
            Assert.That(clock.IsPaused, Is.True);
            Assert.That(clock.Advance(0.1), Is.EqualTo(1), "steps still work");
            Assert.Throws<InvalidOperationException>(() => clock.RecordUnpaced(0.1, 1), "paused clocks run nothing");
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                clock.Resume();
                clock.RecordUnpaced(0.1, -1);
            });

            clock.SetUnpaced(false);
            Assert.That(RunFrames(clock, 10, 0.1), Is.EqualTo(20), "paced again at the requested 1×");
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
            var simulation = new WorldSimulation(WorldFactory.CreateInitial(SimulationConfig.CreateDefault(77)));
            var controller = new SeekFoodController();
            var clock = new FixedStepClock(simulation.World.Config.TicksPerSecond);
            clock.SetSpeed(2f);

            for (var frame = 0; simulation.World.Tick < targetTicks; frame++)
            {
                var ticks = clock.Advance(frameSeconds(frame));
                for (var i = 0; i < ticks && simulation.World.Tick < targetTicks; i++)
                    simulation.Step(controller);
            }

            Assert.That(simulation.World.Creatures, Is.Not.Empty, "the scenario should still have activity");
            return simulation.ComputeFingerprint();
        }
    }
}
