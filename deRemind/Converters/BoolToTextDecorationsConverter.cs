using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;
using Windows.UI.Text;

namespace deRemind.Converters
{
    public class BoolToTextDecorationsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isCompleted = value is bool ? (bool)value : false;
            return isCompleted ? TextDecorations.Strikethrough : TextDecorations.None;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}