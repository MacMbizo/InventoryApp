using System;
using System.Globalization;
using System.Windows.Data;

namespace KitchenInventory.Desktop.Converters
{
    public sealed class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is bool b) return !b;
                // Treat null or non-bool as true -> inverted to false only if parameter says otherwise
                if (value == null) return true; // null considered false -> invert to true
                if (bool.TryParse(value.ToString(), out var parsed)) return !parsed;
                return true;
            }
            catch
            {
                return true;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            if (value == null) return true;
            if (bool.TryParse(value.ToString(), out var parsed)) return !parsed;
            return true;
        }
    }
}