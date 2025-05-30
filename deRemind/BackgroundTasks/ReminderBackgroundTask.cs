// BackgroundTasks/ReminderBackgroundTask.cs
using Windows.ApplicationModel.Background;
using Windows.Storage;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using deRemind.Data;
using deRemind.Models;
using System.Linq;

namespace deRemind.BackgroundTasks
{
    public sealed class ReminderBackgroundTask : IBackgroundTask
    {
        private BackgroundTaskDeferral? _deferral;

        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            _deferral = taskInstance.GetDeferral();
            taskInstance.Canceled += OnCanceled;

            try
            {
                await CheckAndTriggerReminders();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Background task error: {ex.Message}");
            }
            finally
            {
                _deferral?.Complete();
            }
        }

        private async Task CheckAndTriggerReminders()
        {
            try
            {
                using var context = new ReminderDbContext();
                var now = DateTime.Now;

                // Find reminders that should have triggered
                var dueReminders = await context.Reminders
                    .Where(r => r.ReminderDateTime <= now &&
                               !r.IsCompleted &&
                               r.ReminderDateTime > now.AddHours(-1)) // Only check last hour to avoid spam
                    .ToListAsync();

                foreach (var reminder in dueReminders)
                {
                    ShowReminderNotification(reminder);

                    // Handle repeating reminders
                    if (reminder.IsRepeating)
                    {
                        reminder.ReminderDateTime = CalculateNextOccurrence(
                            reminder.ReminderDateTime,
                            reminder.RepeatInterval,
                            now);

                        context.Reminders.Update(reminder);
                    }
                }

                if (dueReminders.Any())
                {
                    await context.SaveChangesAsync();
                }

                // Also check for overdue reminders (older than 1 hour)
                var overdueReminders = await context.Reminders
                    .Where(r => r.ReminderDateTime < now.AddHours(-1) &&
                               !r.IsCompleted &&
                               !r.IsRepeating)
                    .ToListAsync();

                foreach (var reminder in overdueReminders)
                {
                    ShowOverdueNotification(reminder);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking reminders: {ex.Message}");
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

        private void ShowReminderNotification(Reminder reminder)
        {
            try
            {
                var notification = new AppNotificationBuilder()
                    .AddArgument("action", "remind")
                    .AddArgument("reminderId", reminder.Id.ToString())
                    .AddText($"🔔 {reminder.Title}")
                    .AddText(string.IsNullOrEmpty(reminder.Description) ?
                            $"Due: {reminder.ReminderDateTime:MMM dd, yyyy - hh:mm tt}" :
                            reminder.Description)
                    .AddText($"⏰ {DateTime.Now:hh:mm tt}")
                    .SetScenario(AppNotificationScenario.Reminder)
                    .BuildNotification();

                notification.Tag = $"remind_{reminder.Id}";
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

        private void OnCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            System.Diagnostics.Debug.WriteLine($"Background task canceled: {reason}");
            _deferral?.Complete();
        }
    }
}