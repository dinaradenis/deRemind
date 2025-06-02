using Microsoft.UI.Xaml.Data;
using System;

namespace deRemind.Converters
{
    public class DateTimeFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTime dateTime)
            {
                return dateTime.ToString("MMM dd, yyyy - hh:mm tt");
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}