using Microsoft.EntityFrameworkCore;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;

namespace deRemind.Services
{
    public class BackgroundServiceManager
    {
        private const string TASK_NAME = "ReminderBackgroundTask";

        public async Task<bool> RegisterBackgroundTaskAsync()
        {
            try
            {
                // Check if already registered
                foreach (var task in BackgroundTaskRegistration.AllTasks)
                {
                    if (task.Value.Name == TASK_NAME)
                    {
                        Debug.WriteLine("Background task already registered");
                        return true;
                    }
                }

                // Request background access
                var accessStatus = await BackgroundExecutionManager.RequestAccessAsync();

                if (accessStatus == BackgroundAccessStatus.DeniedByUser ||
                    accessStatus == BackgroundAccessStatus.DeniedBySystemPolicy)
                {
                    Debug.WriteLine($"Background access denied: {accessStatus}");
                    return false;
                }

                // Create the background task builder
                var builder = new BackgroundTaskBuilder()
                {
                    Name = TASK_NAME
                    // Don't set TaskEntryPoint for in-process background tasks in WinUI 3
                };

                // Use only one trigger - TimeTrigger for periodic execution
                builder.SetTrigger(new TimeTrigger(15, false));

                // Add conditions to ensure it only runs when appropriate
                builder.AddCondition(new SystemCondition(SystemConditionType.InternetAvailable));

                // Register the task with a lambda function instead of separate class
                var registration = builder.Register();

                // Handle the task execution inline
                registration.Completed += async (sender, args) =>
                {
                    try
                    {
                        // Your reminder checking logic here
                        await CheckRemindersInline();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Background task execution error: {ex.Message}");
                    }
                };

                Debug.WriteLine("Background task registered successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to register background task: {ex.Message}");
                return false;
            }
        }

        private async Task CheckRemindersInline()
        {
            try
            {
                using var context = new deRemind.Data.ReminderDbContext();
                await context.Database.EnsureCreatedAsync();

                var now = DateTime.Now;
                var dueReminders = await context.Reminders
                    .Where(r => r.ReminderDateTime <= now &&
                               !r.IsCompleted &&
                               r.ReminderDateTime > now.AddHours(-1))
                    .ToListAsync();

                foreach (var reminder in dueReminders)
                {
                    // Show notification logic here
                    ShowReminderNotification(reminder);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking reminders inline: {ex.Message}");
            }
        }

        private void ShowReminderNotification(deRemind.Models.Reminder reminder)
        {
            try
            {
                var notification = new Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder()
                    .AddArgument("action", "remind")
                    .AddArgument("reminderId", reminder.Id.ToString())
                    .AddText($"🔔 {reminder.Title}")
                    .AddText(string.IsNullOrEmpty(reminder.Description) ?
                            $"Due: {reminder.ReminderDateTime:MMM dd, yyyy - hh:mm tt}" :
                            reminder.Description)
                    .BuildNotification();

                Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Show(notification);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing notification: {ex.Message}");
            }
        }

        public void UnregisterBackgroundTask()
        {
            try
            {
                foreach (var task in BackgroundTaskRegistration.AllTasks)
                {
                    if (task.Value.Name == TASK_NAME)
                    {
                        task.Value.Unregister(true);
                        Debug.WriteLine("Background task unregistered");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to unregister background task: {ex.Message}");
            }
        }

        public bool IsBackgroundTaskRegistered()
        {
            foreach (var task in BackgroundTaskRegistration.AllTasks)
            {
                if (task.Value.Name == TASK_NAME)
                {
                    return true;
                }
            }
            return false;
        }

        public async Task<BackgroundAccessStatus> GetBackgroundAccessStatusAsync()
        {
            return await BackgroundExecutionManager.RequestAccessAsync();
        }
    }
}