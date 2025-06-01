using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using deRemind.Services;
using System.Threading.Tasks;
using Microsoft.Windows.AppNotifications;

namespace deRemind
{
    public partial class App : Application
    {
        private MainWindow? _window;
        private BackgroundServiceManager? _backgroundServiceManager;
        private StartupTaskManager? _startupTaskManager;

        public App()
        {
            InitializeComponent();

            // Initialize notification system
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();

            // Initialize background services
            await InitializeBackgroundServicesAsync();

            // Handle startup arguments
            var startupArgs = Environment.GetCommandLineArgs();
            bool startMinimized = false;

            // Check if launched by startup task or notification
            foreach (var arg in startupArgs)
            {
                if (arg.Contains("AppNotificationActivated"))
                {
                    System.Diagnostics.Debug.WriteLine("App launched by notification");
                    break;
                }
                else if (arg.Contains("startup"))
                {
                    startMinimized = true;
                    System.Diagnostics.Debug.WriteLine("App launched by startup task");
                    break;
                }
            }

            // Only show window if not started minimized
            if (!startMinimized)
            {
                _window.Activate();
            }
            else
            {
                // Run in background, just initialize services
                System.Diagnostics.Debug.WriteLine("Running in background mode");
            }
        }

        private async Task InitializeBackgroundServicesAsync()
        {
            try
            {
                _backgroundServiceManager = new BackgroundServiceManager();
                _startupTaskManager = new StartupTaskManager();

                var backgroundRegistered = await _backgroundServiceManager.RegisterBackgroundTaskAsync();
                if (backgroundRegistered)
                {
                    System.Diagnostics.Debug.WriteLine("Background task registered successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Failed to register background task");
                }

                var startupEnabled = await _startupTaskManager.EnableStartupAsync();
                if (startupEnabled)
                {
                    System.Diagnostics.Debug.WriteLine("Startup task enabled successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Startup task not enabled");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing background services: {ex.Message}");
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

        // Method to get the main window instance
        public MainWindow? GetMainWindow() => _window;

        public BackgroundServiceManager? BackgroundServiceManager => _backgroundServiceManager;
        public StartupTaskManager? StartupTaskManager => _startupTaskManager;
    }
}