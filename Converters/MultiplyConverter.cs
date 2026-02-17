using System;
using System.Globalization;
using System.Windows.Data;

namespace PLAI.Converters
{
    /// <summary>
    /// Multiplies a numeric value by a factor (provided as ConverterParameter).
    /// Used for responsive max-width sizing of chat bubbles.
    /// </summary>
    public sealed class MultiplyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is null) return 0d;

            if (!double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var input))
            {
                return 0d;
            }

            var factor = 1d;
            if (parameter is not null && double.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                factor = parsed;
            }

            // Keep a sane minimum in case ActualWidth is temporarily 0.
            var result = input * factor;
            return result <= 0 ? 0d : result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
