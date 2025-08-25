using System;
using System.Globalization;
using System.Windows.Data;

namespace KitchenInventory.Desktop.Converters;

public sealed class NeedsAttentionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            // Expected: [quantity, expiryDate, lowStockThreshold, expiringSoonDays]
            decimal quantity = 0m;
            if (values.Length > 0 && values[0] is not null)
            {
                if (values[0] is decimal d) quantity = d;
                else if (values[0] is double db) quantity = (decimal)db;
                else if (decimal.TryParse(values[0].ToString(), NumberStyles.Number, culture, out var pd)) quantity = pd;
            }

            DateTime? expiry = null;
            if (values.Length > 1 && values[1] is not null)
            {
                if (values[1] is DateTime dt) expiry = dt;
                else if (DateTime.TryParse(values[1].ToString(), culture, DateTimeStyles.AssumeLocal, out var pdt)) expiry = pdt;
            }

            decimal lowStockThreshold = 5m;
            if (values.Length > 2 && values[2] is not null)
            {
                if (values[2] is decimal d2) lowStockThreshold = d2;
                else if (values[2] is double db2) lowStockThreshold = (decimal)db2;
                else if (decimal.TryParse(values[2].ToString(), NumberStyles.Number, culture, out var pd2)) lowStockThreshold = pd2;
            }

            int expiringSoonDays = 7;
            if (values.Length > 3 && values[3] is not null)
            {
                if (values[3] is int i) expiringSoonDays = i;
                else if (int.TryParse(values[3].ToString(), NumberStyles.Integer, culture, out var pi)) expiringSoonDays = pi;
            }

            var lowStock = quantity < lowStockThreshold;
            var expSoon = expiry.HasValue && expiry.Value.Date <= DateTime.Today.AddDays(expiringSoonDays);
            return lowStock || expSoon;
        }
        catch
        {
            return false;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}