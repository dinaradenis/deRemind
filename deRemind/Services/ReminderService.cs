using deRemind.Models;
using deRemind.Data;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace deRemind.Services
{
    public class HybridReminderService : IDisposable
    {
        private readonly ObservableCollection<Reminder> _reminders;
        private readonly Dictionary<int, Timer> _timers = new();
        private readonly Timer _backgroundCheckTimer;
        private readonly object _lockObject = new();
        private readonly SemaphoreSlim _dbSemaphore = new(1, 1);
        private bool _disposed = false;

        public HybridReminderService()
        {
            _reminders = new ObservableCollection<Reminder>();
            InitializeNotifications();

            // Background timer to check for missed reminders and reschedule long-term ones
            _backgroundCheckTimer = new Timer(BackgroundCheck, null, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1));

            _ = LoadRemindersAsync();
        }

        public ObservableCollection<Reminder> GetReminders() => _reminders;

        private async Task LoadRemindersAsync()
        {
            await _dbSemaphore.WaitAsync();
            try
            {
                using var context = new ReminderDbContext();
                await context.Database.EnsureCreatedAsync();

                var reminders = await context.Reminders
                    .Where(r => !r.IsCompleted || r.IsRepeating)
                    .OrderBy(r => r.ReminderDateTime)
                    .AsNoTracking() // Performance: No change tracking needed
                    .ToListAsync();

                var now = DateTime.Now;
                var toUpdate = new List<Reminder>();

                lock (_lockObject)
                {
                    _reminders.Clear();
                }

                foreach (var reminder in reminders)
                {
                    // Handle overdue notifications
                    if (reminder.ReminderDateTime <= now && !reminder.IsCompleted)
                    {
                        if (reminder.IsRepeating)
                        {
                            var nextOccurrence = CalculateNextOccurrence(reminder.ReminderDateTime, reminder.RepeatInterval, now);
                            reminder.ReminderDateTime = nextOccurrence;
                            toUpdate.Add(reminder);
                        }
                        else
                        {
                            ShowOverdueNotification(reminder);
                        }
                    }

                    lock (_lockObject)
                    {
                        _reminders.Add(reminder);
                    }

                    // Schedule notifications efficiently
                    if (reminder.ReminderDateTime > now && !reminder.IsCompleted)
                    {
                        ScheduleNotification(reminder);
                    }
                }

                // Batch update database operations
                if (toUpdate.Count > 0)
                {
                    await BatchUpdateRemindersAsync(toUpdate);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading reminders: {ex.Message}");
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        private async Task BatchUpdateRemindersAsync(List<Reminder> reminders)
        {
            try
            {
                using var context = new ReminderDbContext();
                context.Reminders.UpdateRange(reminders);
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error batch updating reminders: {ex.Message}");
            }
        }

        private void BackgroundCheck(object? state)
        {
            if (_disposed) return;

            try
            {
                var now = DateTime.Now;
                var remindersToCheck = new List<Reminder>();

                lock (_lockObject)
                {
                    remindersToCheck.AddRange(_reminders.Where(r => !r.IsCompleted));
                }

                // Check for missed reminders
                var missedReminders = remindersToCheck
                    .Where(r => r.ReminderDateTime <= now && !_timers.ContainsKey(r.Id))
                    .ToList();

                foreach (var reminder in missedReminders)
                {
                    ShowOverdueNotification(reminder);

                    if (reminder.IsRepeating)
                    {
                        reminder.ReminderDateTime = CalculateNextOccurrence(reminder.ReminderDateTime, reminder.RepeatInterval, now);
                        _ = UpdateReminderInDatabaseAsync(reminder);
                        ScheduleNotification(reminder);
                    }
                }

                // Schedule long-term reminders that are now within 24 hours
                var longTermReminders = remindersToCheck
                    .Where(r => r.ReminderDateTime > now &&
                               r.ReminderDateTime <= now.AddHours(24) &&
                               !_timers.ContainsKey(r.Id))
                    .ToList();

                foreach (var reminder in longTermReminders)
                {
                    ScheduleNotification(reminder);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in background check: {ex.Message}");
            }
        }

        private DateTime CalculateNextOccurrence(DateTime lastOccurrence, TimeSpan interval, DateTime currentTime)
        {
            var occurrencesPassed = (long)((currentTime - lastOccurrence).Ticks / interval.Ticks) + 1;
            return lastOccurrence.Add(TimeSpan.FromTicks(interval.Ticks * occurrencesPassed));
        }

        private void ShowOverdueNotification(Reminder reminder)
        {
            try
            {
                var notification = new AppNotificationBuilder()
                    .AddArgument("action", "overdue")
                    .AddArgument("reminderId", reminder.Id.ToString())
                    .AddText($"⚠️ OVERDUE: {reminder.Title}")
                    .AddText($"Was due: {reminder.ReminderDateTime:MMM dd, yyyy - hh:mm tt}")
                    .AddText(reminder.Description)
                    .SetScenario(AppNotificationScenario.Urgent)
                    .BuildNotification();

                notification.Tag = $"overdue_{reminder.Id}";
                AppNotificationManager.Default.Show(notification);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing overdue notification: {ex.Message}");
            }
        }

        public async Task AddReminderAsync(Reminder reminder)
        {
            await _dbSemaphore.WaitAsync();
            try
            {
                using var context = new ReminderDbContext();
                context.Reminders.Add(reminder);
                await context.SaveChangesAsync();

                // Get the generated ID
                lock (_lockObject)
                {
                    _reminders.Add(reminder);
                }

                ScheduleNotification(reminder);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding reminder: {ex.Message}");
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        private async Task UpdateReminderInDatabaseAsync(Reminder reminder)
        {
            await _dbSemaphore.WaitAsync();
            try
            {
                using var context = new ReminderDbContext();
                context.Entry(reminder).State = EntityState.Modified;
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating reminder: {ex.Message}");
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        private void ScheduleNotification(Reminder reminder)
        {
            if (reminder.ReminderDateTime <= DateTime.Now || reminder.IsCompleted)
                return;

            var delay = reminder.ReminderDateTime - DateTime.Now;

            // Only schedule in-memory timers for reminders within 24 hours
            if (delay.TotalHours > 24)
            {
                System.Diagnostics.Debug.WriteLine($"Long-term reminder {reminder.Id} will be scheduled later");
                return;
            }

            // Ensure minimum delay to prevent immediate firing
            if (delay.TotalSeconds < 1)
                delay = TimeSpan.FromSeconds(1);

            lock (_lockObject)
            {
                // Cancel existing timer if any
                if (_timers.TryGetValue(reminder.Id, out var existingTimer))
                {
                    existingTimer.Dispose();
                }

                var timer = new Timer(async (_) =>
                {
                    await HandleReminderTrigger(reminder);
                }, null, delay, Timeout.InfiniteTimeSpan);

                _timers[reminder.Id] = timer;
            }
        }

        private async Task HandleReminderTrigger(Reminder reminder)
        {
            try
            {
                ShowNotification(reminder);

                if (reminder.IsRepeating && !reminder.IsCompleted)
                {
                    reminder.ReminderDateTime = reminder.ReminderDateTime.Add(reminder.RepeatInterval);
                    await UpdateReminderInDatabaseAsync(reminder);

                    // Update in-memory collection
                    lock (_lockObject)
                    {
                        var existingReminder = _reminders.FirstOrDefault(r => r.Id == reminder.Id);
                        if (existingReminder != null)
                        {
                            existingReminder.ReminderDateTime = reminder.ReminderDateTime;
                        }
                    }

                    ScheduleNotification(reminder);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling reminder trigger: {ex.Message}");
            }
            finally
            {
                // Clean up the timer
                lock (_lockObject)
                {
                    if (_timers.TryGetValue(reminder.Id, out var timer))
                    {
                        timer.Dispose();
                        _timers.Remove(reminder.Id);
                    }
                }
            }
        }

        private void ShowNotification(Reminder reminder)
        {
            try
            {
                var notification = new AppNotificationBuilder()
                    .AddArgument("action", "remind")
                    .AddArgument("reminderId", reminder.Id.ToString())
                    .AddText(reminder.Title)
                    .AddText(reminder.Description)
                    .AddText($"⏰ {DateTime.Now:hh:mm tt}")
                    .SetScenario(AppNotificationScenario.Reminder)
                    .BuildNotification();

                notification.Tag = reminder.Id.ToString();
                notification.ExpiresOnReboot = false;

                AppNotificationManager.Default.Show(notification);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing notification: {ex.Message}");
            }
        }

        public async Task CompleteReminderAsync(int id)
        {
            await _dbSemaphore.WaitAsync();
            try
            {
                using var context = new ReminderDbContext();
                var reminder = await context.Reminders.FindAsync(id);
                if (reminder != null)
                {
                    reminder.IsCompleted = true;
                    await context.SaveChangesAsync();
                }

                lock (_lockObject)
                {
                    var localReminder = _reminders.FirstOrDefault(r => r.Id == id);
                    if (localReminder != null)
                    {
                        localReminder.IsCompleted = true;
                    }
                }

                CancelNotification(id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error completing reminder: {ex.Message}");
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public async Task DeleteReminderAsync(int id)
        {
            await _dbSemaphore.WaitAsync();
            try
            {
                using var context = new ReminderDbContext();
                var reminder = await context.Reminders.FindAsync(id);
                if (reminder != null)
                {
                    context.Reminders.Remove(reminder);
                    await context.SaveChangesAsync();
                }

                lock (_lockObject)
                {
                    var localReminder = _reminders.FirstOrDefault(r => r.Id == id);
                    if (localReminder != null)
                    {
                        _reminders.Remove(localReminder);
                    }
                }

                CancelNotification(id);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting reminder: {ex.Message}");
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        private void CancelNotification(int reminderId)
        {
            try
            {
                lock (_lockObject)
                {
                    if (_timers.TryGetValue(reminderId, out var timer))
                    {
                        timer.Dispose();
                        _timers.Remove(reminderId);
                    }
                }

                _ = AppNotificationManager.Default.RemoveByTagAsync(reminderId.ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error canceling notification: {ex.Message}");
            }
        }

        private void InitializeNotifications()
        {
            try
            {
                AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing notifications: {ex.Message}");
            }
        }

        private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine($"Notification activated with args: {args.Argument}");
        }

        // Synchronous wrapper methods for backward compatibility
        public void AddReminder(Reminder reminder) => _ = AddReminderAsync(reminder);
        public void DeleteReminder(int id) => _ = DeleteReminderAsync(id);
        public void CompleteReminder(int id) => _ = CompleteReminderAsync(id);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _backgroundCheckTimer?.Dispose();

            lock (_lockObject)
            {
                foreach (var timer in _timers.Values)
                {
                    timer.Dispose();
                }
                _timers.Clear();
            }

            _dbSemaphore?.Dispose();
        }
    }
}