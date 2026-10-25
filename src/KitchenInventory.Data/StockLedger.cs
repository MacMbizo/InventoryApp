using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KitchenInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KitchenInventory.Data;

/// <summary>
/// An item whose recorded quantity does not match the quantity implied by its movements.
/// </summary>
public sealed record LedgerDiscrepancy(
    int ItemId,
    string ItemName,
    decimal RecordedQuantity,
    decimal LedgerQuantity)
{
    /// <summary>Recorded quantity minus ledger quantity.</summary>
    public decimal Difference => RecordedQuantity - LedgerQuantity;
}

/// <summary>
/// Read-only reconciliation between <see cref="Item.Quantity"/> and the
/// <see cref="StockMovement"/> ledger.
/// </summary>
/// <remarks>
/// The ledger sign convention (see <see cref="StockMovement.Quantity"/>) is
/// <c>Item.Quantity == ΣAdd − ΣConsume + ΣAdjust</c>. Adjust rows written before that
/// convention stored a magnitude, so they surface here as discrepancies until a real
/// stock count corrects the item.
/// </remarks>
public static class StockLedger
{
    /// <summary>Quantities are stored to three decimal places; smaller differences are noise.</summary>
    public const decimal Tolerance = 0.001m;

    /// <summary>The signed change a movement made to its item's stock.</summary>
    private static decimal SignedDelta(MovementType type, decimal quantity) => type switch
    {
        MovementType.Add => quantity,
        MovementType.Consume => -quantity,
        MovementType.Adjust => quantity,
        _ => 0m
    };

    /// <summary>
    /// Compares every stored item's quantity with the quantity implied by its movements and
    /// returns one entry per mismatch. An empty result means the ledger reconciles.
    /// Movements whose item no longer exists (for example delete tombstones) are ignored.
    /// </summary>
    public static async Task<IReadOnlyList<LedgerDiscrepancy>> ReconcileAsync(
        KitchenInventoryDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var items = await db.Items.AsNoTracking()
            .Select(i => new { i.Id, i.Name, i.Quantity })
            .ToListAsync(cancellationToken);

        // Aggregated in memory: the SQLite provider cannot SUM decimal columns server-side.
        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.ItemId != null)
            .Select(m => new { ItemId = m.ItemId!.Value, m.Type, m.Quantity })
            .ToListAsync(cancellationToken);

        var ledgerByItem = movements
            .GroupBy(m => m.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(m => SignedDelta(m.Type, m.Quantity)));

        var discrepancies = new List<LedgerDiscrepancy>();
        foreach (var item in items)
        {
            var ledgerQuantity = ledgerByItem.TryGetValue(item.Id, out var total) ? total : 0m;
            if (Math.Abs(item.Quantity - ledgerQuantity) > Tolerance)
            {
                discrepancies.Add(new LedgerDiscrepancy(item.Id, item.Name, item.Quantity, ledgerQuantity));
            }
        }

        return discrepancies;
    }
}
