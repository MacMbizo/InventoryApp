using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop.Services
{
    public static class CsvExportService
    {
        // Export Items to CSV with stable header order
        // Header: Id,Name,Quantity,Unit,ExpiryDate,CreatedAtUtc,UpdatedAtUtc
        public static string ExportItems(IEnumerable<Item> items)
        {
            var sb = new StringBuilder();
            sb.Append("Id,Name,Quantity,Unit,ExpiryDate,CreatedAtUtc,UpdatedAtUtc\n");
            foreach (var it in items ?? Enumerable.Empty<Item>())
            {
                var id = it.Id.ToString(CultureInfo.InvariantCulture);
                var name = Escape(it.Name);
                var qty = it.Quantity.ToString(CultureInfo.InvariantCulture);
                var unit = Escape(it.Unit);
                var expiry = it.ExpiryDate.HasValue
                    ? it.ExpiryDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : string.Empty;
                var created = it.CreatedAtUtc
                    .ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                var updated = it.UpdatedAtUtc.HasValue
                    ? it.UpdatedAtUtc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
                    : string.Empty;

                sb.Append(id).Append(',')
                  .Append(name).Append(',')
                  .Append(qty).Append(',')
                  .Append(unit).Append(',')
                  .Append(expiry).Append(',')
                  .Append(created).Append(',')
                  .Append(updated)
                  .Append('\n');
            }
            return sb.ToString();
        }

        // Export Stock Movements
        // Header: Id,ItemId,ItemName,Type,Quantity,Reason,User,TimestampUtc
        public static string ExportMovements(IEnumerable<StockMovement> movements, IReadOnlyDictionary<int, string>? itemNameById = null)
        {
            var sb = new StringBuilder();
            sb.Append("Id,ItemId,ItemName,Type,Quantity,Reason,User,TimestampUtc\n");
            foreach (var m in movements ?? Enumerable.Empty<StockMovement>())
            {
                var id = m.Id.ToString(CultureInfo.InvariantCulture);
                var itemId = m.ItemId.HasValue ? m.ItemId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
                var itemName = string.Empty;
                if (m.Item != null && !string.IsNullOrWhiteSpace(m.Item.Name))
                    itemName = m.Item.Name!;
                else if (m.ItemId.HasValue && itemNameById != null && itemNameById.TryGetValue(m.ItemId.Value, out var nm))
                    itemName = nm;

                var type = m.Type.ToString();
                var qty = m.Quantity.ToString(CultureInfo.InvariantCulture);
                var reason = Escape(m.Reason);
                var user = Escape(m.User);
                var ts = m.TimestampUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

                sb.Append(id).Append(',')
                  .Append(itemId).Append(',')
                  .Append(Escape(itemName)).Append(',')
                  .Append(type).Append(',')
                  .Append(qty).Append(',')
                  .Append(reason).Append(',')
                  .Append(user).Append(',')
                  .Append(ts)
                  .Append('\n');
            }
            return sb.ToString();
        }

        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            var v = value.Replace("\"", "\"\"");
            return needsQuotes ? $"\"{v}\"" : v;
        }
    }
}