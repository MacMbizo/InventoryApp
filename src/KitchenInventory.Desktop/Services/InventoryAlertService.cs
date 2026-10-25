using System;
using System.Collections.Generic;
using System.Linq;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop.Services
{
    public enum AlertKind
    {
        LowStock,
        OutOfStock,
        ExpiringSoon,
        Expired
    }

    public enum AlertSeverity
    {
        Info = 0,
        Warning = 1,
        Critical = 2
    }

    public sealed record InventoryAlert(
        int ItemId,
        string Name,
        string Category,
        string Location,
        AlertKind Kind,
        AlertSeverity Severity,
        string Message,
        int? DaysToExpiry)
    {
        /// <summary>Plain-language severity for display, instead of the raw enum value.</summary>
        public string SeverityText => Severity switch
        {
            AlertSeverity.Critical => "Urgent",
            AlertSeverity.Warning => "Watch",
            _ => "For information"
        };

        /// <summary>Plain-language problem type for display, instead of the raw enum value.</summary>
        public string KindText => Kind switch
        {
            AlertKind.OutOfStock => "Out of stock",
            AlertKind.LowStock => "Running low",
            AlertKind.ExpiringSoon => "Use soon",
            AlertKind.Expired => "Out of date",
            _ => Kind.ToString()
        };
    }

    /// <summary>
    /// Pure evaluation of inventory alerts (low/out of stock, expiring, expired).
    /// No WPF or database dependencies so it can be unit tested directly.
    /// </summary>
    public static class InventoryAlertService
    {
        public static IReadOnlyList<InventoryAlert> Evaluate(
            IEnumerable<Item>? items,
            decimal lowStockThreshold,
            int expiringSoonDays,
            DateTime today)
        {
            var alerts = new List<InventoryAlert>();
            if (items == null) return alerts;

            var todayDate = today.Date;
            if (expiringSoonDays < 0) expiringSoonDays = 0;

            foreach (var it in items)
            {
                if (it == null) continue;

                var category = it.Category?.Name ?? string.Empty;
                var location = it.Location?.Name ?? string.Empty;

                if (it.ExpiryDate.HasValue)
                {
                    var days = (it.ExpiryDate.Value.Date - todayDate).Days;

                    if (days < 0)
                    {
                        var agoText = days == -1 ? "Expired yesterday" : $"Expired {-days} days ago";
                        alerts.Add(new InventoryAlert(it.Id, it.Name, category, location,
                            AlertKind.Expired, AlertSeverity.Critical, agoText, days));
                    }
                    else if (days == 0)
                    {
                        alerts.Add(new InventoryAlert(it.Id, it.Name, category, location,
                            AlertKind.ExpiringSoon, AlertSeverity.Critical, "Expires today", 0));
                    }
                    else if (days <= expiringSoonDays)
                    {
                        var soonText = days == 1 ? "Expires tomorrow" : $"Expires in {days} days";
                        alerts.Add(new InventoryAlert(it.Id, it.Name, category, location,
                            AlertKind.ExpiringSoon, AlertSeverity.Warning, soonText, days));
                    }
                }

                if (it.Quantity <= 0m)
                {
                    alerts.Add(new InventoryAlert(it.Id, it.Name, category, location,
                        AlertKind.OutOfStock, AlertSeverity.Critical, "Out of stock", null));
                }
                else if (it.Quantity < lowStockThreshold)
                {
                    var qty = it.Quantity.ToString("0.###");
                    var unit = string.IsNullOrWhiteSpace(it.Unit) ? string.Empty : " " + it.Unit;
                    alerts.Add(new InventoryAlert(it.Id, it.Name, category, location,
                        AlertKind.LowStock, AlertSeverity.Warning, $"Low stock ({qty}{unit})", null));
                }
            }

            return alerts
                .OrderByDescending(a => a.Severity)
                .ThenBy(a => a.DaysToExpiry ?? int.MaxValue)
                .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }
}
