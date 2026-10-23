using System;

namespace Eden.Simulation
{
    /// <summary>
    /// Turns real frame time into a number of whole simulation ticks. The simulation itself never sees frame
    /// time: running faster means more complete ticks per frame, never a larger tick duration. When the machine
    /// cannot keep up, work beyond the per-frame and backlog limits is dropped and reported, so the requested
    /// speed is not reached but no rule is skipped.
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

        /// <summary>Simulated seconds per real second the user asked for.</summary>
        public float RequestedSpeed { get; private set; }

        /// <summary>Simulated seconds per real second actually achieved over the last measurement window.</summary>
        public float EffectiveSpeed { get; private set; }

        /// <summary>Total ticks that were due but dropped because the limits were exceeded.</summary>
        public long DroppedTicks { get; private set; }

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

        public void SetSpeed(float speed)
        {
            if (float.IsNaN(speed) || speed < MinSpeed || speed > MaxSpeed)
                throw new ArgumentOutOfRangeException(nameof(speed), $"Speed must be in [{MinSpeed}, {MaxSpeed}].");
            RequestedSpeed = speed;
        }

        /// <summary>Returns how many ticks to run for a frame that took <paramref name="realDeltaSeconds"/>.</summary>
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
