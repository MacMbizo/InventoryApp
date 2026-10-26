using System;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop.Services
{
    /// <summary>
    /// How loudly a status is meant to be read, which is what decides its icon and its colour. The
    /// words carry the meaning; the tone only reinforces it, because a colour on its own is invisible
    /// to roughly one man in twelve.
    /// </summary>
    public enum ItemStatusTone
    {
        /// <summary>Nothing to act on. Said outright rather than left blank, so "no problems" cannot
        /// be mistaken for "not checked".</summary>
        Ok = 0,

        /// <summary>Worth knowing: running low, or a use-by date that is coming up.</summary>
        Watch = 1,

        /// <summary>Act now: out of stock, or already out of date.</summary>
        Urgent = 2
    }

    /// <summary>What there is to say about one item's condition, in words.</summary>
    public sealed record ItemStatus(string Text, ItemStatusTone Tone)
    {
        /// <summary>True when this is an item the reader should do something about.</summary>
        public bool NeedsAttention => Tone != ItemStatusTone.Ok;
    }

    /// <summary>
    /// Reduces an item to the single line a list can show about its condition.
    /// </summary>
    /// <remarks>
    /// The app already has one place that decides what is wrong with an item and how to word it —
    /// <see cref="InventoryAlertService"/> — so this reduces that answer to one line rather than
    /// restating the rules. An item's status can therefore never disagree with what the Problems tab
    /// says about it, which is the defect this replaced: a row tinted amber, and a tab, and a filter
    /// each with their own idea of what "needs attention" meant.
    /// </remarks>
    public static class ItemStatusService
    {
        /// <summary>What an item with nothing wrong is called.</summary>
        public const string AllWell = "OK";

        /// <summary>
        /// The status of an item, judged with the same thresholds the alerts use. An item can be both
        /// running low and going out of date; a single line can only say one thing, so it says the
        /// loudest, which is the first alert because the list is ordered most urgent first.
        /// </summary>
        public static ItemStatus Evaluate(Item? item, decimal lowStockThreshold, int expiringSoonDays, DateTime today)
        {
            if (item is null) return new ItemStatus(AllWell, ItemStatusTone.Ok);

            var alerts = InventoryAlertService.Evaluate(new[] { item }, lowStockThreshold, expiringSoonDays, today);
            if (alerts.Count == 0) return new ItemStatus(AllWell, ItemStatusTone.Ok);

            var worst = alerts[0];
            return new ItemStatus(worst.Message, ToneFor(worst.Severity));
        }

        /// <summary>
        /// The same answer for a caller holding the item's fields rather than the item itself — a grid
        /// cell is bound to the values on screen, not to the entity behind them. The unit is taken for
        /// the same reason: "Low stock (2 kg)" is the sentence, and a cell that dropped the unit would
        /// word the status differently from the Problems tab.
        /// </summary>
        public static ItemStatus Evaluate(
            decimal quantity, DateTime? expiryDate, decimal lowStockThreshold, int expiringSoonDays, DateTime today, string unit = "")
            => Evaluate(new Item { Quantity = quantity, ExpiryDate = expiryDate, Unit = unit }, lowStockThreshold, expiringSoonDays, today);

        private static ItemStatusTone ToneFor(AlertSeverity severity) => severity switch
        {
            AlertSeverity.Critical => ItemStatusTone.Urgent,
            AlertSeverity.Warning => ItemStatusTone.Watch,
            _ => ItemStatusTone.Ok
        };
    }
}
