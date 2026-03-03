using System;
using System.Threading;
using Xunit;

namespace TiltDetector.Test
{
    public class CustomTimeProviderTests
    {
        private readonly DateTimeOffset _start = new DateTimeOffset(
            year: 2026,
            month: 3,
            day: 1,
            hour: 12,
            minute: 0,
            second: 0,
            offset: TimeSpan.Zero
        );
        private readonly TimeSpan _period = TimeSpan.FromMinutes(1);

        [Fact]
        public void GetUtcNow_ReturnsStartTime()
        {
            var tp = new CustomTimeProvider(_start);
            Assert.Equal(_start, tp.GetUtcNow());
        }

        [Fact]
        public void Advance_FiresTimer_WhenPeriodElapsed()
        {
            var tp = new CustomTimeProvider(_start);
            int count = 0;
            tp.CreateTimer(_ => count++, null, _period, _period);

            tp.Advance(_start + _period);

            Assert.Equal(1, count);
        }

        [Fact]
        public void Advance_DoesNotFireTimer_BeforeDueTime()
        {
            var tp = new CustomTimeProvider(_start);
            int count = 0;
            tp.CreateTimer(_ => count++, null, _period, _period);

            tp.Advance(_start + TimeSpan.FromSeconds(30));

            Assert.Equal(0, count);
        }

        [Fact]
        public void Advance_FiresTimerMultipleTimes_AcrossMultiplePeriods()
        {
            var tp = new CustomTimeProvider(_start);
            int count = 0;
            tp.CreateTimer(_ => count++, null, _period, _period);

            tp.Advance(_start + _period);
            tp.Advance(_start + _period * 2);
            tp.Advance(_start + _period * 3);

            Assert.Equal(3, count);
        }

        [Fact]
        public void TimerDispose_PreventsFiring_AndPreventsMemoryLeaks()
        {
            var tp = new CustomTimeProvider(_start);
            int count = 0;
            var timer = tp.CreateTimer(_ => count++, null, _period, _period);

            // Fire once
            tp.Advance(_start + _period);
            Assert.Equal(1, count);

            // Dispose the timer (This triggers the memory leak fix removing it from the internal list)
            timer.Dispose();

            // Advance time again, should NOT fire
            tp.Advance(_start + _period * 2);
            Assert.Equal(1, count);
        }

        [Fact]
        public void CustomTimeProvider_AllowsCreatingNewTimer_InsideCallbackWithoutCrashing()
        {
            var tp = new CustomTimeProvider(_start);
            int initialTimerFired = 0;
            int nestedTimerFired = 0;

            // This timer will create ANOTHER timer inside its callback.
            tp.CreateTimer(
                _ =>
                {
                    initialTimerFired++;
                    tp.CreateTimer(_ => nestedTimerFired++, null, _period, _period);
                },
                null,
                _period,
                Timeout.InfiniteTimeSpan
            );

            // Fire the first timer. It creates the nested timer.
            tp.Advance(_start + _period);

            // Advance again to fire the nested timer.
            tp.Advance(_start + _period * 2);

            Assert.Equal(1, initialTimerFired);
            Assert.Equal(1, nestedTimerFired);
        }

        [Fact]
        public void CustomTimeProvider_AllowsDisposingTimer_InsideCallbackWithoutCrashing()
        {
            var tp = new CustomTimeProvider(_start);
            int count = 0;
            ITimer? timer = null;

            timer = tp.CreateTimer(
                _ =>
                {
                    count++;
                    timer?.Dispose(); // Timer commits suicide during its own callback
                },
                null,
                _period,
                _period
            );

            // This will crash with a Collection Modified exception if the list isn't snapshotted
            tp.Advance(_start + _period);

            // Advance again to prove it actually died
            tp.Advance(_start + _period * 2);

            Assert.Equal(1, count);
        }

