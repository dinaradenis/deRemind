using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace deRemind.Services
{
    public class OptimizedTimerManager : IDisposable
    {
        private readonly SortedSet<TimerEntry> _timerQueue = new();
        private readonly Timer _masterTimer;
        private readonly object _lock = new object();
        private readonly HashSet<int> _scheduledIds = new();

        public OptimizedTimerManager()
        {
            _masterTimer = new Timer(ProcessNextTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void ScheduleReminder(int reminderId, DateTime when, Func<Task> callback)
        {
            lock (_lock)
            {
                if (_scheduledIds.Contains(reminderId))
                    return;

                _timerQueue.Add(new TimerEntry(reminderId, when, callback));
                _scheduledIds.Add(reminderId);
                UpdateMasterTimer();
            }
        }

        private void UpdateMasterTimer()
        {
            if (_timerQueue.Count == 0)
            {
                _masterTimer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            var next = _timerQueue.Min!;
            var delay = next.When - DateTime.Now;

            if (delay.TotalMilliseconds <= 0)
                delay = TimeSpan.FromMilliseconds(1);

            _masterTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }

        private async void ProcessNextTimer(object? state)
        {
            TimerEntry? entry = null;

            lock (_lock)
            {
                if (_timerQueue.Count > 0)
                {
                    var next = _timerQueue.Min!;
                    if (next.When <= DateTime.Now)
                    {
                        _timerQueue.Remove(next);
                        _scheduledIds.Remove(next.ReminderId);
                        entry = next;
                    }
                }
                UpdateMasterTimer();
            }

            if (entry != null)
            {
                try
                {
                    await entry.Callback();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Timer callback error: {ex.Message}");
                }
            }
        }

        private record TimerEntry(int ReminderId, DateTime When, Func<Task> Callback)
            : IComparable<TimerEntry>
        {
            public int CompareTo(TimerEntry? other)
            {
                if (other == null) return 1;
                var result = When.CompareTo(other.When);
                return result == 0 ? ReminderId.CompareTo(other.ReminderId) : result;
            }
        }

        public bool IsReminderScheduled(int reminderId)
        {
            lock (_lock)
            {
                return _scheduledIds.Contains(reminderId);
            }
        }

        public void CancelReminder(int reminderId)
        {
            lock (_lock)
            {
                var toRemove = _timerQueue.FirstOrDefault(t => t.ReminderId == reminderId);
                if (toRemove != null)
                {
                    _timerQueue.Remove(toRemove);
                    _scheduledIds.Remove(reminderId);
                    UpdateMasterTimer();
                }
            }
        }


        public void Dispose()
        {
            lock (_lock)
            {
                _timerQueue.Clear();
                _scheduledIds.Clear();
                _masterTimer.Dispose();
            }
        }

    }
}
