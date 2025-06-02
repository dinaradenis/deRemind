using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Concurrent;
using Windows.UI.Text;

namespace deRemind.Converters
{
    // Cached singleton converters to reduce memory allocation
    public static class ConverterCache
    {
        public static readonly BoolToTextDecorationsConverter BoolToTextDecorations = new();
        public static readonly BoolToVisibilityConverter BoolToVisibility = new();
        public static readonly DateTimeFormatConverter DateTimeFormat = new();
        public static readonly StringToVisibilityConverter StringToVisibility = new();
    }

    public class BoolToTextDecorationsConverter : IValueConverter
    {
        // Cache common results to avoid repeated allocations
        private static readonly object StrikethroughValue = TextDecorations.Strikethrough;
        private static readonly object NoneValue = TextDecorations.None;

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is true ? StrikethroughValue : NoneValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        // Cache visibility values
        private static readonly object VisibleValue = Visibility.Visible;
        private static readonly object CollapsedValue = Visibility.Collapsed;

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool boolValue = value is true;
            bool inverse = string.Equals(parameter?.ToString(), "Inverse", StringComparison.OrdinalIgnoreCase);

            if (inverse)
                boolValue = !boolValue;

            return boolValue ? VisibleValue : CollapsedValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value is Visibility.Visible;
        }
    }

    public class DateTimeFormatConverter : IValueConverter
    {
        // Cache formatted strings for better performance
        private static readonly ConcurrentDictionary<DateTime, string> _formatCache = new();
        private const string DateFormat = "MMM dd, yyyy - hh:mm tt";

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTime dateTime)
            {
                // Use cached formatting for better performance
                return _formatCache.GetOrAdd(dateTime.Date.Add(TimeSpan.FromMinutes(dateTime.Minute)),
                    dt => dt.ToString(DateFormat));
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }

        // Cleanup cache periodically
        public static void ClearCache()
        {
            if (_formatCache.Count > 1000) // Prevent unlimited growth
            {
                _formatCache.Clear();
            }
        }
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        private static readonly object VisibleValue = Visibility.Visible;
        private static readonly object CollapsedValue = Visibility.Collapsed;

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return string.IsNullOrWhiteSpace(value?.ToString()) ? CollapsedValue : VisibleValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}