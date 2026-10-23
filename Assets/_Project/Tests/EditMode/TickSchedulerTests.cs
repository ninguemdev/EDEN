using System;
using System.Collections.Generic;
using System.Linq;
using Eden.Domain;
using Eden.Simulation;
using NUnit.Framework;

namespace Eden.Tests.EditMode
{
    public class TickSchedulerTests
    {
        [Test]
        public void Events_RunByDueTickThenSchedulingOrder()
        {
            var scheduler = new TickScheduler<string>();
            scheduler.Schedule(5, "a");
            scheduler.Schedule(3, "b");
            scheduler.Schedule(5, "c");
            scheduler.Schedule(3, "d");
            scheduler.Schedule(4, "e");

            Assert.That(DrainThrough(scheduler, 10), Is.EqualTo(new[] { "b", "d", "e", "a", "c" }));
        }

        [Test]
        public void DrainDue_LeavesFutureEventsPending()
        {
            var scheduler = new TickScheduler<int>();
            scheduler.Schedule(2, 20);
            scheduler.Schedule(7, 70);
            var due = new List<ScheduledEvent<int>>();

            scheduler.DrainDue(5, due);

            Assert.That(due.Select(e => e.Payload), Is.EqualTo(new[] { 20 }));
            Assert.That(scheduler.Count, Is.EqualTo(1));
            Assert.That(scheduler.SnapshotPending()[0].DueTick, Is.EqualTo(7));
        }

        [Test]
        public void ProcessedTicks_CannotReceiveEventsOrBeRevisited()
        {
            var scheduler = new TickScheduler<int>();
            scheduler.DrainDue(3, new List<ScheduledEvent<int>>());

            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Schedule(3, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Schedule(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.DrainDue(2, new List<ScheduledEvent<int>>()));
            Assert.DoesNotThrow(() => scheduler.Schedule(4, 1));
        }

        [Test]
        public void SnapshotPending_ListsEventsInRunOrderWithSequences()
        {
            var scheduler = new TickScheduler<string>();
            scheduler.Schedule(9, "late");
            scheduler.Schedule(1, "early");
            scheduler.Schedule(1, "early-second");

            var pending = scheduler.SnapshotPending();

            Assert.That(pending.Select(e => e.Payload), Is.EqualTo(new[] { "early", "early-second", "late" }));
            Assert.That(pending.Select(e => e.Sequence), Is.EqualTo(new[] { 1L, 2L, 0L }));
            Assert.That(scheduler.NextSequence, Is.EqualTo(3));
        }

        [Test]
        public void RandomSchedule_MatchesStableSortReference()
        {
            var random = new Pcg32(31, 4);
            var scheduler = new TickScheduler<int>();
            var expected = new List<(long Tick, int Order)>();
            for (var i = 0; i < 500; i++)
            {
                var tick = random.NextUInt(50);
                scheduler.Schedule(tick, i);
                expected.Add((tick, i));
            }

            var reference = expected.OrderBy(e => e.Tick).Select(e => e.Order).ToArray();
            Assert.That(DrainThrough(scheduler, 49), Is.EqualTo(reference));
        }

        private static List<T> DrainThrough<T>(TickScheduler<T> scheduler, long lastTick)
        {
            var due = new List<ScheduledEvent<T>>();
            for (var tick = 0L; tick <= lastTick; tick++)
                scheduler.DrainDue(tick, due);
            return due.Select(e => e.Payload).ToList();
        }
    }
}
