using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Eden.Evolution;

namespace Eden.Persistence
{
    /// <summary>One bounded, ordered background writer. Reports contain immutable genomes and completed episode arrays;
    /// their ownership is retained until committed. No Unity API is called on this thread.</summary>
    public sealed class BufferedEvolutionLog : IDisposable
    {
        private readonly BlockingCollection<Action> pending;
        private readonly GenerationSegmentWriter store;
        private readonly string directory;
        private readonly ulong experimentHash;
        private readonly Task worker;
        private readonly ConcurrentDictionary<TaskCompletionSource<bool>, bool> saves = new ConcurrentDictionary<TaskCompletionSource<bool>, bool>();
        private Exception error;
        private Action failedWork;
        private int outstanding;
        private bool closed;
        public BufferedEvolutionLog(string directory, ExperimentHeader header, int capacity = 2, bool compatibilityCsv = false)
        {
            if (capacity < 1 || capacity > 8) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.directory = directory; experimentHash = header.ConfigHash;
            store = new GenerationSegmentWriter(directory, header, compatibilityCsv);
            pending = new BlockingCollection<Action>(capacity);
            worker = Task.Factory.StartNew(Write, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        public int PendingCount => Volatile.Read(ref outstanding);
        public Exception Error => Volatile.Read(ref error);
        public bool IsIdle => PendingCount == 0;
        public bool HasCapacity => !closed && Error == null && pending.Count < pending.BoundedCapacity;
        public string LastCommittedSegment => store.LastCommittedSegment;
        public bool TryAppend(GenerationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            return TryQueue(() => store.Append(report));
        }
        public Task TrySaveCheckpoint(string path, Checkpoint checkpoint, string appVersion, string runtime, DateTime utc)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            saves.TryAdd(completion, true);
            var accepted = TryQueue(() =>
            {
                try
                {
                    var info = CheckpointFile.Save(path, checkpoint, appVersion, runtime, utc);
                    MetricsCheckpointCursor.Write(path, info, directory, store.LastCommittedSegment, experimentHash);
                    completion.SetResult(true); saves.TryRemove(completion, out _);
                }
                catch (Exception failure) { completion.SetException(failure); throw; }
            });
            if (!accepted) saves.TryRemove(completion, out _);
            return accepted ? completion.Task : null;
        }
        private bool TryQueue(Action action)
        {
            if (closed || Error != null) return false;
            Interlocked.Increment(ref outstanding);
            if (pending.TryAdd(action)) return true;
            Interlocked.Decrement(ref outstanding);
            return false;
        }
        private void Write()
        {
            foreach (var action in pending.GetConsumingEnumerable())
            {
                try { action(); Interlocked.Decrement(ref outstanding); }
                catch (Exception failure)
                {
                    failedWork = action; // Retain the failed batch; stop accepting work and expose the failure.
                    Volatile.Write(ref error, failure);
                    foreach (var save in saves.Keys) save.TrySetException(failure);
                    return;
                }
            }
        }
        public void Drain()
        {
            if (Error != null) throw new System.IO.IOException("Export stopped; pending results were retained.", Error);
            while (!IsIdle && Error == null) Thread.Sleep(1);
            if (Error != null) throw new System.IO.IOException("Export stopped; pending results were retained.", Error);
        }
        public void Dispose()
        {
            if (closed) return;
            closed = true;
            pending.CompleteAdding();
            worker.GetAwaiter().GetResult(); // Only closing a run drains synchronously; generation completion never waits.
            store.Dispose();
            pending.Dispose();
            GC.KeepAlive(failedWork);
        }
    }
}
