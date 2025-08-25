using System;
using System.Globalization;
using System.Windows.Data;

namespace KitchenInventory.Desktop.Converters;

public sealed class DoubleDecimalConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal dec) return (double)dec;
        if (value is double d) return d;
        if (value is string s && decimal.TryParse(s, NumberStyles.Number, culture, out var parsed)) return (double)parsed;
        return 0d;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d) return System.Convert.ToDecimal(d);
        if (value is decimal dec) return dec;
        if (value is string s && double.TryParse(s, NumberStyles.Number, culture, out var parsed)) return System.Convert.ToDecimal(parsed);
        return 0m;
    }
}