using System;

namespace Eden.Simulation
{
    /// <summary>
    /// Turns real frame time into a number of whole simulation ticks. The simulation itself never sees frame
    /// time: running faster means more complete ticks per frame, never a larger tick duration. When the machine
    /// cannot keep up, work beyond the per-frame and backlog limits is dropped and reported, so the requested
    /// speed is not reached but no rule is skipped. Unpaced, the caller runs as many whole ticks as fit in its own
    /// per-frame work budget and reports them, so that the effective speed is still measured.
    /// </summary>
    public sealed class FixedStepClock
    {
        public const float MinSpeed = 0.125f;
        public const float MaxSpeed = 64f;

        private const double MeasurementWindowSeconds = 0.5;

        // Tolerates floating error so that e.g. three frames of 1/60 s at 20 Hz yield exactly one tick.
        private const double TickEpsilon = 1e-9;

        private readonly int ticksPerSecond;
        private double pendingTicks;
        private int pendingSteps;
        private double windowSeconds;
        private long windowTicks;

        public FixedStepClock(int ticksPerSecond, int maxTicksPerFrame = 64, int maxBacklogTicks = 128)
        {
            if (ticksPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(ticksPerSecond), "Must be positive.");
            if (maxTicksPerFrame <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxTicksPerFrame), "Must be positive.");
            if (maxBacklogTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(maxBacklogTicks), "Must not be negative.");

            this.ticksPerSecond = ticksPerSecond;
            MaxTicksPerFrame = maxTicksPerFrame;
            MaxBacklogTicks = maxBacklogTicks;
            RequestedSpeed = 1f;
        }

        public int MaxTicksPerFrame { get; }
        public int MaxBacklogTicks { get; }
        public bool IsPaused { get; private set; }

        /// <summary>True when ticks are not paced by <see cref="RequestedSpeed"/> but by the caller's work budget.</summary>
        public bool IsUnpaced { get; private set; }

        /// <summary>Simulated seconds per real second the user asked for.</summary>
        public float RequestedSpeed { get; private set; }

        /// <summary>Simulated seconds per real second actually achieved over the last measurement window.</summary>
        public float EffectiveSpeed { get; private set; }

        /// <summary>Total ticks that were due but dropped because the limits were exceeded.</summary>
        public long DroppedTicks { get; private set; }

        /// <summary>
        /// How far real time has gone into the next tick, in [0, 1], for drawing between the last two ticks: the view
        /// shows previous + (current − previous) × fraction. It is 1 while paused or unpaced, where the latest state is
        /// shown as is. Presentation only; it never decides how many ticks run.
        /// </summary>
        public float TickFraction => IsPaused || IsUnpaced ? 1f : (float)Math.Min(1.0, pendingTicks);

        public void Pause()
        {
            IsPaused = true;
            pendingTicks = 0;
            ResetMeasurement();
        }

        public void Resume()
        {
            if (!IsPaused)
                return;
            IsPaused = false;
            pendingSteps = 0;
            pendingTicks = 0;
            ResetMeasurement();
        }

        /// <summary>Pauses if running and queues exactly one tick for the next <see cref="Advance"/>.</summary>
        public void RequestStep()
        {
            if (!IsPaused)
                Pause();
            pendingSteps++;
        }

        /// <summary>Switches between paced and unpaced running; nothing pending is carried over.</summary>
        public void SetUnpaced(bool unpaced)
        {
            if (unpaced == IsUnpaced)
                return;
            IsUnpaced = unpaced;
            pendingTicks = 0;
            ResetMeasurement();
        }

        /// <summary>Records the ticks the caller ran unpaced in a frame that took <paramref name="realDeltaSeconds"/>.</summary>
        public void RecordUnpaced(double realDeltaSeconds, int ticks)
        {
            if (!IsUnpaced || IsPaused)
                throw new InvalidOperationException("Only an unpaced, running clock records unpaced ticks.");
            if (double.IsNaN(realDeltaSeconds) || double.IsInfinity(realDeltaSeconds) || realDeltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(realDeltaSeconds), "Frame time must be finite and non-negative.");
            if (ticks < 0)
                throw new ArgumentOutOfRangeException(nameof(ticks), "Must not be negative.");
            Measure(realDeltaSeconds, ticks);
        }

        public void SetSpeed(float speed)
        {
            if (float.IsNaN(speed) || speed < MinSpeed || speed > MaxSpeed)
                throw new ArgumentOutOfRangeException(nameof(speed), $"Speed must be in [{MinSpeed}, {MaxSpeed}].");
            RequestedSpeed = speed;
        }

        /// <summary>
        /// Returns how many ticks to run for a frame that took <paramref name="realDeltaSeconds"/>: requested steps
        /// while paused, nothing while running unpaced (see <see cref="RecordUnpaced"/>), paced ticks otherwise.
        /// </summary>
        public int Advance(double realDeltaSeconds)
        {
            if (double.IsNaN(realDeltaSeconds) || double.IsInfinity(realDeltaSeconds) || realDeltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(realDeltaSeconds), "Frame time must be finite and non-negative.");

            int ticks;
            if (IsPaused)
            {
                ticks = Math.Min(pendingSteps, MaxTicksPerFrame);
                pendingSteps -= ticks;
            }
            else if (IsUnpaced)
            {
                return 0;
            }
            else
            {
                pendingTicks += realDeltaSeconds * RequestedSpeed * ticksPerSecond;
                var due = (long)Math.Floor(pendingTicks + TickEpsilon);
                var limit = (long)MaxTicksPerFrame + MaxBacklogTicks;
                if (due > limit)
                {
                    DroppedTicks += due - limit;
                    pendingTicks -= due - limit;
                    due = limit;
                }

                ticks = (int)Math.Min(due, MaxTicksPerFrame);
                pendingTicks = Math.Max(0, pendingTicks - ticks);
            }

            Measure(realDeltaSeconds, ticks);
            return ticks;
        }

        private void Measure(double realDeltaSeconds, int ticks)
        {
            windowSeconds += realDeltaSeconds;
            windowTicks += ticks;
            if (windowSeconds < MeasurementWindowSeconds)
                return;
            EffectiveSpeed = (float)(windowTicks / (windowSeconds * ticksPerSecond));
            windowSeconds = 0;
            windowTicks = 0;
        }

        private void ResetMeasurement()
        {
            windowSeconds = 0;
            windowTicks = 0;
            EffectiveSpeed = 0f;
        }
    }
}
