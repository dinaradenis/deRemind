using Windows.ApplicationModel;
using System;
using System.Threading.Tasks;
using System.Diagnostics;

namespace deRemind.Services
{
    public class StartupTaskManager
    {
        private const string STARTUP_TASK_ID = "deRemindStartupTask";

        public async Task<bool> EnableStartupAsync()
        {
            try
            {
                var startupTask = await StartupTask.GetAsync(STARTUP_TASK_ID);

                switch (startupTask.State)
                {
                    case StartupTaskState.Disabled:
                        var newState = await startupTask.RequestEnableAsync();
                        Debug.WriteLine($"Startup task enable request result: {newState}");
                        return newState == StartupTaskState.Enabled;

                    case StartupTaskState.Enabled:
                        Debug.WriteLine("Startup task already enabled");
                        return true;

                    case StartupTaskState.DisabledByUser:
                        Debug.WriteLine("Startup task disabled by user - cannot enable programmatically");
                        return false;

                    case StartupTaskState.DisabledByPolicy:
                        Debug.WriteLine("Startup task disabled by policy - cannot enable");
                        return false;

                    default:
                        Debug.WriteLine($"Startup task state: {startupTask.State}");
                        return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error enabling startup task: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DisableStartupAsync()
        {
            try
            {
                var startupTask = await StartupTask.GetAsync(STARTUP_TASK_ID);

                if (startupTask.State == StartupTaskState.Enabled)
                {
                    startupTask.Disable();
                    Debug.WriteLine("Startup task disabled");
                    return true;
                }

                return true; // Already disabled
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disabling startup task: {ex.Message}");
                return false;
            }
        }

        public async Task<StartupTaskState> GetStartupStateAsync()
        {
            try
            {
                var startupTask = await StartupTask.GetAsync(STARTUP_TASK_ID);
                return startupTask.State;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting startup state: {ex.Message}");
                return StartupTaskState.Disabled;
            }
        }

        public async Task<bool> IsStartupEnabledAsync()
        {
            var state = await GetStartupStateAsync();
            return state == StartupTaskState.Enabled;
        }
    }
}