using deRemind.Models;
using deRemind.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using System;
using System.Threading.Tasks;
using Windows.UI.WindowManagement;
using WinRT.Interop;

namespace deRemind
{
    public sealed partial class MainWindow : Window
    {
        private readonly HybridReminderService _reminderService;
        private bool _isClosing = false;
        private Microsoft.UI.Windowing.AppWindow? _appWindow;
        private readonly BackgroundOperationQueue _backgroundQueue = new BackgroundOperationQueue();

        public MainWindow()
        {
            this.InitializeComponent();
            InitializeWindow();

            _reminderService = new HybridReminderService();
            RemindersListView.ItemsSource = _reminderService.GetReminders();

            InitializeDefaults();
            SetupEventHandlers();

            this.Title = "deRemind";
        }

        private void InitializeWindow()
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
                _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing window: {ex.Message}");
            }
        }

        private void InitializeDefaults()
        {
            // Set default date and time more efficiently
            var now = DateTime.Now;
            ReminderDatePicker.Date = now.Date;
            ReminderTimePicker.Time = now.TimeOfDay.Add(TimeSpan.FromHours(1));
        }

        public async Task InitializeRemindersAsync()
        {
            await _reminderService.InitializeAsync();
        }


        private void SetupEventHandlers()
        {
            // Handle repeating checkbox change with lambda for better performance
            RepeatingCheckBox.Checked += (_, _) => RepeatIntervalComboBox.Visibility = Visibility.Visible;
            RepeatingCheckBox.Unchecked += (_, _) => RepeatIntervalComboBox.Visibility = Visibility.Collapsed;

            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (!_isClosing)
            {
                args.Handled = true;
                _appWindow?.Hide();
                ShowTrayNotification();
            }
            else
            {
                // Cleanup resources
                _reminderService?.Dispose();
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

        public void ShowWindow()
        {
            try
            {
                this.Activate();
                _appWindow?.Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing window: {ex.Message}");
            }
        }

        public void ExitApplication()
        {
            _isClosing = true;
            _reminderService?.Dispose();
            this.Close();
            Application.Current.Exit();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }

        private async void AddReminderButton_Click(object sender, RoutedEventArgs e)
        {
            // Validation here — keep your existing validation code!
            if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
            {
                await ShowMessage("Please enter a title for the reminder.");
                return;
            }

            var reminderDateTime = ReminderDatePicker.Date.Date.Add(ReminderTimePicker.Time);
            if (reminderDateTime <= DateTime.Now)
            {
                await ShowMessage("Please select a future date and time.");
                return;
            }

            AddReminderButton.IsEnabled = false;

            var reminder = new Reminder
            {
                Title = TitleTextBox.Text.Trim(),
                Description = DescriptionTextBox.Text?.Trim() ?? string.Empty,
                ReminderDateTime = reminderDateTime,
                IsRepeating = RepeatingCheckBox.IsChecked == true,
                RepeatInterval = RepeatingCheckBox.IsChecked == true
                    ? RepeatIntervalComboBox.SelectedIndex switch
                    {
                        0 => TimeSpan.FromDays(1),
                        1 => TimeSpan.FromDays(7),
                        2 => TimeSpan.FromDays(30),
                        _ => TimeSpan.FromDays(1)
                    }
                    : TimeSpan.Zero
            };

            await _backgroundQueue.EnqueueAsync(async () =>
            {
                try
                {
                    await _reminderService.AddReminderAsync(reminder);

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ClearForm();
                        _ = ShowMessage("Reminder added successfully!");
                    });
                }
                catch (Exception ex)
                {
                    DispatcherQueue.TryEnqueue(async() =>
                    {
                        await ShowMessage($"Error adding reminder: {ex.Message}");
                    });
                    System.Diagnostics.Debug.WriteLine($"Error adding reminder: {ex}");
                }
                finally
                {
                    DispatcherQueue.TryEnqueue(() => AddReminderButton.IsEnabled = true);
                }
            });
        }


        private async void CompleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: int reminderId })
            {
                try
                {
                    await _reminderService.CompleteReminderAsync(reminderId);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error completing reminder: {ex.Message}");
                }
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: int reminderId })
            {
                try
                {
                    await _reminderService.DeleteReminderAsync(reminderId);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error deleting reminder: {ex.Message}");
                }
            }
        }

        private void ClearForm()
        {
            TitleTextBox.Text = string.Empty;
            DescriptionTextBox.Text = string.Empty;

            var now = DateTime.Now;
            ReminderDatePicker.Date = now.Date;
            ReminderTimePicker.Time = now.TimeOfDay.Add(TimeSpan.FromHours(1));

            RepeatingCheckBox.IsChecked = false;
            RepeatIntervalComboBox.Visibility = Visibility.Collapsed;
            RepeatIntervalComboBox.SelectedIndex = 0;
        }

        private async Task ShowMessage(string message)
        {
            try
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
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing message dialog: {ex.Message}");
            }
        }
    }
}