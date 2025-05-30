// Services/BackgroundServiceManager.cs
using Windows.ApplicationModel.Background;
using Windows.Storage;
using System;
using System.Threading.Tasks;
using System.Diagnostics;

namespace deRemind.Services
{
    public class BackgroundServiceManager
    {
        private const string TASK_NAME = "ReminderBackgroundTask";
        private const string TASK_ENTRY_POINT = "deRemind.BackgroundTasks.ReminderBackgroundTask";

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
                    Name = TASK_NAME,
                    TaskEntryPoint = TASK_ENTRY_POINT
                };

                // Set up triggers
                // Timer trigger - runs every 15 minutes
                builder.SetTrigger(new TimeTrigger(15, false));

                // System trigger - runs when user logs in
                builder.SetTrigger(new SystemTrigger(SystemTriggerType.UserPresent, false));

                // Add conditions
                builder.AddCondition(new SystemCondition(SystemConditionType.UserPresent));

                // Register the task
                var registration = builder.Register();

                Debug.WriteLine("Background task registered successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to register background task: {ex.Message}");
                return false;
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