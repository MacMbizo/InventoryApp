using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop.Services
{
    public enum ReportKind
    {
        StockOnHand = 0,
        LowStock = 1,
        ExpiringSoon = 2,
        MovementHistory = 3
    }

    /// <summary>
    /// A rendered report: a title, column headers and rows of pre-formatted strings.
    /// Rows are formatted once here so the UI, CSV and PDF outputs stay identical.
    /// </summary>
    public sealed class ReportTable
    {
        public ReportTable(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
        {
            Title = title ?? string.Empty;
            Headers = headers ?? Array.Empty<string>();
            Rows = rows ?? Array.Empty<IReadOnlyList<string>>();
        }

        public string Title { get; }
        public IReadOnlyList<string> Headers { get; }
        public IReadOnlyList<IReadOnlyList<string>> Rows { get; }
        public int RowCount => Rows.Count;
    }

    /// <summary>
    /// Pure report builders (no WPF or database dependencies) so they can be unit tested directly.
    /// </summary>
    public static class ReportingService
    {
        public static string Title(ReportKind kind) => kind switch
        {
            ReportKind.StockOnHand => "Stock on Hand",
            ReportKind.LowStock => "Low Stock",
            ReportKind.ExpiringSoon => "Expiring Soon",
            ReportKind.MovementHistory => "Movement History",
            _ => kind.ToString()
        };

        private static readonly string[] ItemHeaders =
            { "Id", "Name", "Category", "Location", "Quantity", "Unit", "Expiry Date" };

        private static readonly string[] ExpiringHeaders =
            { "Id", "Name", "Category", "Location", "Quantity", "Unit", "Expiry Date", "Days To Expiry" };

        private static readonly string[] MovementHeaders =
            { "Id", "Item", "Type", "Quantity", "Reason", "User", "Timestamp (UTC)" };

        public static ReportTable StockOnHand(IEnumerable<Item>? items)
        {
            var rows = new List<IReadOnlyList<string>>();
            foreach (var it in Ordered(items))
            {
                if (it == null) continue;
                rows.Add(ItemRow(it));
            }
            return new ReportTable(Title(ReportKind.StockOnHand), ItemHeaders, rows);
        }

        public static ReportTable LowStock(IEnumerable<Item>? items, decimal lowStockThreshold)
        {
            var rows = new List<IReadOnlyList<string>>();
            if (items != null)
            {
                foreach (var it in items
                             .Where(i => i != null && i.Quantity < lowStockThreshold)
                             .OrderBy(i => i.Quantity)
                             .ThenBy(i => i.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase))
                {
                    rows.Add(ItemRow(it!));
                }
            }
            return new ReportTable(
                $"{Title(ReportKind.LowStock)} (below {FormatQuantity(lowStockThreshold)})",
                ItemHeaders,
                rows);
        }

        public static ReportTable ExpiringSoon(IEnumerable<Item>? items, int expiringSoonDays, DateTime today)
        {
            var rows = new List<IReadOnlyList<string>>();
            if (items != null)
            {
                if (expiringSoonDays < 0) expiringSoonDays = 0;
                var todayDate = today.Date;
                var limit = todayDate.AddDays(expiringSoonDays);

                var expiring = items
                    .Where(i => i != null && i.ExpiryDate.HasValue)
                    .Select(i => new { Item = i!, Date = i!.ExpiryDate!.Value.Date })
                    .Where(x => x.Date >= todayDate && x.Date <= limit)
                    .OrderBy(x => x.Date)
                    .ThenBy(x => x.Item.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                foreach (var x in expiring)
                {
                    var row = new List<string>(ItemRow(x.Item));
                    row.Add(((x.Date - todayDate).Days).ToString(CultureInfo.InvariantCulture));
                    rows.Add(row);
                }
            }
            return new ReportTable(
                $"{Title(ReportKind.ExpiringSoon)} (next {expiringSoonDays} day(s))",
                ExpiringHeaders,
                rows);
        }

        public static ReportTable MovementHistory(
            IEnumerable<StockMovement>? movements,
            DateTime? fromUtc,
            DateTime? toUtc,
            IReadOnlyDictionary<int, string>? itemNames = null)
        {
            var rows = new List<IReadOnlyList<string>>();
            if (movements != null)
            {
                var filtered = movements
                    .Where(m => m != null)
                    .Where(m => !fromUtc.HasValue || m.TimestampUtc >= fromUtc.Value)
                    .Where(m => !toUtc.HasValue || m.TimestampUtc <= toUtc.Value)
                    .OrderByDescending(m => m.TimestampUtc)
                    .ThenByDescending(m => m.Id);

                foreach (var m in filtered)
                {
                    rows.Add(new[]
                    {
                        m.Id.ToString(CultureInfo.InvariantCulture),
                        ResolveItemName(m, itemNames),
                        m.Type.ToString(),
                        FormatQuantity(m.Quantity),
                        m.Reason ?? string.Empty,
                        m.User ?? string.Empty,
                        FormatTimestamp(m.TimestampUtc)
                    });
                }
            }
            return new ReportTable(Title(ReportKind.MovementHistory), MovementHeaders, rows);
        }

        private static IEnumerable<Item?> Ordered(IEnumerable<Item>? items)
            => (items ?? Enumerable.Empty<Item>())
                .OrderBy(i => i?.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(i => i?.Id ?? 0);

        private static IReadOnlyList<string> ItemRow(Item it) => new[]
        {
            it.Id.ToString(CultureInfo.InvariantCulture),
            it.Name ?? string.Empty,
            it.Category?.Name ?? string.Empty,
            it.Location?.Name ?? string.Empty,
            FormatQuantity(it.Quantity),
            it.Unit ?? string.Empty,
            FormatDate(it.ExpiryDate)
        };

        private static string ResolveItemName(StockMovement movement, IReadOnlyDictionary<int, string>? itemNames)
        {
            var direct = movement.Item?.Name;
            if (!string.IsNullOrWhiteSpace(direct)) return direct!;
            if (movement.ItemId.HasValue && itemNames != null && itemNames.TryGetValue(movement.ItemId.Value, out var name))
            {
                return name ?? string.Empty;
            }
            return string.Empty;
        }

        private static string FormatQuantity(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string FormatDate(DateTime? value)
            => value.HasValue ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;

        private static string FormatTimestamp(DateTime value)
            => value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }
}
