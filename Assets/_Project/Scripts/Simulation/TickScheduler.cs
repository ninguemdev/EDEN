using System;
using System.Collections.Generic;

namespace Eden.Simulation
{
    /// <summary>An event due at a simulation tick. Sequence numbers record scheduling order and break ties.</summary>
    public readonly struct ScheduledEvent<T>
    {
        public ScheduledEvent(long dueTick, long sequence, T payload)
        {
            DueTick = dueTick;
            Sequence = sequence;
            Payload = payload;
        }

        public long DueTick { get; }
        public long Sequence { get; }
        public T Payload { get; }
    }

    /// <summary>
    /// Deterministic event agenda: events run in (due tick, scheduling order), independent of frame rate.
    /// Payloads should be plain data so the agenda can be saved with a checkpoint.
    /// </summary>
    public sealed class TickScheduler<T>
    {
        // Sorted descending by (DueTick, Sequence) so the next event to run is at the end of the list.
        private readonly List<ScheduledEvent<T>> queue = new List<ScheduledEvent<T>>();

        public int Count => queue.Count;

        /// <summary>Makes room for <paramref name="capacity"/> pending events, so scheduling up to that many never allocates.</summary>
        public void Reserve(int capacity)
        {
            if (queue.Capacity < capacity)
                queue.Capacity = capacity;
        }
        public long NextSequence { get; private set; }

        /// <summary>Last tick passed to <see cref="DrainDue"/>; events can only be scheduled after it.</summary>
        public long LastDrainedTick { get; private set; } = -1;

        public long Schedule(long dueTick, T payload)
        {
            if (dueTick <= LastDrainedTick)
                throw new ArgumentOutOfRangeException(nameof(dueTick),
                    $"Tick {dueTick} was already processed (last drained tick {LastDrainedTick}).");

            var scheduled = new ScheduledEvent<T>(dueTick, NextSequence++, payload);
            queue.Insert(FirstIndexDueAtOrBefore(dueTick), scheduled);
            return scheduled.Sequence;
        }

        /// <summary>Moves every event due at or before <paramref name="tick"/> into <paramref name="output"/>, in run order.</summary>
        public void DrainDue(long tick, List<ScheduledEvent<T>> output)
        {
            if (output == null)
                throw new ArgumentNullException(nameof(output));
            if (tick < LastDrainedTick)
                throw new ArgumentOutOfRangeException(nameof(tick), $"Ticks must not go backwards (last {LastDrainedTick}).");

            LastDrainedTick = tick;
            while (queue.Count > 0 && queue[queue.Count - 1].DueTick <= tick)
            {
                output.Add(queue[queue.Count - 1]);
                queue.RemoveAt(queue.Count - 1);
            }
        }

        /// <summary>
        /// Rebuilds a saved agenda: <paramref name="pending"/> in run order (as <see cref="SnapshotPending"/> gives it),
        /// every event due after <paramref name="lastDrainedTick"/>, with unique sequences below
        /// <paramref name="nextSequence"/>. Invalid input is rejected without building anything.
        /// </summary>
        public static TickScheduler<T> Restore(IReadOnlyList<ScheduledEvent<T>> pending, long nextSequence,
            long lastDrainedTick)
        {
            if (pending == null)
                throw new ArgumentNullException(nameof(pending));
            if (lastDrainedTick < -1)
                throw new ArgumentOutOfRangeException(nameof(lastDrainedTick), "Must be -1 or a processed tick.");
            if (nextSequence < 0)
                throw new ArgumentOutOfRangeException(nameof(nextSequence), "Must not be negative.");

            var sequences = new HashSet<long>();
            for (var i = 0; i < pending.Count; i++)
            {
                var item = pending[i];
                if (item.DueTick <= lastDrainedTick)
                    throw new ArgumentException($"Event {i} is due at tick {item.DueTick}, already processed.", nameof(pending));
                if (item.Sequence < 0 || item.Sequence >= nextSequence || !sequences.Add(item.Sequence))
                    throw new ArgumentException($"Event {i} has an invalid or repeated sequence {item.Sequence}.", nameof(pending));
                if (i > 0 && !RunsBefore(pending[i - 1], item))
                    throw new ArgumentException($"Event {i} is out of run order.", nameof(pending));
            }

            var scheduler = new TickScheduler<T> { NextSequence = nextSequence, LastDrainedTick = lastDrainedTick };
            for (var i = pending.Count - 1; i >= 0; i--)
                scheduler.queue.Add(pending[i]);
            return scheduler;
        }

        /// <summary>Copy of the pending events in the order they will run.</summary>
        public ScheduledEvent<T>[] SnapshotPending()
        {
            var pending = new ScheduledEvent<T>[queue.Count];
            for (var i = 0; i < pending.Length; i++)
                pending[i] = queue[queue.Count - 1 - i];
            return pending;
        }

        private static bool RunsBefore(ScheduledEvent<T> first, ScheduledEvent<T> second) =>
            first.DueTick < second.DueTick || (first.DueTick == second.DueTick && first.Sequence < second.Sequence);

        // A new event has the highest sequence, so it runs after every event already due at the same tick.
        private int FirstIndexDueAtOrBefore(long dueTick)
        {
            int low = 0, high = queue.Count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (queue[middle].DueTick > dueTick)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }
    }
}
