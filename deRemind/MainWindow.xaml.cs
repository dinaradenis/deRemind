// MainWindow.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using System;
using deRemind.Models;
using deRemind.Services;

namespace deRemind
{
    public sealed partial class MainWindow : Window
    {
        private readonly HybridReminderService _reminderService;

        public MainWindow()
        {
            this.InitializeComponent();
            _reminderService = new HybridReminderService();
            RemindersListView.ItemsSource = _reminderService.GetReminders();

            // Set default date and time
            ReminderDatePicker.Date = DateTime.Today;
            ReminderTimePicker.Time = DateTime.Now.TimeOfDay.Add(TimeSpan.FromHours(1));

            // Handle repeating checkbox change
            RepeatingCheckBox.Checked += (s, e) => RepeatIntervalComboBox.Visibility = Visibility.Visible;
            RepeatingCheckBox.Unchecked += (s, e) => RepeatIntervalComboBox.Visibility = Visibility.Collapsed;
        }

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