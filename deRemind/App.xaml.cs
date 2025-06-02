using deRemind.Data;
using deRemind.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Windows.AppNotifications;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using LaunchArgs = Microsoft.UI.Xaml.LaunchActivatedEventArgs;

namespace deRemind
{
    public partial class App : Application
    {
        private MainWindow? _window;
        private StartupTaskManager? _startupTaskManager;

        public App()
        {
            InitializeComponent();

            // Initialize notification system
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
        }

        protected override void OnLaunched(LaunchArgs args)
        {
            _window = new MainWindow();

            // Show window immediately unless "startup" argument is present
            if (!ShouldStartMinimized(args))
            {
                _window.Activate();
            }

            // Run startup tasks in the background
            _ = Task.Run(async () =>
            {
                await InitializeStartupTaskAsync();
                await _window.InitializeRemindersAsync();
            });
        }

        // Optional helper method
        private bool ShouldStartMinimized(LaunchArgs args)
        {
            var startupArgs = Environment.GetCommandLineArgs();
            return startupArgs.Any(arg => arg.Contains("startup", StringComparison.OrdinalIgnoreCase));
        }


        private async Task InitializeStartupTaskAsync()
        {
            try
            {
                _startupTaskManager = new StartupTaskManager();
                var startupEnabled = await _startupTaskManager.EnableStartupAsync();
                System.Diagnostics.Debug.WriteLine($"Startup task enabled: {startupEnabled}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing startup task: {ex.Message}");
            }
        }

        public static class DatabaseManager
        {
            private static readonly object _lock = new object();
            private static ReminderDbContext? _sharedContext;

            public static ReminderDbContext GetContext()
            {
                if (_sharedContext == null)
                {
                    lock (_lock)
                    {
                        _sharedContext ??= new ReminderDbContext();
                    }
                }
                return _sharedContext;
            }

            public static void DisposeContext()
            {
                lock (_lock)
                {
                    _sharedContext?.Dispose();
                    _sharedContext = null;
                }
            }
        }

        private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine($"Notification invoked with arguments: {args.Argument}");

            var arguments = args.Argument;

            // Show the main window when notification is clicked
            if (_window != null)
            {
                _window.DispatcherQueue.TryEnqueue(() =>
                {
                    _window.ShowWindow(); // Use our custom method instead of Activate()
                });
            }
            else
            {
                // Create and show window if it doesn't exist
                _window = new MainWindow();
                _window.Activate();
            }
        }
    }
}