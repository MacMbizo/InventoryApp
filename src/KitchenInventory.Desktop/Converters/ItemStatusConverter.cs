using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using KitchenInventory.Desktop.Services;

namespace KitchenInventory.Desktop.Converters;

/// <summary>
/// Works out what a row should say about its item, from the values the grid is already showing.
/// </summary>
/// <remarks>
/// The rules are <see cref="ItemStatusService"/>'s; this only unpicks the bindings, because a grid
/// cell holds values rather than the entity they came from. Values that cannot be read produce a
/// status of "OK" rather than an accusation: a cell the app is mid-way through editing must not
/// announce that the item is out of stock.
/// </remarks>
public sealed class ItemStatusConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            // Expected: [quantity, expiryDate, lowStockThreshold, expiringSoonDays, unit]
            if (ToDecimal(values.ElementAtOrDefault(0), culture) is not decimal quantity) return AllWell();

            // An absent threshold means the caller did not bind one; the app's own default is 5, and
            // the same is true of the 7-day expiry window.
            var lowStockThreshold = ToDecimal(values.ElementAtOrDefault(2), culture) ?? 5m;
            var expiringSoonDays = ToInt(values.ElementAtOrDefault(3), culture) ?? 7;

            return ItemStatusService.Evaluate(
                quantity,
                ToDate(values.ElementAtOrDefault(1), culture),
                lowStockThreshold,
                expiringSoonDays,
                DateTime.Today,
                values.ElementAtOrDefault(4) as string ?? string.Empty);
        }
        catch
        {
            // A status cell is not worth taking the window down for.
            return AllWell();
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static ItemStatus AllWell() => new(ItemStatusService.AllWell, ItemStatusTone.Ok);

    private static decimal? ToDecimal(object? value, CultureInfo culture) => value switch
    {
        decimal dec => dec,
        double d => (decimal)d,
        float f => (decimal)f,
        int i => i,
        long l => l,
        string s when decimal.TryParse(s, NumberStyles.Number, culture, out var parsed) => parsed,
        _ => null
    };

    private static DateTime? ToDate(object? value, CultureInfo culture) => value switch
    {
        DateTime dt => dt,
        DateTimeOffset dto => dto.DateTime,
        string s when DateTime.TryParse(s, culture, DateTimeStyles.AssumeLocal, out var parsed) => parsed,
        _ => null
    };

    private static int? ToInt(object? value, CultureInfo culture) => value switch
    {
        int i => i,
        string s when int.TryParse(s, NumberStyles.Integer, culture, out var parsed) => parsed,
        _ => null
    };
}