        [Fact]
        public void Change_UpdatesSchedule_WhenCalledOutsideCallback()
        {
            var tp = new CustomTimeProvider(_start);
            int count = 0;
            var timer = tp.CreateTimer(_ => count++, null, Timeout.InfiniteTimeSpan, _period);

            tp.Advance(_start + _period);
            Assert.Equal(0, count); // Didn't fire because dueTime was Infinite

            // Manually change it to fire 1 minute from "now"
            timer.Change(TimeSpan.FromMinutes(1), _period);

            tp.Advance(tp.GetUtcNow() + TimeSpan.FromMinutes(1));
            Assert.Equal(1, count);
        }

        [Fact]
        public void Change_ExecutingContextFix_WorksSafelyInsideCallback()
        {
            var tp = new CustomTimeProvider(_start);
            int count = 0;
            ITimer? timer = null;

            // The timer starts with an infinite period (meaning it will only fire once)
            timer = tp.CreateTimer(
                _ =>
                {
                    count++;
                    // INSIDE THE CALLBACK: Tell the timer to fire again in 5 minutes.
                    // This triggers the _executingTickTime logic.
                    timer?.Change(TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);
                },
                null,
                TimeSpan.FromMinutes(1),
                Timeout.InfiniteTimeSpan
            );

            // Fire the initial tick at +1 minute
            tp.Advance(_start + TimeSpan.FromMinutes(1));
            Assert.Equal(1, count);

            // Advance 4 minutes (total 5 mins passed). Shouldn't fire yet.
            tp.Advance(_start + TimeSpan.FromMinutes(5));
            Assert.Equal(1, count);

            // Advance to the 6 minute mark (1 min initial + 5 min Change delay). Should fire!
            tp.Advance(_start + TimeSpan.FromMinutes(6));
            Assert.Equal(2, count);
        }

        [Fact]
        public void CreateTimer_FiresImmediately_WhenDueTimeIsZero()
        {
            var tp = new CustomTimeProvider(_start);
            int count = 0;

            // Create timer with TimeSpan.Zero
            tp.CreateTimer(_ => count++, null, TimeSpan.Zero, _period);

            // Should fire immediately WITHOUT calling Advance
            Assert.Equal(1, count);
        }

        [Fact]
        public void CreateTimer_FiresImmediately_WhenDueTimeIsNegative()
        {
            var tp = new CustomTimeProvider(_start);
            int count = 0;

            // Create timer with a negative TimeSpan
            tp.CreateTimer(_ => count++, null, TimeSpan.FromMilliseconds(-5), _period);

            // Should fire immediately (proves the <= logic works)
            Assert.Equal(1, count);
        }

        [Fact]
        public void Change_FiresImmediately_WhenDueTimeIsZero()
        {
            var tp = new CustomTimeProvider(_start);
            int count = 0;

            // Create a timer paused indefinitely
            var timer = tp.CreateTimer(_ => count++, null, Timeout.InfiniteTimeSpan, _period);
            Assert.Equal(0, count);

            // Change it to fire instantly
            timer.Change(TimeSpan.Zero, _period);

            // Should fire immediately without calling Advance
            Assert.Equal(1, count);
        }

        [Fact]
        public void Advance_MaintainsAbsoluteSchedule_AndPreventsTimerDrift()
        {
            var tp = new CustomTimeProvider(_start);
            int count = 0;

            // Create timer with 1-minute due time and 1-minute period
            tp.CreateTimer(_ => count++, null, _period, _period);

            // Simulate delayed execution: Advance 1 minute + 15 seconds
            var drift = TimeSpan.FromSeconds(15);
            tp.Advance(_start + _period + drift);

            // Timer should fire once for the first period
            Assert.Equal(1, count);

            // Advance exactly to the 2-minute mark
            tp.Advance(_start + _period * 2);

            // _nextFire catches up strictly by _period intervals (to 2m00s).
            // Count should be 2!
            Assert.Equal(2, count);
        }
    }
}
