using deRemind.Models;
using deRemind.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using System;
using Windows.UI.WindowManagement;
using WinRT.Interop;

namespace deRemind
{
    public sealed partial class MainWindow : Window
    {
        private readonly HybridReminderService _reminderService;
        private bool _isClosing = false;
        private Microsoft.UI.Windowing.AppWindow appWindow;

        public MainWindow()
        {

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

            this.InitializeComponent();
            _reminderService = new HybridReminderService();
            RemindersListView.ItemsSource = _reminderService.GetReminders();

            // Set default date and time
            ReminderDatePicker.Date = DateTime.Today;
            ReminderTimePicker.Time = DateTime.Now.TimeOfDay.Add(TimeSpan.FromHours(1));

            // Handle repeating checkbox change
            RepeatingCheckBox.Checked += (s, e) => RepeatIntervalComboBox.Visibility = Visibility.Visible;
            RepeatingCheckBox.Unchecked += (s, e) => RepeatIntervalComboBox.Visibility = Visibility.Collapsed;

            // Override the close behavior
            this.Closed += MainWindow_Closed;

            this.Title = "deRemind";
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            // Cancel the close operation and hide the window instead
            if (!_isClosing)
            {
                args.Handled = true;
                appWindow.Hide();

                // Optional: Show a notification that the app is still running
                ShowTrayNotification();
            }
        }

        private void ShowTrayNotification()
        {
            try
            {
                var notification = new Microsoft.Windows.AppNotifications.Builder.AppNotificationBuilder()
                    .AddText("deRemind is running in the background")
                    .AddText("Your reminders will continue to work. Click to reopen the app.")
                    .AddArgument("action", "reopen")
                    .BuildNotification();

                Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Show(notification);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing tray notification: {ex.Message}");
            }
        }

        // Method to show the window when notification is clicked
        public void ShowWindow()
        {
            try
            {
                // Show and activate the window
                this.Activate();

                // Bring window to foreground
                var hwnd = WindowNative.GetWindowHandle(this);
                var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
                appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                if (appWindow != null)
                {
                    appWindow.Show();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing window: {ex.Message}");
            }
        }

        // Method to properly close the app
        public void ExitApplication()
        {
            _isClosing = true;
            this.Close();
            Application.Current.Exit();
        }

        // Add a menu item or button to exit the app properly
        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }

        // Rest of your existing methods remain the same...
        private void AddReminderButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
            {
                ShowMessage("Please enter a title for the reminder.");
                return;
            }

            var reminderDateTime = ReminderDatePicker.Date.Date.Add(ReminderTimePicker.Time);

            if (reminderDateTime <= DateTime.Now)
            {
                ShowMessage("Please select a future date and time.");
                return;
            }

            var reminder = new Reminder
            {
                Title = TitleTextBox.Text,
                Description = DescriptionTextBox.Text,
                ReminderDateTime = reminderDateTime,
                IsRepeating = RepeatingCheckBox.IsChecked == true
            };

            if (reminder.IsRepeating)
            {
                switch (RepeatIntervalComboBox.SelectedIndex)
                {
                    case 0: // Daily
                        reminder.RepeatInterval = TimeSpan.FromDays(1);
                        break;
                    case 1: // Weekly
                        reminder.RepeatInterval = TimeSpan.FromDays(7);
                        break;
                    case 2: // Monthly
                        reminder.RepeatInterval = TimeSpan.FromDays(30);
                        break;
                }
            }

            _reminderService.AddReminder(reminder);
            ClearForm();
            ShowMessage("Reminder added successfully!");
        }

        private void CompleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int reminderId)
            {
                _reminderService.CompleteReminder(reminderId);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int reminderId)
            {
                _reminderService.DeleteReminder(reminderId);
            }
        }

        private void ClearForm()
        {
            TitleTextBox.Text = string.Empty;
            DescriptionTextBox.Text = string.Empty;
            ReminderDatePicker.Date = DateTime.Today;
            ReminderTimePicker.Time = DateTime.Now.TimeOfDay.Add(TimeSpan.FromHours(1));
            RepeatingCheckBox.IsChecked = false;
            RepeatIntervalComboBox.Visibility = Visibility.Collapsed;
        }

        private async void ShowMessage(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "deRemind",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}