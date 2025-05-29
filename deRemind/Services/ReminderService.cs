// Alternative ReminderService with Timer-based scheduling
using deRemind.Models;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace deRemind.Services
{
    public class ReminderService
    {
        private ObservableCollection<Reminder> _reminders;
        private static int _nextId = 1;
        private Dictionary<int, Timer> _timers = new Dictionary<int, Timer>();

        public ReminderService()
        {
            _reminders = new ObservableCollection<Reminder>();
            InitializeNotifications();
        }

        public ObservableCollection<Reminder> GetReminders() => _reminders;

        public void AddReminder(Reminder reminder)
        {
            reminder.Id = _nextId++;
            _reminders.Add(reminder);
            ScheduleNotification(reminder);
        }

        public void UpdateReminder(Reminder reminder)
        {
            var existing = _reminders.FirstOrDefault(r => r.Id == reminder.Id);
            if (existing != null)
            {
                CancelNotification(reminder.Id);
                var index = _reminders.IndexOf(existing);
                _reminders[index] = reminder;
                ScheduleNotification(reminder);
            }
        }

        public void DeleteReminder(int id)
        {
            var reminder = _reminders.FirstOrDefault(r => r.Id == id);
            if (reminder != null)
            {
                _reminders.Remove(reminder);
                CancelNotification(id);
            }
        }

        public void CompleteReminder(int id)
        {
            var reminder = _reminders.FirstOrDefault(r => r.Id == id);
            if (reminder != null)
            {
                reminder.IsCompleted = true;
                CancelNotification(id);
            }
        }

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