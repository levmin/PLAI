using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PLAI.Converters
{
    /// <summary>
    /// Inverts a boolean value and converts it to Visibility.
    /// True -> Collapsed, False -> Visible.
    /// </summary>
    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return b ? Visibility.Collapsed : Visibility.Visible;
            }

            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility v)
            {
                return v != Visibility.Visible;
            }

            return true;
        }
    }
}
