using System;
using System.Globalization;
using System.Windows.Data;

namespace KitchenInventory.Desktop.Converters;

/// <summary>
/// Renders a stored quantity the way a person reads one. Quantities are held to three decimal
/// places, so a value bound straight to the screen shows "10.00" where the reader thinks "10
/// kilos"; this trims the trailing zeros without losing a real fraction ("10", "2.5", "0.25").
/// It is the single place that decision is made — the item grid and both movement lists bind
/// through it, so their numbers cannot drift apart.
/// </summary>
public sealed class QuantityDisplayConverter : IValueConverter
{
    /// <summary>Grouped thousands, and up to three decimals with the trailing zeros dropped.</summary>
    private const string Format = "#,##0.###";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return null;
        // An amount that cannot be read is shown as it arrived rather than as a misleading zero.
        if (ToDecimal(value, culture) is not decimal amount) return value.ToString();
        return amount.ToString(Format, culture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        switch (value)
        {
            case decimal dec:
                return dec;
            case double d:
                return (decimal)d;
            case string text when decimal.TryParse(text, NumberStyles.Number, culture, out var parsed):
                return parsed;
            case string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant):
                return invariant;
            default:
                // Half-typed input ("1.") leaves the stored quantity alone instead of becoming 0.
                return Binding.DoNothing;
        }
    }

    private static decimal? ToDecimal(object value, CultureInfo culture)
        => value switch
        {
            decimal dec => dec,
            double d => (decimal)d,
            float f => (decimal)f,
            int i => i,
            long l => l,
            string s when decimal.TryParse(s, NumberStyles.Number, culture, out var parsed) => parsed,
            _ => null
        };
}
