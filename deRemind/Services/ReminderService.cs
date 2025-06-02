using deRemind.Models;
using deRemind.Data;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using System.Collections.Generic;

namespace deRemind.Services
{
    public class ReminderService : IDisposable
    {
        private readonly ObservableCollection<Reminder> _reminders;
        private readonly Timer _processingTimer;
        private readonly SemaphoreSlim _dbSemaphore = new(1, 1);
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly DispatcherQueue? _dispatcherQueue;
        private bool _disposed = false;

        // High-performance collections
        private readonly ConcurrentDictionary<int, DateTime> _nextNotificationTimes = new();
        private readonly ConcurrentHashSet<int> _processedOverdueReminders = new();
        private readonly ConcurrentQueue<DatabaseOperation> _databaseOperations = new();

        // Batch processing
        private readonly Timer _batchProcessor;
        private volatile bool _processingBatch = false;

        // Performance metrics
        private DateTime _lastProcessingTime = DateTime.MinValue;
        private readonly object _statsLock = new();
        private int _processedNotifications = 0;

        public ReminderService(DispatcherQueue? dispatcherQueue = null)
        {
            _reminders = new ObservableCollection<Reminder>();
            _dispatcherQueue = dispatcherQueue;

            _processingTimer = new Timer(ProcessAllOperations, null,
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            _batchProcessor = new Timer(ProcessDatabaseBatch, null,
                TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

            InitializeNotifications();
            _ = InitializeAsync();
        }

        public ObservableCollection<Reminder> GetReminders() => _reminders;

        public async Task InitializeAsync()
        {
            await LoadRemindersAsync();
        }

        private async Task LoadRemindersAsync()
        {
            if (_disposed) return;

            await _dbSemaphore.WaitAsync(_cancellationTokenSource.Token);
            try
            {
                var context = DatabaseService.Instance.GetContext();
                var now = DateTime.Now;
                var cutoffDate = now.AddDays(-7);
                var reminderData = await context.Reminders
                    .Where(r => !r.IsCompleted || (r.IsCompleted && r.ReminderDateTime > cutoffDate) || r.IsRepeating)
                    .Select(r => new
                    {
                        r.Id,
                        r.Title,
                        r.Description,
                        r.ReminderDateTime,
                        r.IsCompleted,
                        r.IsRepeating,
                        r.RepeatInterval
                    })
                    .OrderBy(r => r.ReminderDateTime)
                    .AsNoTracking()
                    .ToListAsync(_cancellationTokenSource.Token);

                var reminders = reminderData.Select(r => new Reminder
                {
                    Id = r.Id,
                    Title = r.Title,
                    Description = r.Description,
                    ReminderDateTime = r.ReminderDateTime,
                    IsCompleted = r.IsCompleted,
                    IsRepeating = r.IsRepeating,
                    RepeatInterval = r.RepeatInterval
                }).ToList();

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
            var batchOperations = new List<DatabaseOperation>();
            var processedReminders = new List<Reminder>();

            foreach (var reminder in reminders)
            {
                if (ShouldProcessReminder(reminder, now))
                {
                    if (reminder.IsRepeating)
                    {
                        reminder.ReminderDateTime = CalculateNextOccurrence(
                            reminder.ReminderDateTime, reminder.RepeatInterval, now);
                        batchOperations.Add(new DatabaseOperation(OperationType.Update, reminder));
                    }
                    else
                    {
                        ProcessOverdueReminder(reminder);
                    }
                }

                processedReminders.Add(reminder);
                CacheNotificationTime(reminder, now);
            }

            UpdateUICollection(() =>
            {
                _reminders.Clear();
                foreach (var reminder in processedReminders)
                {
                    _reminders.Add(reminder);
                }
            });

            foreach (var operation in batchOperations)
            {
                _databaseOperations.Enqueue(operation);
            }
        }

        private bool ShouldProcessReminder(Reminder reminder, DateTime now)
        {
            return reminder.ReminderDateTime <= now &&
                   !reminder.IsCompleted &&
                   !_processedOverdueReminders.Contains(reminder.Id);
        }

        private void ProcessOverdueReminder(Reminder reminder)
        {
            _ = Task.Run(() => ShowOverdueNotification(reminder));
            _processedOverdueReminders.TryAdd(reminder.Id);
        }

        private void CacheNotificationTime(Reminder reminder, DateTime now)
        {
            if (reminder.ReminderDateTime > now && !reminder.IsCompleted)
            {
                _nextNotificationTimes.TryAdd(reminder.Id, reminder.ReminderDateTime);
            }
        }

        private async void ProcessAllOperations(object? state)
        {
            if (_disposed) return;

            var now = DateTime.Now;
            if (now - _lastProcessingTime < TimeSpan.FromSeconds(25))
                return;

            _lastProcessingTime = now;

            try
            {
                var notificationTasks = new List<Task>();
                var updateOperations = new List<DatabaseOperation>();
                var toRemove = new List<int>();

                foreach (var kvp in _nextNotificationTimes)
                {
                    if (now >= kvp.Value)
                    {
                        var reminder = FindReminderById(kvp.Key);
                        if (reminder != null)
                        {
                            notificationTasks.Add(Task.Run(() => ShowNotification(reminder)));
                            if (reminder.IsRepeating)
                            {
                                reminder.ReminderDateTime = reminder.ReminderDateTime.Add(reminder.RepeatInterval);
                                _nextNotificationTimes.TryUpdate(kvp.Key, reminder.ReminderDateTime, kvp.Value);
                                updateOperations.Add(new DatabaseOperation(OperationType.Update, reminder));
                            }
                            else
                            {
                                toRemove.Add(kvp.Key);
                            }
                        }
                    }
                }

                foreach (var id in toRemove)
                {
                    _nextNotificationTimes.TryRemove(id, out _);
                }

                foreach (var operation in updateOperations)
                {
                    _databaseOperations.Enqueue(operation);
                }

                if (notificationTasks.Count > 0)
                {
                    await Task.WhenAll(notificationTasks);
                    lock (_statsLock)
                    {
                        _processedNotifications += notificationTasks.Count;
                    }
                }

                if (updateOperations.Count > 0)
                {
                    UpdateUICollection(() =>
                    {
                        foreach (var op in updateOperations)
                        {
                            var existing = _reminders.FirstOrDefault(r => r.Id == op.Reminder.Id);
                            if (existing != null)
                            {
                                existing.ReminderDateTime = op.Reminder.ReminderDateTime;
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in processing: {ex.Message}");
            }
        }

        private Reminder? FindReminderById(int id)
        {
            return _reminders.FirstOrDefault(r => r.Id == id);
        }

        private async void ProcessDatabaseBatch(object? state)
        {
            if (_disposed || _processingBatch || _databaseOperations.IsEmpty) return;

            _processingBatch = true;
            try
            {
                var operations = new List<DatabaseOperation>();
                while (_databaseOperations.TryDequeue(out var operation) && operations.Count < 100)
                {
                    operations.Add(operation);
                }

                if (operations.Count > 0)
                {
                    await ExecuteBatchOperations(operations);
                }
            }
            finally
            {
                _processingBatch = false;
            }
        }

        private async Task ExecuteBatchOperations(List<DatabaseOperation> operations)
        {
            await _dbSemaphore.WaitAsync(_cancellationTokenSource.Token);
            try
            {
                var context = DatabaseService.Instance.GetContext();
                var updates = operations.Where(o => o.Type == OperationType.Update).ToList();
                var deletes = operations.Where(o => o.Type == OperationType.Delete).ToList();

                if (updates.Count > 0)
                {
                    var updateIds = updates.Select(u => u.Reminder.Id).ToList();
                    var updateLookup = updates.ToDictionary(u => u.Reminder.Id, u => u.Reminder);

                    await context.Reminders
                        .Where(r => updateIds.Contains(r.Id))
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(r => r.ReminderDateTime, r => updateLookup[r.Id].ReminderDateTime)
                            .SetProperty(r => r.IsCompleted, r => updateLookup[r.Id].IsCompleted),
                            _cancellationTokenSource.Token);
                }

                if (deletes.Count > 0)
                {
                    var deleteIds = deletes.Select(d => d.Reminder.Id).ToList();
                    await context.Reminders
                        .Where(r => deleteIds.Contains(r.Id))
                        .ExecuteDeleteAsync(_cancellationTokenSource.Token);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Batch operation error: {ex.Message}");
            }
            finally
            {
                _dbSemaphore.Release();
            }
        }

        private DateTime CalculateNextOccurrence(DateTime lastOccurrence, TimeSpan interval, DateTime currentTime)
        {
            if (interval.TotalMilliseconds <= 0)
                return currentTime.AddHours(1);

            var elapsed = currentTime - lastOccurrence;
            var intervals = (long)Math.Ceiling(elapsed.TotalMilliseconds / interval.TotalMilliseconds);
            return lastOccurrence.Add(TimeSpan.FromMilliseconds(interval.TotalMilliseconds * intervals));
        }

        public async Task AddReminderAsync(Reminder reminder)
        {
            if (_disposed) return;

            _databaseOperations.Enqueue(new DatabaseOperation(OperationType.Add, reminder));
            UpdateUICollection(() => _reminders.Add(reminder));

            if (reminder.ReminderDateTime > DateTime.Now && !reminder.IsCompleted)
            {
                _nextNotificationTimes.TryAdd(reminder.Id, reminder.ReminderDateTime);
            }
        }

        public Task CompleteReminderAsync(int id)
        {
            if (_disposed) return Task.CompletedTask;

            var reminder = FindReminderById(id);
            if (reminder != null)
            {
                reminder.IsCompleted = true;
                _databaseOperations.Enqueue(new DatabaseOperation(OperationType.Update, reminder));
                _nextNotificationTimes.TryRemove(id, out _);
                UpdateUICollection(() =>
                {
                    var localReminder = FindReminderById(id);
                    if (localReminder != null)
                    {
                        localReminder.IsCompleted = true;
                    }
                });
            }
            return Task.CompletedTask;
        }

        public Task DeleteReminderAsync(int id)
        {
            if (_disposed) return Task.CompletedTask;

            var reminder = FindReminderById(id);
            if (reminder != null)
            {
                _databaseOperations.Enqueue(new DatabaseOperation(OperationType.Delete, reminder));
                UpdateUICollection(() => _reminders.Remove(reminder));
                _nextNotificationTimes.TryRemove(id, out _);
                _processedOverdueReminders.TryRemove(id);
            }
            return Task.CompletedTask;
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
                    .AddText($"\u23F0 {DateTime.Now:hh:mm tt}")
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
                    .AddText($"⚠ OVERDUE: {reminder.Title}")
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
                    try { action(); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Error updating UI: {ex.Message}"); }
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
            System.Diagnostics.Debug.WriteLine($"Notification activated: {args.Argument}");
        }

        public int GetProcessedNotificationCount()
        {
            lock (_statsLock)
            {
                return _processedNotifications;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cancellationTokenSource.Cancel();

            _processingTimer?.Dispose();
            _batchProcessor?.Dispose();

            _dbSemaphore?.Dispose();
            _cancellationTokenSource?.Dispose();

            _nextNotificationTimes.Clear();
            _processedOverdueReminders.Clear();

            DatabaseService.Instance.DisposeContext();
        }
    }

    public enum OperationType { Add, Update, Delete }

    public record DatabaseOperation(OperationType Type, Reminder Reminder);

    public class ConcurrentHashSet<T> : IDisposable
    {
        private readonly ConcurrentDictionary<T, byte> _dictionary = new();

        public bool TryAdd(T item) => _dictionary.TryAdd(item, 0);
        public bool TryRemove(T item) => _dictionary.TryRemove(item, out _);
        public bool Contains(T item) => _dictionary.ContainsKey(item);
        public void Clear() => _dictionary.Clear();
        public void Dispose() => _dictionary.Clear();
    }
}
