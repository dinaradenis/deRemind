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

            // Only initialize startup task
            await InitializeStartupTaskAsync();

            // Handle startup arguments
            var startupArgs = Environment.GetCommandLineArgs();
            bool startMinimized = startupArgs.Any(arg => arg.Contains("startup"));

            if (!startMinimized)
            {
                _window.Activate();
            }
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