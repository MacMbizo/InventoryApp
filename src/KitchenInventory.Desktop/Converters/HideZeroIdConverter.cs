using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KitchenInventory.Desktop.Converters
{
    public sealed class HideZeroIdConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                // Treat null, 0, or non-parsable values as hidden
                if (value is null) return Visibility.Collapsed;
                int id;
                if (value is int i) id = i;
                else if (!int.TryParse(value.ToString(), NumberStyles.Integer, culture, out id)) return Visibility.Collapsed;
                return id == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            catch
            {
                return Visibility.Visible;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}