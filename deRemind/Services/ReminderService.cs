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
    public class HybridReminderService
    {
        private ObservableCollection<Reminder> _reminders;
        private Dictionary<int, Timer> _timers = new Dictionary<int, Timer>();

        public HybridReminderService()
        {
            _reminders = new ObservableCollection<Reminder>();
            InitializeNotifications();
            _ = LoadRemindersAsync();
        }

        public ObservableCollection<Reminder> GetReminders() => _reminders;

        private async Task LoadRemindersAsync()
        {
            try
            {
                using var context = new ReminderDbContext();
                await context.Database.EnsureCreatedAsync();

                var reminders = await context.Reminders
                    .Where(r => !r.IsCompleted || r.IsRepeating)
                    .OrderBy(r => r.ReminderDateTime)
                    .ToListAsync();

                _reminders.Clear();
                var now = DateTime.Now;

                foreach (var reminder in reminders)
                {
                    _reminders.Add(reminder);

                    // Handle overdue notifications (app was closed)
                    if (reminder.ReminderDateTime <= now && !reminder.IsCompleted)
                    {
                        if (reminder.IsRepeating)
                        {
                            // Calculate next occurrence for repeating reminders
                            var nextOccurrence = CalculateNextOccurrence(reminder.ReminderDateTime, reminder.RepeatInterval, now);
                            reminder.ReminderDateTime = nextOccurrence;
                            await UpdateReminderInDatabase(reminder);
                            ScheduleNotification(reminder);
                        }
                        else
                        {
                            // Show overdue notification immediately
                            ShowOverdueNotification(reminder);
                        }
                    }
                    // Schedule future notifications
                    else if (reminder.ReminderDateTime > now && !reminder.IsCompleted)
                    {
                        ScheduleNotification(reminder);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading reminders: {ex.Message}");
            }
        }

        private DateTime CalculateNextOccurrence(DateTime lastOccurrence, TimeSpan interval, DateTime currentTime)
        {
            var nextOccurrence = lastOccurrence;
            while (nextOccurrence <= currentTime)
            {
                nextOccurrence = nextOccurrence.Add(interval);
            }
            return nextOccurrence;
        }

        private void ShowOverdueNotification(Reminder reminder)
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

        public async Task AddReminderAsync(Reminder reminder)
        {
            try
            {
                using var context = new ReminderDbContext();
                context.Reminders.Add(reminder);
                await context.SaveChangesAsync();

                _reminders.Add(reminder);

                // Schedule in-memory timer
                ScheduleNotification(reminder);

                // For production: Also schedule with Task Scheduler or other persistent method
                // SchedulePersistentNotification(reminder);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding reminder: {ex.Message}");
            }
        }

        private async Task UpdateReminderInDatabase(Reminder reminder)
        {
            try
            {
                using var context = new ReminderDbContext();
                context.Reminders.Update(reminder);
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating reminder in database: {ex.Message}");
            }
        }

        private void ScheduleNotification(Reminder reminder)
        {
            if (reminder.ReminderDateTime <= DateTime.Now || reminder.IsCompleted)
                return;

            var delay = reminder.ReminderDateTime - DateTime.Now;

            // Don't schedule if delay is too long (> 24 hours) to avoid memory issues
            if (delay.TotalHours > 24)
            {
                System.Diagnostics.Debug.WriteLine($"Skipping long-term scheduling for reminder {reminder.Id} (due in {delay.TotalHours:F1} hours)");
                return;
            }

            var timer = new Timer(async (_) =>
            {
                ShowNotification(reminder);

                // Handle repeating reminders
                if (reminder.IsRepeating && !reminder.IsCompleted)
                {
                    reminder.ReminderDateTime = reminder.ReminderDateTime.Add(reminder.RepeatInterval);
                    await UpdateReminderInDatabase(reminder);

                    // Update the in-memory collection
                    var existingReminder = _reminders.FirstOrDefault(r => r.Id == reminder.Id);
                    if (existingReminder != null)
                    {
                        existingReminder.ReminderDateTime = reminder.ReminderDateTime;
                    }

                    ScheduleNotification(reminder);
                }

                // Clean up the timer
                if (_timers.ContainsKey(reminder.Id))
                {
                    _timers[reminder.Id].Dispose();
                    _timers.Remove(reminder.Id);
                }
            }, null, delay, Timeout.InfiniteTimeSpan);

            // Store the timer so we can cancel it later
            if (_timers.ContainsKey(reminder.Id))
            {
                _timers[reminder.Id].Dispose();
            }
            _timers[reminder.Id] = timer;
        }

        private void ShowNotification(Reminder reminder)
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

        // Background task to check for missed reminders (can be called periodically)
        public async Task CheckForMissedReminders()
        {
            var now = DateTime.Now;
            var missedReminders = _reminders.Where(r =>
                r.ReminderDateTime <= now &&
                !r.IsCompleted &&
                !_timers.ContainsKey(r.Id)).ToList();

            foreach (var reminder in missedReminders)
            {
                ShowOverdueNotification(reminder);

                if (reminder.IsRepeating)
                {
                    reminder.ReminderDateTime = CalculateNextOccurrence(reminder.ReminderDateTime, reminder.RepeatInterval, now);
                    await UpdateReminderInDatabase(reminder);
                    ScheduleNotification(reminder);
                }
            }
        }

        // Existing methods...
        public async Task CompleteReminderAsync(int id)
        {
            try
            {
                using var context = new ReminderDbContext();
                var reminder = await context.Reminders.FindAsync(id);
                if (reminder != null)
                {
                    reminder.IsCompleted = true;
                    await context.SaveChangesAsync();
                }

                var localReminder = _reminders.FirstOrDefault(r => r.Id == id);
                if (localReminder != null)
                {
                    localReminder.IsCompleted = true;
                    CancelNotification(id);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error completing reminder: {ex.Message}");
            }
        }

        public async Task DeleteReminderAsync(int id)
        {
            try
            {
                using var context = new ReminderDbContext();
                var reminder = await context.Reminders.FindAsync(id);
                if (reminder != null)
                {
                    context.Reminders.Remove(reminder);
                    await context.SaveChangesAsync();
                }

                var localReminder = _reminders.FirstOrDefault(r => r.Id == id);
                if (localReminder != null)
                {
                    _reminders.Remove(localReminder);
                    CancelNotification(id);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting reminder: {ex.Message}");
            }
        }

        private void CancelNotification(int reminderId)
        {
            try
            {
                if (_timers.ContainsKey(reminderId))
                {
                    _timers[reminderId].Dispose();
                    _timers.Remove(reminderId);
                }

                // Updated to use RemoveByTagAsync instead of RemoveByTag
                AppNotificationManager.Default.RemoveByTagAsync(reminderId.ToString()).AsTask().Wait();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing notification: {ex.Message}");
            }
        }

        private void InitializeNotifications()
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
        }

        private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
        {
            // Handle notification interactions
            System.Diagnostics.Debug.WriteLine($"Notification activated with args: {args.Argument}");
        }

        // Synchronous wrapper methods for backward compatibility
        public void AddReminder(Reminder reminder) => _ = AddReminderAsync(reminder);
        public void DeleteReminder(int id) => _ = DeleteReminderAsync(id);
        public void CompleteReminder(int id) => _ = CompleteReminderAsync(id);
    }
}