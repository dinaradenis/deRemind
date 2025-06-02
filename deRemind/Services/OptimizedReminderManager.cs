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
using Microsoft.UI.Xaml;
using System.Collections.Concurrent;
using Microsoft.UI.Dispatching;
using System.ComponentModel;

namespace deRemind.Services
{
    public class OptimizedReminderService : IDisposable
    {
        private readonly ObservableCollection<Reminder> _reminders;
        private readonly Timer _unifiedTimer;
        private readonly SemaphoreSlim _dbSemaphore = new(1, 1);
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly DispatcherQueue? _dispatcherQueue;
        private bool _disposed = false;

        // Cache for performance
        private readonly Dictionary<int, DateTime> _nextNotificationTimes = new();
        private readonly HashSet<int> _processedOverdueReminders = new();

        // Batching support
        private readonly ConcurrentQueue<Reminder> _pendingUpdates = new();
        private readonly Timer _batchUpdateTimer;

        public OptimizedReminderService(DispatcherQueue? dispatcherQueue = null)
        {
            _reminders = new ObservableCollection<Reminder>();
            _dispatcherQueue = dispatcherQueue;

            // Single unified timer instead of multiple timers
            _unifiedTimer = new Timer(ProcessAllReminders, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

            // Batch update timer
            _batchUpdateTimer = new Timer(ProcessBatchUpdates, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            InitializeNotifications();
            _ = InitializeAsync();
        }

        public ObservableCollection<Reminder> GetReminders() => _reminders;

        private async Task InitializeAsync()
        {
            await LoadRemindersAsync();
        }

        private async Task LoadRemindersAsync()
        {
            if (_disposed) return;

            await _dbSemaphore.WaitAsync(_cancellationTokenSource.Token);
            try
            {
                using var context = new ReminderDbContext();
                await context.Database.EnsureCreatedAsync(_cancellationTokenSource.Token);

                var now = DateTime.Now;
                var cutoffDate = DateTime.Now.AddDays(-7);
                var reminders = await context.Reminders
                    .Where(r => !r.IsCompleted ||
                               (r.IsCompleted && r.ReminderDateTime > cutoffDate) ||
                               r.IsRepeating)
                    .Select(r => new Reminder
                    {
                        Id = r.Id,
                        Title = r.Title,
                        Description = r.Description,
                        ReminderDateTime = r.ReminderDateTime,
                        IsCompleted = r.IsCompleted,
                        IsRepeating = r.IsRepeating,
                        RepeatInterval = r.RepeatInterval
                    })
                    .OrderBy(r => r.ReminderDateTime)
                    .AsNoTracking()
                    .ToListAsync(_cancellationTokenSource.Token);

                await ProcessLoadedReminders(reminders, now);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading reminders: {ex.Message}");
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        private async Task ProcessLoadedReminders(List<Reminder> reminders, DateTime now)
        {
            var toUpdate = new List<Reminder>();
            var processedReminders = new List<Reminder>();

            // Process all reminders in a single pass
            foreach (var reminder in reminders)
            {
                if (reminder.ReminderDateTime <= now && !reminder.IsCompleted && reminder.IsRepeating)
                {
                    reminder.ReminderDateTime = CalculateNextOccurrence(reminder.ReminderDateTime, reminder.RepeatInterval, now);
                    toUpdate.Add(reminder);
                }
                else if (reminder.ReminderDateTime <= now && !reminder.IsCompleted && !reminder.IsRepeating
                         && !_processedOverdueReminders.Contains(reminder.Id))
                {
                    _ = Task.Run(() => ShowOverdueNotification(reminder));
                    _processedOverdueReminders.Add(reminder.Id);
                }

                processedReminders.Add(reminder);

                // Cache next notification time
                if (reminder.ReminderDateTime > now && !reminder.IsCompleted)
                {
                    _nextNotificationTimes[reminder.Id] = reminder.ReminderDateTime;
                }
            }

            // Single UI update
            UpdateUICollection(() =>
            {
                _reminders.Clear();
                foreach (var reminder in processedReminders)
                {
                    _reminders.Add(reminder);
                }
            });

            // Batch database update
            if (toUpdate.Count > 0)
            {
                await BatchUpdateRemindersAsync(toUpdate);
            }
        }

        // Unified timer callback - processes all reminders in one go
        private async void ProcessAllReminders(object? state)
        {
            if (_disposed) return;

            try
            {
                var now = DateTime.Now;
                var remindersToNotify = new List<Reminder>();
                var remindersToUpdate = new List<Reminder>();

                // Get snapshot of reminders
                var currentReminders = new List<Reminder>();
                UpdateUICollection(() => currentReminders.AddRange(_reminders.Where(r => !r.IsCompleted)));

                foreach (var reminder in currentReminders)
                {
                    // Check if it's time for notification
                    if (_nextNotificationTimes.TryGetValue(reminder.Id, out var nextTime) && now >= nextTime)
                    {
                        remindersToNotify.Add(reminder);
                        _nextNotificationTimes.Remove(reminder.Id);

                        if (reminder.IsRepeating)
                        {
                            reminder.ReminderDateTime = reminder.ReminderDateTime.Add(reminder.RepeatInterval);
                            _nextNotificationTimes[reminder.Id] = reminder.ReminderDateTime;
                            remindersToUpdate.Add(reminder);
                        }
                    }
                }

                // Process notifications
                foreach (var reminder in remindersToNotify)
                {
                    ShowNotification(reminder);
                }

                // Batch update database and UI
                if (remindersToUpdate.Count > 0)
                {
                    await BatchUpdateRemindersAsync(remindersToUpdate);

                    UpdateUICollection(() =>
                    {
                        foreach (var updated in remindersToUpdate)
                        {
                            var existing = _reminders.FirstOrDefault(r => r.Id == updated.Id);
                            if (existing != null)
                            {
                                existing.ReminderDateTime = updated.ReminderDateTime;
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in unified timer: {ex.Message}");
            }
        }

        private async void ProcessBatchUpdates(object? state)
        {
            if (_disposed || _pendingUpdates.IsEmpty) return;

            var updates = new List<Reminder>();
            while (_pendingUpdates.TryDequeue(out var reminder))
            {
                updates.Add(reminder);
            }

            if (updates.Count > 0)
            {
                await BatchUpdateRemindersAsync(updates);
            }
        }

        private async Task BatchUpdateRemindersAsync(List<Reminder> reminders)
        {
            if (_disposed || reminders.Count == 0) return;

            await _dbSemaphore.WaitAsync(_cancellationTokenSource.Token);
            try
            {
                using var context = new ReminderDbContext();

                // Use ExecuteUpdate for better performance on bulk updates
                var reminderIds = reminders.Select(r => r.Id).ToList();
                var reminderLookup = reminders.ToDictionary(r => r.Id);

                await context.Reminders
                    .Where(r => reminderIds.Contains(r.Id))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(r => r.ReminderDateTime, r => reminderLookup[r.Id].ReminderDateTime)
                        .SetProperty(r => r.IsCompleted, r => reminderLookup[r.Id].IsCompleted),
                        _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in batch update: {ex.Message}");
                // Fallback to individual updates
                await FallbackIndividualUpdates(reminders);
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        private async Task FallbackIndividualUpdates(List<Reminder> reminders)
        {
            try
            {
                using var context = new ReminderDbContext();
                context.Reminders.UpdateRange(reminders);
                await context.SaveChangesAsync(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fallback update failed: {ex.Message}");
            }
        }

        private DateTime CalculateNextOccurrence(DateTime lastOccurrence, TimeSpan interval, DateTime currentTime)
        {
            if (interval.TotalMilliseconds <= 0)
                return currentTime.AddHours(1);

            var occurrencesPassed = (long)Math.Ceiling((currentTime - lastOccurrence).TotalMilliseconds / interval.TotalMilliseconds);
            return lastOccurrence.Add(TimeSpan.FromMilliseconds(interval.TotalMilliseconds * occurrencesPassed));
        }

        public async Task AddReminderAsync(Reminder reminder)
        {
            if (_disposed) return;

            await _dbSemaphore.WaitAsync(_cancellationTokenSource.Token);
            try
            {
                using var context = new ReminderDbContext();
                context.Reminders.Add(reminder);
                await context.SaveChangesAsync(_cancellationTokenSource.Token);

                UpdateUICollection(() => _reminders.Add(reminder));

                // Cache notification time
                if (reminder.ReminderDateTime > DateTime.Now && !reminder.IsCompleted)
                {
                    _nextNotificationTimes[reminder.Id] = reminder.ReminderDateTime;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding reminder: {ex.Message}");
                throw;
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        public async Task CompleteReminderAsync(int id)
        {
            if (_disposed) return;

            // Queue for batch processing instead of immediate update
            var reminder = _reminders.FirstOrDefault(r => r.Id == id);
            if (reminder != null)
            {
                reminder.IsCompleted = true;
                _pendingUpdates.Enqueue(reminder);
                _nextNotificationTimes.Remove(id);

                UpdateUICollection(() =>
                {
                    var localReminder = _reminders.FirstOrDefault(r => r.Id == id);
                    if (localReminder != null)
                    {
                        localReminder.IsCompleted = true;
                    }
                });
            }
        }

        public async Task DeleteReminderAsync(int id)
        {
            if (_disposed) return;

            await _dbSemaphore.WaitAsync(_cancellationTokenSource.Token);
            try
            {
                using var context = new ReminderDbContext();
                await context.Reminders.Where(r => r.Id == id)
                    .ExecuteDeleteAsync(_cancellationTokenSource.Token);

                UpdateUICollection(() =>
                {
                    var reminder = _reminders.FirstOrDefault(r => r.Id == id);
                    if (reminder != null)
                    {
                        _reminders.Remove(reminder);
                    }
                });

                _nextNotificationTimes.Remove(id);
                _processedOverdueReminders.Remove(id);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting reminder: {ex.Message}");
                throw;
            }
            finally
            {
                _dbSemaphore.Release();
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

        private void UpdateUICollection(Action action)
        {
            if (_dispatcherQueue?.HasThreadAccess == true)
            {
                action();
            }
            else
            {
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error updating UI collection: {ex.Message}");
                    }
                });
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cancellationTokenSource.Cancel();

            _unifiedTimer?.Dispose();
            _batchUpdateTimer?.Dispose();

            _dbSemaphore?.Dispose();
            _cancellationTokenSource?.Dispose();

            _nextNotificationTimes.Clear();
            _processedOverdueReminders.Clear();
        }
    }
}