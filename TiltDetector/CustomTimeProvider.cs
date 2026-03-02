using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TiltDetector
{
    public class CustomTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        private readonly List<CustomTimer> _timers = new();
        private readonly object _lock = new();

        public CustomTimeProvider(DateTimeOffset startTime)
        {
            _utcNow = startTime;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public bool Advance(DateTimeOffset newTime)
        {
            CustomTimer[] timersSnapshot;

            lock (_lock)
            {
                if (newTime <= _utcNow)
                    return false;

                _utcNow = newTime;
                // Snapshot the list to prevent Collection Modified exceptions
                timersSnapshot = _timers.ToArray();
            }

            // Execute callbacks outside the lock to prevent deadlocks
            foreach (var timer in timersSnapshot)
            {
                timer.TryFire(newTime);
            }

            return true;
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            CustomTimer timer;
            lock (_lock)
            {
                timer = new CustomTimer(this, callback, state, dueTime, period);
                _timers.Add(timer);
            }

            if (dueTime <= TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
            {
                timer.TryFire(GetUtcNow());
            }

            return timer;
        }

        private class CustomTimer : ITimer
        {
            private readonly CustomTimeProvider _provider;
            private readonly TimerCallback _callback;
            private readonly object? _state;

            private TimeSpan _period;
            private DateTimeOffset _nextFire;
            private bool _isDisposed;
            private readonly object _timerLock = new();

            // The Executing Context Magic
            private DateTimeOffset? _executingTickTime;

            public CustomTimer(
                CustomTimeProvider provider,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period
            )
            {
                _provider = provider;
                _callback = callback;
                _state = state;
                _period = period;

                var now = _provider.GetUtcNow();
                _nextFire =
                    dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : now + dueTime;
            }

            public void TryFire(DateTimeOffset now)
            {
                lock (_timerLock)
                {
                    if (_isDisposed || now < _nextFire)
                        return;

                    if (_period == Timeout.InfiniteTimeSpan)
                        _nextFire = DateTimeOffset.MaxValue;
                    else
                        _nextFire = now + _period;
                }

                // Store the exact context time BEFORE firing the callback
                _executingTickTime = now;

                try
                {
                    // Invoke the user's logic outside the lock
                    _callback(_state);
                }
                finally
                {
                    // Clear the context when the callback finishes
                    _executingTickTime = null;
                }
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                bool fireNow = false;
                DateTimeOffset currentGlobalTime;

                lock (_timerLock)
                {
                    if (_isDisposed)
                        return false;

                    _period = period;

                    // Prioritize the executing context time over the global provider time
                    var now = _executingTickTime ?? _provider.GetUtcNow();

                    _nextFire =
                        dueTime == Timeout.InfiniteTimeSpan
                            ? DateTimeOffset.MaxValue
                            : now + dueTime;

                    // If Change() is explicitly given TimeSpan.Zero, it should fire immediately
                    if (dueTime <= TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
                    {
                        fireNow = true;
                    }

                    currentGlobalTime = _provider.GetUtcNow();
                }

                if (fireNow)
                {
                    TryFire(currentGlobalTime);
                }

                return true;
            }

            public void Dispose()
            {
                lock (_timerLock)
                {
                    if (_isDisposed)
                        return;
                    _isDisposed = true;
                }

                // Safely remove itself from the parent's list
                lock (_provider._lock)
                {
                    _provider._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
