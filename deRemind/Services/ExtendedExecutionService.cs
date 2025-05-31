using Windows.ApplicationModel.ExtendedExecution;
using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;

namespace deRemind.Services
{
    public class ExtendedExecutionService : IDisposable
    {
        private ExtendedExecutionSession? _session;
        private Timer? _reminderCheckTimer;
        private readonly HybridReminderService _reminderService;

        public ExtendedExecutionService(HybridReminderService reminderService)
        {
            _reminderService = reminderService;
        }

        public async Task<bool> RequestExtendedExecutionAsync()
        {
            try
            {
                // Clear any existing session
                ClearExtendedExecution();

                // Create new session
                _session = new ExtendedExecutionSession
                {
                    Reason = ExtendedExecutionReason.Unspecified,
                    Description = "deRemind needs to run in background to deliver notifications on time."
                };

                _session.Revoked += OnExtendedExecutionRevoked;

                var result = await _session.RequestExtensionAsync();

                if (result == ExtendedExecutionResult.Allowed)
                {
                    Debug.WriteLine("Extended execution granted");
                    StartReminderTimer();
                    return true;
                }
                else
                {
                    Debug.WriteLine($"Extended execution denied: {result}");
                    ClearExtendedExecution();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error requesting extended execution: {ex.Message}");
                return false;
            }
        }

        private void StartReminderTimer()
        {
            // Check for reminders every minute
            _reminderCheckTimer = new Timer(async (_) =>
            {
                try
                {
                    await _reminderService.CheckForMissedReminders();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error checking reminders: {ex.Message}");
                }
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        }

        private void OnExtendedExecutionRevoked(object sender, ExtendedExecutionRevokedEventArgs args)
        {
            Debug.WriteLine($"Extended execution revoked: {args.Reason}");
            ClearExtendedExecution();
        }

        private void ClearExtendedExecution()
        {
            _reminderCheckTimer?.Dispose();
            _reminderCheckTimer = null;

            if (_session != null)
            {
                _session.Revoked -= OnExtendedExecutionRevoked;
                _session.Dispose();
                _session = null;
            }
        }

        public void Dispose()
        {
            ClearExtendedExecution();
        }
    }
}