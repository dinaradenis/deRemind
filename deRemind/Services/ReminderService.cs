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
    public class ReminderService
    {
        private ObservableCollection<Reminder> _reminders;
        private Dictionary<int, Timer> _timers = new Dictionary<int, Timer>();

        public ReminderService()
        {
            _reminders = new ObservableCollection<Reminder>();
            InitializeNotifications();
            _ = LoadRemindersAsync(); // Load reminders asynchronously
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
                foreach (var reminder in reminders)
                {
                    _reminders.Add(reminder);

                    // Reschedule notifications for future reminders
                    if (reminder.ReminderDateTime > DateTime.Now && !reminder.IsCompleted)
                    {
                        ScheduleNotification(reminder);
                    }
                    // Handle overdue repeating reminders
                    else if (reminder.IsRepeating && !reminder.IsCompleted && reminder.ReminderDateTime <= DateTime.Now)
                    {
                        // Calculate next occurrence
                        var nextOccurrence = reminder.ReminderDateTime;
                        while (nextOccurrence <= DateTime.Now)
                        {
                            nextOccurrence = nextOccurrence.Add(reminder.RepeatInterval);
                        }
                        reminder.ReminderDateTime = nextOccurrence;
                        await UpdateReminderAsync(reminder);
                        ScheduleNotification(reminder);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading reminders: {ex.Message}");
            }
        }

        public async Task AddReminderAsync(Reminder reminder)
        {
            try
            {
                using var context = new ReminderDbContext();
                context.Reminders.Add(reminder);
                await context.SaveChangesAsync();

                _reminders.Add(reminder);
                ScheduleNotification(reminder);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding reminder: {ex.Message}");
            }
        }

        public async Task UpdateReminderAsync(Reminder reminder)
        {
            try
            {
                using var context = new ReminderDbContext();
                context.Reminders.Update(reminder);
                await context.SaveChangesAsync();

                var existing = _reminders.FirstOrDefault(r => r.Id == reminder.Id);
                if (existing != null)
                {
                    CancelNotification(reminder.Id);
                    var index = _reminders.IndexOf(existing);
                    _reminders[index] = reminder;
                    ScheduleNotification(reminder);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating reminder: {ex.Message}");
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

        // Synchronous wrapper methods for backward compatibility
        public void AddReminder(Reminder reminder) => _ = AddReminderAsync(reminder);
        public void UpdateReminder(Reminder reminder) => _ = UpdateReminderAsync(reminder);
        public void DeleteReminder(int id) => _ = DeleteReminderAsync(id);
        public void CompleteReminder(int id) => _ = CompleteReminderAsync(id);

        private void InitializeNotifications()
        {
            // Register for notifications
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
        }

        private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
        {
            // Handle notification click
            var arguments = args.Argument;
            if (int.TryParse(arguments, out int reminderId))
            {
                var reminder = _reminders.FirstOrDefault(r => r.Id == reminderId);
                if (reminder != null && reminder.IsRepeating)
                {
                    // Schedule next occurrence for repeating reminders
                    reminder.ReminderDateTime = reminder.ReminderDateTime.Add(reminder.RepeatInterval);
                    _ = UpdateReminderAsync(reminder);
                    ScheduleNotification(reminder);
                }
            }
        }

        private void ScheduleNotification(Reminder reminder)
        {
            if (reminder.ReminderDateTime <= DateTime.Now || reminder.IsCompleted)
                return;

            var delay = reminder.ReminderDateTime - DateTime.Now;

            var timer = new Timer((_) =>
            {
                ShowNotification(reminder);

                // Handle repeating reminders
                if (reminder.IsRepeating && !reminder.IsCompleted)
                {
                    reminder.ReminderDateTime = reminder.ReminderDateTime.Add(reminder.RepeatInterval);
                    _ = UpdateReminderAsync(reminder);
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
                .SetScenario(AppNotificationScenario.Reminder)
                .BuildNotification();

            notification.Tag = reminder.Id.ToString();
            notification.ExpiresOnReboot = true;

            AppNotificationManager.Default.Show(notification);
        }

        private void CancelNotification(int reminderId)
        {
            try
            {
                // Cancel the timer
                if (_timers.ContainsKey(reminderId))
                {
                    _timers[reminderId].Dispose();
                    _timers.Remove(reminderId);
                }

                // Remove any existing notification
                AppNotificationManager.Default.RemoveByTagAsync(reminderId.ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing notification: {ex.Message}");
            }
        }
    }
}